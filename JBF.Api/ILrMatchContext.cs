using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public interface ILrMatchContext
{
    CCSPlayerController Inmate { get; }

    CCSPlayerController Guardian { get; }

    void Finish(CCSPlayerController? winner);
}
