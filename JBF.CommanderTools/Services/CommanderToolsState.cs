using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.CommanderTools.Extensions;

namespace JBF.CommanderTools.Services;

internal sealed class CommanderToolsState
{
    public bool FriendlyFireEnabled { get; set; }

    public bool NoBlockEnabled { get; set; }

    public bool BhopEnabled { get; set; }

    public bool ColorDivisionEnabled { get; set; }

    public bool FreeDayEnabled { get; set; }

    public void ResetRound()
    {
        FriendlyFireEnabled = false;
        NoBlockEnabled = false;
        BhopEnabled = false;
        ColorDivisionEnabled = false;
        FreeDayEnabled = false;

        ConVar.Find("mp_teammates_are_enemies")?.SetValue(false);
        ConVar.Find("mp_solid_teammates")?.SetValue(1);

        foreach (var player in Utilities.GetPlayers()
                     .Where(player => player.IsValid && player.PawnIsAlive))
        {
            player.SetRenderColor(Color.White);
        }
    }
}
