using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using JBF.CommanderTools.Extensions;
using JBF.CommanderTools.Services;

namespace JBF.CommanderTools.Tools;

internal sealed class HealTool : ICommanderTool
{
    private readonly PlayerSelectionService _playerSelection;

    public HealTool(PlayerSelectionService playerSelection)
    {
        _playerSelection = playerSelection;
    }

    public string Id => "heal";
    public string Text => "Вылечить игрока";
    public int Order => 50;

    public void Execute(CCSPlayerController commander)
    {
        _playerSelection.Open(
            commander,
            "Кого вылечить?",
            Utilities.GetPlayers().Where(player => player.IsUsable() && player.PawnIsAlive),
            player =>
            {
                player.SetHealthValue(player.Team == CsTeam.Terrorist ? 100 : 150);
                Server.PrintToChatAll(JailbreakChat.Format(
                    $"КМД {commander.PlayerName} вылечил игрока {player.PlayerName}."));
            });
    }
}
