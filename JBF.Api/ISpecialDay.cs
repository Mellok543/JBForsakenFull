using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public interface ISpecialDay
{
    string Id { get; }

    string Name { get; }

    int Order { get; }

    void Start(ISpecialDayContext context);

    void Stop();

    void OnPlayerSpawn(CCSPlayerController player);

    void OnPlayerDeath(CCSPlayerController? victim, CCSPlayerController? attacker);

    HookResult OnTakeDamage(CBaseEntity entity, CTakeDamageInfo damageInfo);
}
