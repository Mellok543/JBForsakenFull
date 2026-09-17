using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace JBF.HudTest;

internal sealed class HudPanel
{
    private readonly string _layoutResource;
    private readonly Action<string>? _log;
    private CCSCustomHudLayout? _entity;
    private readonly HashSet<int> _visible = [];

    public event Action<CCSPlayerController, string>? Clicked;

    public bool IsReady => _entity is { IsValid: true };

    public HudPanel(string layoutResource, Action<string>? log = null)
    {
        _layoutResource = layoutResource;
        _log = log;
    }

    public void Start(BasePlugin plugin, bool hotReload)
    {
        plugin.RegisterListener<Listeners.OnCustomHudClicked>(OnClicked);
        plugin.RegisterListener<Listeners.OnMapStart>(_ => Server.NextWorldUpdate(Spawn));

        // Never touch EntitySystem immediately during a normal server startup.
        // On hot reload the map is already alive, so the next world update is safe.
        if (hotReload)
            Server.NextWorldUpdate(Spawn);
    }

    /// <summary>
    /// Explicitly create the HUD entity. This is safe when called from a player command,
    /// because a valid player can only exist after the entity system has initialized.
    /// </summary>
    public bool EnsureReady()
    {
        if (IsReady)
            return true;

        Spawn();
        return IsReady;
    }

    public void Stop(BasePlugin plugin)
    {
        plugin.RemoveListener<Listeners.OnCustomHudClicked>(OnClicked);

        try
        {
            HideAll();

            if (_entity is not null && _entity.IsValid)
                _entity.Remove();
        }
        catch (Exception ex)
        {
            _log?.Invoke($"HudTest: cleanup skipped because entity system is unavailable: {ex.Message}");
        }

        _entity = null;
        _visible.Clear();
    }

    public void SetText(CCSPlayerController player, string panelId, string value, string variable = "text")
    {
        if (!IsReady || !player.IsValid)
            return;

        _entity!.SetDialogVariableStringForPlayer(player, panelId, variable, value ?? string.Empty);
    }

    public void SetClass(CCSPlayerController player, string panelId, string className, bool has)
    {
        if (!IsReady || !player.IsValid)
            return;

        _entity!.SetHasClassForPlayer(player, panelId, className, has);
    }

    public bool Show(CCSPlayerController player, string rootPanelId, string visibleClass = "shown")
    {
        if (!IsReady || !player.IsValid || player.IsBot)
            return false;

        _visible.Add(player.Slot);
        SetClass(player, rootPanelId, visibleClass, true);
        _entity!.SetInputCaptureEnabled(player, true);
        return true;
    }

    public void Hide(CCSPlayerController player, string rootPanelId, string visibleClass = "shown")
    {
        if (!IsReady || !player.IsValid)
            return;

        _visible.Remove(player.Slot);
        SetClass(player, rootPanelId, visibleClass, false);
        _entity!.SetInputCaptureEnabled(player, false);
    }

    public void HideAll()
    {
        foreach (var slot in _visible.ToList())
        {
            _visible.Remove(slot);

            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is null || !player.IsValid || !IsReady)
                continue;

            _entity!.SetInputCaptureEnabled(player, false);
        }
    }

    private void Spawn()
    {
        if (IsReady)
            return;

        try
        {
            // Do not delete every custom_hud_layout on the server: other plugins may own one.
            var entity = Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout");
            if (entity is null || !entity.IsValid)
            {
                _log?.Invoke("HudTest: failed to create custom_hud_layout entity.");
                return;
            }

            entity.StrLayout = _layoutResource;
            entity.DispatchSpawn();
            _entity = entity;
            _log?.Invoke($"HudTest: HUD entity ready: {_layoutResource}");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"HudTest: spawn failed: {ex.Message}");
        }
    }

    private void OnClicked(CCSPlayerController player, CCSCustomHudLayout layout, string buttonId)
    {
        if (!player.IsValid || !_visible.Contains(player.Slot))
            return;

        // Ignore clicks from custom_hud_layout entities owned by other plugins.
        if (_entity is null || !_entity.IsValid || layout.Handle != _entity.Handle)
            return;

        Clicked?.Invoke(player, buttonId);
    }
}
