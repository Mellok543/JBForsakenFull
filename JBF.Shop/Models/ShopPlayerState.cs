namespace JBF.Shop.Models;

internal sealed class ShopPlayerState
{
    public ulong SteamId { get; set; }

    public string PlayerName { get; set; } = string.Empty;

    public int Credits { get; set; }

    public int LawfulStreak { get; set; }

    public int RebelStreak { get; set; }

    public int GuardDutyStreak { get; set; }
}
