using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using JBF.Cosmetics.Models;
using JBF.Cosmetics.Services;
using Microsoft.Extensions.Logging;

namespace JBF.Cosmetics;

public sealed class JBFCosmetics : BasePlugin
{
    private CosmeticsService? _service;
    private CosmeticsRenderer? _renderer;
    private CosmeticsConfig? _config;
    private string? _configPath;

    public override string ModuleName => "JBF Cosmetics";
    public override string ModuleVersion => "1.7.1";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        _configPath = Path.Combine(ModuleDirectory, "cosmetics.json");
        var config = CosmeticsConfig.LoadOrCreate(_configPath, m => Logger.LogError("{Message}", m));
        _config = config;
        _renderer = new CosmeticsRenderer(m => Logger.LogInformation("{Message}", m));
        _renderer.Clicked += OnHudClicked;
        _renderer.Start(this, hotReload);
        _service = new CosmeticsService(config, _renderer, m => Logger.LogError("{Message}", m));
        Capabilities.RegisterPluginCapability(CosmeticsCapability.Api, () => _service!);
        RegisterListener<Listeners.OnClientDisconnect>(slot => _service?.Disconnect(slot));
        RegisterListener<Listeners.OnMapStart>(_ => _service?.OnMapStart());
        RegisterListener<Listeners.OnServerPrecacheResources>(OnServerPrecacheResources);

        if (hotReload)
        {
            foreach (var player in Utilities.GetPlayers().Where(p => p is { IsValid: true, IsBot: false, PawnIsAlive: true }))
                AddTimer(0.25f, () => _service?.RefreshVisuals(player));
        }
    }

    public override void Unload(bool hotReload)
    {
        if (_renderer is not null)
        {
            _renderer.Clicked -= OnHudClicked;
            _renderer.Stop(this);
        }
        _service?.Shutdown();
        _service = null;
        _renderer = null;
        _config = null;
        _configPath = null;
    }

    private void OnServerPrecacheResources(ResourceManifest manifest)
    {
        if (_config is null) return;

        foreach (var item in _config.Items.Where(x =>
                     x.Enabled &&
                     x.Category is CosmeticCategory.Head or CosmeticCategory.Back or CosmeticCategory.ShoulderPet &&
                     !string.IsNullOrWhiteSpace(x.AssetPath)))
        {
            try
            {
                manifest.AddResource(item.AssetPath);
                Logger.LogInformation("Cosmetics: added resource to manifest: {AssetPath}", item.AssetPath);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Cosmetics: failed to add resource to manifest: {AssetPath}", item.AssetPath);
            }
        }
    }

    [GameEventHandler]
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player is { IsValid: true, IsBot: false })
            AddTimer(0.25f, () => _service?.RefreshVisuals(player));
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        _service?.OnDeath(@event.Userid);
        return HookResult.Continue;
    }

    [ConsoleCommand("css_cos_reload", "Reload cosmetics config")]
    [RequiresPermissions("@jbf/admin")]
    public void ReloadConfig(CCSPlayerController? player, CommandInfo command)
    {
        if (_service is null || _config is null || string.IsNullOrWhiteSpace(_configPath))
        {
            command.ReplyToCommand("[JBF] Cosmetics ещё не готов.");
            return;
        }

        if (!CosmeticsConfig.TryLoad(_configPath, out var config, out var error))
        {
            command.ReplyToCommand($"[JBF] Ошибка cosmetics.json: {error}");
            return;
        }

        if (!_service.ReloadConfig(config, out var reloadError))
        {
            command.ReplyToCommand($"[JBF] Не удалось перезагрузить Cosmetics: {reloadError}");
            return;
        }

        _config.Items = config.Items;
        command.ReplyToCommand("[JBF] Cosmetics config перезагружен. Новым моделям всё ещё нужна смена карты для resource manifest.");
    }

    [ConsoleCommand("css_cos_grant_self", "Grant a cosmetic to yourself for testing")]
    [RequiresPermissions("@jbf/admin")]
    [CommandHelper(minArgs: 1, usage: "<cosmetic_id>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void GrantSelf(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || _service is null) return;
        var id = command.GetArg(1);
        if (!_service.Grant(player, id, "Admin"))
            command.ReplyToCommand($"[JBF] Не удалось выдать косметику '{id}'.");
        else
            command.ReplyToCommand($"[JBF] Косметика '{id}' выдана.");
    }

    [ConsoleCommand("css_cosmetics", "Open JBF cosmetics")]
    [ConsoleCommand("css_cos", "Open JBF cosmetics")]
    public void Open(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null) _service?.Open(player);
    }

    private void OnHudClicked(CCSPlayerController player, string id)
    {
        if (_service is null) return;
        switch (id)
        {
            case "jbf_cos_close": _service.Close(player); return;
            case "jbf_cos_cat_all": _service.SetCategory(player, null); return;
            case "jbf_cos_cat_head": _service.SetCategory(player, CosmeticCategory.Head); return;
            case "jbf_cos_cat_back": _service.SetCategory(player, CosmeticCategory.Back); return;
            case "jbf_cos_cat_pet": _service.SetCategory(player, CosmeticCategory.ShoulderPet); return;
            case "jbf_cos_cat_trail": _service.SetCategory(player, CosmeticCategory.Trail); return;
            case "jbf_cos_cat_aura": _service.SetCategory(player, CosmeticCategory.Aura); return;
            case "jbf_cos_cat_death": _service.SetCategory(player, CosmeticCategory.DeathEffect); return;
            case "jbf_cos_cat_player": _service.SetCategory(player, CosmeticCategory.PlayerModel); return;
            case "jbf_cos_prev": _service.ChangePage(player, -1); return;
            case "jbf_cos_next": _service.ChangePage(player, 1); return;
            case "jbf_cos_action": _service.ToggleSelected(player); return;
        }

        foreach (var prefix in new[] { "jbf_cos_select_", "jbf_cos_item_" })
        {
            if (!id.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (int.TryParse(id[prefix.Length..], out var slot))
                _service.SelectSlot(player, slot);
            return;
        }
    }
}
