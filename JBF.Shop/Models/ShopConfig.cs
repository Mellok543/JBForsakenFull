using System.Text.Json;

namespace JBF.Shop.Models;

internal sealed class ShopConfig
{
    public ShopDatabaseConfig Database { get; set; } = new();

    public ShopRewardsConfig Rewards { get; set; } = new();

    public ShopItemsConfig Items { get; set; } = new();

    public static ShopConfig LoadOrCreate(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
        {
            var created = new ShopConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(created, JsonOptions()));
            return created;
        }

        try
        {
            return JsonSerializer.Deserialize<ShopConfig>(File.ReadAllText(path), JsonOptions()) ?? new ShopConfig();
        }
        catch
        {
            return new ShopConfig();
        }
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true
        };
    }
}
