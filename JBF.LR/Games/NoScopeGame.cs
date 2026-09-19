using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using JBF.Api;
using JBF.LR.Extensions;

namespace JBF.LR.Games;

internal sealed class NoScopeGame : ILrGame
{
    private const CsItem Weapon = CsItem.AWP;

    private ILrMatchContext? _context;

    public string Id => "no-scope-awp";
    public string Name => "No Scope AWP";
    public int Order => 20;
    public bool DisableAllDamage => false;

    public void Start(ILrMatchContext context)
    {
        _context = context;
        GiveWeapon(context.Inmate);
        GiveWeapon(context.Guardian);
    }

    public void Stop()
    {
        _context = null;
    }

    public HookResult OnWeaponZoom(EventWeaponZoom @event, GameEventInfo info)
    {
        if (_context is null)
        {
            return HookResult.Continue;
        }

        var player = @event.Userid;
        if (player is null || (player.Slot != _context.Inmate.Slot && player.Slot != _context.Guardian.Slot))
        {
            return HookResult.Continue;
        }

        player.PrintToCenter("No Scope");
        player.RemoveWeapons();
        player.GiveNamedItem(CsItem.Knife);
        player.GiveNamedItem(Weapon);
        return HookResult.Continue;
    }

    private static void GiveWeapon(CCSPlayerController player)
    {
        player.ResetForLr();
        player.GiveNamedItem(Weapon);
    }
}
