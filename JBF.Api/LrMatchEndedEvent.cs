using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public sealed record LrMatchEndedEvent(
    string GameName,
    CCSPlayerController Inmate,
    CCSPlayerController Guardian,
    CCSPlayerController? Winner);
