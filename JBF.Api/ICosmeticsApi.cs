using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public interface ICosmeticsApi
{
    bool Owns(CCSPlayerController player, string cosmeticId);
    bool Grant(CCSPlayerController player, string cosmeticId, string source = "Admin");
    bool Equip(CCSPlayerController player, string cosmeticId);
    bool Unequip(CCSPlayerController player, CosmeticCategory category);
    string? GetEquipped(CCSPlayerController player, CosmeticCategory category);
}
