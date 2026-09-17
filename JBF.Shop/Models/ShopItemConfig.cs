namespace JBF.Shop.Models;

internal sealed class ShopItemConfig
{
    public bool Enabled { get; set; } = true;

    public int Cost { get; set; }

    public int RoundLimit { get; set; }
}
