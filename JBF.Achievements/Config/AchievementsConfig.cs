using System.Text.Encodings.Web;
using System.Text.Json;
using JBF.Achievements.Models;

namespace JBF.Achievements.Config;

internal sealed class AchievementsConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public AchievementDatabaseConfig Database { get; set; } = new();
    public AchievementCaps Caps { get; set; } = new();
    public List<AchievementDefinition> Achievements { get; set; } = CreateDefaults();

    public static AchievementsConfig LoadOrCreate(string path, Action<string>? log = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            var created = new AchievementsConfig();
            Save(path, created);
            return created;
        }

        try
        {
            var config = JsonSerializer.Deserialize<AchievementsConfig>(File.ReadAllText(path), JsonOptions) ?? new AchievementsConfig();
            var existing = config.Achievements.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var achievement in CreateDefaults())
                if (!existing.Contains(achievement.Id)) config.Achievements.Add(achievement);
            Save(path, config);
            return config;
        }
        catch (Exception ex)
        {
            log?.Invoke(ex.Message);
            return new AchievementsConfig();
        }
    }

    private static void Save(string path, AchievementsConfig config) =>
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));

    private static List<AchievementDefinition> CreateDefaults() =>
    [
        new() { Id="rounds-100", Name="Новичок", Description="Сыграть 100 раундов.", Category="Общие", StatKey="rounds_played", Target=100, Reward=new(){ BonusHealth=2 } },
        new() { Id="rounds-500", Name="Постоянный игрок", Description="Сыграть 500 раундов.", Category="Общие", StatKey="rounds_played", Target=500, Reward=new(){ BonusHealth=3 } },
        new() { Id="rounds-5000", Name="Ветеран", Description="Сыграть 5000 раундов.", Category="Общие", StatKey="rounds_played", Target=5000, Reward=new(){ BonusHealth=5 } },
        new() { Id="kills-500", Name="Опытный боец", Description="Совершить 500 убийств.", Category="Общие", StatKey="kills_total", Target=500, Reward=new(){ BonusArmor=5 } },
        new() { Id="kills-2500", Name="Машина", Description="Совершить 2500 убийств.", Category="Общие", StatKey="kills_total", Target=2500, Reward=new(){ BonusHealth=5 } },
        new() { Id="headshots-100", Name="Точная рука", Description="Совершить 100 убийств в голову.", Category="Общие", StatKey="headshot_kills", Target=100, Reward=new(){ BonusArmor=3 } },
        new() { Id="headshots-500", Name="Снайпер", Description="Совершить 500 убийств в голову.", Category="Общие", StatKey="headshot_kills", Target=500, Reward=new(){ BonusArmor=5 } },
        new() { Id="knife-50", Name="Ближний бой", Description="Убить 50 противников ножом.", Category="Общие", StatKey="knife_kills", Target=50, Reward=new(){ GravityMultiplier=0.99f } },
        new() { Id="knife-250", Name="Без патронов", Description="Убить 250 противников ножом.", Category="Общие", StatKey="knife_kills", Target=250, Reward=new(){ GravityMultiplier=0.97f } },
        new() { Id="grenade-25", Name="Гренадёр", Description="Убить 25 противников HE-гранатой.", Category="Общие", StatKey="grenade_kills", Target=25, Reward=new(){ SpawnItem="weapon_hegrenade" } },
        new() { Id="ct-wins-100", Name="На посту", Description="Победить 100 раундов за КТ.", Category="Охрана", StatKey="ct_round_wins", Target=100, Reward=new(){ BonusArmor=3 } },
        new() { Id="ct-wins-500", Name="Старший охранник", Description="Победить 500 раундов за КТ.", Category="Охрана", StatKey="ct_round_wins", Target=500, Reward=new(){ BonusArmor=5 } },
        new() { Id="ct-wins-1000", Name="Надзиратель", Description="Победить 1000 раундов за КТ.", Category="Охрана", StatKey="ct_round_wins", Target=1000, Reward=new(){ BonusArmor=15 } },
        new() { Id="t-wins-100", Name="Выживший", Description="Победить 100 раундов за T.", Category="Заключённый", StatKey="t_round_wins", Target=100, Reward=new(){ BonusHealth=3 } },
        new() { Id="t-wins-500", Name="Старожил камеры", Description="Победить 500 раундов за T.", Category="Заключённый", StatKey="t_round_wins", Target=500, Reward=new(){ BonusHealth=5 } },
        new() { Id="kill-ct-100", Name="Непослушный", Description="Убить 100 игроков КТ за T.", Category="Заключённый", StatKey="ct_kills_as_t", Target=100, Reward=new(){ SpeedMultiplier=1.01f } },
        new() { Id="kill-ct-300", Name="Опасный заключённый", Description="Убить 300 игроков КТ за T.", Category="Заключённый", StatKey="ct_kills_as_t", Target=300, Reward=new(){ SpeedMultiplier=1.02f } },
        new() { Id="kill-ct-750", Name="Бунтарь", Description="Убить 750 игроков КТ за T.", Category="Заключённый", StatKey="ct_kills_as_t", Target=750, Reward=new(){ SpeedMultiplier=1.03f } },
        new() { Id="warden-50", Name="Голос порядка", Description="Стать командиром 50 раз.", Category="Командир", StatKey="warden_claims", Target=50, Reward=new(){ BonusArmor=3 } },
        new() { Id="warden-200", Name="Опытный КМД", Description="Стать командиром 200 раз.", Category="Командир", StatKey="warden_claims", Target=200, Reward=new(){ BonusHealth=5 } },
        new() { Id="warden-500", Name="Я здесь главный", Description="Стать командиром 500 раз.", Category="Командир", StatKey="warden_claims", Target=500, Reward=new(){ BonusHealth=10 } },
        new() { Id="lr-play-50", Name="Последний шанс", Description="Принять участие в 50 LR.", Category="LR", StatKey="lr_participations", Target=50, Reward=new(){ BonusHealth=2 } },
        new() { Id="lr-wins-25", Name="Дуэлянт", Description="Победить 25 раз в LR.", Category="LR", StatKey="lr_wins", Target=25, Reward=new(){ BonusArmor=3 } },
        new() { Id="lr-wins-100", Name="Последняя надежда", Description="Победить 100 раз в LR.", Category="LR", StatKey="lr_wins", Target=100, Reward=new(){ SpawnItem="weapon_smokegrenade" } },
        new() { Id="secret-one-hp", Name="Последний вздох", Description="Выжить в раунде, имея ровно 1 HP.", Category="Секретные", StatKey="survive_with_1hp", Target=1, Secret=true, Reward=new(){ BonusHealth=5 } },
        new() { Id="secret-warden-knife", Name="Переворот", Description="Убить командира ножом.", Category="Секретные", StatKey="warden_knife_kills", Target=1, Secret=true, Reward=new(){ SpawnItem="weapon_flashbang" } },
        new() { Id="secret-kill-1hp", Name="На волоске", Description="Убить противника, имея ровно 1 HP.", Category="Секретные", StatKey="kills_with_1hp", Target=1, Secret=true, Reward=new(){ BonusArmor=5 } }
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
