using CounterStrikeSharp.API.Core;
using JBF.Api;

namespace JBF.Core.Services;

internal sealed class PlayerStateService : IPlayerStateApi
{
    private readonly HashSet<ulong> _rebels = [];
    private readonly HashSet<ulong> _freeDays = [];

    public event Action<CCSPlayerController>? RebelStarted;
    public event Action<RebelKilledEvent>? RebelKilled;
    public event Action<CCSPlayerController>? FreeDayGranted;

    public bool IsRebel(CCSPlayerController player)
    {
        var steamId = GetSteamId(player);
        return steamId is not null && _rebels.Contains(steamId.Value);
    }

    public bool HasFreeDay(CCSPlayerController player)
    {
        var steamId = GetSteamId(player);
        return steamId is not null && _freeDays.Contains(steamId.Value);
    }

    public bool MarkRebel(CCSPlayerController player)
    {
        var steamId = GetSteamId(player);
        if (steamId is null || !_rebels.Add(steamId.Value))
            return false;

        RebelStarted?.Invoke(player);
        return true;
    }

    public bool SetFreeDay(CCSPlayerController player, bool enabled)
    {
        var steamId = GetSteamId(player);
        if (steamId is null)
            return false;

        if (enabled)
        {
            if (!_freeDays.Add(steamId.Value))
                return false;

            FreeDayGranted?.Invoke(player);
            return true;
        }

        return _freeDays.Remove(steamId.Value);
    }

    public void NotifyDeath(CCSPlayerController victim, CCSPlayerController? killer)
    {
        var steamId = GetSteamId(victim);
        if (steamId is null)
            return;

        if (_rebels.Remove(steamId.Value))
            RebelKilled?.Invoke(new RebelKilledEvent(victim, killer));

        _freeDays.Remove(steamId.Value);
    }

    public void RemovePlayer(CCSPlayerController player)
    {
        var steamId = GetSteamId(player);
        if (steamId is null)
            return;

        _rebels.Remove(steamId.Value);
        _freeDays.Remove(steamId.Value);
    }

    public void ResetRound()
    {
        _rebels.Clear();
        _freeDays.Clear();
    }

    private static ulong? GetSteamId(CCSPlayerController player)
    {
        return player.IsValid ? player.AuthorizedSteamID?.SteamId64 : null;
    }
}
