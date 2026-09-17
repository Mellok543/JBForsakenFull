using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public interface IShopApi
{
    void ReloadConfig();

    bool TryGetCredits(CCSPlayerController player, out int credits);

    bool TrySetCredits(CCSPlayerController player, int credits, out int newBalance);

    bool TryAddCredits(CCSPlayerController player, int amount, out int newBalance);
}
