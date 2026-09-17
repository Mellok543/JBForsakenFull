using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using JBF.Api;
using JBF.Menu.Services;
using Microsoft.Extensions.Logging;

namespace JBF.Menu;

public sealed class JBFMenu : BasePlugin
{
    private MenuService? _menuService;

    public override string ModuleName => "JBF Menu";
    public override string ModuleVersion => "3.0.1";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        _menuService = new MenuService(
            "panorama/layout/custom_game/jbf_menu.xml",
            message => Logger.LogInformation("{Message}", message));

        _menuService.Start(this, hotReload);

        Capabilities.RegisterPluginCapability(MenuCapability.Api, () => _menuService);
        Capabilities.RegisterPluginCapability(UiCapability.Api, () => _menuService);
        RegisterListener<Listeners.OnPlayerButtonsChanged>(_menuService.HandleButtonsChanged);
        RegisterListener<Listeners.OnClientDisconnect>(_menuService.HandleClientDisconnect);
    }

    public override void Unload(bool hotReload)
    {
        _menuService?.Stop(this);
        _menuService = null;
    }
}
