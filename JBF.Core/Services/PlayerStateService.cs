using CounterStrikeSharp.API.Core;
using JBF.Api;

namespace JBF.Core.Services;

internal sealed class PlayerStateService : IPlayerStateApi
{
    private const int RebelDamageThreshold = 20;

    private readonly HashSet<ulong> _rebels = [];
    private readonly HashSet<ulong> _freeDays = [];
    private readonly HashSet<ulong> _freeDayGrantedThisRound = [];
    private readonly Dictionary<ulong, int> _ctDamage = [];

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

    public bool AddDamageToCt(CCSPlayerController player, int damage)
    {
        var steamId = GetSteamId(player);
        if (steamId is null || damage <= 0 || _rebels.Contains(steamId.Value))
            return false;

        var current = _ctDamage.GetValueOrDefault(steamId.Value);
        var total = current > int.MaxValue - damage ? int.MaxValue : current + damage;
        _ctDamage[steamId.Value] = total;

        // Rebel only after dealing MORE than 20 total HP damage to CT in the current round.
        if (total <= RebelDamageThreshold)
            return false;

        return MarkRebel(player);
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
            var changed = _freeDays.Add(steamId.Value);
            if (_freeDayGrantedThisRound.Add(steamId.Value))
                FreeDayGranted?.Invoke(player);
            return changed;
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
        _ctDamage.Remove(steamId.Value);
    }

    public void RemovePlayer(CCSPlayerController player)
    {
        var steamId = GetSteamId(player);
        if (steamId is null)
            return;

        _rebels.Remove(steamId.Value);
        _freeDays.Remove(steamId.Value);
        _freeDayGrantedThisRound.Remove(steamId.Value);
        _ctDamage.Remove(steamId.Value);
    }

    public void ResetRound()
    {
        _rebels.Clear();
        _freeDays.Clear();
        _freeDayGrantedThisRound.Clear();
        _ctDamage.Clear();
    }

    private static ulong? GetSteamId(CCSPlayerController player)
    {
        return player.IsValid ? player.AuthorizedSteamID?.SteamId64 : null;
    }
}
