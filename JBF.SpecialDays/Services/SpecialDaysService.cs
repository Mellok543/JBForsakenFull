using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using JBF.Api;

namespace JBF.SpecialDays.Services;

internal sealed class SpecialDaysService : ISpecialDaysApi, ISpecialDayContext
{
    private readonly Dictionary<string, RegisteredSpecialDay> _days = new(StringComparer.OrdinalIgnoreCase);
    private ISpecialDay? _activeDay;
    private ISpecialDay? _pendingDay;
    private bool _finishRequested;
    private bool? _previousIgnoreRoundWinConditions;

    public bool IsActive => _activeDay is not null;
    public bool HasPending => _pendingDay is not null;
    public string? ActiveDayName => _activeDay?.Name;
    public string? PendingDayName => _pendingDay?.Name;

    public IDisposable RegisterDay(ISpecialDay day)
    {
        ArgumentNullException.ThrowIfNull(day);
        if (string.IsNullOrWhiteSpace(day.Id)) throw new ArgumentException("Special day id cannot be empty.", nameof(day));
        if (string.IsNullOrWhiteSpace(day.Name)) throw new ArgumentException("Special day name cannot be empty.", nameof(day));

        var token = Guid.NewGuid();
        _days[day.Id] = new RegisteredSpecialDay(token, day);
        return new ActionDisposable(() => UnregisterDay(day.Id, token));
    }

    public bool TryStop()
    {
        if (_activeDay is not null)
        {
            StopActiveDay();
            return true;
        }

        if (_pendingDay is null) return false;

        var name = _pendingDay.Name;
        _pendingDay = null;
        Server.PrintToChatAll(JailbreakChat.Format($"Игровой день отменён: {name}."));
        NotifyAll($"Игровой день отменён: {name}", UiNotificationType.Warning);
        ClearRoundStatusAll();
        return true;
    }

    public void OpenSelectionMenu(CCSPlayerController commander)
    {
        if (WardenCapability.Api.Get()?.IsWarden(commander) != true)
        {
            commander.PrintToChat(JailbreakChat.Format("Игровой день может назначить только командир."));
            return;
        }

        if (IsActive || HasPending)
        {
            commander.PrintToChat(JailbreakChat.Format("Игровой день уже назначен или запущен."));
            return;
        }

        if (_days.Count == 0)
        {
            commander.PrintToChat(JailbreakChat.Format("Нет доступных игровых дней."));
            return;
        }

        var menuApi = MenuCapability.Api.Get();
        if (menuApi is null)
        {
            commander.PrintToChat(JailbreakChat.Format("Menu API недоступно."));
            return;
        }

        var options = _days.Values
            .Select(registration => registration.Day)
            .OrderBy(day => day.Order)
            .ThenBy(day => day.Name, StringComparer.OrdinalIgnoreCase)
            .Select(day => new JailbreakMenuOption(day.Name, player => Schedule(player, day)))
            .ToArray();

        menuApi.Open(commander, "Игровые дни", options);
    }

    public void StartPendingDay()
    {
        if (_pendingDay is null) return;

        _activeDay = _pendingDay;
        _pendingDay = null;
        _finishRequested = false;

        var conVar = ConVar.Find("mp_ignore_round_win_conditions");
        if (conVar is not null)
        {
            _previousIgnoreRoundWinConditions ??= conVar.GetPrimitiveValue<bool>();
            conVar.SetValue(true);
        }

        var name = _activeDay.Name;
        Server.PrintToChatAll(JailbreakChat.Format($"Начался игровой день: {name}."));
        AnnounceAll("ИГРОВОЙ ДЕНЬ", name, UiNotificationType.Important, 6.0f);
        SetRoundStatusAll("ИГРОВОЙ ДЕНЬ", name);
        _activeDay.Start(this);
    }

    public void StopActiveDay()
    {
        if (_activeDay is null) return;

        var name = _activeDay.Name;
        _activeDay.Stop();
        _activeDay = null;
        _finishRequested = false;
        RestoreWinConditionConVar();
        Server.PrintToChatAll(JailbreakChat.Format($"Игровой день завершён: {name}."));
        AnnounceAll("ИГРОВОЙ ДЕНЬ ЗАВЕРШЁН", name, UiNotificationType.Info, 4.0f);
        ClearRoundStatusAll();
    }

