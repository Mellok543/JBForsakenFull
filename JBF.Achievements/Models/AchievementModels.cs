namespace JBF.Achievements.Models;

internal sealed class AchievementDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Общие";
    public string StatKey { get; set; } = string.Empty;
    public long Target { get; set; }
    public bool Secret { get; set; }
    public AchievementReward Reward { get; set; } = new();
}

internal sealed class AchievementReward
{
    public int BonusHealth { get; set; }
    public int BonusArmor { get; set; }
    public float SpeedMultiplier { get; set; } = 1.0f;
    public float GravityMultiplier { get; set; } = 1.0f;
    public string? SpawnItem { get; set; }
}

internal sealed class PlayerAchievementState
{
    public ulong SteamId { get; init; }
    public Dictionary<string, long> Stats { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Unlocked { get; } = new(StringComparer.OrdinalIgnoreCase);
}

internal readonly record struct AchievementBonuses(
    int BonusHealth,
    int BonusArmor,
    float SpeedMultiplier,
    float GravityMultiplier,
    IReadOnlyList<string> SpawnItems);
