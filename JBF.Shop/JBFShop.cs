using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Timers;
using JBF.Api;
using JBF.Shop.Extensions;
using JBF.Shop.Services;

namespace JBF.Shop;

public sealed class JBFShop : BasePlugin
{
    private ShopService? _shop;
    private ILrApi? _lrApi;
    private IPlayerStateApi? _playerStateApi;

    public override string ModuleName => "JBF Shop";
    public override string ModuleVersion => "1.4.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        _shop = new ShopService(this);
        Capabilities.RegisterPluginCapability(ShopCapability.Api, () => _shop!);
        RegisterListener<Listeners.OnTick>(OnTick);
        AddTimer(2.0f, RefreshSubscriptions, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        RefreshSubscriptions();
    }

    public override void Unload(bool hotReload)
    {
        UnsubscribeFromLr();
        UnsubscribeFromPlayerState();
        _shop?.Shutdown();
        _shop = null;
    }

    [ConsoleCommand("css_shop", "Open JBF shop")]
    public void OnShopCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            command.ReplyToCommand(JailbreakChat.Format("Команда доступна только игрокам."));
            return;
        }

        _shop?.OpenShop(player);
    }

    [ConsoleCommand("css_credits", "Show JBF credits")]
    public void OnCreditsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            command.ReplyToCommand(JailbreakChat.Format("Команда доступна только игрокам."));
            return;
        }

        if (!player.IsUsable())
        {
            command.ReplyToCommand(JailbreakChat.Format("Игрок недоступен."));
            return;
        }

        _shop?.PrintCreditStatus(player);
    }


    [ConsoleCommand("css_shop_reload", "Reload JBF shop config")]
    [RequiresPermissions("@jbf/admin")]
    public void OnShopReload(CCSPlayerController? player, CommandInfo command)
    {
        if (_shop is null)
        {
            command.ReplyToCommand(JailbreakChat.Format("Shop ещё не готов."));
            return;
        }

        if (!_shop.TryReloadConfig(out var error))
        {
            command.ReplyToCommand(JailbreakChat.Format($"Ошибка JBF.Shop.json: {error}"));
            return;
        }

        command.ReplyToCommand(JailbreakChat.Format("Shop config перезагружен."));
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _shop?.ResetRound();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _shop?.RewardRoundPlayers((CounterStrikeSharp.API.Modules.Utils.CsTeam)@event.Winner);
        _shop?.ResetRound();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        _shop?.RewardKill(@event.Userid, @event.Attacker);
        return HookResult.Continue;
    }

    private void OnTick()
    {
        _shop?.Tick();
    }

    private void RefreshSubscriptions()
    {
        EnsureLrSubscription();
        EnsurePlayerStateSubscription();
    }

    private void EnsureLrSubscription()
    {
        var current = LrCapability.Api.Get();
        if (ReferenceEquals(current, _lrApi))
            return;

        UnsubscribeFromLr();
        _lrApi = current;
        if (_lrApi is not null)
            _lrApi.MatchEnded += OnLrMatchEnded;
    }

    private void UnsubscribeFromLr()
    {
        if (_lrApi is not null)
            _lrApi.MatchEnded -= OnLrMatchEnded;

        _lrApi = null;
    }

    private void EnsurePlayerStateSubscription()
    {
        var current = PlayerStateCapability.Api.Get();
        if (ReferenceEquals(current, _playerStateApi))
            return;

        UnsubscribeFromPlayerState();
        _playerStateApi = current;

        if (_playerStateApi is null)
            return;

        _playerStateApi.RebelStarted += OnRebelStarted;
        _playerStateApi.RebelKilled += OnRebelKilled;
    }

    private void UnsubscribeFromPlayerState()
    {
        if (_playerStateApi is not null)
        {
            _playerStateApi.RebelStarted -= OnRebelStarted;
            _playerStateApi.RebelKilled -= OnRebelKilled;
        }

        _playerStateApi = null;
    }

    private void OnRebelStarted(CCSPlayerController player)
    {
        _shop?.MarkRebel(player);
    }

    private void OnRebelKilled(RebelKilledEvent data)
    {
        _shop?.RewardRebelKilled(data);
    }

    private void OnLrMatchEnded(LrMatchEndedEvent match)
    {
        _shop?.RewardLrMatch(match);
    }
}
