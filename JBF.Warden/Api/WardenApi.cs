using CounterStrikeSharp.API.Core;
using JBF.Api;
using JBF.Warden.Services;

namespace JBF.Warden.Api;

internal sealed class WardenApi : IWardenApi
{
    private readonly WardenService _wardenService;

    public WardenApi(WardenService wardenService)
    {
        _wardenService = wardenService;
    }

    public CCSPlayerController? Warden => _wardenService.Warden;

    public bool IsWarden(CCSPlayerController player)
    {
        return _wardenService.IsWarden(player);
    }

    public bool TryResign(CCSPlayerController player)
    {
        return _wardenService.TryResign(player);
    }
}
