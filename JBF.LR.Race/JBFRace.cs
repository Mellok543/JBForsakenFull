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
    public override string ModuleVersion => "1.0.0";
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
    private readonly List<CBeam> _markers = [];
    private ILrMatchContext? _context;
    private Vector? _start;
    private Vector? _finish;
    private SetupState _state;

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
            _start = new Vector(origin.X, origin.Y, origin.Z);
            _state = SetupState.WaitingFinish;
            UiCapability.Api.Get()?.Notify(player, "Старт сохранён. Встаньте в ФИНИШ и нажмите E", UiNotificationType.Info, 8.0f);
            return;
        }

        if (_state != SetupState.WaitingFinish || _start is null) return;
        _finish = new Vector(origin.X, origin.Y, origin.Z);
        DrawCourse();
        BeginRace();
    }

    public void Tick()
    {
        if (_context is null || _state != SetupState.Running || _finish is null) return;
        CheckFinish(_context.Inmate, _context.Guardian);
        if (_context is not null) CheckFinish(_context.Guardian, _context.Inmate);
    }

    private void BeginRace()
    {
        if (_context is null || _start is null) return;
        _context.Inmate.PlayerPawn.Value?.Teleport(_start, new QAngle(), new Vector());
        _context.Guardian.PlayerPawn.Value?.Teleport(_start, new QAngle(), new Vector());
        _state = SetupState.Running;
        UiCapability.Api.Get()?.Notify(_context.Inmate, "СТАРТ!", UiNotificationType.Success, 3.0f);
        UiCapability.Api.Get()?.Notify(_context.Guardian, "СТАРТ!", UiNotificationType.Success, 3.0f);
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

    private static float Distance2D(Vector a, Vector b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private enum SetupState { None, WaitingStart, WaitingFinish, Running }
}
