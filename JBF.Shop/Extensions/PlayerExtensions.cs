using System.Diagnostics.CodeAnalysis;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace JBF.Shop.Extensions;

internal static class PlayerExtensions
{
    public static bool IsUsable([NotNullWhen(true)] this CCSPlayerController? player)
    {
        return player is { IsValid: true, PlayerPawn.IsValid: true } &&
               player.PlayerPawn.Value?.IsValid == true;
    }

    public static ulong? GetSteamId64(this CCSPlayerController player)
    {
        return player.AuthorizedSteamID?.SteamId64;
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

    public static void SetArmorValue(this CCSPlayerController player, int armor)
    {
        if (!player.IsUsable() || !player.PawnIsAlive)
        {
            return;
        }

        player.PlayerPawn.Value!.ArmorValue = armor;
        Utilities.SetStateChanged(player.PlayerPawn.Value, "CCSPlayerPawnBase", "m_ArmorValue");
    }
}
