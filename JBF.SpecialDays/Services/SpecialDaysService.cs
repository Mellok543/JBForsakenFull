using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using JBF.Api;

namespace JBF.SpecialDays.Services;

internal sealed class SpecialDaysService : ISpecialDaysApi, ISpecialDayContext
{
    private readonly Dictionary<string, RegisteredSpecialDay> _days =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly SpecialDayVipSuppressionService _vipSuppression = new();
    private readonly Action<string>? _log;

    private RegisteredSpecialDay? _pending;
    private RegisteredSpecialDay? _active;
    private bool _finishRequested;
    private bool? _previousIgnoreRoundWinConditions;

    public SpecialDaysService(Action<string>? log = null)
    {
        _log = log;
    }

    public bool IsActive => _active is not null;
    public bool HasPending => _pending is not null;
    public string? ActiveDayName => _active?.Day.Name;
    public string? PendingDayName => _pending?.Day.Name;

    public IDisposable RegisterDay(ISpecialDay day)
    {
        ArgumentNullException.ThrowIfNull(day);

        if (string.IsNullOrWhiteSpace(day.Id))
            throw new ArgumentException("Special day id cannot be empty.", nameof(day));

        if (string.IsNullOrWhiteSpace(day.Name))
            throw new ArgumentException("Special day name cannot be empty.", nameof(day));

        var token = Guid.NewGuid();
        var registration = new RegisteredSpecialDay(token, day);
        _days[day.Id] = registration;

        _log?.Invoke($"Special day registered: {day.Id} ({day.Name}).");

        return new ActionDisposable(() => UnregisterDay(day.Id, token));
    }

    public bool TryStop()
    {
        if (_active is not null)
        {
            StopActiveDay("отменён");
            return true;
        }

        if (_pending is null)
            return false;

        var name = _pending.Day.Name;
        _pending = null;

        Server.PrintToChatAll(JailbreakChat.Format($"Игровой день отменён: {name}."));
        NotifyAll($"Игровой день отменён: {name}", UiNotificationType.Warning);
        ClearRoundStatusAll();
        return true;
    }

