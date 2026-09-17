using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public sealed record WardenKilledEvent(
    CCSPlayerController Warden,
    CCSPlayerController? Attacker,
    string Weapon);
