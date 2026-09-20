using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace JBF.SpecialDays.BossFight;

public sealed class JBFBossFightDay : BasePlugin, ISpecialDay
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
    private CCSPlayerController? _boss;
    private Timer? _preparationTimer;
    private bool _combatStarted;

    public override string ModuleName => "JBF Special Day: Boss Fight";
    public override string ModuleVersion => "1.1.0";
    public override string ModuleAuthor => "Mell";

    public string Id => "boss-fight";
    public string Name => "Боссфайт";
    public int Order => 30;

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        var specialDays = SpecialDaysCapability.Api.GetOptional();
        if (specialDays is null)
        {
            Logger.LogWarning("Special days capability is unavailable; boss fight was not registered.");
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
        _combatStarted = false;
        var players = ActivePlayers().ToArray();
        if (players.Length < 2)
        {
            _context.Finish(RoundEndReason.RoundDraw);
            return;
        }

        _boss = players[Random.Shared.Next(players.Length)];
        SetRenderColor(_boss, Color.Red);
        SetHealthValue(_boss, 10000);
        Server.PrintToChatAll(JailbreakChat.Format($"Босс: {_boss.PlayerName}. Подготовка 30 секунд."));

        foreach (var player in players.Where(player => player != _boss))
        {
            OpenWeaponSelection(player);
        }

        _preparationTimer = AddTimer(30.0f, StartCombat, TimerFlags.STOP_ON_MAPCHANGE);
    }

    public void Stop()
    {
        _preparationTimer?.Kill();
        _preparationTimer = null;
        _combatStarted = false;
        _context = null;
        ConVar.Find("mp_teammates_are_enemies")?.SetValue(false);

        if (IsUsable(_boss) && _boss.PawnIsAlive)
        {
            SetRenderColor(_boss, Color.White);
        }

        _boss = null;
    }

    public void OnPlayerSpawn(CCSPlayerController player)
    {
        if (player == _boss)
        {
            Server.NextFrame(() =>
            {
                SetRenderColor(player, Color.Red);
                SetHealthValue(player, 10000);
            });
        }
    }

    public void OnPlayerDeath(CCSPlayerController? victim, CCSPlayerController? attacker)
    {
        Server.NextFrame(EvaluateWinner);
    }

    private void EvaluateWinner()
    {
        if (_context is null || _boss is null) return;

        var bossAlive = IsUsable(_boss) && _boss.PawnIsAlive;
        var othersAlive = ActivePlayers().Any(player => player.Slot != _boss.Slot);

        if (!bossAlive)
        {
            _context.Finish(_boss.Team == CsTeam.Terrorist
                ? RoundEndReason.CTsWin
                : RoundEndReason.TerroristsWin);
            return;
        }

        if (!othersAlive)
        {
            _context.Finish(_boss.Team == CsTeam.Terrorist
                ? RoundEndReason.TerroristsWin
                : RoundEndReason.CTsWin);
        }
    }

    public HookResult OnTakeDamage(CBaseEntity entity, CTakeDamageInfo damageInfo)
    {
        if (entity.DesignerName != "player")
        {
            return HookResult.Continue;
        }

        var victim = entity.As<CCSPlayerPawn>().Controller.Value?.As<CCSPlayerController>();
        var attackerEntity = damageInfo.Attacker.Value;
        if (!IsUsable(victim) || attackerEntity?.DesignerName != "player")
        {
            return HookResult.Continue;
        }

        var attacker = attackerEntity.As<CCSPlayerPawn>().Controller.Value?.As<CCSPlayerController>();
        if (!IsUsable(attacker))
        {
            return HookResult.Continue;
        }

        return _combatStarted && (victim == _boss || attacker == _boss)
            ? HookResult.Continue
            : HookResult.Handled;
    }

    private void StartCombat()
    {
        _combatStarted = true;
        ConVar.Find("mp_teammates_are_enemies")?.SetValue(true);
        Server.PrintToChatAll(JailbreakChat.Format("Огонь по боссу разрешён!"));
    }

    private static void OpenWeaponSelection(CCSPlayerController player)
    {
        var menuApi = MenuCapability.Api.GetOptional();
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

    private static void SetRenderColor(CCSPlayerController player, Color color)
    {
        if (!IsUsable(player) || !player.PawnIsAlive)
        {
            return;
        }

        player.PlayerPawn.Value!.Render = color;
        Utilities.SetStateChanged(player.PlayerPawn.Value, "CBaseModelEntity", "m_clrRender");
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
