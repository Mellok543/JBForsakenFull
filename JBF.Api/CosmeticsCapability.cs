using CounterStrikeSharp.API.Core.Capabilities;

namespace JBF.Api;

public static class CosmeticsCapability
{
    public static PluginCapability<ICosmeticsApi> Api { get; } = new("jbf:cosmetics");
}
