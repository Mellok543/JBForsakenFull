using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public interface ILrInventoryRules
{
    bool IsWeaponAllowed(CBasePlayerWeapon weapon);
}
