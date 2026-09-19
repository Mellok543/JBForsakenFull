using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Timers;
using JBF.Api;
using JBF.BattlePass.Models;
using JBF.BattlePass.Services;
using Microsoft.Extensions.Logging;

namespace JBF.BattlePass;

public sealed class JBFBattlePass : BasePlugin
{
    private BattlePassService? _service;
    private BattlePassRenderer? _renderer;
    private ILrApi? _lr;
    private IPlayerStateApi? _playerState;
    private IWardenApi? _warden;
    private string? _configPath;

    public override string ModuleName => "JBF Battle Pass";
    public override string ModuleVersion => "1.3.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        _configPath = Path.Combine(ModuleDirectory, "battlepass.json");
        var config = BattlePassConfig.LoadOrCreate(_configPath, message => Logger.LogError("{Message}", message));
        _renderer = new BattlePassRenderer(message => Logger.LogInformation("{Message}", message));
        _renderer.Clicked += OnHudClicked;
        _renderer.Start(this, hotReload);
        _service = new BattlePassService(config, _renderer, message => Logger.LogError("{Message}", message));

        Capabilities.RegisterPluginCapability(BattlePassCapability.Api, () => _service!);
        RegisterListener<Listeners.OnClientDisconnect>(OnDisconnect);
        AddTimer(10.0f, () => _service?.TickPlayTime(), TimerFlags.REPEAT);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        RefreshSubscriptions();
        AddTimer(2.0f, RefreshSubscriptions, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    public override void Unload(bool hotReload)
    {
        UnsubscribeLr();
        UnsubscribePlayerState();
        UnsubscribeWarden();
        if (_renderer is not null)
        {
            _renderer.Clicked -= OnHudClicked;
            _renderer.Stop(this);
        }
        _service?.Shutdown();
        _service = null;
        _renderer = null;
        _configPath = null;
    }

    [ConsoleCommand("css_bp", "Open Winter Battle Pass")]
    [ConsoleCommand("css_battlepass", "Open Winter Battle Pass")]
    public void OnBattlePass(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null) return;
        _service?.Open(player);
    }

    [ConsoleCommand("css_bp_reload", "Reload Battle Pass config")]
    [RequiresPermissions("@jbf/admin")]
    public void OnReload(CCSPlayerController? player, CommandInfo command)
    {
        if (_service is null || string.IsNullOrWhiteSpace(_configPath))
        {
            command.ReplyToCommand("[JBF] Battle Pass ещё не готов.");
            return;
        }

        if (!BattlePassConfig.TryLoad(_configPath, out var config, out var error))
        {
            command.ReplyToCommand($"[JBF] Ошибка battlepass.json: {error}");
            return;
        }

        if (!_service.ReloadConfig(config, out var reloadError))
        {
            command.ReplyToCommand($"[JBF] Не удалось перезагрузить Battle Pass: {reloadError}");
            return;
        }

        command.ReplyToCommand("[JBF] Battle Pass config перезагружен.");
    }

