using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using JBF.Api;

namespace JBF.Cosmetics.Models;

internal sealed class CosmeticsConfig
{
    public CosmeticsDatabaseConfig Database { get; set; } = new();
    public List<CosmeticDefinition> Items { get; set; } =
    [
        new()
        {
            Id = "frozen_forsaken_crown",
            Name = "Frozen Forsaken Crown",
            Description = "Ледяная корона зимнего сезона JBForsaken.",
            Category = CosmeticCategory.Head,
            Rarity = "Legendary",
            Source = "BattlePass",
            AssetPath = "models/cosmetics/frozen_forsaken_crown/frozen_forsaken_crown.vmdl",
            Attachment = "head",
            Offset = [0.0f, 0.0f, 0.0f],
            Rotation = [0.0f, 0.0f, 0.0f],
            Scale = 1.0f,
            PreviewKey = "crown",
            Enabled = true
        }
    ];

    public static CosmeticsConfig LoadOrCreate(string path, Action<string>? log = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        options.Converters.Add(new JsonStringEnumConverter());

        if (!File.Exists(path))
        {
            var created = new CosmeticsConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(created, options));
            return created;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<CosmeticsConfig>(File.ReadAllText(path), options) ?? new CosmeticsConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(loaded, options));
            return loaded;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Cosmetics config load failed: {ex.Message}");
            return new CosmeticsConfig();
        }
    }
    public static bool TryLoad(string path, out CosmeticsConfig config, out string error)
    {
        config = new CosmeticsConfig();
        error = string.Empty;

        try
        {
            if (!File.Exists(path))
            {
                error = "Файл не найден.";
                return false;
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            options.Converters.Add(new JsonStringEnumConverter());

            config = JsonSerializer.Deserialize<CosmeticsConfig>(File.ReadAllText(path), options)
                     ?? throw new InvalidDataException("Пустой или некорректный JSON.");

            var duplicate = config.Items
                .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1);
            if (duplicate is not null)
                throw new InvalidDataException($"Повторяющийся или пустой cosmetic Id: '{duplicate.Key}'.");

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

}

internal sealed class CosmeticsDatabaseConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public string Port { get; set; } = "3306";
    public string Database { get; set; } = "jbforsaken";
    public string User { get; set; } = "jbf";
    public string Password { get; set; } = "change_me";
    public string TablePrefix { get; set; } = "jbf_";
}

internal sealed class CosmeticDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public CosmeticCategory Category { get; set; }
    public string Rarity { get; set; } = "Common";
    public string Source { get; set; } = "BattlePass";
    public string AssetPath { get; set; } = "";
    public string Attachment { get; set; } = "";
    public float[] Offset { get; set; } = [0.0f, 0.0f, 0.0f];
    public float[] Rotation { get; set; } = [0.0f, 0.0f, 0.0f];
    public float Scale { get; set; } = 1.0f;
    public string PreviewKey { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
