using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;

namespace JBF.LR.Golf;

public sealed class JBFGolf : BasePlugin
{
    private readonly GolfGame _game = new();
    private IDisposable? _registration;

    public override string ModuleName => "JBF LR: Golf";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnPlayerButtonsChanged>(_game.OnButtonsChanged);
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

    [GameEventHandler]
    public HookResult OnDecoyDetonate(EventDecoyDetonate @event, GameEventInfo info) => _game.OnDecoyDetonate(@event);
}

internal sealed class GolfGame : ILrGame, ILrInventoryRules
{
    private const float MarkerRadius = 72.0f;
    private readonly List<CBeam> _markers = [];
    private ILrMatchContext? _context;
    private Vector? _start;
    private Vector? _hole;
    private float? _inmateDistance;
    private float? _guardianDistance;
    private SetupState _state;

    public string Id => "golf";
    public string Name => "Гольф";
    public int Order => 60;
    public bool DisableAllDamage => true;

    public void Start(ILrMatchContext context)
    {
        _context = context;
        LrPlayerRules.Normalize(context.Inmate);
        LrPlayerRules.Normalize(context.Guardian);
        _state = SetupState.WaitingStart;
        UiCapability.Api.Get()?.Notify(context.Inmate, "Встаньте в центр СТАРТА и нажмите E", UiNotificationType.Info, 8.0f);
    }

    public void Stop()
    {
        ClearMarkers();
        _context = null;
        _start = null;
        _hole = null;
        _inmateDistance = null;
        _guardianDistance = null;
        _state = SetupState.None;
    }

    public bool IsWeaponAllowed(CBasePlayerWeapon weapon)
    {
        var name = weapon.DesignerName ?? string.Empty;
        return name.Contains("knife", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("weapon_decoy", StringComparison.OrdinalIgnoreCase);
    }

    public void OnButtonsChanged(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
    {
        if (_context is null || player.Slot != _context.Inmate.Slot || !pressed.HasFlag(PlayerButtons.Use)) return;
        var origin = player.PlayerPawn.Value?.AbsOrigin;
        if (origin is null) return;

        if (_state == SetupState.WaitingStart)
        {
            _start = new Vector(origin.X, origin.Y, origin.Z);
            _state = SetupState.WaitingHole;
            UiCapability.Api.Get()?.Notify(player, "Старт сохранён. Встаньте в центр ЛУНКИ и нажмите E", UiNotificationType.Info, 8.0f);
            return;
        }

        if (_state != SetupState.WaitingHole || _start is null) return;
        _hole = new Vector(origin.X, origin.Y, origin.Z);
        DrawCourse();
        BeginGolf();
    }

    public HookResult OnDecoyDetonate(EventDecoyDetonate @event)
    {
        if (_context is null || _state != SetupState.Throwing || _hole is null || @event.Userid is null)
            return HookResult.Continue;

        var player = @event.Userid;
        if (player.Slot != _context.Inmate.Slot && player.Slot != _context.Guardian.Slot)
            return HookResult.Continue;

        var point = new Vector(@event.X, @event.Y, @event.Z);
        var distance = Distance2D(point, _hole);

        if (player.Slot == _context.Inmate.Slot)
            _inmateDistance = distance;
        else
            _guardianDistance = distance;

        UiCapability.Api.Get()?.Notify(player, $"Точность броска: {distance:0.0} ед.", UiNotificationType.Info, 4.0f);

        if (_inmateDistance is null || _guardianDistance is null) return HookResult.Continue;

        if (MathF.Abs(_inmateDistance.Value - _guardianDistance.Value) < 1.0f)
        {
            _inmateDistance = null;
            _guardianDistance = null;
            GiveDecoy(_context.Inmate);
            GiveDecoy(_context.Guardian);
            UiCapability.Api.Get()?.Notify(_context.Inmate, "Ничья. Повторный бросок", UiNotificationType.Warning, 4.0f);
            UiCapability.Api.Get()?.Notify(_context.Guardian, "Ничья. Повторный бросок", UiNotificationType.Warning, 4.0f);
            return HookResult.Continue;
        }

        var winner = _inmateDistance < _guardianDistance ? _context.Inmate : _context.Guardian;
        var loser = winner.Slot == _context.Inmate.Slot ? _context.Guardian : _context.Inmate;
        var context = _context;
        _state = SetupState.None;
        context.Finish(winner);
        if (LrPlayerRules.IsUsable(loser)) loser.PlayerPawn.Value!.CommitSuicide(false, true);
        return HookResult.Continue;
    }

    private void BeginGolf()
    {
        if (_context is null || _start is null) return;
        var left = new Vector(_start.X - 48.0f, _start.Y, _start.Z);
        var right = new Vector(_start.X + 48.0f, _start.Y, _start.Z);
        _context.Inmate.PlayerPawn.Value?.Teleport(left, new QAngle(), new Vector());
        _context.Guardian.PlayerPawn.Value?.Teleport(right, new QAngle(), new Vector());
        GiveDecoy(_context.Inmate);
        GiveDecoy(_context.Guardian);
        _state = SetupState.Throwing;
        UiCapability.Api.Get()?.Notify(_context.Inmate, "Бросайте decoy как можно ближе к центру лунки", UiNotificationType.Success, 6.0f);
        UiCapability.Api.Get()?.Notify(_context.Guardian, "Бросайте decoy как можно ближе к центру лунки", UiNotificationType.Success, 6.0f);
    }

    private static void GiveDecoy(CCSPlayerController player)
    {
        if (!LrPlayerRules.IsUsable(player)) return;
        player.GiveNamedItem("weapon_decoy");
    }

    private void DrawCourse()
    {
        ClearMarkers();
        if (_start is null || _hole is null) return;
        DrawRing(_start, MarkerRadius, Color.LimeGreen);
        DrawRing(_hole, MarkerRadius, Color.OrangeRed);
        AddBeam(_start, _hole, Color.Gold, 2.5f);
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
        beam.EndPos.X = end.X;
        beam.EndPos.Y = end.Y;
        beam.EndPos.Z = end.Z;
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

    private enum SetupState { None, WaitingStart, WaitingHole, Throwing }
}
