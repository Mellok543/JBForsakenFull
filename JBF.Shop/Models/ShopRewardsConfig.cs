namespace JBF.Shop.Models;

internal sealed class ShopRewardsConfig
{
    public int RoundParticipation { get; set; } = 5;

    public int RoundSurvival { get; set; } = 3;

    public int InmateKillGuard { get; set; } = 4;

    public int LrParticipation { get; set; } = 3;

    public int LrWin { get; set; } = 15;

    // T: consecutive rounds without becoming a rebel.
    public int LawfulStreakBase { get; set; } = 2;
    public int LawfulStreakStep { get; set; } = 2;
    public int LawfulStreakMaxReward { get; set; } = 20;

    // T: consecutive rounds in which the player became a rebel.
    public int RebelStreakBase { get; set; } = 3;
    public int RebelStreakStep { get; set; } = 2;
    public int RebelStreakMaxReward { get; set; } = 21;

    // CT: consecutive rounds served as a guard.
    public int GuardDutyStreakBase { get; set; } = 3;
    public int GuardDutyStreakStep { get; set; } = 1;
    public int GuardDutyStreakMaxReward { get; set; } = 12;

    public int GuardKillRebel { get; set; } = 6;

    public int InmateTeamWin { get; set; } = 4;

    public int GuardTeamWin { get; set; } = 5;
}
