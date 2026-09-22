namespace JBF.Shop.Models;

internal sealed class ShopItemsConfig
{
    public ShopItemConfig Hp50 { get; set; } = new()
    {
        Cost = 25,
        RoundLimit = 1
    };

    public ShopItemConfig Hp100 { get; set; } = new()
    {
        Cost = 45,
        RoundLimit = 1
    };

    public ShopItemConfig Armor50 { get; set; } = new()
    {
        Cost = 20,
        RoundLimit = 1
    };

    public ShopItemConfig Armor100 { get; set; } = new()
    {
        Cost = 35,
        RoundLimit = 1
    };

    public ShopItemConfig Smoke { get; set; } = new()
    {
        Cost = 30,
        RoundLimit = 2
    };

    public ShopItemConfig Flash { get; set; } = new()
    {
        Cost = 25,
        RoundLimit = 2
    };

    public ShopItemConfig Speed { get; set; } = new()
    {
        Cost = 45,
        RoundLimit = 1
    };

    public ShopItemConfig Gravity { get; set; } = new()
    {
        Cost = 45,
        RoundLimit = 1
    };

    public ShopItemConfig DoubleJump { get; set; } = new()
    {
        Cost = 55,
        RoundLimit = 1
    };

    public ShopItemConfig Bhop { get; set; } = new()
    {
        Cost = 60,
        RoundLimit = 1
    };

    public ShopItemConfig SmallModel { get; set; } = new()
    {
        Cost = 50,
        RoundLimit = 1
    };

    public ShopItemConfig GuardDisguise { get; set; } = new()
    {
        Cost = 65,
        RoundLimit = 1
    };
}