    [ConsoleCommand("css_bp_close", "Close Battle Pass")]
    public void OnClose(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null) return;
        _service?.Close(player);
    }

    [ConsoleCommand("css_bp_tab", "Switch Battle Pass tab")]
    public void OnTab(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || command.ArgCount < 2) return;
        _service?.SetTab(player, command.GetArg(1));
    }

    [ConsoleCommand("css_bp_page", "Change Battle Pass page")]
    public void OnPage(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || command.ArgCount < 2 || !int.TryParse(command.GetArg(1), out var delta)) return;
        _service?.ChangePage(player, delta);
    }

    [ConsoleCommand("css_bp_claim_slot", "Claim reward by visible slot")]
    public void OnClaim(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || command.ArgCount < 2 || !int.TryParse(command.GetArg(1), out var slot)) return;
        _service?.ClaimSlot(player, slot);
    }

    [ConsoleCommand("css_bp_use_slot", "Use inventory item by visible slot")]
    public void OnUse(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || command.ArgCount < 2 || !int.TryParse(command.GetArg(1), out var slot)) return;
        _service?.UseInventorySlot(player, slot);
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _service?.OnRoundStart();
        RefreshSubscriptions();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _service?.OnRoundEnd((CounterStrikeSharp.API.Modules.Utils.CsTeam)@event.Winner);
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        _service?.OnKill(@event.Userid, @event.Attacker, @event.Headshot, @event.Weapon);
        return HookResult.Continue;
    }

    private void OnHudClicked(CCSPlayerController player, string buttonId)
    {
        if (_service is null) return;

        switch (buttonId)
        {
            case "jbf_bp_close":
                _service.Close(player);
                return;
            case "jbf_bp_track":
                _service.SetTab(player, "track");
                return;
            case "jbf_bp_missions":
                _service.SetTab(player, "missions");
                return;
            case "jbf_bp_inventory":
                _service.SetTab(player, "inventory");
                return;
        }

        if (buttonId.StartsWith("jbf_bp_page_prev_", StringComparison.Ordinal))
        {
            _service.ChangePage(player, -1);
            return;
        }

        if (buttonId.StartsWith("jbf_bp_page_next_", StringComparison.Ordinal))
        {
            _service.ChangePage(player, 1);
            return;
        }

        if (TryParseSlot(buttonId, "jbf_bp_claim_", out var claimSlot))
        {
            _service.ClaimSlot(player, claimSlot);
            return;
        }

        if (TryParseSlot(buttonId, "jbf_bp_use_", out var useSlot))
            _service.UseInventorySlot(player, useSlot);
    }

    private static bool TryParseSlot(string buttonId, string prefix, out int slot)
    {
        slot = -1;
        return buttonId.StartsWith(prefix, StringComparison.Ordinal)
               && int.TryParse(buttonId[prefix.Length..], out slot);
    }

    private void OnDisconnect(int slot) => _service?.Disconnect(slot);

    private void RefreshSubscriptions()
    {
        SubscribeLr();
        SubscribePlayerState();
        SubscribeWarden();
    }

    private void SubscribeLr()
    {
        var current = LrCapability.Api.Get();
        if (ReferenceEquals(current, _lr)) return;
        UnsubscribeLr();
        _lr = current;
        if (_lr is not null) _lr.MatchEnded += OnLrEnded;
    }

    private void UnsubscribeLr()
    {
        if (_lr is not null) _lr.MatchEnded -= OnLrEnded;
        _lr = null;
    }

    private void SubscribePlayerState()
    {
        var current = PlayerStateCapability.Api.Get();
        if (ReferenceEquals(current, _playerState)) return;
        UnsubscribePlayerState();
        _playerState = current;
        if (_playerState is null) return;
        _playerState.RebelStarted += OnRebelStarted;
        _playerState.RebelKilled += OnRebelKilled;
        _playerState.FreeDayGranted += OnFreeDayGranted;
    }

    private void UnsubscribePlayerState()
    {
        if (_playerState is not null)
        {
            _playerState.RebelStarted -= OnRebelStarted;
            _playerState.RebelKilled -= OnRebelKilled;
            _playerState.FreeDayGranted -= OnFreeDayGranted;
        }
        _playerState = null;
    }

    private void SubscribeWarden()
    {
        var current = WardenCapability.Api.Get();
        if (ReferenceEquals(current, _warden)) return;
        UnsubscribeWarden();
        _warden = current;
        if (_warden is not null) _warden.WardenClaimed += OnWardenClaimed;
    }

    private void UnsubscribeWarden()
    {
        if (_warden is not null) _warden.WardenClaimed -= OnWardenClaimed;
        _warden = null;
    }

    private void OnRebelStarted(CCSPlayerController player) => _service?.OnRebelStarted(player);
    private void OnRebelKilled(RebelKilledEvent data) => _service?.OnRebelKilled(data);
    private void OnFreeDayGranted(CCSPlayerController player) => _service?.OnFreeDayGranted(player);
    private void OnWardenClaimed(CCSPlayerController player) => _service?.OnWardenClaimed(player);
    private void OnLrEnded(LrMatchEndedEvent match) => _service?.OnLrEnded(match);
}
