using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using JBF.Core.Api;
using JBF.Core.Services;

namespace JBF.Core;

public sealed class JBFCore : BasePlugin
{
    private readonly RoundService _roundService = new();
    private readonly IJailbreakApi _api;
    private readonly PlayerEffectsService _effects;
    private readonly PlayerStateService _playerState = new();

    public JBFCore()
    {
        _api = new JailbreakApi(_roundService);
        _effects = new PlayerEffectsService(this);
    }

    public override string ModuleName => "JBF Core";
    public override string ModuleVersion => "1.2.1";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        Capabilities.RegisterPluginCapability(JailbreakCapability.Api, () => _api);
        Capabilities.RegisterPluginCapability(PlayerEffectsCapability.Api, () => _effects);
        Capabilities.RegisterPluginCapability(PlayerStateCapability.Api, () => _playerState);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _effects.ClearAll();
        _playerState.ResetRound();
        _roundService.RoundStart();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _effects.ClearAll();
        _playerState.ResetRound();
        _roundService.RoundEnd();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        var victim = @event.Userid;
        var attacker = @event.Attacker;

        if (victim is null || attacker is null ||
            !victim.IsValid || !attacker.IsValid ||
            attacker.Slot == victim.Slot)
            return HookResult.Continue;

        // Rebel is intentionally silent and exists only for state/statistics/achievements.
        // Count actual health damage after armor; the threshold is cumulative for the round.
        if (attacker.Team == CsTeam.Terrorist && victim.Team == CsTeam.CounterTerrorist)
            _playerState.AddDamageToCt(attacker, @event.DmgHealth);

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if (@event.Userid is { IsValid: true } victim)
            _playerState.NotifyDeath(victim, @event.Attacker);

        return HookResult.Continue;
    }

    public override void Unload(bool hotReload)
    {
        _effects.ClearAll();
        _playerState.ResetRound();
    }

    private void OnClientDisconnect(int playerSlot)
    {
        var player = CounterStrikeSharp.API.Utilities.GetPlayerFromSlot(playerSlot);
        if (player is null)
            return;

        _effects.Clear(player);
        _playerState.RemovePlayer(player);
    }
}
