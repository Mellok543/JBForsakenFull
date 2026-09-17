using CounterStrikeSharp.API.Core.Capabilities;

namespace JBF.Api;

public static class PlayerStateCapability
{
    public static PluginCapability<IPlayerStateApi> Api { get; } = new("jbf:player-state");
}
