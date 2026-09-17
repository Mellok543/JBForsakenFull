using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using JBF.Api;

namespace JBF.LR.RussianRoulette;

public sealed class JBFRussianRoulette : BasePlugin
{
    private IDisposable? _registration;
    public override string ModuleName => "JBF LR: Russian Roulette";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Mell";
    public override void OnAllPluginsLoaded(bool hotReload)
    {
        var lr = LrCapability.Api.Get();
        if (lr is not null) _registration = lr.RegisterGame(new RussianRouletteGame());
    }
    public override void Unload(bool hotReload) => _registration?.Dispose();
}

internal sealed class RussianRouletteGame : ILrGame, ILrInventoryRules
{
    private ILrMatchContext? _context;
    public string Id => "russian-roulette";
    public string Name => "Русская рулетка";
    public int Order => 40;
    public bool DisableAllDamage => false;

    public void Start(ILrMatchContext context)
    {
        _context = context;
        Prepare(context.Inmate);
        Prepare(context.Guardian);
        var inmateStarts = Random.Shared.Next(0, 2) == 0;
        LrPlayerRules.SetAmmo(context.Inmate, inmateStarts ? 1 : 0, 0);
        LrPlayerRules.SetAmmo(context.Guardian, inmateStarts ? 0 : 1, 0);
    }

    public void Stop() => _context = null;

    public HookResult OnTakeDamage(CBaseEntity entity, CTakeDamageInfo damageInfo)
    {
        if (_context is null) return HookResult.Handled;
        var victim = entity.As<CCSPlayerPawn>().Controller.Value?.As<CCSPlayerController>();
        if (victim is null) return HookResult.Handled;

        // Every valid hit is a new chamber pull. Roughly one of six shots is lethal.
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
}
