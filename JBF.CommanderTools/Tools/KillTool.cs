using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using JBF.CommanderTools.Extensions;
using JBF.CommanderTools.Services;

namespace JBF.CommanderTools.Tools;

internal sealed class KillTool : ICommanderTool
{
    private readonly PlayerSelectionService _playerSelection;

    public KillTool(PlayerSelectionService playerSelection)
    {
        _playerSelection = playerSelection;
    }

    public string Id => "kill";
    public string Text => "Убить заключённого";
    public int Order => 60;

    public void Execute(CCSPlayerController commander)
    {
        _playerSelection.Open(
            commander,
            "Кого убить?",
            Utilities.GetPlayers().Where(player =>
                player.IsUsable() && player.PawnIsAlive && player.Team == CsTeam.Terrorist),
            player =>
            {
                player.CommitSuicide(true, true);
                commander.PrintToChat(JailbreakChat.Format($"{player.PlayerName} убит."));
            });
    }
}
