using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public interface IWardenApi
{
    CCSPlayerController? Warden { get; }

    bool IsWarden(CCSPlayerController player);

    bool TryResign(CCSPlayerController player);
}
