using System.Diagnostics.CodeAnalysis;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace JBF.SpecialDays.ArmRace;

public sealed class JBFArmRaceDay : BasePlugin, ISpecialDay
{
    private static readonly ArmRaceLevel[] Levels =
    [
        new(0, [CsItem.G3SG1, CsItem.SCAR20]),
        new(2, [CsItem.AWP]),
        new(4, [CsItem.AK47, CsItem.AUG, CsItem.Famas, CsItem.Galil, CsItem.M4A4, CsItem.M4A1, CsItem.SG553]),
        new(6, [CsItem.Knife], false)
    ];

    private readonly Dictionary<int, int> _kills = [];
    private readonly Dictionary<int, DateTime> _spawnProtectionUntil = [];
    private IDisposable? _registration;
    private ISpecialDayContext? _context;
    private Timer? _respawnTimer;

    public override string ModuleName => "JBF Special Day: Arm Race";
    public override string ModuleVersion => "1.2.0";
    public override string ModuleAuthor => "Mell";

    public string Id => "arm-race";
    public string Name => "Гонка вооружений";
    public int Order => 40;

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        var specialDays = SpecialDaysCapability.Api.Get();
        if (specialDays is null)
        {
            Logger.LogWarning("Special days capability is unavailable; arm race was not registered.");
            return;
        }

        _registration = specialDays.RegisterDay(this);
    }

    public override void Unload(bool hotReload)
    {
        _registration?.Dispose();
        Stop();
    }

    public void Start(ISpecialDayContext context)
    {
        _context = context;
        _kills.Clear();
        foreach (var player in ActivePlayers())
        {
            _kills[player.Slot] = 0;
            GiveLevelWeapon(player);
        }

        _respawnTimer = AddTimer(3.0f, RespawnDeadPlayers,
            TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    public void Stop()
    {
        _respawnTimer?.Kill();
        _respawnTimer = null;
        _kills.Clear();
        _spawnProtectionUntil.Clear();

        foreach (var player in Utilities.GetPlayers().Where(IsUsable))
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn is null) continue;
            pawn.TakesDamage = true;
            if (pawn.WeaponServices is not null)
                pawn.WeaponServices.PreventWeaponPickup = false;
        }

        _context = null;
    }

    public void OnPlayerSpawn(CCSPlayerController player)
    {
        _kills.TryAdd(player.Slot, 0);
        Server.NextFrame(() =>
        {
            GiveLevelWeapon(player);
            ApplySpawnProtection(player);
        });
    }

    public void OnPlayerDeath(CCSPlayerController? victim, CCSPlayerController? attacker)
    {
        if (IsUsable(victim))
        {
            _spawnProtectionUntil.Remove(victim.Slot);
            var dropped = victim.PlayerPawn.Value?.WeaponServices?.MyWeapons
                .Select(handle => handle.Value)
                .Where(weapon => weapon is { IsValid: true })
                .ToArray() ?? [];

            Server.NextFrame(() =>
            {
                foreach (var weapon in dropped)
                {
                    try
                    {
                        if (weapon.IsValid) weapon.Remove();
                    }
                    catch { }
                }
            });
        }

        if (!IsUsable(attacker) || attacker == victim ||
            attacker.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
        {
            return;
        }

        var kills = _kills.GetValueOrDefault(attacker.Slot) + 1;
        _kills[attacker.Slot] = kills;
        if (kills > Levels[^1].RequiredKills)
        {
            _context?.Finish(attacker.Team == CsTeam.Terrorist
                ? RoundEndReason.TerroristsWin
                : RoundEndReason.CTsWin);
            return;
        }

        GiveLevelWeapon(attacker);
    }

    public HookResult OnTakeDamage(CBaseEntity entity, CTakeDamageInfo damageInfo)
    {
        if (_context is null || entity.DesignerName != "player")
            return HookResult.Continue;

        var victim = entity.As<CCSPlayerPawn>().Controller.Value?.As<CCSPlayerController>();
        if (!IsUsable(victim)) return HookResult.Continue;

        if (_spawnProtectionUntil.TryGetValue(victim.Slot, out var until))
        {
            if (DateTime.UtcNow < until)
                return HookResult.Handled;

            _spawnProtectionUntil.Remove(victim.Slot);
            if (victim.PlayerPawn.Value is { IsValid: true } pawn)
                pawn.TakesDamage = true;
        }

        return HookResult.Continue;
    }

    private void GiveLevelWeapon(CCSPlayerController player)
    {
        if (!IsUsable(player) || !player.PawnIsAlive ||
            player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
        {
            return;
        }

        var kills = _kills.GetValueOrDefault(player.Slot);
        var level = Levels.Last(candidate => candidate.RequiredKills <= kills);
        var pawn = player.PlayerPawn.Value!;

        // PreventWeaponPickup also blocks GiveNamedItem on CS2. Temporarily allow pickup while
        // constructing the player's loadout, then lock pickups again on the next frame.
        if (pawn.WeaponServices is not null)
            pawn.WeaponServices.PreventWeaponPickup = false;

        player.RemoveWeapons();
        player.GiveNamedItem(CsItem.Knife);

        if (level.GiveZeus)
            player.GiveNamedItem(CsItem.Zeus);

        var weapon = level.Weapons[Random.Shared.Next(level.Weapons.Count)];
        if (weapon != CsItem.Knife)
            player.GiveNamedItem(weapon);

        Server.NextFrame(() =>
        {
            if (_context is null || !IsUsable(player) || !player.PawnIsAlive) return;
            var currentPawn = player.PlayerPawn.Value;
            if (currentPawn?.WeaponServices is not null)
                currentPawn.WeaponServices.PreventWeaponPickup = true;
        });
    }

    private void ApplySpawnProtection(CCSPlayerController player)
    {
        if (_context is null || !IsUsable(player) || !player.PawnIsAlive) return;

        var pawn = player.PlayerPawn.Value!;
        pawn.TakesDamage = false;
        _spawnProtectionUntil[player.Slot] = DateTime.UtcNow.AddSeconds(3);

        AddTimer(3.05f, () =>
        {
            if (_context is null || !IsUsable(player) || !player.PawnIsAlive) return;
            _spawnProtectionUntil.Remove(player.Slot);
            player.PlayerPawn.Value!.TakesDamage = true;
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private static IEnumerable<CCSPlayerController> ActivePlayers()
    {
        return Utilities.GetPlayers().Where(player =>
            IsUsable(player) && player.PawnIsAlive &&
            player.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist);
    }

    private static void RespawnDeadPlayers()
    {
        foreach (var player in Utilities.GetPlayers().Where(player =>
                     IsUsable(player) && !player.PawnIsAlive &&
                     player.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist))
        {
            player.Respawn();
        }
    }

    private static bool IsUsable([NotNullWhen(true)] CCSPlayerController? player)
    {
        return player is { IsValid: true, PlayerPawn.IsValid: true } &&
               player.PlayerPawn.Value?.IsValid == true;
    }
}