    public void HandlePlayerSpawn(CCSPlayerController? player)
    {
        if (player is null) return;

        _activeDay?.OnPlayerSpawn(player);
        if (_activeDay is not null && player.IsValid && !player.IsBot)
            UiCapability.Api.Get()?.SetRoundStatus(player, "ИГРОВОЙ ДЕНЬ", _activeDay.Name);
    }

    public void HandlePlayerDeath(CCSPlayerController? victim, CCSPlayerController? attacker)
    {
        _activeDay?.OnPlayerDeath(victim, attacker);
    }

    public HookResult HandleTakeDamage(CBaseEntity entity, CTakeDamageInfo damageInfo)
    {
        return _activeDay?.OnTakeDamage(entity, damageInfo) ?? HookResult.Continue;
    }

    public void Shutdown()
    {
        _pendingDay = null;
        StopActiveDay();
        _days.Clear();
        RestoreWinConditionConVar();
        ClearRoundStatusAll();
    }

    public void Finish(RoundEndReason reason) => FinishActiveDay(reason);

    private void Schedule(CCSPlayerController commander, ISpecialDay day)
    {
        if (IsActive || HasPending)
        {
            commander.PrintToChat(JailbreakChat.Format("Игровой день уже назначен или запущен."));
            return;
        }

        _pendingDay = day;
        Server.PrintToChatAll(JailbreakChat.Format($"В следующем раунде будет игровой день: {day.Name}."));
        NotifyAll($"Следующий раунд: {day.Name}", UiNotificationType.Important, 5.0f);
        SetRoundStatusAll("СЛЕДУЮЩИЙ РАУНД", day.Name);
    }

    private void UnregisterDay(string id, Guid token)
    {
        if (!_days.TryGetValue(id, out var registration) || registration.Token != token) return;

        _days.Remove(id);
        if (ReferenceEquals(_pendingDay, registration.Day))
        {
            _pendingDay = null;
            ClearRoundStatusAll();
        }

        if (!ReferenceEquals(_activeDay, registration.Day)) return;

        registration.Day.Stop();
        _activeDay = null;
        _finishRequested = false;
        RestoreWinConditionConVar();
        Server.PrintToChatAll(JailbreakChat.Format($"Игровой день выгружен: {registration.Day.Name}."));
        ClearRoundStatusAll();
    }

    private void RestoreWinConditionConVar()
    {
        if (_previousIgnoreRoundWinConditions is not { } previous) return;

        ConVar.Find("mp_ignore_round_win_conditions")?.SetValue(previous);
        _previousIgnoreRoundWinConditions = null;
    }

    private void FinishActiveDay(RoundEndReason reason)
    {
        if (_activeDay is null || _finishRequested) return;

        _finishRequested = true;
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
            .FirstOrDefault()?.GameRules;
        gameRules?.TerminateRound(5.0f, reason);
    }

    private static IEnumerable<CCSPlayerController> HumanPlayers() =>
        Utilities.GetPlayers().Where(player => player is { IsValid: true, IsBot: false });

    private static void NotifyAll(string text, UiNotificationType type, float durationSeconds = 4.0f)
    {
        var ui = UiCapability.Api.Get();
        if (ui is null) return;
        foreach (var player in HumanPlayers()) ui.Notify(player, text, type, durationSeconds);
    }

    private static void AnnounceAll(string title, string subtitle, UiNotificationType type, float durationSeconds)
    {
        var ui = UiCapability.Api.Get();
        if (ui is null) return;
        foreach (var player in HumanPlayers()) ui.Announce(player, title, subtitle, type, durationSeconds);
    }

    private static void SetRoundStatusAll(string title, string value)
    {
        var ui = UiCapability.Api.Get();
        if (ui is null) return;
        foreach (var player in HumanPlayers()) ui.SetRoundStatus(player, title, value);
    }

    private static void ClearRoundStatusAll()
    {
        var ui = UiCapability.Api.Get();
        if (ui is null) return;
        foreach (var player in HumanPlayers()) ui.ClearRoundStatus(player);
    }
}
