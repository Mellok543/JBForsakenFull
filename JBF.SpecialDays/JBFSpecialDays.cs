using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using JBF.Api;
using JBF.SpecialDays.Services;
using Microsoft.Extensions.Logging;

namespace JBF.SpecialDays;

public sealed class JBFSpecialDays : BasePlugin
{
    private readonly SpecialDaysService _specialDays = new();
    private IDisposable? _menuRegistration;

    public override string ModuleName => "JBF Special Days";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        Capabilities.RegisterPluginCapability(SpecialDaysCapability.Api, () => _specialDays);
        RegisterListener<Listeners.OnEntityTakeDamagePre>(_specialDays.HandleTakeDamage);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        var commanderMenu = CommanderMenuCapability.Api.Get();
        if (commanderMenu is null)
        {
            Logger.LogWarning("Commander menu capability is unavailable; special days menu was not registered.");
            return;
        }

        _menuRegistration = commanderMenu.RegisterItem(
            new CommanderMenuItem("special-days", "Игровые дни", _specialDays.OpenSelectionMenu, 120));
    }

    public override void Unload(bool hotReload)
    {
        _menuRegistration?.Dispose();
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
}
