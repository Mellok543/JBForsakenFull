using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using JBF.CommanderTools.Extensions;
using JBF.CommanderTools.Services;

namespace JBF.CommanderTools.Tools;

internal sealed class FreeDayTool : ICommanderTool
{
    private readonly CommanderToolsState _state;

    public FreeDayTool(CommanderToolsState state)
    {
        _state = state;
    }

    public string Id => "free-day";
    public string Text => "FreeDay";
    public int Order => 90;

    public void Execute(CCSPlayerController commander)
    {
        _state.FreeDayEnabled = !_state.FreeDayEnabled;

        foreach (var inmate in Utilities.GetPlayers().Where(player =>
                     player.IsUsable() && player.PawnIsAlive && player.Team == CsTeam.Terrorist))
        {
            inmate.SetRenderColor(_state.FreeDayEnabled ? Color.Green : Color.White);
        }

        commander.PrintToChat(JailbreakChat.Format($"FreeDay: {(_state.FreeDayEnabled ? "включён" : "выключен")}."));
        CommanderMenuCapability.Api.Get()?.Open(commander);
    }
}
