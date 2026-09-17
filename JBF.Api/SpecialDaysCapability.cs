using CounterStrikeSharp.API.Core.Capabilities;

namespace JBF.Api;

public static class SpecialDaysCapability
{
    public static PluginCapability<ISpecialDaysApi> Api { get; } = new("jbf:special-days");
}
