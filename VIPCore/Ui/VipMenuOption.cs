using CounterStrikeSharp.API.Core;

namespace VIPCore.Ui;

internal sealed record VipMenuOption(
    string Text,
    Action<CCSPlayerController> OnSelect,
    bool IsDisabled = false,
    string? DisabledReason = null);
