using CounterStrikeSharp.API.Core;
using JBF.Api;

namespace JBF.Menu.Services;

internal sealed class ActiveMenuState
{
    public required CCSPlayerController Player { get; init; }

    public required string Title { get; init; }

    public required IReadOnlyList<JailbreakMenuOption> Options { get; init; }

    public int SelectedIndex { get; set; }
}
