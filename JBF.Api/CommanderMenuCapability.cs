using CounterStrikeSharp.API.Core.Capabilities;

namespace JBF.Api;

public static class CommanderMenuCapability
{
    public static PluginCapability<ICommanderMenuApi> Api { get; } = new("jbf:commander-menu");
}
