using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public sealed record CommanderMenuItem(
    string Id,
    string Text,
    Action<CCSPlayerController> OnSelect,
    int Order = 0);
