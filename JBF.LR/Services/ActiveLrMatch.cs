using CounterStrikeSharp.API.Core;
using JBF.Api;

namespace JBF.LR.Services;

internal sealed class ActiveLrMatch
{
    public ActiveLrMatch(ILrGame game, CCSPlayerController inmate, CCSPlayerController guardian)
    {
        Game = game;
        Inmate = inmate;
        Guardian = guardian;
    }

    public ILrGame Game { get; }

    public CCSPlayerController Inmate { get; }

    public CCSPlayerController Guardian { get; }
}