    public void OpenSelectionMenu(CCSPlayerController commander)
    {
        if (!IsHuman(commander))
            return;

        if (WardenCapability.Api.GetOptional()?.IsWarden(commander) != true)
        {
            commander.PrintToChat(
                JailbreakChat.Format("Игровой день может назначить только командир."));
            return;
        }

        if (IsActive || HasPending)
        {
            commander.PrintToChat(
                JailbreakChat.Format("Игровой день уже назначен или запущен."));
            return;
        }

        var available = _days.Values
            .OrderBy(x => x.Day.Order)
            .ThenBy(x => x.Day.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (available.Length == 0)
        {
            commander.PrintToChat(JailbreakChat.Format("Нет доступных игровых дней."));
            return;
        }

        var menu = MenuCapability.Api.GetOptional();
        if (menu is null)
        {
            commander.PrintToChat(JailbreakChat.Format("Menu API недоступно."));
            return;
        }

        var options = available
            .Select(registration =>
                new JailbreakMenuOption(
                    registration.Day.Name,
                    player => Schedule(player, registration)))
            .ToArray();

        menu.Open(commander, "Игровые дни", options);
    }

    public void OnRoundStart()
    {
        if (_active is not null)
            StopActiveDay(null, announce: false);

        if (_pending is null)
            return;

        StartPendingDay();
    }

    public void OnRoundEnd()
    {
        if (_active is not null)
            StopActiveDay("завершён");
    }

    public void OnPlayerSpawn(CCSPlayerController? player)
    {
        if (_active is null || !IsHuman(player))
            return;

        _vipSuppression.Suppress(player!);
        UiCapability.Api.GetOptional()?.SetRoundStatus(
            player!,
            "ИГРОВОЙ ДЕНЬ",
            _active.Day.Name);

        SafeCall(
            $"OnPlayerSpawn:{_active.Day.Id}",
            () => _active.Day.OnPlayerSpawn(player!));
    }

    public void OnPlayerDeath(CCSPlayerController? victim, CCSPlayerController? attacker)
    {
        if (_active is null)
            return;

        SafeCall(
            $"OnPlayerDeath:{_active.Day.Id}",
            () => _active.Day.OnPlayerDeath(victim, attacker));
    }

    public HookResult HandleTakeDamage(CBaseEntity entity, CTakeDamageInfo damageInfo)
    {
        if (_active is null)
            return HookResult.Continue;

        try
        {
            return _active.Day.OnTakeDamage(entity, damageInfo);
        }
        catch (Exception ex)
        {
            _log?.Invoke(
                $"Special day '{_active.Day.Id}' OnTakeDamage failed: {ex}");
            return HookResult.Continue;
        }
    }

    public void HandleDisconnect(int slot)
    {
        _vipSuppression.Forget(slot);
    }

    public void End()
    {
        if (_active is null)
            return;

        StopActiveDay("завершён");
    }

    public void Finish(RoundEndReason reason)
    {
        if (_active is null || _finishRequested)
            return;

        _finishRequested = true;

        ConVar.Find("mp_ignore_round_win_conditions")?.SetValue(false);

        Server.NextFrame(() =>
        {
            if (_active is null)
            {
                _finishRequested = false;
                return;
            }

            var rules = Utilities
                .FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                .FirstOrDefault()
                ?.GameRules;

            if (rules is null)
            {
                _finishRequested = false;
                _log?.Invoke("Special day finish failed: cs_gamerules not found.");
                return;
            }

            rules.TerminateRound(3.0f, reason);
        });
    }

    public void Shutdown()
    {
        _pending = null;

        if (_active is not null)
            StopActiveDay(null, announce: false);

        _vipSuppression.RestoreAll(HumanPlayers());
        RestoreWinConditions();
        ClearRoundStatusAll();
        _days.Clear();
    }

    private void Schedule(
        CCSPlayerController commander,
        RegisteredSpecialDay registration)
    {
        if (IsActive || HasPending)
        {
            commander.PrintToChat(
                JailbreakChat.Format("Игровой день уже назначен или запущен."));
            return;
        }

        if (!_days.TryGetValue(registration.Day.Id, out var current) ||
            current.Token != registration.Token)
        {
            commander.PrintToChat(
                JailbreakChat.Format("Этот игровой день больше недоступен."));
            return;
        }

        _pending = current;

        Server.PrintToChatAll(
            JailbreakChat.Format(
                $"В следующем раунде будет игровой день: {current.Day.Name}."));

        NotifyAll(
            $"Следующий раунд: {current.Day.Name}",
            UiNotificationType.Important,
            5.0f);

        SetRoundStatusAll("СЛЕДУЮЩИЙ РАУНД", current.Day.Name);
    }

    private void StartPendingDay()
    {
        if (_pending is null)
            return;

        _active = _pending;
        _pending = null;
        _finishRequested = false;

        SaveAndDisableNormalWinConditions();

        foreach (var player in HumanPlayers())
            _vipSuppression.Suppress(player);

        Server.PrintToChatAll(
            JailbreakChat.Format($"Начался игровой день: {_active.Day.Name}."));

        AnnounceAll(
            "ИГРОВОЙ ДЕНЬ",
            _active.Day.Name,
            UiNotificationType.Important,
            6.0f);

        SetRoundStatusAll("ИГРОВОЙ ДЕНЬ", _active.Day.Name);

        try
        {
            _active.Day.Start(this);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Special day '{_active.Day.Id}' Start failed: {ex}");
            StopActiveDay("остановлен из-за ошибки");
        }
    }

    private void StopActiveDay(string? result, bool announce = true)
    {
        var registration = _active;
        if (registration is null)
            return;

        _active = null;
        _finishRequested = false;

        SafeCall($"Stop:{registration.Day.Id}", registration.Day.Stop);

        _vipSuppression.RestoreAll(HumanPlayers());
        RestoreWinConditions();
        ClearRoundStatusAll();

        if (!announce)
            return;

        var suffix = string.IsNullOrWhiteSpace(result) ? "завершён" : result;
        Server.PrintToChatAll(
            JailbreakChat.Format(
                $"Игровой день {suffix}: {registration.Day.Name}."));

        AnnounceAll(
            "ИГРОВОЙ ДЕНЬ ЗАВЕРШЁН",
            registration.Day.Name,
            UiNotificationType.Info,
            4.0f);
    }

    private void UnregisterDay(string id, Guid token)
    {
        if (!_days.TryGetValue(id, out var registration) ||
            registration.Token != token)
            return;

        _days.Remove(id);

        if (_pending?.Token == token)
        {
            _pending = null;
            ClearRoundStatusAll();
        }

        if (_active?.Token == token)
            StopActiveDay("выгружен");

        _log?.Invoke($"Special day unregistered: {id}.");
    }

    private void SaveAndDisableNormalWinConditions()
    {
        var conVar = ConVar.Find("mp_ignore_round_win_conditions");
        if (conVar is null)
            return;

        _previousIgnoreRoundWinConditions ??= conVar.GetPrimitiveValue<bool>();
        conVar.SetValue(true);
    }

    private void RestoreWinConditions()
    {
        if (_previousIgnoreRoundWinConditions is not { } previous)
            return;

        ConVar.Find("mp_ignore_round_win_conditions")?.SetValue(previous);
        _previousIgnoreRoundWinConditions = null;
    }

    private void SafeCall(string operation, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Special days {operation} failed: {ex}");
        }
    }

    private static bool IsHuman(CCSPlayerController? player) =>
        player is { IsValid: true, IsBot: false };

    private static IEnumerable<CCSPlayerController> HumanPlayers() =>
        Utilities.GetPlayers().Where(IsHuman)!;

    private static void NotifyAll(
        string text,
        UiNotificationType type,
        float durationSeconds = 4.0f)
    {
        var ui = UiCapability.Api.GetOptional();
        if (ui is null)
            return;

        foreach (var player in HumanPlayers())
            ui.Notify(player, text, type, durationSeconds);
    }

    private static void AnnounceAll(
        string title,
        string subtitle,
        UiNotificationType type,
        float durationSeconds)
    {
        var ui = UiCapability.Api.GetOptional();
        if (ui is null)
            return;

        foreach (var player in HumanPlayers())
            ui.Announce(player, title, subtitle, type, durationSeconds);
    }

    private static void SetRoundStatusAll(string title, string value)
    {
        var ui = UiCapability.Api.GetOptional();
        if (ui is null)
            return;

        foreach (var player in HumanPlayers())
            ui.SetRoundStatus(player, title, value);
    }

    private static void ClearRoundStatusAll()
    {
        var ui = UiCapability.Api.GetOptional();
        if (ui is null)
            return;

        foreach (var player in HumanPlayers())
            ui.ClearRoundStatus(player);
    }
}
