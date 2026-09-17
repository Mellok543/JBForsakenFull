using CounterStrikeSharp.API.Core;
using JBF.Api;

namespace JBF.LR.Services;

internal sealed class LrMatchContext : ILrMatchContext
{
    private readonly Action<CCSPlayerController?> _finish;

    public LrMatchContext(
        CCSPlayerController inmate,
        CCSPlayerController guardian,
        Action<CCSPlayerController?> finish)
    {
        Inmate = inmate;
        Guardian = guardian;
        _finish = finish;
    }

    public CCSPlayerController Inmate { get; }

    public CCSPlayerController Guardian { get; }

    public void Finish(CCSPlayerController? winner)
    {
        _finish(winner);
    }
}
