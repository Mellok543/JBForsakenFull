using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;

namespace JBF.LR.RussianRoulette;

public sealed class JBFRussianRoulette : BasePlugin
{
    private readonly RussianRouletteGame _game = new();
    private IDisposable? _registration;

    public override string ModuleName => "JBF LR: Russian Roulette";
    public override string ModuleVersion => "1.1.0";
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
    private const float DesiredSeparation = 140.0f;
    private const float MaxApproachPerPlayer = 128.0f;
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
        CaptureSafePositions(context);

        var inmateStarts = Random.Shared.Next(0, 2) == 0;
        LrPlayerRules.SetAmmo(context.Inmate, inmateStarts ? 1 : 0, 0);
        LrPlayerRules.SetAmmo(context.Guardian, inmateStarts ? 0 : 1, 0);
        UiCapability.Api.Get()?.Notify(context.Inmate, "Движение заблокировано на время рулетки", UiNotificationType.Info, 4.0f);
        UiCapability.Api.Get()?.Notify(context.Guardian, "Движение заблокировано на время рулетки", UiNotificationType.Info, 4.0f);
    }

    public void Stop()
    {
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

    private void CaptureSafePositions(ILrMatchContext context)
    {
        var a = context.Inmate.PlayerPawn.Value?.AbsOrigin;
        var b = context.Guardian.PlayerPawn.Value?.AbsOrigin;
        if (a is null || b is null) return;

        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var distance = MathF.Sqrt(dx * dx + dy * dy);

        var inmate = new Vector(a.X, a.Y, a.Z);
        var guardian = new Vector(b.X, b.Y, b.Z);

        // Pull both players toward each other, but cap the displacement so we do not perform
        // a large blind teleport across map geometry.
        if (distance > DesiredSeparation && distance > 1.0f)
        {
            var shift = MathF.Min((distance - DesiredSeparation) * 0.5f, MaxApproachPerPlayer);
            var nx = dx / distance;
            var ny = dy / distance;
            inmate.X += nx * shift;
            inmate.Y += ny * shift;
            guardian.X -= nx * shift;
            guardian.Y -= ny * shift;
        }

        _inmateAnchor = inmate;
        _guardianAnchor = guardian;

        var inmateYaw = MathF.Atan2(_guardianAnchor.Y - _inmateAnchor.Y, _guardianAnchor.X - _inmateAnchor.X) * 180.0f / MathF.PI;
        var guardianYaw = MathF.Atan2(_inmateAnchor.Y - _guardianAnchor.Y, _inmateAnchor.X - _guardianAnchor.X) * 180.0f / MathF.PI;
        _inmateAngle = new QAngle(0, inmateYaw, 0);
        _guardianAngle = new QAngle(0, guardianYaw, 0);

        context.Inmate.PlayerPawn.Value?.Teleport(_inmateAnchor, _inmateAngle, new Vector());
        context.Guardian.PlayerPawn.Value?.Teleport(_guardianAnchor, _guardianAngle, new Vector());
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
