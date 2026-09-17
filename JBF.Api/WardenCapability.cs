using CounterStrikeSharp.API.Core.Capabilities;

namespace JBF.Api;

public static class WardenCapability
{
    public static PluginCapability<IWardenApi> Api { get; } = new("jbf:warden");
}
