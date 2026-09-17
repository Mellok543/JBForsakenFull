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

    public bool IsActive => _activeDay is not null;

    public bool HasPending => _pendingDay is not null;

    public string? ActiveDayName => _activeDay?.Name;

    public string? PendingDayName => _pendingDay?.Name;

    public IDisposable RegisterDay(ISpecialDay day)
    {
        ArgumentNullException.ThrowIfNull(day);

        if (string.IsNullOrWhiteSpace(day.Id))
        {
            throw new ArgumentException("Special day id cannot be empty.", nameof(day));
        }

        if (string.IsNullOrWhiteSpace(day.Name))
        {
            throw new ArgumentException("Special day name cannot be empty.", nameof(day));
        }

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

        if (_pendingDay is null)
        {
            return false;
        }

        var name = _pendingDay.Name;
        _pendingDay = null;
        Server.PrintToChatAll(JailbreakChat.Format($"Игровой день отменён: {name}."));
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
        if (_pendingDay is null)
        {
            return;
        }

        _activeDay = _pendingDay;
        _pendingDay = null;
        _finishRequested = false;
        ConVar.Find("mp_ignore_round_win_conditions")?.SetValue(true);
        Server.PrintToChatAll(JailbreakChat.Format($"Начался игровой день: {_activeDay.Name}."));
        _activeDay.Start(this);
    }

    public void StopActiveDay()
    {
        if (_activeDay is null)
        {
            return;
        }

        var name = _activeDay.Name;
        _activeDay.Stop();
        _activeDay = null;
        _finishRequested = false;
        ConVar.Find("mp_ignore_round_win_conditions")?.SetValue(false);
        Server.PrintToChatAll(JailbreakChat.Format($"Игровой день завершён: {name}."));
    }

    public void HandlePlayerSpawn(CCSPlayerController? player)
    {
        if (player is not null)
        {
            _activeDay?.OnPlayerSpawn(player);
        }
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
        ConVar.Find("mp_ignore_round_win_conditions")?.SetValue(false);
    }

    public void Finish(RoundEndReason reason)
    {
        FinishActiveDay(reason);
    }

    private void Schedule(CCSPlayerController commander, ISpecialDay day)
    {
        if (IsActive || HasPending)
        {
            commander.PrintToChat(JailbreakChat.Format("Игровой день уже назначен или запущен."));
            return;
        }

        _pendingDay = day;
        Server.PrintToChatAll(JailbreakChat.Format($"В следующем раунде будет игровой день: {day.Name}."));
    }

    private void UnregisterDay(string id, Guid token)
    {
        if (!_days.TryGetValue(id, out var registration) || registration.Token != token)
        {
            return;
        }

        _days.Remove(id);

        if (ReferenceEquals(_pendingDay, registration.Day))
        {
            _pendingDay = null;
        }

        if (!ReferenceEquals(_activeDay, registration.Day))
        {
            return;
        }

        registration.Day.Stop();
        _activeDay = null;
        _finishRequested = false;
        ConVar.Find("mp_ignore_round_win_conditions")?.SetValue(false);
        Server.PrintToChatAll(JailbreakChat.Format($"Игровой день выгружен: {registration.Day.Name}."));
    }

    private void FinishActiveDay(RoundEndReason reason)
    {
        if (_activeDay is null || _finishRequested)
        {
            return;
        }

        _finishRequested = true;
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
            .FirstOrDefault()?.GameRules;
        gameRules?.TerminateRound(5.0f, reason);
    }
}
