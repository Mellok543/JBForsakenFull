using CounterStrikeSharp.API.Core.Capabilities;

namespace JBF.Api;

public static class BattlePassCapability
{
    public static PluginCapability<IBattlePassApi> Api { get; } = new("jbf:battlepass");
}
