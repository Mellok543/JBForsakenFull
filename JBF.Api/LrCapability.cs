using CounterStrikeSharp.API.Core.Capabilities;

namespace JBF.Api;

public static class LrCapability
{
    public static PluginCapability<ILrApi> Api { get; } = new("jbf:lr");
}
