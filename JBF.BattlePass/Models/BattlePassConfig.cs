using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

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
            Save(path, created);
            return created;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<BattlePassConfig>(File.ReadAllText(path), JsonOptions()) ?? new BattlePassConfig();

            foreach (var mission in DefaultMissions())
            {
                if (loaded.Missions.All(existing => !existing.Id.Equals(mission.Id, StringComparison.OrdinalIgnoreCase)))
                    loaded.Missions.Add(mission);
            }

            // Rewrite an existing config once with readable UTF-8 characters instead of \uXXXX escapes.
            Save(path, loaded);
            return loaded;
        }
        catch (Exception ex)
        {
            log?.Invoke($"BattlePass config load failed: {ex.Message}");
            return new BattlePassConfig();
        }
    }

    public static bool TryLoad(string path, out BattlePassConfig config, out string error)
    {
        config = new BattlePassConfig();
        error = string.Empty;

        try
        {
            if (!File.Exists(path))
            {
                error = "Файл не найден.";
                return false;
            }

            config = JsonSerializer.Deserialize<BattlePassConfig>(File.ReadAllText(path), JsonOptions())
                     ?? throw new InvalidDataException("Пустой или некорректный JSON.");

            if (string.IsNullOrWhiteSpace(config.SeasonId))
                throw new InvalidDataException("SeasonId не может быть пустым.");
            if (config.XpPerLevel <= 0)
                throw new InvalidDataException("XpPerLevel должен быть больше 0.");
            if (config.MaxLevel <= 0)
                throw new InvalidDataException("MaxLevel должен быть больше 0.");

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void Save(string path, BattlePassConfig config)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions()));
    }

    private static JsonSerializerOptions JsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static List<MissionDefinition> DefaultMissions() =>
    [
        // Daily pool. Three are selected per player every day.
        new("daily-rounds-5", "Сыграть 5 раундов", "round_played", 5, 250, MissionPeriod.Daily),
        new("daily-kills-5", "Сделать 5 убийств", "kill", 5, 300, MissionPeriod.Daily),
        new("daily-headshots-3", "Сделать 3 убийства в голову", "headshot_kill", 3, 350, MissionPeriod.Daily),
        new("daily-survive-3", "Выжить в 3 раундах", "round_survived", 3, 300, MissionPeriod.Daily),
        new("daily-t-rounds-4", "Сыграть 4 раунда за заключённых", "t_round_played", 4, 300, MissionPeriod.Daily),
        new("daily-ct-rounds-3", "Сыграть 3 раунда за охрану", "ct_round_played", 3, 325, MissionPeriod.Daily),
        new("daily-lawful-2", "Закончить 2 раунда за T без бунта", "lawful_round", 2, 375, MissionPeriod.Daily),
        new("daily-rebel-1", "Стать бунтарём", "became_rebel", 1, 300, MissionPeriod.Daily),
        new("daily-kill-guard-2", "Убить 2 охранников за T", "kill_guard", 2, 375, MissionPeriod.Daily),
        new("daily-kill-inmate-3", "Убить 3 заключённых за CT", "kill_inmate", 3, 375, MissionPeriod.Daily),
        new("daily-kill-rebel-1", "Убить бунтаря за CT", "kill_rebel", 1, 400, MissionPeriod.Daily),
        new("daily-warden-1", "Стать начальником", "warden_claim", 1, 300, MissionPeriod.Daily),
        new("daily-lr-play-1", "Сыграть один LR", "lr_play", 1, 300, MissionPeriod.Daily),
        new("daily-lr-win-1", "Выиграть один LR", "lr_win", 1, 425, MissionPeriod.Daily),
        new("daily-team-win-2", "Победить в 2 раундах со своей командой", "team_win", 2, 350, MissionPeriod.Daily),
        new("daily-time-20", "Провести 20 минут на сервере", "play_minute", 20, 350, MissionPeriod.Daily),
        new("daily-freeday-1", "Получить FreeDay", "freeday_received", 1, 300, MissionPeriod.Daily),
        new("daily-knife-1", "Сделать убийство ножом", "knife_kill", 1, 450, MissionPeriod.Daily),

        // Weekly pool. Three are selected per player every week.
        new("weekly-rounds-30", "Сыграть 30 раундов", "round_played", 30, 1000, MissionPeriod.Weekly),
        new("weekly-kills-35", "Сделать 35 убийств", "kill", 35, 1200, MissionPeriod.Weekly),
        new("weekly-headshots-12", "Сделать 12 убийств в голову", "headshot_kill", 12, 1250, MissionPeriod.Weekly),
        new("weekly-survive-15", "Выжить в 15 раундах", "round_survived", 15, 1100, MissionPeriod.Weekly),
        new("weekly-lawful-10", "Закончить 10 раундов за T без бунта", "lawful_round", 10, 1300, MissionPeriod.Weekly),
        new("weekly-rebel-6", "Стать бунтарём в 6 раундах", "became_rebel", 6, 1250, MissionPeriod.Weekly),
        new("weekly-kill-guard-10", "Убить 10 охранников за T", "kill_guard", 10, 1450, MissionPeriod.Weekly),
        new("weekly-kill-inmate-20", "Убить 20 заключённых за CT", "kill_inmate", 20, 1450, MissionPeriod.Weekly),
        new("weekly-kill-rebel-5", "Убить 5 бунтарей за CT", "kill_rebel", 5, 1500, MissionPeriod.Weekly),
        new("weekly-warden-5", "Стать начальником 5 раз", "warden_claim", 5, 1100, MissionPeriod.Weekly),
        new("weekly-lr-play-6", "Сыграть 6 LR", "lr_play", 6, 1250, MissionPeriod.Weekly),
        new("weekly-lr-win-4", "Выиграть 4 LR", "lr_win", 4, 1600, MissionPeriod.Weekly),
        new("weekly-team-win-12", "Победить в 12 раундах со своей командой", "team_win", 12, 1300, MissionPeriod.Weekly),
        new("weekly-time-180", "Провести 3 часа на сервере", "play_minute", 180, 1600, MissionPeriod.Weekly),
        new("weekly-knife-4", "Сделать 4 убийства ножом", "knife_kill", 4, 1700, MissionPeriod.Weekly),

        // Season objectives are always active.
        new("season-rounds-200", "Сыграть 200 раундов", "round_played", 200, 4000, MissionPeriod.Season),
        new("season-kills-200", "Сделать 200 убийств", "kill", 200, 4500, MissionPeriod.Season),
        new("season-headshots-60", "Сделать 60 убийств в голову", "headshot_kill", 60, 4500, MissionPeriod.Season),
        new("season-lawful-60", "Закончить 60 раундов за T без бунта", "lawful_round", 60, 5000, MissionPeriod.Season),
        new("season-rebel-30", "Стать бунтарём в 30 раундах", "became_rebel", 30, 4500, MissionPeriod.Season),
        new("season-kill-rebel-25", "Убить 25 бунтарей за CT", "kill_rebel", 25, 5000, MissionPeriod.Season),
        new("season-warden-25", "Стать начальником 25 раз", "warden_claim", 25, 4000, MissionPeriod.Season),
        new("season-lr-play-30", "Сыграть 30 LR", "lr_play", 30, 4500, MissionPeriod.Season),
        new("season-lr-win-20", "Выиграть 20 LR", "lr_win", 20, 5500, MissionPeriod.Season),
        new("season-team-win-75", "Победить в 75 раундах со своей командой", "team_win", 75, 5000, MissionPeriod.Season),
        new("season-time-600", "Провести 10 часов на сервере", "play_minute", 600, 5000, MissionPeriod.Season)
    ];

    private static List<RewardDefinition> DefaultRewards()
    {
        var list = new List<RewardDefinition>();
        for (var level = 1; level <= 30; level++)
        {
            if (level % 10 == 0)
                list.Add(new(level, RewardType.Credits, 750 + level * 10, null, $"Большая награда • {750 + level * 10} кредитов", "credits"));
            else if (level % 5 == 0)
                list.Add(new(level, RewardType.Weapon, 1, "weapon_deagle", "Desert Eagle ×1", "deagle"));
            else if (level % 4 == 0)
                list.Add(new(level, RewardType.Consumable, 1, "weapon_hegrenade", "HE Grenade ×1", "hegrenade"));
            else if (level % 3 == 0)
                list.Add(new(level, RewardType.Consumable, 1, "weapon_smokegrenade", "Smoke ×1", "smokegrenade"));
            else if (level % 2 == 0)
                list.Add(new(level, RewardType.Consumable, 1, "weapon_flashbang", "Flashbang ×1", "flashbang"));
            else
                list.Add(new(level, RewardType.Credits, 100 + level * 10, null, $"{100 + level * 10} кредитов", "credits"));
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
internal sealed record RewardDefinition(
    int Level,
    RewardType Type,
    int Amount,
    string? ItemId,
    string Name,
    string? ImageKey = null,
    string? ImagePath = null);

internal enum MissionPeriod { Daily, Weekly, Season }
internal enum RewardType { Credits, Consumable, Weapon, Cosmetic, Vip, Effect }
