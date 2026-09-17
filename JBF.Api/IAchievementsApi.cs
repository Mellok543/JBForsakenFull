using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public interface IAchievementsApi
{
    bool IsUnlocked(CCSPlayerController player, string achievementId);

    long GetStat(CCSPlayerController player, string statKey);

    bool TrySetStat(CCSPlayerController player, string statKey, long value);

    bool TryUnlock(CCSPlayerController player, string achievementId);

    bool TryResetAchievement(CCSPlayerController player, string achievementId);
}
