using System.Text.Json;

namespace JBF.Shop.Models;

internal sealed class ShopConfig
{
    public ShopDatabaseConfig Database { get; set; } = new();

    public ShopRewardsConfig Rewards { get; set; } = new();

    public ShopItemsConfig Items { get; set; } = new();

    public static ShopConfig LoadOrCreate(string path, Action<string>? logError = null)
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
        catch (Exception exception)
        {
            logError?.Invoke($"Failed to load shop config '{path}': {exception.Message}");
            return new ShopConfig();
        }
    }

    public static bool TryLoad(string path, out ShopConfig config, out string error)
    {
        config = new ShopConfig();
        error = string.Empty;

        try
        {
            if (!File.Exists(path))
            {
                error = "Файл конфигурации не найден.";
                return false;
            }

            config = JsonSerializer.Deserialize<ShopConfig>(File.ReadAllText(path), JsonOptions())
                     ?? throw new InvalidDataException("Пустой или некорректный JSON.");

            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }
}
