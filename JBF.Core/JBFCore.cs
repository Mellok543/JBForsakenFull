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
    public override string ModuleVersion => "1.2.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        Capabilities.RegisterPluginCapability(JailbreakCapability.Api, () => _api);
        Capabilities.RegisterPluginCapability(PlayerEffectsCapability.Api, () => _effects);
        Capabilities.RegisterPluginCapability(PlayerStateCapability.Api, () => _playerState);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        RegisterListener<Listeners.OnEntityTakeDamagePre>(OnTakeDamage);
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

    private HookResult OnTakeDamage(CBaseEntity entity, CTakeDamageInfo damageInfo)
    {
        if (entity.DesignerName != "player")
            return HookResult.Continue;

        var victim = entity.As<CCSPlayerPawn>().Controller.Value?.As<CCSPlayerController>();
        var attackerEntity = damageInfo.Attacker.Value;
        if (victim is null || !victim.IsValid || attackerEntity?.DesignerName != "player")
            return HookResult.Continue;

        var attacker = attackerEntity.As<CCSPlayerPawn>().Controller.Value?.As<CCSPlayerController>();
        if (attacker is null || !attacker.IsValid || attacker.Slot == victim.Slot)
            return HookResult.Continue;

        // Rebel is intentionally silent: it is only internal state for statistics/achievements.
        if (attacker.Team == CsTeam.Terrorist && victim.Team == CsTeam.CounterTerrorist)
            _playerState.MarkRebel(attacker);

        return HookResult.Continue;
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
