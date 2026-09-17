using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using JBF.Api;
using JBF.Menu.Services;

namespace JBF.Menu;

public sealed class JBFMenu : BasePlugin
{
    private MenuService? _menuService;

    public override string ModuleName => "JBF Menu";
    public override string ModuleVersion => "2.0.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        _menuService = new MenuService(
            "panorama/layout/custom_game/jbf_menu.xml",
            message => Logger.LogInformation("{Message}", message));

        _menuService.Start(this, hotReload);

        Capabilities.RegisterPluginCapability(MenuCapability.Api, () => _menuService);
        RegisterListener<Listeners.OnPlayerButtonsChanged>(_menuService.HandleButtonsChanged);
        RegisterListener<Listeners.OnClientDisconnect>(_menuService.HandleClientDisconnect);
    }

    public override void Unload(bool hotReload)
    {
        _menuService?.Stop(this);
        _menuService = null;
    }
}
