namespace JBF.BattlePass.Models;

internal sealed class BattlePassPlayerState
{
    public ulong SteamId { get; init; }
    public string PlayerName { get; set; } = string.Empty;
    public int Xp { get; set; }
    public HashSet<int> ClaimedLevels { get; } = [];
    public Dictionary<string, int> MissionProgress { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> CompletedMissionPeriods { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> Inventory { get; } = new(StringComparer.OrdinalIgnoreCase);
}
