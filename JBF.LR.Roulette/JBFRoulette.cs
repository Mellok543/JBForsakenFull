using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using JBF.Api;

namespace JBF.LR.Roulette;

public sealed class JBFRoulette : BasePlugin
{
    private readonly RouletteGame _game = new();
    private ILrApi? _registeredApi;
    private IDisposable? _registration;
    public override string ModuleName => "JBF LR: Roulette";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Mell";
    public override void Load(bool hotReload)
    {
        AddTimer(2.0f, EnsureRegistration, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        EnsureRegistration();
    }
    public override void Unload(bool hotReload)
    {
        _registration?.Dispose();
        _registration = null;
        _registeredApi = null;
    }
    private void EnsureRegistration()
    {
        var current = LrCapability.Api.GetOptional();
        if (ReferenceEquals(current, _registeredApi))
            return;

        _registration?.Dispose();
        _registration = null;
        _registeredApi = current;

        if (current is not null)
            _registration = current.RegisterGame(_game);
    }
}

internal sealed class RouletteGame : ILrGame, ILrInventoryRules
{
    private ILrMatchContext? _context;
    public string Id => "roulette";
    public string Name => "Рулетка";
    public int Order => 30;
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
