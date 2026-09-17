using CounterStrikeSharp.API.Core.Capabilities;

namespace JBF.Api;

public static class AchievementsCapability
{
    public static PluginCapability<IAchievementsApi> Api { get; } = new("jbf:achievements");
}
