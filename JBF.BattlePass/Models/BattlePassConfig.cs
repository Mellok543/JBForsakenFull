using System.Text.Json;

namespace JBF.BattlePass.Models;

internal sealed class BattlePassConfig
{
    public string SeasonId { get; set; } = "winter-2026";
    public string SeasonName { get; set; } = "WINTER SEASON 2026";
    public int XpPerLevel { get; set; } = 1000;
    public int MaxLevel { get; set; } = 30;
    public DatabaseConfig Database { get; set; } = new();
    public List<MissionDefinition> Missions { get; set; } = DefaultMissions();
    public List<RewardDefinition> Rewards { get; set; } = DefaultRewards();

    public static BattlePassConfig LoadOrCreate(string path, Action<string>? log = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            var created = new BattlePassConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(created, JsonOptions()));
            return created;
        }

        try
        {
            return JsonSerializer.Deserialize<BattlePassConfig>(File.ReadAllText(path), JsonOptions()) ?? new BattlePassConfig();
        }
        catch (Exception ex)
        {
            log?.Invoke($"BattlePass config load failed: {ex.Message}");
            return new BattlePassConfig();
        }
    }

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private static List<MissionDefinition> DefaultMissions() =>
    [
        new("daily-rounds", "Сыграть 5 раундов", "round_played", 5, 250, MissionPeriod.Daily),
        new("daily-kills", "Сделать 5 убийств", "kill", 5, 300, MissionPeriod.Daily),
        new("daily-time", "Провести 15 минут на сервере", "play_minute", 15, 300, MissionPeriod.Daily),
        new("weekly-rounds", "Сыграть 25 раундов", "round_played", 25, 900, MissionPeriod.Weekly),
        new("weekly-lr", "Выиграть 3 LR", "lr_win", 3, 1000, MissionPeriod.Weekly),
        new("weekly-kills", "Сделать 40 убийств", "kill", 40, 1200, MissionPeriod.Weekly),
        new("season-rounds", "Сыграть 200 раундов", "round_played", 200, 4000, MissionPeriod.Season),
        new("season-lr", "Выиграть 25 LR", "lr_win", 25, 4500, MissionPeriod.Season),
        new("season-time", "Провести 10 часов на сервере", "play_minute", 600, 5000, MissionPeriod.Season)
    ];

    private static List<RewardDefinition> DefaultRewards()
    {
        var list = new List<RewardDefinition>();
        for (var level = 1; level <= 30; level++)
        {
            if (level % 10 == 0)
                list.Add(new(level, RewardType.Credits, 750 + level * 10, null, $"Большая награда • {750 + level * 10} кредитов"));
            else if (level % 5 == 0)
                list.Add(new(level, RewardType.Weapon, 1, "weapon_deagle", "Desert Eagle ×1"));
            else if (level % 4 == 0)
                list.Add(new(level, RewardType.Consumable, 1, "weapon_hegrenade", "HE Grenade ×1"));
            else if (level % 3 == 0)
                list.Add(new(level, RewardType.Consumable, 1, "weapon_smokegrenade", "Smoke ×1"));
            else if (level % 2 == 0)
                list.Add(new(level, RewardType.Consumable, 1, "weapon_flashbang", "Flashbang ×1"));
            else
                list.Add(new(level, RewardType.Credits, 100 + level * 10, null, $"{100 + level * 10} кредитов"));
        }
        return list;
    }
}

internal sealed class DatabaseConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public string Port { get; set; } = "3306";
    public string Database { get; set; } = "jbforsaken";
    public string User { get; set; } = "jbf";
    public string Password { get; set; } = "change_me";
    public string TablePrefix { get; set; } = "jbf_";
}

internal sealed record MissionDefinition(string Id, string Name, string ObjectiveId, int Target, int XpReward, MissionPeriod Period);
internal sealed record RewardDefinition(int Level, RewardType Type, int Amount, string? ItemId, string Name);

internal enum MissionPeriod { Daily, Weekly, Season }
internal enum RewardType { Credits, Consumable, Weapon, Cosmetic, Vip, Effect }
