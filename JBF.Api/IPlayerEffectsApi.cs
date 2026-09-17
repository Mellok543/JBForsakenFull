using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public interface IPlayerEffectsApi
{
    void ApplySpeed(CCSPlayerController player, float multiplier, float durationSeconds, string source);
    void ApplyGravity(CCSPlayerController player, float scale, float durationSeconds, string source);
    void Clear(CCSPlayerController player);
    void ClearAll();
}
