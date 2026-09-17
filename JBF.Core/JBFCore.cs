using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using JBF.Api;
using JBF.Core.Api;
using JBF.Core.Services;

namespace JBF.Core;

public sealed class JBFCore : BasePlugin
{
    private readonly RoundService _roundService = new();
    private readonly IJailbreakApi _api;
    private readonly PlayerEffectsService _effects;

    public JBFCore()
    {
        _api = new JailbreakApi(_roundService);
        _effects = new PlayerEffectsService(this);
    }

    public override string ModuleName => "JBF Core";
    public override string ModuleVersion => "1.1.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        Capabilities.RegisterPluginCapability(JailbreakCapability.Api, () => _api);
        Capabilities.RegisterPluginCapability(PlayerEffectsCapability.Api, () => _effects);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _effects.ClearAll();
        _roundService.RoundStart();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _effects.ClearAll();
        _roundService.RoundEnd();
        return HookResult.Continue;
    }

    public override void Unload(bool hotReload)
    {
        _effects.ClearAll();
    }

    private void OnClientDisconnect(int playerSlot)
    {
        var player = CounterStrikeSharp.API.Utilities.GetPlayerFromSlot(playerSlot);
        if (player is not null)
            _effects.Clear(player);
    }
}
