using System.Text.Encodings.Web;
using System.Text.Json;

namespace JBF.MainMenu.Config;

internal sealed class MainMenuConfig
{
    public string MenuTitle { get; set; } = "JBForsaken • Главное меню";

    public List<MainMenuItemConfig> MenuItems { get; set; } =
    [
        new() { Text = "Магазин", Command = "css_shop" },
        new() { Text = "Боевой пропуск", Command = "css_bp" },
        new() { Text = "Косметика", Command = "css_cosmetics" },
        new() { Text = "Достижения", Command = "css_achievements" },
        new() { Text = "Статистика", Command = "css_stats" }
    ];

    public string HelpHeader { get; set; } = "Доступные команды:";

    public List<HelpCommandConfig> HelpCommands { get; set; } =
    [
        new() { Command = "!menu", Description = "Открыть главное меню" },
        new() { Command = "!shop", Description = "Открыть магазин" },
        new() { Command = "!bp", Description = "Открыть боевой пропуск" },
        new() { Command = "!cosmetics", Description = "Открыть меню косметики" },
        new() { Command = "!achievements", Description = "Открыть достижения" },
        new() { Command = "!stats", Description = "Открыть статистику" },
        new() { Command = "!help", Description = "Показать список команд" }
    ];

    public static MainMenuConfig LoadOrCreate(string path, Action<string>? log = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
        {
            var created = new MainMenuConfig();
            Save(path, created);
            return created;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<MainMenuConfig>(
                             File.ReadAllText(path),
                             JsonOptions())
                         ?? new MainMenuConfig();

            Save(path, loaded);
            return loaded;
        }
        catch (Exception ex)
        {
            log?.Invoke($"MainMenu config load failed: {ex.Message}");
            return new MainMenuConfig();
        }
    }

    public static bool TryLoad(string path, out MainMenuConfig config, out string error)
    {
        config = new MainMenuConfig();
        error = string.Empty;

        try
        {
            if (!File.Exists(path))
            {
                error = "Файл не найден.";
                return false;
            }

            config = JsonSerializer.Deserialize<MainMenuConfig>(
                         File.ReadAllText(path),
                         JsonOptions())
                     ?? throw new InvalidDataException("Пустой или некорректный JSON.");

            if (string.IsNullOrWhiteSpace(config.MenuTitle))
                config.MenuTitle = "JBForsaken • Главное меню";

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void Save(string path, MainMenuConfig config)
        => File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions()));

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

internal sealed class MainMenuItemConfig
{
    public string Text { get; set; } = "";
    public string Command { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

internal sealed class HelpCommandConfig
{
    public string Command { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
