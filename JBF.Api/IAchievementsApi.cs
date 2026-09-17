using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public interface IAchievementsApi
{
    bool IsUnlocked(CCSPlayerController player, string achievementId);

    long GetStat(CCSPlayerController player, string statKey);
}
