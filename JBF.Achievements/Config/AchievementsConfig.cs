using System.Text.Json;
using JBF.Achievements.Models;

namespace JBF.Achievements.Config;

internal sealed class AchievementsConfig
{
    public AchievementDatabaseConfig Database { get; set; } = new();
    public AchievementCaps Caps { get; set; } = new();
    public List<AchievementDefinition> Achievements { get; set; } = CreateDefaults();

    public static AchievementsConfig LoadOrCreate(string path, Action<string>? log = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };

        if (!File.Exists(path))
        {
            var created = new AchievementsConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(created, options));
            return created;
        }

        try
        {
            return JsonSerializer.Deserialize<AchievementsConfig>(File.ReadAllText(path), options) ?? new AchievementsConfig();
        }
        catch (Exception ex)
        {
            log?.Invoke(ex.Message);
            return new AchievementsConfig();
        }
    }

    private static List<AchievementDefinition> CreateDefaults() =>
    [
        new() { Id = "warden-500", Name = "Я здесь главный", Description = "Стать командиром 500 раз.", Category = "Командир", StatKey = "warden_claims", Target = 500, Reward = new() { BonusHealth = 10 } },
        new() { Id = "ct-wins-1000", Name = "Надзиратель", Description = "Победить 1000 раундов за КТ.", Category = "Охрана", StatKey = "ct_round_wins", Target = 1000, Reward = new() { BonusArmor = 15 } },
        new() { Id = "kill-ct-750", Name = "Бунтарь", Description = "Убить 750 игроков КТ за T.", Category = "Заключённый", StatKey = "ct_kills_as_t", Target = 750, Reward = new() { SpeedMultiplier = 1.03f } },
        new() { Id = "lr-wins-100", Name = "Последняя надежда", Description = "Победить 100 раз в LR.", Category = "LR", StatKey = "lr_wins", Target = 100, Reward = new() { SpawnItem = "weapon_smokegrenade" } },
        new() { Id = "rounds-5000", Name = "Ветеран", Description = "Сыграть 5000 раундов.", Category = "Общие", StatKey = "rounds_played", Target = 5000, Reward = new() { BonusHealth = 5 } },
        new() { Id = "knife-250", Name = "Без патронов", Description = "Убить 250 противников ножом.", Category = "Общие", StatKey = "knife_kills", Target = 250, Reward = new() { GravityMultiplier = 0.97f } },
        new() { Id = "secret-one-hp", Name = "Последний вздох", Description = "Выжить в раунде, имея ровно 1 HP.", Category = "Секретные", StatKey = "survive_with_1hp", Target = 1, Secret = true, Reward = new() { BonusHealth = 5 } },
        new() { Id = "secret-warden-knife", Name = "Переворот", Description = "Убить командира ножом.", Category = "Секретные", StatKey = "warden_knife_kills", Target = 1, Secret = true, Reward = new() { SpawnItem = "weapon_flashbang" } }
    ];
}

internal sealed class AchievementDatabaseConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public string Port { get; set; } = "3306";
    public string Database { get; set; } = "jbf";
    public string User { get; set; } = "jbf";
    public string Password { get; set; } = "CHANGE_ME";
    public string TablePrefix { get; set; } = "jbf_";
}

internal sealed class AchievementCaps
{
    public int MaxBonusHealth { get; set; } = 30;
    public int MaxBonusArmor { get; set; } = 30;
    public float MaxSpeedMultiplier { get; set; } = 1.10f;
    public float MinGravityMultiplier { get; set; } = 0.85f;
}
