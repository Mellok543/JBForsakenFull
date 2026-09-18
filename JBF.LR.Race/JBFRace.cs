using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;

namespace JBF.LR.Race;

public sealed class JBFRace : BasePlugin
{
    private readonly RaceGame _game = new();
    private IDisposable? _registration;

    public override string ModuleName => "JBF LR: Race";
    public override string ModuleVersion => "1.2.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnPlayerButtonsChanged>(_game.OnButtonsChanged);
        RegisterListener<Listeners.OnTick>(_game.Tick);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        var lr = LrCapability.Api.Get();
        if (lr is not null) _registration = lr.RegisterGame(_game);
    }

    public override void Unload(bool hotReload)
    {
        _registration?.Dispose();
        _game.Stop();
    }
}

internal sealed class RaceGame : ILrGame, ILrInventoryRules
{
    private const float FinishRadius = 72.0f;
    private const float StartLaneOffset = 42.0f;
    private const float MinCourseLength = 300.0f;
    private const int CountdownSeconds = 3;
    private const float WallClearance = 48.0f;
    private static readonly TraceOptions WallTraceOptions = new() { InteractsWith = Masks.SolidBrushOnly, InteractsExclude = Contents.Pickup };
    private readonly List<CBeam> _markers = [];
    private ILrMatchContext? _context;
    private Vector? _start;
    private Vector? _finish;
    private SetupState _state;
    private Vector? _inmateStart;
    private Vector? _guardianStart;
    private QAngle? _startAngle;
    private DateTime _countdownEndsAt;
    private int _lastCountdownValue;

    public string Id => "race";
    public string Name => "Гонка";
    public int Order => 70;
    public bool DisableAllDamage => true;

    public void Start(ILrMatchContext context)
    {
        _context = context;
        LrPlayerRules.Normalize(context.Inmate);
        LrPlayerRules.Normalize(context.Guardian);
        _state = SetupState.WaitingStart;
        UiCapability.Api.Get()?.Notify(context.Inmate, "Встаньте в точку СТАРТ и нажмите E", UiNotificationType.Info, 8.0f);
    }

    public void Stop()
    {
        ClearMarkers();
        _context = null;
        _start = null;
        _finish = null;
        _state = SetupState.None;
        _inmateStart = null;
        _guardianStart = null;
        _startAngle = null;
        _countdownEndsAt = default;
        _lastCountdownValue = 0;
    }

    public bool IsWeaponAllowed(CBasePlayerWeapon weapon) =>
        weapon.DesignerName?.Contains("knife", StringComparison.OrdinalIgnoreCase) == true;

