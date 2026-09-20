using CounterStrikeSharp.API.Core.Capabilities;

namespace JBF.Api;

public static class WeekCycleCapability
{
    public static PluginCapability<IWeekCycleApi> Api { get; } = new("jbf:week-cycle");
}
