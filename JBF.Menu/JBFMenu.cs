using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using JBF.Api;
using JBF.Menu.Services;

namespace JBF.Menu;

public sealed class JBFMenu : BasePlugin
{
    private readonly MenuService _menuService = new();

    public override string ModuleName => "JBF Menu";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        Capabilities.RegisterPluginCapability(MenuCapability.Api, () => _menuService);
        RegisterListener<Listeners.OnPlayerButtonsChanged>(_menuService.HandleButtonsChanged);
        RegisterListener<Listeners.OnClientDisconnect>(_menuService.HandleClientDisconnect);
        RegisterListener<Listeners.OnTick>(_menuService.Render);
    }
}