    public void OnButtonsChanged(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
    {
        if (_context is null || player.Slot != _context.Inmate.Slot || !pressed.HasFlag(PlayerButtons.Use)) return;
        var origin = player.PlayerPawn.Value?.AbsOrigin;
        if (origin is null) return;

        if (_state == SetupState.WaitingStart)
        {
            var candidate = new Vector(origin.X, origin.Y, origin.Z);
            if (IsTooCloseToWall(player, candidate))
            {
                UiCapability.Api.Get()?.Notify(player, "Точку нельзя ставить рядом со стеной.", UiNotificationType.Warning, 4.0f);
                return;
            }

            _start = candidate;
            _state = SetupState.WaitingFinish;
            UiCapability.Api.Get()?.Notify(player, "Старт сохранён. Встаньте в ФИНИШ и нажмите E", UiNotificationType.Info, 8.0f);
            return;
        }

        if (_state != SetupState.WaitingFinish || _start is null) return;

        var candidate = new Vector(origin.X, origin.Y, origin.Z);
        if (IsTooCloseToWall(player, candidate))
        {
            UiCapability.Api.Get()?.Notify(player, "Точку нельзя ставить рядом со стеной.", UiNotificationType.Warning, 4.0f);
            return;
        }

        if (Distance2D(candidate, _start) < MinCourseLength)
        {
            UiCapability.Api.Get()?.Notify(player,
                $"Финиш слишком близко к старту. Минимум {MinCourseLength:0} юнитов.",
                UiNotificationType.Warning, 5.0f);
            return;
        }

        _finish = candidate;
        DrawCourse();
        BeginRace();
    }

    public void Tick()
    {
        if (_context is null) return;

        if (_state == SetupState.Countdown)
        {
            HoldAtStart();
            UpdateCountdown();
            return;
        }

        if (_state != SetupState.Running || _finish is null) return;
        CheckFinish(_context.Inmate, _context.Guardian);
        if (_context is not null) CheckFinish(_context.Guardian, _context.Inmate);
    }

    private void BeginRace()
    {
        if (_context is null || _start is null || _finish is null) return;

        var dx = _finish.X - _start.X;
        var dy = _finish.Y - _start.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        var px = length > 1.0f ? -dy / length : 1.0f;
        var py = length > 1.0f ? dx / length : 0.0f;

        _inmateStart = new Vector(_start.X + px * StartLaneOffset, _start.Y + py * StartLaneOffset, _start.Z);
        _guardianStart = new Vector(_start.X - px * StartLaneOffset, _start.Y - py * StartLaneOffset, _start.Z);
        var yaw = MathF.Atan2(dy, dx) * 180.0f / MathF.PI;
        _startAngle = new QAngle(0, yaw, 0);

        _context.Inmate.PlayerPawn.Value?.Teleport(_inmateStart, _startAngle, new Vector());
        _context.Guardian.PlayerPawn.Value?.Teleport(_guardianStart, _startAngle, new Vector());

        _state = SetupState.Countdown;
        _countdownEndsAt = DateTime.UtcNow.AddSeconds(CountdownSeconds);
        _lastCountdownValue = 0;
        UpdateCountdown();
    }

    private void HoldAtStart()
    {
        if (_context is null || _inmateStart is null || _guardianStart is null || _startAngle is null) return;
        _context.Inmate.PlayerPawn.Value?.Teleport(_inmateStart, _startAngle, new Vector());
        _context.Guardian.PlayerPawn.Value?.Teleport(_guardianStart, _startAngle, new Vector());
    }

    private void UpdateCountdown()
    {
        if (_context is null || _state != SetupState.Countdown) return;

        var remaining = (_countdownEndsAt - DateTime.UtcNow).TotalSeconds;
        if (remaining <= 0)
        {
            _state = SetupState.Running;
            _lastCountdownValue = 0;
            UiCapability.Api.Get()?.Notify(_context.Inmate, "СТАРТ!", UiNotificationType.Success, 2.0f);
            UiCapability.Api.Get()?.Notify(_context.Guardian, "СТАРТ!", UiNotificationType.Success, 2.0f);
            return;
        }

        var value = Math.Clamp((int)Math.Ceiling(remaining), 1, CountdownSeconds);
        if (value == _lastCountdownValue) return;
        _lastCountdownValue = value;
        UiCapability.Api.Get()?.Notify(_context.Inmate, $"Старт через {value}...", UiNotificationType.Important, 1.1f);
        UiCapability.Api.Get()?.Notify(_context.Guardian, $"Старт через {value}...", UiNotificationType.Important, 1.1f);
    }

    private void CheckFinish(CCSPlayerController runner, CCSPlayerController loser)
    {
        var pos = runner.PlayerPawn.Value?.AbsOrigin;
        if (pos is null || _finish is null || Distance2D(pos, _finish) > FinishRadius) return;
        var context = _context;
        if (context is null) return;
        _state = SetupState.None;
        context.Finish(runner);
        if (LrPlayerRules.IsUsable(loser)) loser.PlayerPawn.Value!.CommitSuicide(false, true);
    }

    private void DrawCourse()
    {
        ClearMarkers();
        if (_start is null || _finish is null) return;
        DrawRing(_start, FinishRadius, Color.LimeGreen);
        DrawRing(_finish, FinishRadius, Color.OrangeRed);
        AddBeam(_start, _finish, Color.Gold, 2.5f);
    }

    private void DrawRing(Vector center, float radius, Color color)
    {
        const int segments = 16;
        for (var i = 0; i < segments; i++)
        {
            var a = MathF.PI * 2 * i / segments;
            var b = MathF.PI * 2 * (i + 1) / segments;
            AddBeam(new Vector(center.X + MathF.Cos(a) * radius, center.Y + MathF.Sin(a) * radius, center.Z + 3),
                new Vector(center.X + MathF.Cos(b) * radius, center.Y + MathF.Sin(b) * radius, center.Z + 3), color, 2.0f);
        }
    }

    private void AddBeam(Vector start, Vector end, Color color, float width)
    {
        var beam = Utilities.CreateEntityByName<CBeam>("beam");
        if (beam is null) return;
        beam.Width = width;
        beam.Render = color;
        beam.DispatchSpawn();
        beam.Teleport(start, new QAngle(), new Vector());
        beam.EndPos.X = end.X; beam.EndPos.Y = end.Y; beam.EndPos.Z = end.Z;
        Utilities.SetStateChanged(beam, "CBeam", "m_vecEndPos");
        _markers.Add(beam);
    }

    private void ClearMarkers()
    {
        foreach (var beam in _markers.Where(x => x.IsValid)) beam.Remove();
        _markers.Clear();
    }

    private static bool IsTooCloseToWall(CCSPlayerController player, Vector point)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn is null) return true;

        var start = new Vector(point.X, point.Y, point.Z + 24.0f);
        var directions = new (float X, float Y)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        foreach (var (x, y) in directions)
        {
            var end = new Vector(start.X + x * WallClearance, start.Y + y * WallClearance, start.Z);
            var trace = Trace.TraceEndShape(start, end, pawn, WallTraceOptions);
            if (trace.DidHit() && Distance2D(start, trace.HitPoint) < WallClearance - 1.0f)
                return true;
        }

        return false;
    }

    private static float Distance2D(Vector a, Vector b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private enum SetupState { None, WaitingStart, WaitingFinish, Countdown, Running }
}
