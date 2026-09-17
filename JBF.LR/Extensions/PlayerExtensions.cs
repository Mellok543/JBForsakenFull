using System.Diagnostics.CodeAnalysis;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;

namespace JBF.LR.Extensions;

internal static class PlayerExtensions
{
    public static bool IsUsable([NotNullWhen(true)] this CCSPlayerController? player)
    {
        return player is { IsValid: true, PlayerPawn.IsValid: true } &&
               player.PlayerPawn.Value?.IsValid == true;
    }

    public static void ResetForLr(this CCSPlayerController player)
    {
        if (!player.IsUsable() || !player.PawnIsAlive)
        {
            return;
        }

        player.RemoveWeapons();
        player.GiveNamedItem(CsItem.Knife);
        player.SetHealthValue(100);
        player.SetArmorValue(100);
        player.PlayerPawn.Value!.TakesDamage = true;
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

    public static void SetAmmo(this CCSPlayerController player, int clip, int reserve)
    {
        if (!player.IsUsable() || !player.PawnIsAlive)
        {
            return;
        }

        var weapons = player.PlayerPawn.Value!.WeaponServices?.MyWeapons;
        if (weapons is null)
        {
            return;
        }

        foreach (var weaponHandle in weapons)
        {
            var weapon = weaponHandle.Value;
            if (weapon is null || !weapon.IsValid)
            {
                continue;
            }

            if (clip >= 0)
            {
                weapon.Clip1 = clip;
                Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iClip1");
            }

            if (reserve >= 0)
            {
                weapon.ReserveAmmo[0] = reserve;
                Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_pReserveAmmo");
            }
        }
    }
}
