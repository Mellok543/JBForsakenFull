using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using JBF.Api;

namespace JBF.LR.NoScope;

public sealed class JBFNoScope : BasePlugin
{
    private readonly NoScopeGroup _game = new();
    private ILrApi? _registeredApi;
    private IDisposable? _registration;
    public override string ModuleName => "JBF LR: No Scope";
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

internal sealed class NoScopeGroup : ILrGame, ILrGameVariants
{
    public string Id => "no-scope";
    public string Name => "Дуэль без прицела";
    public int Order => 20;
    public bool DisableAllDamage => false;
    public IReadOnlyList<ILrGame> Variants { get; } =
    [
        new NoScopeVariant("no-scope-awp", "AWP", 10, "weapon_awp"),
        new NoScopeVariant("no-scope-ssg08", "SSG 08", 20, "weapon_ssg08"),
        new NoScopeVariant("no-scope-scar20", "SCAR-20", 30, "weapon_scar20")
    ];
    public void Start(ILrMatchContext context) { }
    public void Stop() { }
}

internal sealed class NoScopeVariant(string id, string name, int order, string weaponName)
    : ILrGame, ILrInventoryRules
{
    private ILrMatchContext? _context;
    public string Id => id;
    public string Name => name;
    public int Order => order;
    public bool DisableAllDamage => false;

    public void Start(ILrMatchContext context)
    {
        _context = context;
        Give(context.Inmate);
        Give(context.Guardian);
    }

    public void Stop() => _context = null;

    public HookResult OnWeaponZoom(EventWeaponZoom @event, GameEventInfo info)
    {
        if (_context is null || @event.Userid is not { } player) return HookResult.Continue;
        if (player.Slot != _context.Inmate.Slot && player.Slot != _context.Guardian.Slot) return HookResult.Continue;
        player.PrintToCenter("No Scope");
        Give(player);
        return HookResult.Continue;
    }

    public bool IsWeaponAllowed(CBasePlayerWeapon weapon)
    {
        var designer = weapon.DesignerName ?? string.Empty;
        return designer.Contains("knife", StringComparison.OrdinalIgnoreCase) ||
               designer.Equals(weaponName, StringComparison.OrdinalIgnoreCase);
    }

    private void Give(CCSPlayerController player)
    {
        LrPlayerRules.Normalize(player);
        player.GiveNamedItem(weaponName);
    }
}
