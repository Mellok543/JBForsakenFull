using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using JBF.Api;

namespace JBF.Core.Services;

internal sealed class PlayerEffectsService : IPlayerEffectsApi
{
    private const float Tolerance = 0.001f;

    private readonly BasePlugin _plugin;
    private readonly Dictionary<(ulong SteamId, string Kind, string Source), EffectState> _effects = [];
    private long _version;

    public PlayerEffectsService(BasePlugin plugin)
    {
        _plugin = plugin;
    }

    public void ApplySpeed(CCSPlayerController player, float multiplier, float durationSeconds, string source)
    {
        Apply(player, "speed", source, multiplier, durationSeconds,
            pawn => pawn.VelocityModifier,
            (pawn, value) =>
            {
                pawn.VelocityModifier = value;
                Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_flVelocityModifier");
            });
    }

    public void ApplyGravity(CCSPlayerController player, float scale, float durationSeconds, string source)
    {
        Apply(player, "gravity", source, scale, durationSeconds,
            pawn => pawn.GravityScale,
            (pawn, value) =>
            {
                pawn.GravityScale = value;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_flGravityScale");
            });
    }

    public void Clear(CCSPlayerController player)
    {
        var steamId = player.AuthorizedSteamID?.SteamId64;
        if (steamId is null)
            return;

        foreach (var key in _effects.Keys.Where(key => key.SteamId == steamId.Value).ToArray())
            _effects.Remove(key);
    }

    public void ClearAll() => _effects.Clear();

    private void Apply(
        CCSPlayerController player,
        string kind,
        string source,
        float value,
        float durationSeconds,
        Func<CCSPlayerPawn, float> getter,
        Action<CCSPlayerPawn, float> setter)
    {
        if (!player.IsValid || !player.PawnIsAlive || player.PlayerPawn.Value is not { IsValid: true } pawn)
            throw new InvalidOperationException("Player is not alive.");

        var steamId = player.AuthorizedSteamID?.SteamId64
            ?? throw new InvalidOperationException("SteamID is unavailable.");

        var key = (steamId, kind, source);
        var version = Interlocked.Increment(ref _version);
        var original = _effects.TryGetValue(key, out var previous) ? previous.OriginalValue : getter(pawn);

        _effects[key] = new EffectState(version, original, value);
        setter(pawn, value);

        _plugin.AddTimer(Math.Max(0.05f, durationSeconds), () =>
        {
            if (!_effects.TryGetValue(key, out var effect) || effect.Version != version)
                return;

            _effects.Remove(key);

            if (!player.IsValid || !player.PawnIsAlive || player.AuthorizedSteamID?.SteamId64 != steamId ||
                player.PlayerPawn.Value is not { IsValid: true } currentPawn)
                return;

            if (Math.Abs(getter(currentPawn) - effect.AppliedValue) > Tolerance)
                return;

            setter(currentPawn, effect.OriginalValue);
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private sealed record EffectState(long Version, float OriginalValue, float AppliedValue);
}
