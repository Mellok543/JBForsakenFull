using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public interface ILrApi
{
    event Action<LrMatchEndedEvent>? MatchEnded;

    bool IsActive { get; }

    string? ActiveGameName { get; }

    CCSPlayerController? Inmate { get; }

    CCSPlayerController? Guardian { get; }

    IDisposable RegisterGame(ILrGame game);

    void OpenMenu(CCSPlayerController player);

    bool TryEndActive();
}
