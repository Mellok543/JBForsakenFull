using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;

namespace JBF.Api;

public static class LrPlayerRules
{
    public static void Normalize(CCSPlayerController player, int health = 100, int armor = 100)
    {
        if (!IsUsable(player)) return;
        player.RemoveWeapons();
        player.GiveNamedItem(CsItem.Knife);
        SetHealth(player, health);
        SetArmor(player, armor);
        player.PlayerPawn.Value!.TakesDamage = true;
        player.PlayerPawn.Value.GravityScale = 1.0f;
        player.PlayerPawn.Value.VelocityModifier = 1.0f;
        Utilities.SetStateChanged(player.PlayerPawn.Value, "CBaseEntity", "m_flGravityScale");
        Utilities.SetStateChanged(player.PlayerPawn.Value, "CCSPlayerPawnBase", "m_flVelocityModifier");
    }

    public static bool IsUsable(CCSPlayerController? player) =>
        player is { IsValid: true, PawnIsAlive: true, PlayerPawn.IsValid: true } &&
        player.PlayerPawn.Value is { IsValid: true };

    public static void SetHealth(CCSPlayerController player, int health)
    {
        if (!IsUsable(player)) return;
        player.PlayerPawn.Value!.Health = health;
        player.PlayerPawn.Value.MaxHealth = health;
        Utilities.SetStateChanged(player.PlayerPawn.Value, "CBaseEntity", "m_iHealth");
        Utilities.SetStateChanged(player.PlayerPawn.Value, "CBaseEntity", "m_iMaxHealth");
    }

    public static void SetArmor(CCSPlayerController player, int armor)
    {
        if (!IsUsable(player)) return;
        player.PlayerPawn.Value!.ArmorValue = armor;
        Utilities.SetStateChanged(player.PlayerPawn.Value, "CCSPlayerPawnBase", "m_ArmorValue");
    }

    public static void SetAmmo(CCSPlayerController player, int clip, int reserve)
    {
        if (!IsUsable(player)) return;
        var weapons = player.PlayerPawn.Value!.WeaponServices?.MyWeapons;
        if (weapons is null) return;

        foreach (var handle in weapons)
        {
            var weapon = handle.Value;
            if (weapon is null || !weapon.IsValid || (weapon.DesignerName?.Contains("knife", StringComparison.OrdinalIgnoreCase) ?? false)) continue;
            weapon.Clip1 = clip;
            weapon.ReserveAmmo[0] = reserve;
            Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iClip1");
            Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_pReserveAmmo");
        }
    }
}
