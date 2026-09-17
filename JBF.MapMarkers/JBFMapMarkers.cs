using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using JBF.MapMarkers.Services;

namespace JBF.MapMarkers;

public sealed class JBFMapMarkers : BasePlugin
{
    private readonly MapMarkerService _markerService = new();

    public override string ModuleName => "JBF Map Markers";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnTick>(_markerService.Tick);
    }

    public override void Unload(bool hotReload)
    {
        _markerService.Reset();
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _markerService.Reset();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerPing(EventPlayerPing @event, GameEventInfo info)
    {
        return _markerService.HandlePlayerPing(@event);
    }
}
