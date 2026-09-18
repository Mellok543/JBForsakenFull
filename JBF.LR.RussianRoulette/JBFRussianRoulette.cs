using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;
using JBF.Api;

namespace JBF.LR.RussianRoulette;

public sealed class JBFRussianRoulette : BasePlugin
{
    private readonly RussianRouletteGame _game = new();
    private IDisposable? _registration;

    public override string ModuleName => "JBF LR: Russian Roulette";
    public override string ModuleVersion => "1.2.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
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

internal sealed class RussianRouletteGame : ILrGame, ILrInventoryRules
{
    private const float MovementTolerance = 8.0f;
    private const float ArenaRadius = 110.0f;
    private const float PlayerOffset = 70.0f;
    private const float WallMargin = 28.0f;
    private const float WallCheckHeight = 24.0f;
    private static readonly TraceOptions WallTraceOptions = new()
    {
        InteractsWith = Masks.SolidBrushOnly,
        InteractsExclude = Contents.Pickup
    };

    private readonly List<CBeam> _markers = [];
    private ILrMatchContext? _context;
    private Vector? _inmateAnchor;
    private Vector? _guardianAnchor;
    private QAngle? _inmateAngle;
    private QAngle? _guardianAngle;

    public string Id => "russian-roulette";
    public string Name => "Русская рулетка";
    public int Order => 40;
    public bool DisableAllDamage => false;

    public void Start(ILrMatchContext context)
    {
        _context = context;
        Prepare(context.Inmate);
        Prepare(context.Guardian);
        BuildArena(context);

        var inmateStarts = Random.Shared.Next(0, 2) == 0;
        LrPlayerRules.SetAmmo(context.Inmate, inmateStarts ? 1 : 0, 0);
        LrPlayerRules.SetAmmo(context.Guardian, inmateStarts ? 0 : 1, 0);
        UiCapability.Api.Get()?.Notify(context.Inmate, "Движение заблокировано на время рулетки", UiNotificationType.Info, 4.0f);
        UiCapability.Api.Get()?.Notify(context.Guardian, "Движение заблокировано на время рулетки", UiNotificationType.Info, 4.0f);
    }

    public void Stop()
    {
        ClearMarkers();
        _context = null;
        _inmateAnchor = null;
        _guardianAnchor = null;
        _inmateAngle = null;
        _guardianAngle = null;
    }

    public void Tick()
    {
        if (_context is null) return;
        KeepAtAnchor(_context.Inmate, _inmateAnchor, _inmateAngle);
        KeepAtAnchor(_context.Guardian, _guardianAnchor, _guardianAngle);
    }

    public HookResult OnTakeDamage(CBaseEntity entity, CTakeDamageInfo damageInfo)
    {
        if (_context is null) return HookResult.Handled;
        var victim = entity.As<CCSPlayerPawn>().Controller.Value?.As<CCSPlayerController>();
        if (victim is null) return HookResult.Handled;

        if (Random.Shared.Next(0, 6) != 0)
        {
            UiCapability.Api.Get()?.Notify(victim, "Щелчок... повезло", UiNotificationType.Info, 2.0f);
            return HookResult.Handled;
        }

        LrPlayerRules.SetHealth(victim, 1);
        UiCapability.Api.Get()?.Notify(victim, "Боевой патрон!", UiNotificationType.Error, 2.0f);
        return HookResult.Continue;
    }

    public HookResult OnBulletImpact(EventBulletImpact @event, GameEventInfo info)
    {
        if (_context is null || @event.Userid is null) return HookResult.Continue;
        if (@event.Userid.Slot == _context.Inmate.Slot)
        {
            LrPlayerRules.SetAmmo(_context.Inmate, 0, 0);
            LrPlayerRules.SetAmmo(_context.Guardian, 1, 0);
        }
        else if (@event.Userid.Slot == _context.Guardian.Slot)
        {
            LrPlayerRules.SetAmmo(_context.Guardian, 0, 0);
            LrPlayerRules.SetAmmo(_context.Inmate, 1, 0);
        }
        return HookResult.Continue;
    }

