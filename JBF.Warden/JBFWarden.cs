using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using JBF.Warden.Api;
using JBF.Warden.Services;

namespace JBF.Warden;

public sealed class JBFWarden : BasePlugin
{
    private readonly WardenService _wardenService = new();
    private readonly IWardenApi _api;

    public JBFWarden()
    {
        _api = new WardenApi(_wardenService);
    }

    public override string ModuleName => "JBF Warden";
    public override string ModuleVersion => "1.3.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        Capabilities.RegisterPluginCapability(WardenCapability.Api, () => _api);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
    }

    [ConsoleCommand("css_w", "Become the warden and open the commander menu")]
    public void OnWardenCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CanBecomeWarden(player, command))
        {
            return;
        }

        if (!_wardenService.TryClaim(player!))
        {
            command.ReplyToCommand(JailbreakChat.Format("Командир уже выбран."));
            return;
        }

        command.ReplyToCommand(JailbreakChat.Format("Вы командир."));

        var commanderMenu = CommanderMenuCapability.Api.GetOptional();
        if (commanderMenu is null)
        {
            command.ReplyToCommand(JailbreakChat.Format("Модуль меню командира недоступен."));
            return;
        }

        commanderMenu.Open(player!);
    }

    [ConsoleCommand("css_uw", "Leave the warden role")]
    public void OnResignCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !_wardenService.TryResign(player))
        {
            command.ReplyToCommand(JailbreakChat.Format("Вы не являетесь командиром."));
            return;
        }

        MenuCapability.Api.GetOptional()?.Close(player);
        command.ReplyToCommand(JailbreakChat.Format("Вы покинули пост командира."));
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _wardenService.Reset();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _wardenService.Reset();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player is not null && _wardenService.IsWarden(player))
        {
            _wardenService.NotifyKilled(player, @event.Attacker, @event.Weapon);
            _wardenService.Reset();
            MenuCapability.Api.GetOptional()?.Close(player);
        }

        return HookResult.Continue;
    }

    private bool CanBecomeWarden(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !player.IsValid || !player.PawnIsAlive || player.Team != CsTeam.CounterTerrorist)
        {
            command.ReplyToCommand(JailbreakChat.Format("Команда доступна только живым игрокам КТ."));
            return false;
        }

        var jailbreakApi = JailbreakCapability.Api.GetOptional();
        if (jailbreakApi is null)
        {
            command.ReplyToCommand(JailbreakChat.Format("Core API недоступно."));
            return false;
        }

        if (!jailbreakApi.IsRoundActive)
        {
            command.ReplyToCommand(JailbreakChat.Format("Сейчас нет активного раунда."));
            return false;
        }

        if (SpecialDaysCapability.Api.GetOptional()?.IsActive == true)
        {
            command.ReplyToCommand(JailbreakChat.Format("Во время игрового дня выбрать командира нельзя."));
            return false;
        }

        if (LrCapability.Api.GetOptional()?.IsActive == true)
        {
            command.ReplyToCommand(JailbreakChat.Format("Во время LR выбрать командира нельзя."));
            return false;
        }

        return true;
    }

    private void OnClientDisconnect(int playerSlot)
    {
        if (_wardenService.Warden?.Slot == playerSlot)
        {
            _wardenService.Reset();
        }
    }
}
