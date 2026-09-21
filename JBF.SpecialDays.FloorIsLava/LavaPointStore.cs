using System.Text.Encodings.Web;
using System.Text.Json;

namespace JBF.SpecialDays.FloorIsLava;

internal sealed class LavaPointStore
{
    private readonly string _mapsDirectory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public LavaPointStore(string moduleDirectory)
    {
        _mapsDirectory = Path.Combine(moduleDirectory, "config", "maps");
        Directory.CreateDirectory(_mapsDirectory);
    }

    public LavaMapConfig Load(string mapName)
    {
        var path = GetPath(mapName);

        if (!File.Exists(path))
            return new LavaMapConfig { Map = mapName };

        try
        {
            var config = JsonSerializer.Deserialize<LavaMapConfig>(
                File.ReadAllText(path),
                JsonOptions);

            if (config is null)
                return new LavaMapConfig { Map = mapName };

            config.Map = mapName;
            config.Points ??= [];
            return config;
        }
        catch
        {
            return new LavaMapConfig { Map = mapName };
        }
    }

    public void Save(LavaMapConfig config)
    {
        Directory.CreateDirectory(_mapsDirectory);
        File.WriteAllText(
            GetPath(config.Map),
            JsonSerializer.Serialize(config, JsonOptions));
    }

    private string GetPath(string mapName)
    {
        var safe = string.Concat(
            mapName.Select(ch =>
                char.IsLetterOrDigit(ch) || ch is '_' or '-'
                    ? ch
                    : '_'));

        return Path.Combine(_mapsDirectory, $"{safe}.json");
    }
}
