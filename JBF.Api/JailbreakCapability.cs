using CounterStrikeSharp.API.Core.Capabilities;

namespace JBF.Api;

public static class JailbreakCapability
{
    public static PluginCapability<IJailbreakApi>  Api { get; } = new("jbf:core");
}