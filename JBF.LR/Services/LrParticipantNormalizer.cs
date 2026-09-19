using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using JBF.Api;

namespace JBF.LR.Services;

internal sealed class LrParticipantNormalizer
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);
    private DateTime _nextUpdate;

    public void Tick()
    {
        var now = DateTime.UtcNow;
        if (now < _nextUpdate)
            return;

        _nextUpdate = now + Interval;

        var lr = LrCapability.Api.Get();
        if (lr?.IsActive != true)
            return;

        Normalize(lr.Inmate);
        Normalize(lr.Guardian);
    }

    private static void Normalize(CCSPlayerController? player)
    {
        if (player is not { IsValid: true, PawnIsAlive: true } ||
            player.PlayerPawn.Value is not { IsValid: true } pawn)
        {
            return;
        }

        // LR must be fair regardless of persistent VIP/perk effects.
        // Health/armor and inventory are normalized by LrService when the duel starts;
        // here we continuously neutralize movement perks that other plugins may reapply.
        if (Math.Abs(pawn.VelocityModifier - 1.0f) > 0.001f)
        {
            pawn.VelocityModifier = 1.0f;
            Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_flVelocityModifier");
        }

        if (Math.Abs(pawn.GravityScale - 1.0f) > 0.001f)
        {
            pawn.GravityScale = 1.0f;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_flGravityScale");
        }
    }
}
