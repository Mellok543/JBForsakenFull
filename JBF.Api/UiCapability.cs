using CounterStrikeSharp.API.Core.Capabilities;

namespace JBF.Api;

public static class UiCapability
{
    public static PluginCapability<IUiApi> Api { get; } = new("jbf:ui");
}
