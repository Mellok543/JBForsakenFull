using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;

namespace JBF.BattlePass.Services;

internal sealed class BattlePassRenderer
{
    private const string Root = "jbf_bp_root";
    private readonly Action<string>? _log;
    private CCSCustomHudLayout? _entity;
    private readonly HashSet<int> _visible = [];

    public event Action<CCSPlayerController, string>? Clicked;

    public BattlePassRenderer(Action<string>? log = null) => _log = log;

    public void Start(BasePlugin plugin, bool hotReload)
    {
        plugin.RegisterListener<Listeners.OnCustomHudClicked>(OnCustomHudClicked);
        plugin.RegisterListener<Listeners.OnMapStart>(_ => Server.NextWorldUpdate(Spawn));
        if (hotReload)
            Server.NextWorldUpdate(Spawn);
        else
            Server.NextWorldUpdate(Spawn);
    }

    public void Stop(BasePlugin plugin)
    {
        plugin.RemoveListener<Listeners.OnCustomHudClicked>(OnCustomHudClicked);
        try
        {
            foreach (var slot in _visible.ToArray())
            {
                var player = Utilities.GetPlayerFromSlot(slot);
                if (player is { IsValid: true }) Hide(player);
            }
            if (_entity is { IsValid: true }) _entity.Remove();
        }
        catch (Exception ex) { _log?.Invoke($"BattlePass HUD cleanup failed: {ex.Message}"); }
        _visible.Clear();
        _entity = null;
    }

    public void Show(CCSPlayerController player)
    {
        if (!EnsureReady() || !player.IsValid || player.IsBot) return;
        _visible.Add(player.Slot);
        SetClass(player, Root, "shown", true);
        _entity!.SetInputCaptureEnabled(player, true);
    }

    public void Hide(CCSPlayerController player)
    {
        if (_entity is not { IsValid: true } || !player.IsValid) return;
        SetClass(player, Root, "shown", false);
        _entity.SetInputCaptureEnabled(player, false);
        _visible.Remove(player.Slot);
    }

    public void Forget(int slot) => _visible.Remove(slot);

    public void Text(CCSPlayerController player, string panelId, string text)
    {
        if (!EnsureReady() || !player.IsValid) return;
        _entity!.SetDialogVariableStringForPlayer(player, panelId, "text", text ?? string.Empty);
    }

    public void Image(CCSPlayerController player, string panelId, string source)
    {
        if (!EnsureReady() || !player.IsValid) return;

        try
        {
            _entity!.SetDialogVariableStringForPlayer(player, panelId, "src", source ?? string.Empty);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"BattlePass HUD image update skipped for '{panelId}': {ex.Message}");
        }
    }

    public void SetClass(CCSPlayerController player, string panelId, string className, bool value)
    {
        if (!EnsureReady() || !player.IsValid) return;
        _entity!.SetHasClassForPlayer(player, panelId, className, value);
    }

    private bool EnsureReady()
    {
        if (_entity is { IsValid: true }) return true;
        Spawn();
        return _entity is { IsValid: true };
    }

    private void Spawn()
    {
        if (_entity is { IsValid: true }) return;
        try
        {
            var entity = Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout");
            if (entity is null || !entity.IsValid)
            {
                _log?.Invoke("BattlePass: failed to create custom_hud_layout.");
                return;
            }
            entity.StrLayout = "panorama/layout/custom_game/jbf_battlepass.xml";
            entity.DispatchSpawn();
            _entity = entity;
            _log?.Invoke("BattlePass Panorama HUD ready.");
        }
        catch (Exception ex) { _log?.Invoke($"BattlePass HUD spawn failed: {ex.Message}"); }
    }

    private void OnCustomHudClicked(CCSPlayerController player, CCSCustomHudLayout layout, string buttonId)
    {
        if (!player.IsValid || !_visible.Contains(player.Slot)) return;
        if (string.IsNullOrWhiteSpace(buttonId) || !buttonId.StartsWith("jbf_bp_", StringComparison.Ordinal)) return;
        Clicked?.Invoke(player, buttonId);
    }
}
