using CounterStrikeSharp.API.Core.Capabilities;

namespace JBF.Api;

public static class ShopCapability
{
    public static PluginCapability<IShopApi> Api { get; } = new("jbf:shop");
}
