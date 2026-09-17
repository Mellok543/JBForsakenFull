using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using JBF.Api;
using JBF.Shop.Extensions;
using JBF.Shop.Services;

namespace JBF.Shop;

public sealed class JBFShop : BasePlugin
{
    private ShopService? _shop;
    private ILrApi? _lrApi;

    public override string ModuleName => "JBF Shop";
    public override string ModuleVersion => "1.1.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        _shop = new ShopService(this);
        Capabilities.RegisterPluginCapability(ShopCapability.Api, () => _shop!);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _lrApi = LrCapability.Api.Get();
        if (_lrApi is not null)
        {
            _lrApi.MatchEnded += OnLrMatchEnded;
        }
    }

    public override void Unload(bool hotReload)
    {
        if (_lrApi is not null)
        {
            _lrApi.MatchEnded -= OnLrMatchEnded;
            _lrApi = null;
        }

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

        command.ReplyToCommand(JailbreakChat.Format($"Ваш баланс: {_shop?.GetCredits(player) ?? 0} кредитов."));
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
        _shop?.RewardRoundPlayers();
        _shop?.ResetRound();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        _shop?.RewardKill(@event.Userid, @event.Attacker);
        return HookResult.Continue;
    }

    private void OnLrMatchEnded(LrMatchEndedEvent match)
    {
        _shop?.RewardLrMatch(match);
    }
}
