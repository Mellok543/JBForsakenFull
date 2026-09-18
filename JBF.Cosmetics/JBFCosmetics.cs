using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using JBF.Api;
using JBF.Cosmetics.Models;
using JBF.Cosmetics.Services;
using Microsoft.Extensions.Logging;

namespace JBF.Cosmetics;

public sealed class JBFCosmetics : BasePlugin
{
    private CosmeticsService? _service;
    private CosmeticsRenderer? _renderer;

    public override string ModuleName => "JBF Cosmetics";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        var config = CosmeticsConfig.LoadOrCreate(Path.Combine(ModuleDirectory, "cosmetics.json"), m => Logger.LogError("{Message}", m));
        _renderer = new CosmeticsRenderer(m => Logger.LogInformation("{Message}", m));
        _renderer.Clicked += OnHudClicked;
        _renderer.Start(this, hotReload);
        _service = new CosmeticsService(config, _renderer, m => Logger.LogError("{Message}", m));
        Capabilities.RegisterPluginCapability(CosmeticsCapability.Api, () => _service!);
        RegisterListener<Listeners.OnClientDisconnect>(slot => _service?.Disconnect(slot));
    }

    public override void Unload(bool hotReload)
    {
        if (_renderer is not null)
        {
            _renderer.Clicked -= OnHudClicked;
            _renderer.Stop(this);
        }
        _service = null;
        _renderer = null;
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
            case "jbf_cos_cat_trail": _service.SetCategory(player, CosmeticCategory.Trail); return;
            case "jbf_cos_cat_aura": _service.SetCategory(player, CosmeticCategory.Aura); return;
            case "jbf_cos_cat_death": _service.SetCategory(player, CosmeticCategory.DeathEffect); return;
            case "jbf_cos_cat_player": _service.SetCategory(player, CosmeticCategory.PlayerModel); return;
            case "jbf_cos_prev": _service.ChangePage(player, -1); return;
            case "jbf_cos_next": _service.ChangePage(player, 1); return;
            case "jbf_cos_action": _service.ToggleSelected(player); return;
        }

        const string prefix = "jbf_cos_select_";
        if (id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id[prefix.Length..], out var slot))
            _service.SelectSlot(player, slot);
    }
}
