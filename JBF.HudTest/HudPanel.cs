using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace JBF.HudTest;

internal sealed class HudPanel
{
    private readonly string _layoutResource;
    private readonly Action<string>? _log;
    private CCSCustomHudLayout? _entity;
    private readonly HashSet<int> _visible = [];

    public bool IsReady => _entity is { IsValid: true };
    public bool IsVisible(int slot) => _visible.Contains(slot);

    public HudPanel(string layoutResource, Action<string>? log = null)
    {
        _layoutResource = layoutResource;
        _log = log;
    }

    public void Start(BasePlugin plugin, bool hotReload)
    {
        plugin.RegisterListener<Listeners.OnMapStart>(_ => Server.NextWorldUpdate(Spawn));

        // Never touch EntitySystem immediately during a normal server startup.
        // On hot reload the map is already alive, so the next world update is safe.
        if (hotReload)
            Server.NextWorldUpdate(Spawn);
    }

    public bool EnsureReady()
    {
        if (IsReady)
            return true;

        Spawn();
        return IsReady;
    }

    public void Stop(BasePlugin plugin)
    {
        try
        {
            _visible.Clear();

            if (_entity is not null && _entity.IsValid)
                _entity.Remove();
        }
        catch (Exception ex)
        {
            _log?.Invoke($"HudTest: cleanup skipped because entity system is unavailable: {ex.Message}");
        }

        _entity = null;
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

        // Intentionally DO NOT enable Panorama input capture here.
        // The player keeps normal mouse look and the HUD is controlled through game buttons.
        return true;
    }

    public void Hide(CCSPlayerController player, string rootPanelId, string visibleClass = "shown")
    {
        if (!IsReady || !player.IsValid)
            return;

        _visible.Remove(player.Slot);
        SetClass(player, rootPanelId, visibleClass, false);
    }

    private void Spawn()
    {
        if (IsReady)
            return;

        try
        {
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
}
