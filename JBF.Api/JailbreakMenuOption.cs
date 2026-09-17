using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public sealed record JailbreakMenuOption(
    string Text,
    Action<CCSPlayerController> OnSelect,
    bool IsDisabled = false,
    string? DisabledReason = null);
