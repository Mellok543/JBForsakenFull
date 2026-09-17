using System.Diagnostics.CodeAnalysis;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace JBF.SpecialDays.HungerGames;

public sealed class JBFHungerGamesDay : BasePlugin, ISpecialDay
{
    private static readonly (string Name, CsItem Item)[] Weapons =
    [
        ("AK-47", CsItem.AK47),
        ("M4A4", CsItem.M4A4),
        ("M4A1-S", CsItem.M4A1),
        ("AWP", CsItem.AWP),
        ("Negev", CsItem.Negev),
        ("Nova", CsItem.Nova)
    ];

    private IDisposable? _registration;
    private ISpecialDayContext? _context;
    private Timer? _preparationTimer;
    private bool _preparing;

    public override string ModuleName => "JBF Special Day: Hunger Games";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Mell";

    public string Id => "hunger-games";
    public string Name => "Голодные игры";
    public int Order => 20;

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        var specialDays = SpecialDaysCapability.Api.Get();
        if (specialDays is null)
        {
            Logger.LogWarning("Special days capability is unavailable; hunger games was not registered.");
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
        _preparing = true;
        foreach (var player in ActivePlayers())
        {
            PreparePlayer(player);
        }

        Server.PrintToChatAll(JailbreakChat.Format("У вас 30 секунд на выбор оружия."));
        _preparationTimer = AddTimer(30.0f, StartCombat, TimerFlags.STOP_ON_MAPCHANGE);
    }

    public void Stop()
    {
        _preparing = false;
        _preparationTimer?.Kill();
        _preparationTimer = null;
        _context = null;
        ConVar.Find("mp_teammates_are_enemies")?.SetValue(false);

        foreach (var player in ActivePlayers())
        {
            player.PlayerPawn.Value!.TakesDamage = true;
        }
    }

    public void OnPlayerSpawn(CCSPlayerController player)
    {
        Server.NextFrame(() => PreparePlayer(player));
    }

    public void OnPlayerDeath(CCSPlayerController? victim, CCSPlayerController? attacker)
    {
        if (IsUsable(attacker) && attacker.PawnIsAlive && attacker != victim)
        {
            SetHealthValue(attacker, attacker.PlayerPawn.Value!.Health + 100);
        }

        var survivors = ActivePlayers().ToArray();
        if (survivors.Length <= 1)
        {
            var reason = survivors.FirstOrDefault()?.Team == CsTeam.Terrorist
                ? RoundEndReason.TerroristsWin
                : RoundEndReason.CTsWin;
            _context?.Finish(reason);
        }
    }

    public HookResult OnTakeDamage(CBaseEntity entity, CTakeDamageInfo damageInfo)
    {
        return HookResult.Continue;
    }

    private void PreparePlayer(CCSPlayerController player)
    {
        if (!IsUsable(player) || !player.PawnIsAlive ||
            player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
        {
            return;
        }

        player.RemoveWeapons();
        player.GiveNamedItem(CsItem.Knife);
        player.PlayerPawn.Value!.TakesDamage = !_preparing;
        if (_preparing)
        {
            OpenWeaponSelection(player);
        }
    }

    private void StartCombat()
    {
        _preparing = false;
        ConVar.Find("mp_teammates_are_enemies")?.SetValue(true);
        foreach (var player in ActivePlayers())
        {
            player.PlayerPawn.Value!.TakesDamage = true;
            player.PrintToChat(JailbreakChat.Format("Огонь разрешён!"));
        }
    }

    private static void OpenWeaponSelection(CCSPlayerController player)
    {
        var menuApi = MenuCapability.Api.Get();
        if (menuApi is null)
        {
            player.PrintToChat(JailbreakChat.Format("Menu API недоступно."));
            return;
        }

        var options = Weapons.Select(weapon =>
            new JailbreakMenuOption(weapon.Name, controller => controller.GiveNamedItem(weapon.Item))).ToArray();
        menuApi.Open(player, "Выберите оружие", options);
    }

    private static IEnumerable<CCSPlayerController> ActivePlayers()
    {
        return Utilities.GetPlayers().Where(player =>
            IsUsable(player) && player.PawnIsAlive &&
            player.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist);
    }

    private static bool IsUsable([NotNullWhen(true)] CCSPlayerController? player)
    {
        return player is { IsValid: true, PlayerPawn.IsValid: true } &&
               player.PlayerPawn.Value?.IsValid == true;
    }

    private static void SetHealthValue(CCSPlayerController player, int health)
    {
        if (!IsUsable(player) || !player.PawnIsAlive)
        {
            return;
        }

        player.PlayerPawn.Value!.Health = health;
        Utilities.SetStateChanged(player.PlayerPawn.Value, "CBaseEntity", "m_iHealth");
    }
}
