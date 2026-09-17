using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
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

    public override string ModuleName => "JBF Battle Pass";
    public override string ModuleVersion => "1.0.2";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        var configPath = Path.Combine(ModuleDirectory, "battlepass.json");
        var config = BattlePassConfig.LoadOrCreate(configPath, message => Logger.LogError("{Message}", message));
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
        SubscribeLr();
    }

    public override void Unload(bool hotReload)
    {
        if (_lr is not null) _lr.MatchEnded -= OnLrEnded;
        _lr = null;
        if (_renderer is not null)
        {
            _renderer.Clicked -= OnHudClicked;
            _renderer.Stop(this);
        }
        _service?.Shutdown();
        _service = null;
        _renderer = null;
    }

    [ConsoleCommand("css_bp", "Open Winter Battle Pass")]
    [ConsoleCommand("css_battlepass", "Open Winter Battle Pass")]
    public void OnBattlePass(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null) return;
        _service?.Open(player);
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
        SubscribeLr();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _service?.OnRoundEnd();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        _service?.OnKill(@event.Userid, @event.Attacker);
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

    private void SubscribeLr()
    {
        var current = LrCapability.Api.Get();
        if (ReferenceEquals(current, _lr)) return;
        if (_lr is not null) _lr.MatchEnded -= OnLrEnded;
        _lr = current;
        if (_lr is not null) _lr.MatchEnded += OnLrEnded;
    }

    private void OnLrEnded(LrMatchEndedEvent match) => _service?.OnLrEnded(match);
}
