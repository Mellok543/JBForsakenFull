namespace JBF.Shop.Models;

internal sealed class ShopRewardsConfig
{
    public int RoundParticipation { get; set; } = 5;

    public int RoundSurvival { get; set; } = 3;

    public int InmateKillGuard { get; set; } = 4;

    public int LrParticipation { get; set; } = 3;

    public int LrWin { get; set; } = 15;
}
