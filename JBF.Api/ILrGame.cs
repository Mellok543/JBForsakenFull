using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public interface ILrGame
{
    string Id { get; }

    string Name { get; }

    int Order { get; }

    bool DisableAllDamage { get; }

    void Start(ILrMatchContext context);

    void Stop();

    HookResult OnTakeDamage(CBaseEntity entity, CTakeDamageInfo damageInfo)
    {
        return HookResult.Continue;
    }

    HookResult OnBulletImpact(EventBulletImpact @event, GameEventInfo info)
    {
        return HookResult.Continue;
    }

    HookResult OnWeaponZoom(EventWeaponZoom @event, GameEventInfo info)
    {
        return HookResult.Continue;
    }
}
