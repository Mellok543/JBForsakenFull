using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace JBF.CommanderTools.Extensions;

internal static class PlayerExtensions
{
    public static bool IsUsable([NotNullWhen(true)] this CCSPlayerController? player)
    {
        return player is { IsValid: true, PlayerPawn.IsValid: true } &&
               player.PlayerPawn.Value?.IsValid == true;
    }

    public static void SetHealthValue(this CCSPlayerController player, int health)
    {
        if (!player.IsUsable() || !player.PawnIsAlive)
        {
            return;
        }

        player.PlayerPawn.Value!.Health = health;
        Utilities.SetStateChanged(player.PlayerPawn.Value, "CBaseEntity", "m_iHealth");
    }

    public static void SetRenderColor(this CCSPlayerController player, Color color)
    {
        if (!player.IsUsable() || !player.PawnIsAlive)
        {
            return;
        }

        player.PlayerPawn.Value!.Render = color;
        Utilities.SetStateChanged(player.PlayerPawn.Value, "CBaseModelEntity", "m_clrRender");
    }
}
