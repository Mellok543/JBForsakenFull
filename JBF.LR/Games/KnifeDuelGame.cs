using CounterStrikeSharp.API.Modules.Entities.Constants;
using JBF.Api;
using JBF.LR.Extensions;

namespace JBF.LR.Games;

internal sealed class KnifeDuelGame : ILrGame
{
    public string Id => "knife-duel";
    public string Name => "На ножах";
    public int Order => 10;
    public bool DisableAllDamage => false;

    public void Start(ILrMatchContext context)
    {
        context.Inmate.ResetForLr();
        context.Guardian.ResetForLr();
        context.Inmate.GiveNamedItem(CsItem.Knife);
        context.Guardian.GiveNamedItem(CsItem.Knife);
    }

    public void Stop()
    {
    }
}
