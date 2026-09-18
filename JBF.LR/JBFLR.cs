using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using JBF.Api;
using JBF.LR.Services;

namespace JBF.LR;

public sealed class JBFLR : BasePlugin
{
    private readonly LrService _lr = new();

    public override string ModuleName => "JBF LR Core";
    public override string ModuleVersion => "2.1.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        Capabilities.RegisterPluginCapability(LrCapability.Api, () => _lr);
        RegisterListener<Listeners.OnEntityTakeDamagePre>(_lr.HandleTakeDamage);
        RegisterListener<Listeners.OnClientDisconnect>(_lr.HandleDisconnect);
        RegisterListener<Listeners.OnTick>(_lr.Tick);
    }

    public override void Unload(bool hotReload)
    {
        _lr.ResetRound();
    }

    [ConsoleCommand("css_lr", "Open Last Request menu")]
    public void OnLrCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            command.ReplyToCommand(JailbreakChat.Format("Команда доступна только игрокам."));
            return;
        }

        _lr.OpenMenu(player);
    }

    [ConsoleCommand("css_lrstatus", "Show current Last Request status")]
    public void OnLrStatusCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!_lr.IsActive)
        {
            command.ReplyToCommand(JailbreakChat.Format("Сейчас нет активного LR."));
            return;
        }

        command.ReplyToCommand(JailbreakChat.Format(
            $"Текущий LR: {_lr.ActiveGameName} | T: {_lr.Inmate?.PlayerName} | CT: {_lr.Guardian?.PlayerName}."));
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _lr.ResetRound();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _lr.ResetRound();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        _lr.HandlePlayerDeath(@event.Userid);
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnBulletImpact(EventBulletImpact @event, GameEventInfo info) => _lr.HandleBulletImpact(@event, info);

    [GameEventHandler]
    public HookResult OnWeaponZoom(EventWeaponZoom @event, GameEventInfo info) => _lr.HandleWeaponZoom(@event, info);
}
