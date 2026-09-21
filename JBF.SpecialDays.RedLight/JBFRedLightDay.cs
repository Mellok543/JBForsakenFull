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

namespace JBF.SpecialDays.RedLight;

public sealed class JBFRedLightDay : BasePlugin, ISpecialDay
{
    private const float PreparationSeconds = 30.0f;
    private const float RedReactionGraceSeconds = 1.0f;
    private const float RedMovementTolerance = 8.0f;
    private const float RedDamageIntervalSeconds = 0.5f;
    private const int BaseRedMovementDamage = 10;
    private const int RedMovementDamageIncreasePerRound = 5;
    private const int MaxRedMovementDamage = 50;
    private const float MinimumGreenTravelDistance = 100.0f;
    private const int MaxIdleWarnings = 2;
    private const float DefaultRoundTimeSeconds = 300.0f;

    private static readonly string[] DoorEntityNames =
    [
        "func_door",
        "func_movelinear",
        "func_door_rotating",
        "prop_door_rotating"
    ];

    private readonly Dictionary<int, Position> _lastPosition = [];
    private readonly Dictionary<int, Position> _redAnchor = [];
    private readonly Dictionary<int, float> _greenTravel = [];
    private readonly Dictionary<int, int> _idleWarnings = [];
    private readonly Dictionary<int, DateTime> _nextRedDamageAt = [];
    private readonly HashSet<int> _eliminatingSlots = [];

    private IDisposable? _registration;
    private ISpecialDayContext? _context;
    private Timer? _phaseTimer;
    private Timer? _roundTimer;
    private Phase _phase = Phase.Stopped;
    private DateTime _redGraceUntil;
    private int _redRound;

    public override string ModuleName => "JBF Special Day: Red Light";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Mell";

