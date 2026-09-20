using CounterStrikeSharp.API.Core.Capabilities;

namespace JBF.Api;

public static class CapabilityExtensions
{
    public static T? GetOptional<T>(this PluginCapability<T> capability)
        where T : class
    {
        try
        {
            return capability.Get();
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }
}
