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

namespace JBF.SpecialDays.HideAndSeek;

public sealed class JBFHideAndSeekDay : BasePlugin, ISpecialDay
{
    private const float HideTimeSeconds = 60.0f;
    private const float DefaultRoundTimeSeconds = 300.0f;

    private static readonly string[] DoorEntityNames =
    [
        "func_door",
        "func_movelinear",
        "func_door_rotating",
        "prop_door_rotating"
    ];

    private IDisposable? _registration;
    private ISpecialDayContext? _context;
    private Timer? _hideTimer;
    private Timer? _roundTimer;
    private bool _hidingPhase;

    public override string ModuleName => "JBF Special Day: Hide and Seek";
    public override string ModuleVersion => "2.0.0";
    public override string ModuleAuthor => "Mell";

    public string Id => "hide-and-seek";
    public string Name => "Прятки";
    public int Order => 10;

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        var specialDays = SpecialDaysCapability.Api.GetOptional();
        if (specialDays is null)
        {
            Logger.LogWarning(
                "Special days capability is unavailable; hide and seek was not registered.");
            return;
        }

        _registration = specialDays.RegisterDay(this);
    }

    public override void Unload(bool hotReload)
    {
        _registration?.Dispose();
        _registration = null;
        Stop();
    }

    public void Start(ISpecialDayContext context)
    {
        Stop();

        _context = context;
        _hidingPhase = true;

        OpenCells();

        foreach (var player in ActivePlayers())
            ApplyCurrentState(player);

        Server.PrintToChatAll(
            JailbreakChat.Format(
                $"Прятки: КТ заморожены на {(int)HideTimeSeconds} секунд. Урон временно отключён."));

        AnnounceAll(
            "ПРЯТКИ",
            $"{(int)HideTimeSeconds} секунд на то, чтобы спрятаться");

        _hideTimer = AddTimer(
            HideTimeSeconds,
            StartHunt,
            TimerFlags.STOP_ON_MAPCHANGE);

        _roundTimer = AddTimer(
            GetRoundTimeSeconds(),
            HandleRoundTimeExpired,
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    public void Stop()
    {
        _hideTimer?.Kill();
        _roundTimer?.Kill();
        _hideTimer = null;
        _roundTimer = null;

        _hidingPhase = false;
        _context = null;

        foreach (var player in ActivePlayers())
        {
            if (player.Team == CsTeam.CounterTerrorist)
                SetMoveType(player, MoveType_t.MOVETYPE_WALK);
        }
    }

    public void OnPlayerSpawn(CCSPlayerController player)
    {
        if (_context is null)
            return;

        Server.NextFrame(() => ApplyCurrentState(player));
    }

    public void OnPlayerDeath(CCSPlayerController? victim, CCSPlayerController? attacker)
    {
        if (_context is null)
            return;

        Server.NextFrame(EvaluateState);
    }

    public HookResult OnTakeDamage(CBaseEntity entity, CTakeDamageInfo damageInfo)
    {
        if (_context is null || entity.DesignerName != "player")
            return HookResult.Continue;

        var attackerEntity = damageInfo.Attacker.Value;
        if (attackerEntity?.DesignerName != "player")
            return HookResult.Continue;

        var attacker = attackerEntity
            .As<CCSPlayerPawn>()
            .Controller.Value?
            .As<CCSPlayerController>();

        if (!IsUsable(attacker))
            return HookResult.Continue;

        // During the first minute neither team can deal player damage.
        if (_hidingPhase)
            return HookResult.Handled;

        // Prisoners can never deal player damage during Hide and Seek.
        if (attacker.Team == CsTeam.Terrorist)
            return HookResult.Handled;

        // After the hiding phase CT damage is allowed.
        return HookResult.Continue;
    }

    private void StartHunt()
    {
        if (_context is null)
            return;

        _hidingPhase = false;
        _hideTimer = null;

        foreach (var guard in ActivePlayers().Where(
                     player => player.Team == CsTeam.CounterTerrorist))
        {
            SetMoveType(guard, MoveType_t.MOVETYPE_WALK);
            guard.PrintToChat(JailbreakChat.Format("Время вышло. Поиск начался!"));
        }

        Server.PrintToChatAll(
            JailbreakChat.Format("Прятки: КТ разморожены и теперь могут наносить урон."));

        AnnounceAll("ПРЯТКИ", "Поиск начался");
    }

    private void HandleRoundTimeExpired()
    {
        _roundTimer = null;

        if (_context is null)
            return;

        // If at least one prisoner survived until the round timer expired,
        // prisoners win Hide and Seek.
        _context.Finish(RoundEndReason.TerroristsWin);
    }

    private void EvaluateState()
    {
        if (_context is null)
            return;

        var aliveT = ActivePlayers()
            .Count(player => player.Team == CsTeam.Terrorist);

        var aliveCt = ActivePlayers()
            .Count(player => player.Team == CsTeam.CounterTerrorist);

        if (aliveT == 0)
        {
            _context.Finish(RoundEndReason.CTsWin);
            return;
        }

        if (aliveCt == 0)
        {
            _context.Finish(RoundEndReason.TerroristsWin);
            return;
        }

        // The LR core starts its phase when one or two prisoners remain.
        // End only the special-day mode so the same round can continue into LR.
        if (aliveT <= 2)
            _context.End();
    }

    private void ApplyCurrentState(CCSPlayerController player)
    {
        if (_context is null ||
            !IsUsable(player) ||
            !player.PawnIsAlive ||
            player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
        {
            return;
        }

        if (player.Team == CsTeam.CounterTerrorist)
        {
            SetMoveType(
                player,
                _hidingPhase
                    ? MoveType_t.MOVETYPE_NONE
                    : MoveType_t.MOVETYPE_WALK);
        }
    }

    private static float GetRoundTimeSeconds()
    {
        try
        {
            var minutes = ConVar.Find("mp_roundtime")?.GetPrimitiveValue<float>() ?? 0.0f;
            if (minutes > 0.0f)
                return Math.Max(1.0f, minutes * 60.0f);
        }
        catch
        {
            // Fall back to a safe default when the cvar is unavailable.
        }

        return DefaultRoundTimeSeconds;
    }

    private static IEnumerable<CCSPlayerController> ActivePlayers() =>
        Utilities.GetPlayers().Where(player =>
            IsUsable(player) &&
            player.PawnIsAlive &&
            player.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist);

    private static void OpenCells()
    {
        foreach (var designerName in DoorEntityNames)
        {
            foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(designerName))
            {
                if (entity.IsValid)
                    entity.AcceptInput("Open");
            }
        }

        foreach (var breakable in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("func_breakable"))
        {
            if (breakable.IsValid)
                breakable.AcceptInput("Break");
        }
    }

    private static void AnnounceAll(string title, string subtitle)
    {
        var ui = UiCapability.Api.GetOptional();
        if (ui is null)
            return;

        foreach (var player in Utilities.GetPlayers().Where(
                     player => player is { IsValid: true, IsBot: false }))
        {
            ui.Announce(
                player,
                title,
                subtitle,
                UiNotificationType.Important,
                5.0f);
        }
    }

    private static bool IsUsable([NotNullWhen(true)] CCSPlayerController? player) =>
        player is { IsValid: true, PlayerPawn.IsValid: true } &&
        player.PlayerPawn.Value?.IsValid == true;

    private static void SetMoveType(
        CCSPlayerController player,
        MoveType_t moveType)
    {
        if (!IsUsable(player) || !player.PawnIsAlive)
            return;

        var pawn = player.PlayerPawn.Value!;
        pawn.ActualMoveType = moveType;
        pawn.MoveType = moveType;

        Utilities.SetStateChanged(
            pawn,
            "CBaseEntity",
            "m_MoveType");
    }
}
