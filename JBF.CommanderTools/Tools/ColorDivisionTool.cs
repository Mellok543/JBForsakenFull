using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using JBF.CommanderTools.Extensions;
using JBF.CommanderTools.Services;

namespace JBF.CommanderTools.Tools;

internal sealed class ColorDivisionTool : ICommanderTool
{
    private readonly CommanderToolsState _state;

    public ColorDivisionTool(CommanderToolsState state)
    {
        _state = state;
    }

    public string Id => "color-division";
    public string Text => "Разделение по цветам";
    public int Order => 80;

    public void Execute(CCSPlayerController commander)
    {
        _state.ColorDivisionEnabled = !_state.ColorDivisionEnabled;
        var inmates = Utilities.GetPlayers()
            .Where(player => player.IsUsable() && player.PawnIsAlive && player.Team == CsTeam.Terrorist)
            .ToArray();

        for (var index = 0; index < inmates.Length; index++)
        {
            var color = !_state.ColorDivisionEnabled
                ? Color.White
                : index % 2 == 0 ? Color.Red : Color.Blue;
            inmates[index].SetRenderColor(color);
        }

        commander.PrintToChat(JailbreakChat.Format($"Разделение по цветам: {(_state.ColorDivisionEnabled ? "включено" : "выключено")}."));
        CommanderMenuCapability.Api.Get()?.Open(commander);
    }
}
