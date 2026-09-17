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

    public JBFCore()
    {
        _api = new JailbreakApi(_roundService);
    }

    public override string ModuleName => "JBF Core";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        Capabilities.RegisterPluginCapability(JailbreakCapability.Api, () => _api);
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _roundService.RoundStart();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _roundService.RoundEnd();
        return HookResult.Continue;
    }
}
