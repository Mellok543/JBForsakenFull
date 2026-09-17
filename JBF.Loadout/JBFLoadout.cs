using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;

namespace JBF.Loadout;

public sealed class JBFLoadout : BasePlugin
{
    public override string ModuleName => "JBF Loadout";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Mell";

    [GameEventHandler]
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player is null || !player.IsValid)
            return HookResult.Continue;

        Server.NextFrame(() => ApplyLoadout(player));
        return HookResult.Continue;
    }

    private static void ApplyLoadout(CCSPlayerController player)
    {
        if (!player.IsValid || !player.PawnIsAlive || player.PlayerPawn.Value is not { IsValid: true } pawn)
            return;

        switch (player.Team)
        {
            case CsTeam.CounterTerrorist:
                player.RemoveItemBySlot(gear_slot_t.GEAR_SLOT_RIFLE);
                player.RemoveItemBySlot(gear_slot_t.GEAR_SLOT_PISTOL);
                EnsureKnife(player);
                player.GiveNamedItem("weapon_m4a1");
                player.GiveNamedItem("weapon_usp_silencer");

                pawn.ArmorValue = 100;
                Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_ArmorValue");

                var itemServices = pawn.ItemServices?.As<CCSPlayer_ItemServices>();
                if (itemServices is not null)
                    itemServices.HasHelmet = true;
                break;

            case CsTeam.Terrorist:
                player.RemoveItemBySlot(gear_slot_t.GEAR_SLOT_RIFLE);
                player.RemoveItemBySlot(gear_slot_t.GEAR_SLOT_PISTOL);
                EnsureKnife(player);
                break;
        }
    }

    private static void EnsureKnife(CCSPlayerController player)
    {
        var hasKnife = player.PlayerPawn.Value?.WeaponServices?.MyWeapons
            .Select(handle => handle.Value)
            .Any(weapon => weapon?.IsValid == true &&
                           weapon.As<CCSWeaponBase>().VData?.GearSlot == gear_slot_t.GEAR_SLOT_KNIFE) == true;

        if (!hasKnife)
            player.GiveNamedItem("weapon_knife");
    }
}
