using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public interface IWardenApi
{
    CCSPlayerController? Warden { get; }

    event Action<CCSPlayerController>? WardenClaimed;
    event Action<WardenKilledEvent>? WardenKilled;

    bool IsWarden(CCSPlayerController player);

    bool TryResign(CCSPlayerController player);
}
