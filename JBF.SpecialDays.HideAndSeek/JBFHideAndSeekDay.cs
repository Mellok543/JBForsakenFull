using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace JBF.SpecialDays.HideAndSeek;

public sealed class JBFHideAndSeekDay : BasePlugin, ISpecialDay
{
    private static readonly string[] DoorEntityNames =
    [
        "func_door",
        "func_movelinear",
        "func_door_rotating",
        "prop_door_rotating"
    ];

    private IDisposable? _registration;
    private ISpecialDayContext? _context;
    private Timer? _freezeTimer;
    private Timer? _roundTimer;
    private bool _ctFrozen;

    public override string ModuleName => "JBF Special Day: Hide and Seek";
    public override string ModuleVersion => "1.2.0";
    public override string ModuleAuthor => "Mell";

    public string Id => "hide-and-seek";
    public string Name => "Прятки";
    public int Order => 10;

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        var specialDays = SpecialDaysCapability.Api.Get();
        if (specialDays is null)
        {
            Logger.LogWarning("Special days capability is unavailable; hide and seek was not registered.");
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
        _ctFrozen = true;
        OpenCells();

        foreach (var player in Utilities.GetPlayers().Where(player => IsUsable(player) && player.PawnIsAlive))
        {
            PreparePlayer(player);
        }

        _freezeTimer = AddTimer(60.0f, ReleaseGuards, TimerFlags.STOP_ON_MAPCHANGE);
        _roundTimer = AddTimer(300.0f, () => _context?.Finish(RoundEndReason.TerroristsWin),
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    public void Stop()
    {
        _ctFrozen = false;
        _freezeTimer?.Kill();
        _roundTimer?.Kill();
        _freezeTimer = null;
        _roundTimer = null;
        _context = null;

        foreach (var player in Utilities.GetPlayers().Where(player => IsUsable(player) && player.PawnIsAlive))
        {
            if (player.Team == CsTeam.CounterTerrorist)
            {
                SetMoveType(player, MoveType_t.MOVETYPE_WALK);
            }

            if (player.Team == CsTeam.Terrorist)
            {
                SetRenderColor(player, Color.White);
            }
        }
    }

    public void OnPlayerSpawn(CCSPlayerController player)
    {
        if (IsUsable(player) && player.PawnIsAlive)
        {
            Server.NextFrame(() => PreparePlayer(player));
        }
    }

    public void OnPlayerDeath(CCSPlayerController? victim, CCSPlayerController? attacker)
    {
        Server.NextFrame(EvaluateWinner);
    }

    private void EvaluateWinner()
    {
        if (_context is null) return;

        var aliveT = Utilities.GetPlayers().Count(player =>
            IsUsable(player) && player.PawnIsAlive && player.Team == CsTeam.Terrorist);
        var aliveCt = Utilities.GetPlayers().Count(player =>
            IsUsable(player) && player.PawnIsAlive && player.Team == CsTeam.CounterTerrorist);

        if (aliveT == 0 && aliveCt > 0)
            _context.Finish(RoundEndReason.CTsWin);
        else if (aliveCt == 0 && aliveT > 0)
            _context.Finish(RoundEndReason.TerroristsWin);
    }

    public HookResult OnTakeDamage(CBaseEntity entity, CTakeDamageInfo damageInfo)
    {
        if (entity.DesignerName != "player")
            return HookResult.Continue;

        var victim = entity.As<CCSPlayerPawn>().Controller.Value?.As<CCSPlayerController>();
        var attackerEntity = damageInfo.Attacker.Value;
        if (attackerEntity?.DesignerName != "player")
            return HookResult.Continue;

        var attacker = attackerEntity.As<CCSPlayerPawn>().Controller.Value?.As<CCSPlayerController>();
        if (!IsUsable(victim) || !IsUsable(attacker))
            return HookResult.Continue;

        // Prisoners cannot damage guards during Hide and Seek.
        if (attacker.Team == CsTeam.Terrorist && victim.Team == CsTeam.CounterTerrorist)
            return HookResult.Handled;

        // Guards are frozen during the hiding phase, so they also cannot damage prisoners yet.
        if (_ctFrozen && attacker.Team == CsTeam.CounterTerrorist && victim.Team == CsTeam.Terrorist)
            return HookResult.Handled;

        return HookResult.Continue;
    }

    private void PreparePlayer(CCSPlayerController player)
    {
        if (!IsUsable(player) || !player.PawnIsAlive)
        {
            return;
        }

        if (player.Team == CsTeam.Terrorist)
        {
            SetRenderColor(player, Color.Green);
            player.PrintToChat(JailbreakChat.Format("У вас FreeDay. Прячьтесь!"));
        }
        else if (player.Team == CsTeam.CounterTerrorist && _ctFrozen)
        {
            SetMoveType(player, MoveType_t.MOVETYPE_NONE);
            player.PrintToChat(JailbreakChat.Format("Вы заморожены на 60 секунд."));
        }
    }

    private void ReleaseGuards()
    {
        _ctFrozen = false;
        foreach (var guard in Utilities.GetPlayers().Where(player =>
                     IsUsable(player) && player.PawnIsAlive && player.Team == CsTeam.CounterTerrorist))
        {
            SetMoveType(guard, MoveType_t.MOVETYPE_WALK);
            guard.PrintToChat(JailbreakChat.Format("Поиск начался!"));
        }
    }

    private static void OpenCells()
    {
        foreach (var entityName in DoorEntityNames)
        {
            foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(entityName))
            {
                if (entity.IsValid)
                {
                    entity.AcceptInput("Open");
                }
            }
        }

        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("func_breakable"))
        {
            if (entity.IsValid)
            {
                entity.AcceptInput("Break");
            }
        }
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

    private static void SetMoveType(CCSPlayerController player, MoveType_t moveType)
    {
        if (!IsUsable(player) || !player.PawnIsAlive)
        {
            return;
        }

        var pawn = player.PlayerPawn.Value!;
        pawn.ActualMoveType = moveType;
        pawn.MoveType = moveType;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
    }
}
