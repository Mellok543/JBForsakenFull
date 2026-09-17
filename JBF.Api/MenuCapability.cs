using CounterStrikeSharp.API.Core.Capabilities;

namespace JBF.Api;

public static class MenuCapability
{
    public static PluginCapability<IMenuApi> Api { get; } = new("jbf:menu");
}
