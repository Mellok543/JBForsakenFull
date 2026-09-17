using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public interface IBattlePassApi
{
    string SeasonId { get; }
    string SeasonName { get; }

    void AddProgress(CCSPlayerController player, string objectiveId, int amount = 1);
    void AddXp(CCSPlayerController player, int amount, string reason = "");
    int GetXp(CCSPlayerController player);
    int GetLevel(CCSPlayerController player);
}