    public string Id => "red-light";
    public string Name => "Красный свет";
    public int Order => 20;

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnTick>(Tick);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        var specialDays = SpecialDaysCapability.Api.GetOptional();
        if (specialDays is null)
        {
            Logger.LogWarning(
                "Special days capability is unavailable; Red Light was not registered.");
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
        _phase = Phase.Preparation;
        _idleWarnings.Clear();
        _nextRedDamageAt.Clear();
        _eliminatingSlots.Clear();
        _greenTravel.Clear();
        _redRound = 0;
        _lastPosition.Clear();
        _redAnchor.Clear();

        OpenCells();
        PrintRules();

        AnnounceAll(
            "КРАСНЫЙ СВЕТ",
            "Подготовка • 30 секунд",
            UiNotificationType.Important,
            6.0f);

        _phaseTimer = AddTimer(
            PreparationSeconds,
            StartGreenPhase,
            TimerFlags.STOP_ON_MAPCHANGE);

        _roundTimer = AddTimer(
            GetRoundTimeSeconds(),
            HandleRoundTimeExpired,
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    public void Stop()
    {
        _phaseTimer?.Kill();
        _roundTimer?.Kill();
        _phaseTimer = null;
        _roundTimer = null;

        _phase = Phase.Stopped;
        _context = null;
        _redGraceUntil = default;

        _lastPosition.Clear();
        _redAnchor.Clear();
        _greenTravel.Clear();
        _idleWarnings.Clear();
        _nextRedDamageAt.Clear();
        _eliminatingSlots.Clear();
        _redRound = 0;
    }

    public void OnPlayerSpawn(CCSPlayerController player)
    {
        if (_context is null || !IsUsable(player))
            return;

        _idleWarnings.TryAdd(player.Slot, 0);
        _eliminatingSlots.Remove(player.Slot);

        Server.NextFrame(() =>
        {
            if (_context is null || !IsActiveTerrorist(player))
                return;

            var position = GetPosition(player);
            _lastPosition[player.Slot] = position;

            if (_phase == Phase.Green)
                _greenTravel[player.Slot] = 0.0f;
            else if (_phase == Phase.Red)
                _redAnchor[player.Slot] = position;
        });
    }

    public void OnPlayerDeath(CCSPlayerController? victim, CCSPlayerController? attacker)
    {
        if (victim is not null)
        {
            _lastPosition.Remove(victim.Slot);
            _redAnchor.Remove(victim.Slot);
            _greenTravel.Remove(victim.Slot);
            _nextRedDamageAt.Remove(victim.Slot);
            _eliminatingSlots.Remove(victim.Slot);
        }

        if (_context is not null)
            Server.NextFrame(EvaluateRoundState);
    }

    public HookResult OnTakeDamage(CBaseEntity entity, CTakeDamageInfo damageInfo)
    {
        if (_context is null || entity.DesignerName != "player")
            return HookResult.Continue;

        var victim = entity
            .As<CCSPlayerPawn>()
            .Controller.Value?
            .As<CCSPlayerController>();

        var attackerEntity = damageInfo.Attacker.Value;
        if (attackerEntity?.DesignerName != "player")
            return HookResult.Continue;

        var attacker = attackerEntity
            .As<CCSPlayerPawn>()
            .Controller.Value?
            .As<CCSPlayerController>();

        // Allow self-damage/suicide. CommitSuicide and admin slay can pass through
        // the damage hook with the player as both attacker and victim.
        if (IsUsable(victim) &&
            IsUsable(attacker) &&
            victim.Slot == attacker.Slot)
        {
            return HookResult.Continue;
        }

        // Block only damage from one player to another.
        return HookResult.Handled;
    }

    private void Tick()
    {
        if (_context is null || _phase is not (Phase.Green or Phase.Red))
            return;

        foreach (var player in ActiveTerrorists().ToArray())
        {
            var current = GetPosition(player);

            if (_phase == Phase.Green)
            {
                if (_lastPosition.TryGetValue(player.Slot, out var previous))
                {
                    var traveled = Distance(previous, current);
                    _greenTravel[player.Slot] =
                        _greenTravel.GetValueOrDefault(player.Slot) + traveled;
                }

                _lastPosition[player.Slot] = current;
                continue;
            }

            // Movement during the reaction grace period is allowed. The anchor
            // follows the player until the grace period expires.
            if (DateTime.UtcNow < _redGraceUntil)
            {
                _redAnchor[player.Slot] = current;
                continue;
            }

            if (!_redAnchor.TryGetValue(player.Slot, out var anchor))
            {
                _redAnchor[player.Slot] = current;
                continue;
            }

            if (Distance(anchor, current) <= RedMovementTolerance)
                continue;

            var now = DateTime.UtcNow;
            if (_nextRedDamageAt.TryGetValue(player.Slot, out var nextDamageAt) &&
                now < nextDamageAt)
            {
                continue;
            }

            ApplyRedMovementDamage(player);
            _nextRedDamageAt[player.Slot] =
                now.AddSeconds(RedDamageIntervalSeconds);

            // Reset the anchor after each damage tick. A player who keeps moving
            // will continue taking damage, while a player who stops will not.
            _redAnchor[player.Slot] = current;
        }
    }

    private void StartGreenPhase()
    {
        if (_context is null)
            return;

        _phaseTimer = null;
        _phase = Phase.Green;
        _greenTravel.Clear();
        _lastPosition.Clear();
        _redAnchor.Clear();
        _nextRedDamageAt.Clear();

        foreach (var player in ActiveTerrorists())
        {
            _greenTravel[player.Slot] = 0.0f;
            _lastPosition[player.Slot] = GetPosition(player);
            _idleWarnings.TryAdd(player.Slot, 0);
        }

        AnnounceAll(
            "ЗЕЛЁНЫЙ СВЕТ",
            "ДВИГАЙТЕСЬ!",
            UiNotificationType.Success,
            2.0f);

        _phaseTimer = AddTimer(
            RandomSeconds(3.0f, 7.0f),
            StartRedPhase,
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void StartRedPhase()
    {
        if (_context is null)
            return;

        _phaseTimer = null;

        EvaluateGreenParticipation();
        if (_context is null)
            return;

        _phase = Phase.Red;
        _redRound++;
        _redGraceUntil = DateTime.UtcNow.AddSeconds(RedReactionGraceSeconds);
        _redAnchor.Clear();
        _nextRedDamageAt.Clear();

        foreach (var player in ActiveTerrorists())
            _redAnchor[player.Slot] = GetPosition(player);

        AnnounceAll(
            "КРАСНЫЙ СВЕТ",
            "ЗАМРИ!",
            UiNotificationType.Error,
            2.0f);

        _phaseTimer = AddTimer(
            RandomSeconds(2.0f, 5.0f),
            StartGreenPhase,
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void EvaluateGreenParticipation()
    {
        foreach (var player in ActiveTerrorists().ToArray())
        {
            var traveled = _greenTravel.GetValueOrDefault(player.Slot);
            if (traveled >= MinimumGreenTravelDistance)
                continue;

            var warnings = _idleWarnings.GetValueOrDefault(player.Slot);

            if (warnings >= MaxIdleWarnings)
            {
                Eliminate(player);
                continue;
            }

            warnings++;
            _idleWarnings[player.Slot] = warnings;

            player.PrintToChat(
                JailbreakChat.Format(
                    $"Красный свет: на зелёный нужно двигаться. Предупреждение {warnings}/{MaxIdleWarnings}."));
        }

        Server.NextFrame(EvaluateRoundState);
    }

    private void ApplyRedMovementDamage(CCSPlayerController player)
    {
        if (!IsActiveTerrorist(player))
            return;

        var pawn = player.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid)
            return;

        var damage = Math.Min(
            MaxRedMovementDamage,
            BaseRedMovementDamage +
            Math.Max(0, _redRound - 1) * RedMovementDamageIncreasePerRound);

        var newHealth = pawn.Health - damage;

        if (newHealth <= 0)
        {
            Eliminate(player);
            return;
        }

        pawn.Health = newHealth;
        Utilities.SetStateChanged(
            pawn,
            "CBaseEntity",
            "m_iHealth");
    }

    private void Eliminate(CCSPlayerController player)
    {
        if (!IsActiveTerrorist(player) || !_eliminatingSlots.Add(player.Slot))
            return;

        try
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn is null || !pawn.IsValid)
            {
                _eliminatingSlots.Remove(player.Slot);
                return;
            }

            pawn.CommitSuicide(true, true);
        }
        catch (Exception ex)
        {
            _eliminatingSlots.Remove(player.Slot);
            Logger.LogWarning(
                ex,
                "Failed to eliminate {Player} during Red Light.",
                player.PlayerName);
        }
    }

    private void EvaluateRoundState()
    {
        if (_context is null)
            return;

        var aliveT = ActiveTerrorists().Count();
        var aliveCt = ActiveCounterTerrorists().Count();

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

        // Hand control back to the normal LR system.
        if (aliveT <= 2)
            _context.End();
    }

    private void HandleRoundTimeExpired()
    {
        _roundTimer = null;

        if (_context is null)
            return;

        _context.Finish(RoundEndReason.TerroristsWin);
    }

    private static void PrintRules()
    {
        Server.PrintToChatAll(
            JailbreakChat.Format("Игровой день: Красный свет / Зелёный свет."));
        Server.PrintToChatAll(
            JailbreakChat.Format("Джайлы открыты. Игра начнётся через 30 секунд."));
        Server.PrintToChatAll(
            JailbreakChat.Format("ЗЕЛЁНЫЙ: заключённые должны двигаться."));
        Server.PrintToChatAll(
            JailbreakChat.Format("КРАСНЫЙ: остановитесь. После короткого времени реакции движение наносит урон."));
        Server.PrintToChatAll(
            JailbreakChat.Format(
                $"Можно получить только {MaxIdleWarnings} предупреждения за бездействие на зелёном. Следующее нарушение = смерть."));
        Server.PrintToChatAll(
            JailbreakChat.Format("Чем дальше идёт игра, тем больше урон за движение на красный. Урон между игроками отключён."));
    }

    private static float RandomSeconds(float minimum, float maximum) =>
        minimum + (float)Random.Shared.NextDouble() * (maximum - minimum);

    private static float GetRoundTimeSeconds()
    {
        try
        {
            var minutes =
                ConVar.Find("mp_roundtime")?.GetPrimitiveValue<float>() ?? 0.0f;

            if (minutes > 0.0f)
                return Math.Max(1.0f, minutes * 60.0f);
        }
        catch
        {
        }

        return DefaultRoundTimeSeconds;
    }

    private static IEnumerable<CCSPlayerController> ActiveTerrorists() =>
        Utilities.GetPlayers().Where(IsActiveTerrorist);

    private static IEnumerable<CCSPlayerController> ActiveCounterTerrorists() =>
        Utilities.GetPlayers().Where(player =>
            IsUsable(player) &&
            player.PawnIsAlive &&
            player.Team == CsTeam.CounterTerrorist);

    private static bool IsActiveTerrorist(
        [NotNullWhen(true)] CCSPlayerController? player) =>
        IsUsable(player) &&
        player.PawnIsAlive &&
        player.Team == CsTeam.Terrorist;

    private static bool IsUsable(
        [NotNullWhen(true)] CCSPlayerController? player) =>
        player is { IsValid: true, PlayerPawn.IsValid: true } &&
        player.PlayerPawn.Value?.IsValid == true;

    private static Position GetPosition(CCSPlayerController player)
    {
        var origin = player.PlayerPawn.Value?.AbsOrigin;
        return origin is null
            ? default
            : new Position(origin.X, origin.Y, origin.Z);
    }

    private static float Distance(Position a, Position b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var dz = b.Z - a.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

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

        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("func_breakable"))
        {
            if (entity.IsValid)
                entity.AcceptInput("Break");
        }
    }

    private static void AnnounceAll(
        string title,
        string subtitle,
        UiNotificationType type,
        float durationSeconds)
    {
        var ui = UiCapability.Api.GetOptional();
        if (ui is null)
            return;

        foreach (var player in Utilities.GetPlayers().Where(
                     player => player is { IsValid: true, IsBot: false }))
        {
            ui.Announce(player, title, subtitle, type, durationSeconds);
        }
    }

    private readonly record struct Position(float X, float Y, float Z);

    private enum Phase
    {
        Stopped,
        Preparation,
        Green,
        Red
    }
}
