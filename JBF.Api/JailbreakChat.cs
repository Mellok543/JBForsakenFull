using CounterStrikeSharp.API.Modules.Utils;

namespace JBF.Api;

public static class JailbreakChat
{
    public static string Format(string message)
    {
        return $"{ChatColors.Gold}[JBF]{ChatColors.Default} {message}";
    }
}
