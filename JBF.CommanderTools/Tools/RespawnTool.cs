using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using JBF.Api;
using JBF.CommanderTools.Services;

namespace JBF.CommanderTools.Tools;

internal sealed class RespawnTool : ICommanderTool
{
    private readonly PlayerSelectionService _playerSelection;

    public RespawnTool(PlayerSelectionService playerSelection)
    {
        _playerSelection = playerSelection;
    }

    public string Id => "respawn";
    public string Text => "Возродить игрока";
    public int Order => 70;

    public void Execute(CCSPlayerController commander)
    {
        _playerSelection.Open(
            commander,
            "Кого возродить?",
            Utilities.GetPlayers().Where(player => player.IsValid && !player.PawnIsAlive),
            player =>
            {
                player.Respawn();
                commander.PrintToChat(JailbreakChat.Format($"{player.PlayerName} возрождён."));
            });
    }
}
