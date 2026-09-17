using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Achievements.Config;
using JBF.Achievements.Services;
using JBF.Api;
using Microsoft.Extensions.Logging;

namespace JBF.Achievements;

public sealed class JBFAchievements : BasePlugin
{
    private AchievementService? _achievements;
    private IWardenApi? _wardenApi;
    private ILrApi? _lrApi;

    public override string ModuleName => "JBF Achievements";
    public override string ModuleVersion => "1.1.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        var configPath = Path.Combine(ModuleDirectory, "config", "achievements.json");
        var config = AchievementsConfig.LoadOrCreate(configPath, message => Logger.LogError("Achievements config error: {Message}", message));
        _achievements = new AchievementService(this, config);
        Capabilities.RegisterPluginCapability(AchievementsCapability.Api, () => _achievements!);
        AddTimer(1.0f, RefreshDependencies, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    public override void OnAllPluginsLoaded(bool hotReload) => RefreshDependencies();

    public override void Unload(bool hotReload)
    {
        DetachWarden();
        DetachLr();
        _achievements?.Dispose();
        _achievements = null;
    }

    [ConsoleCommand("css_ach", "Open achievements menu")]
    [ConsoleCommand("css_achievements", "Open achievements menu")]
    public void OnAchievementsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !player.IsValid)
        {
            command.ReplyToCommand(JailbreakChat.Format("Команда доступна только игрокам."));
            return;
        }
        _achievements?.OpenMenu(player);
    }

    [ConsoleCommand("css_stats", "Open player statistics")]
    public void OnStatsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !player.IsValid)
        {
            command.ReplyToCommand(JailbreakChat.Format("Команда доступна только игрокам."));
            return;
        }
        _achievements?.OpenStats(player);
    }

    [GameEventHandler]
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        if (@event.Userid is { } player) _achievements?.ApplySpawnBonuses(player);
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        _achievements?.HandleKill(@event.Userid, @event.Attacker, @event.Weapon, @event.Headshot);
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _achievements?.HandleRoundEnd((CsTeam)@event.Winner);
        return HookResult.Continue;
    }

    private void RefreshDependencies()
    {
        var currentWarden = WardenCapability.Api.Get();
        if (!ReferenceEquals(currentWarden, _wardenApi))
        {
            DetachWarden();
            _wardenApi = currentWarden;
            if (_wardenApi is not null)
            {
                _wardenApi.WardenClaimed += OnWardenClaimed;
                _wardenApi.WardenKilled += OnWardenKilled;
            }
        }

        var currentLr = LrCapability.Api.Get();
        if (!ReferenceEquals(currentLr, _lrApi))
        {
            DetachLr();
            _lrApi = currentLr;
            if (_lrApi is not null) _lrApi.MatchEnded += OnLrMatchEnded;
        }
    }

    private void OnWardenClaimed(CCSPlayerController player) => _achievements?.Increment(player, "warden_claims");

    private void OnWardenKilled(WardenKilledEvent data)
    {
        if (data.Attacker is null) return;
        if (data.Weapon.Contains("knife", StringComparison.OrdinalIgnoreCase) || data.Weapon.Equals("bayonet", StringComparison.OrdinalIgnoreCase))
            _achievements?.Increment(data.Attacker, "warden_knife_kills");
    }

    private void OnLrMatchEnded(LrMatchEndedEvent match) => _achievements?.HandleLrEnded(match);

    private void DetachWarden()
    {
        if (_wardenApi is null) return;
        _wardenApi.WardenClaimed -= OnWardenClaimed;
        _wardenApi.WardenKilled -= OnWardenKilled;
        _wardenApi = null;
    }

    private void DetachLr()
    {
        if (_lrApi is null) return;
        _lrApi.MatchEnded -= OnLrMatchEnded;
        _lrApi = null;
    }
}
