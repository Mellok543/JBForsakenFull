using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using JBF.Api;

namespace JBF.LR.MachineGuns;

public sealed class JBFMachineGuns : BasePlugin
{
    private readonly MachineGunGame _game = new();
    private ILrApi? _registeredApi;
    private IDisposable? _registration;
    public override string ModuleName => "JBF LR: Machine Guns";
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
        var current = LrCapability.Api.Get();
        if (ReferenceEquals(current, _registeredApi))
            return;

        _registration?.Dispose();
        _registration = null;
        _registeredApi = current;

        if (current is not null)
            _registration = current.RegisterGame(_game);
    }
}

internal sealed class MachineGunGame : ILrGame, ILrInventoryRules
{
    public string Id => "machine-guns";
    public string Name => "Битва на пулемётах";
    public int Order => 50;
    public bool DisableAllDamage => false;

    public void Start(ILrMatchContext context)
    {
        Prepare(context.Inmate);
        Prepare(context.Guardian);
    }

    public void Stop() { }

    public bool IsWeaponAllowed(CBasePlayerWeapon weapon)
    {
        var name = weapon.DesignerName ?? string.Empty;
        return name.Contains("knife", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("weapon_negev", StringComparison.OrdinalIgnoreCase);
    }

    private static void Prepare(CCSPlayerController player)
    {
        LrPlayerRules.Normalize(player, 500, 100);
        player.GiveNamedItem("weapon_negev");
    }
}
