using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Timers;
using JBF.Api;
using JBF.SpecialDays.Services;

namespace JBF.SpecialDays;

public sealed class JBFSpecialDays : BasePlugin
{
    private readonly SpecialDaysService _specialDays = new();
    private IDisposable? _menuRegistration;
    private ICommanderMenuApi? _commanderMenu;

    public override string ModuleName => "JBF Special Days";
    public override string ModuleVersion => "1.1.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        Capabilities.RegisterPluginCapability(SpecialDaysCapability.Api, () => _specialDays);
        RegisterListener<Listeners.OnEntityTakeDamagePre>(_specialDays.HandleTakeDamage);
        AddTimer(2.0f, EnsureCommanderMenuRegistration, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        EnsureCommanderMenuRegistration();
    }

    public override void Unload(bool hotReload)
    {
        ReleaseCommanderMenuRegistration();
        _specialDays.Shutdown();
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _specialDays.StartPendingDay();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _specialDays.StopActiveDay();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        _specialDays.HandlePlayerSpawn(@event.Userid);
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        _specialDays.HandlePlayerDeath(@event.Userid, @event.Attacker);
        return HookResult.Continue;
    }

    private void EnsureCommanderMenuRegistration()
    {
        var current = CommanderMenuCapability.Api.Get();
        if (ReferenceEquals(current, _commanderMenu) && _menuRegistration is not null)
            return;

        ReleaseCommanderMenuRegistration();
        if (current is null)
            return;

        _menuRegistration = current.RegisterItem(
            new CommanderMenuItem("special-days", "Игровые дни", _specialDays.OpenSelectionMenu, 120));
        _commanderMenu = current;
    }

    private void ReleaseCommanderMenuRegistration()
    {
        _menuRegistration?.Dispose();
        _menuRegistration = null;
        _commanderMenu = null;
    }
}
