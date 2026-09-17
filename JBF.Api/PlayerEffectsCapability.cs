using CounterStrikeSharp.API.Core.Capabilities;

namespace JBF.Api;

public static class PlayerEffectsCapability
{
    public static PluginCapability<IPlayerEffectsApi> Api { get; } = new("jbf:player-effects");
}
