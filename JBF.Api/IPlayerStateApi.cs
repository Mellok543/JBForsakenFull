using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public interface IPlayerStateApi
{
    event Action<CCSPlayerController>? RebelStarted;
    event Action<RebelKilledEvent>? RebelKilled;
    event Action<CCSPlayerController>? FreeDayGranted;

    bool IsRebel(CCSPlayerController player);
    bool HasFreeDay(CCSPlayerController player);

    bool MarkRebel(CCSPlayerController player);
    bool SetFreeDay(CCSPlayerController player, bool enabled);
    void ResetRound();
}

public readonly record struct RebelKilledEvent(
    CCSPlayerController Rebel,
    CCSPlayerController? Killer);
