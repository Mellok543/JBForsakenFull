using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using JBF.Api;
using JBF.LR.Extensions;

namespace JBF.LR.Games;

internal sealed class RouletteGame : ILrGame
{
    private ILrMatchContext? _context;

    public string Id => "roulette";
    public string Name => "Рулетка";
    public int Order => 30;
    public bool DisableAllDamage => false;

    public void Start(ILrMatchContext context)
    {
        _context = context;
        context.Inmate.ResetForLr();
        context.Guardian.ResetForLr();
        context.Inmate.GiveNamedItem(CsItem.DesertEagle);
        context.Guardian.GiveNamedItem(CsItem.DesertEagle);

        var inmateStarts = Random.Shared.Next(0, 2) == 0;
        context.Inmate.SetAmmo(inmateStarts ? 1 : 0, 0);
        context.Guardian.SetAmmo(inmateStarts ? 0 : 1, 0);
    }

    public void Stop()
    {
        _context = null;
    }

    public HookResult OnBulletImpact(EventBulletImpact @event, GameEventInfo info)
    {
        if (_context is null || @event.Userid is null)
        {
            return HookResult.Continue;
        }

        if (@event.Userid.Slot == _context.Inmate.Slot)
        {
            _context.Inmate.SetAmmo(0, 0);
            _context.Guardian.SetAmmo(1, 0);
        }
        else if (@event.Userid.Slot == _context.Guardian.Slot)
        {
            _context.Guardian.SetAmmo(0, 0);
            _context.Inmate.SetAmmo(1, 0);
        }

        return HookResult.Continue;
    }
}