    public bool IsWeaponAllowed(CBasePlayerWeapon weapon)
    {
        var name = weapon.DesignerName ?? string.Empty;
        return name.Contains("knife", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("weapon_deagle", StringComparison.OrdinalIgnoreCase);
    }

    private static void Prepare(CCSPlayerController player)
    {
        LrPlayerRules.Normalize(player);
        player.GiveNamedItem(CsItem.DesertEagle);
    }

    private void BuildArena(ILrMatchContext context)
    {
        ClearMarkers();

        var inmateOrigin = context.Inmate.PlayerPawn.Value?.AbsOrigin;
        var guardianOrigin = context.Guardian.PlayerPawn.Value?.AbsOrigin;
        if (inmateOrigin is null || guardianOrigin is null) return;

        var preferredCenter = new Vector(
            (inmateOrigin.X + guardianOrigin.X) * 0.5f,
            (inmateOrigin.Y + guardianOrigin.Y) * 0.5f,
            MathF.Min(inmateOrigin.Z, guardianOrigin.Z));

        var center = FindSafeCenter(context, preferredCenter, inmateOrigin, guardianOrigin);
        if (center is null)
        {
            UiCapability.Api.Get()?.Notify(context.Inmate,
                "Рядом недостаточно свободного места для русской рулетки.",
                UiNotificationType.Warning, 5.0f);
            UiCapability.Api.Get()?.Notify(context.Guardian,
                "Рядом недостаточно свободного места для русской рулетки.",
                UiNotificationType.Warning, 5.0f);

            // Keep the players where they are rather than placing either of them into geometry.
            _inmateAnchor = new Vector(inmateOrigin.X, inmateOrigin.Y, inmateOrigin.Z);
            _guardianAnchor = new Vector(guardianOrigin.X, guardianOrigin.Y, guardianOrigin.Z);
        }
        else
        {
            var dx = guardianOrigin.X - inmateOrigin.X;
            var dy = guardianOrigin.Y - inmateOrigin.Y;
            var length = MathF.Sqrt(dx * dx + dy * dy);
            var nx = length > 1.0f ? dx / length : 1.0f;
            var ny = length > 1.0f ? dy / length : 0.0f;

            _inmateAnchor = new Vector(
                center.X - nx * PlayerOffset,
                center.Y - ny * PlayerOffset,
                center.Z);

            _guardianAnchor = new Vector(
                center.X + nx * PlayerOffset,
                center.Y + ny * PlayerOffset,
                center.Z);

            DrawArena(center);
        }

        var inmateYaw = MathF.Atan2(
            _guardianAnchor.Y - _inmateAnchor.Y,
            _guardianAnchor.X - _inmateAnchor.X) * 180.0f / MathF.PI;
        var guardianYaw = MathF.Atan2(
            _inmateAnchor.Y - _guardianAnchor.Y,
            _inmateAnchor.X - _guardianAnchor.X) * 180.0f / MathF.PI;

        _inmateAngle = new QAngle(0, inmateYaw, 0);
        _guardianAngle = new QAngle(0, guardianYaw, 0);

        context.Inmate.PlayerPawn.Value?.Teleport(_inmateAnchor, _inmateAngle, new Vector());
        context.Guardian.PlayerPawn.Value?.Teleport(_guardianAnchor, _guardianAngle, new Vector());
    }

    private static Vector? FindSafeCenter(
        ILrMatchContext context,
        Vector preferredCenter,
        Vector inmateOrigin,
        Vector guardianOrigin)
    {
        var candidates = new List<Vector> { preferredCenter };
        foreach (var distance in new[] { 64.0f, 128.0f, 192.0f })
        {
            for (var i = 0; i < 8; i++)
            {
                var angle = MathF.PI * 2.0f * i / 8.0f;
                candidates.Add(new Vector(
                    preferredCenter.X + MathF.Cos(angle) * distance,
                    preferredCenter.Y + MathF.Sin(angle) * distance,
                    preferredCenter.Z));
            }
        }

        foreach (var center in candidates)
        {
            if (!HasArenaClearance(context.Inmate, center)) continue;
            if (!PathIsClear(context.Inmate, inmateOrigin, center)) continue;
            if (!PathIsClear(context.Guardian, guardianOrigin, center)) continue;
            return center;
        }

        return null;
    }

    private static bool HasArenaClearance(CCSPlayerController player, Vector center)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn is null) return false;

        var start = new Vector(center.X, center.Y, center.Z + WallCheckHeight);
        var required = ArenaRadius + WallMargin;

        for (var i = 0; i < 16; i++)
        {
            var angle = MathF.PI * 2.0f * i / 16.0f;
            var end = new Vector(
                start.X + MathF.Cos(angle) * required,
                start.Y + MathF.Sin(angle) * required,
                start.Z);

            var trace = Trace.TraceEndShape(start, end, pawn, WallTraceOptions);
            if (trace.DidHit() && Distance2D(start, trace.HitPoint) < required - 1.0f)
                return false;
        }

        return true;
    }

    private static bool PathIsClear(CCSPlayerController player, Vector from, Vector center)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn is null) return false;

        var start = new Vector(from.X, from.Y, from.Z + WallCheckHeight);
        var end = new Vector(center.X, center.Y, center.Z + WallCheckHeight);
        var trace = Trace.TraceEndShape(start, end, pawn, WallTraceOptions);
        return !trace.DidHit() || Distance2D(trace.HitPoint, end) < 8.0f;
    }

    private void DrawArena(Vector center)
    {
        const int segments = 24;
        for (var i = 0; i < segments; i++)
        {
            var a = MathF.PI * 2.0f * i / segments;
            var b = MathF.PI * 2.0f * (i + 1) / segments;

            AddBeam(
                new Vector(
                    center.X + MathF.Cos(a) * ArenaRadius,
                    center.Y + MathF.Sin(a) * ArenaRadius,
                    center.Z + 3.0f),
                new Vector(
                    center.X + MathF.Cos(b) * ArenaRadius,
                    center.Y + MathF.Sin(b) * ArenaRadius,
                    center.Z + 3.0f),
                Color.OrangeRed,
                2.5f);
        }

        if (_inmateAnchor is not null && _guardianAnchor is not null)
            AddBeam(_inmateAnchor, _guardianAnchor, Color.Gold, 1.5f);
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
        foreach (var beam in _markers.Where(x => x.IsValid))
            beam.Remove();
        _markers.Clear();
    }

    private static float Distance2D(Vector a, Vector b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static void KeepAtAnchor(CCSPlayerController player, Vector? anchor, QAngle? angle)
    {
        if (anchor is null || !LrPlayerRules.IsUsable(player)) return;
        var pawn = player.PlayerPawn.Value;
        var pos = pawn?.AbsOrigin;
        if (pawn is null || pos is null) return;
        var dx = pos.X - anchor.X;
        var dy = pos.Y - anchor.Y;
        if (MathF.Sqrt(dx * dx + dy * dy) > MovementTolerance)
            pawn.Teleport(anchor, angle, new Vector());
    }
}
