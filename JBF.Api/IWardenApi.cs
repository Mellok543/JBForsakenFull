using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public interface IWardenApi
{
    CCSPlayerController? Warden { get; }

    event Action<CCSPlayerController>? WardenClaimed;

    bool IsWarden(CCSPlayerController player);

    bool TryResign(CCSPlayerController player);
}
