using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Timers;
using JBF.Api;
using JBF.SpecialDays.Services;
using Microsoft.Extensions.Logging;

namespace JBF.SpecialDays;

public sealed class JBFSpecialDays : BasePlugin
{
    private SpecialDaysService? _specialDays;
    private IDisposable? _menuRegistration;
    private ICommanderMenuApi? _commanderMenu;

    public override string ModuleName => "JBF Special Days";
    public override string ModuleVersion => "2.0.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        _specialDays = new SpecialDaysService(
            message => Logger.LogInformation("{Message}", message));

        Capabilities.RegisterPluginCapability(SpecialDaysCapability.Api, () => _specialDays);

        RegisterListener<Listeners.OnEntityTakeDamagePre>(_specialDays.HandleTakeDamage);
        RegisterListener<Listeners.OnClientDisconnect>(_specialDays.HandleDisconnect);

        AddTimer(
            2.0f,
            EnsureCommanderMenuRegistration,
            TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        EnsureCommanderMenuRegistration();
    }

    public override void Unload(bool hotReload)
    {
        ReleaseCommanderMenuRegistration();
        _specialDays?.Shutdown();
        _specialDays = null;
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _specialDays?.OnRoundStart();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _specialDays?.OnRoundEnd();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        _specialDays?.OnPlayerSpawn(@event.Userid);
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        _specialDays?.OnPlayerDeath(@event.Userid, @event.Attacker);
        return HookResult.Continue;
    }

    private void EnsureCommanderMenuRegistration()
    {
        if (_specialDays is null)
            return;

        var current = CommanderMenuCapability.Api.GetOptional();
        if (current is null)
        {
            ReleaseCommanderMenuRegistration();
            return;
        }

        if (ReferenceEquals(current, _commanderMenu) && _menuRegistration is not null)
            return;

        ReleaseCommanderMenuRegistration();

        _menuRegistration = current.RegisterItem(
            new CommanderMenuItem(
                "special-days",
                "Игровые дни",
                _specialDays.OpenSelectionMenu,
                120));

        _commanderMenu = current;
    }

    private void ReleaseCommanderMenuRegistration()
    {
        _menuRegistration?.Dispose();
        _menuRegistration = null;
        _commanderMenu = null;
    }
}
