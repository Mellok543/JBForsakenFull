using CounterStrikeSharp.API.Core;

namespace JBF.Warden.Services;

internal sealed class WardenService
{
    public CCSPlayerController? Warden { get; private set; }

    public bool IsWarden(CCSPlayerController player)
    {
        return Warden is not null && Warden.IsValid && Warden.Slot == player.Slot;
    }

    public bool TryClaim(CCSPlayerController player)
    {
        if (Warden is not null && Warden.IsValid)
        {
            return IsWarden(player);
        }

        Warden = player;
        return true;
    }

    public bool TryResign(CCSPlayerController player)
    {
        if (!IsWarden(player))
        {
            return false;
        }

        Warden = null;
        return true;
    }

    public void Reset()
    {
        Warden = null;
    }
}
