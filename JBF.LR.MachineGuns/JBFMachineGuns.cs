using CounterStrikeSharp.API.Core;
using JBF.Api;

namespace JBF.LR.MachineGuns;

public sealed class JBFMachineGuns : BasePlugin
{
    private IDisposable? _registration;
    public override string ModuleName => "JBF LR: Machine Guns";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Mell";
    public override void OnAllPluginsLoaded(bool hotReload)
    {
        var lr = LrCapability.Api.Get();
        if (lr is not null) _registration = lr.RegisterGame(new MachineGunGame());
    }
    public override void Unload(bool hotReload) => _registration?.Dispose();
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
