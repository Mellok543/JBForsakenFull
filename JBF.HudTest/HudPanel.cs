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

    public HudPanel(string layoutResource, Action<string>? log = null)
    {
        _layoutResource = layoutResource;
        _log = log;
    }

    public void Start(BasePlugin plugin)
    {
        plugin.RegisterListener<Listeners.OnCustomHudClicked>(OnClicked);
        plugin.RegisterListener<Listeners.OnMapStart>(_ => Server.NextWorldUpdate(Spawn));
        Spawn();
    }

    public void Stop(BasePlugin plugin)
    {
        plugin.RemoveListener<Listeners.OnCustomHudClicked>(OnClicked);
        HideAll();

        if (_entity is not null && _entity.IsValid)
            _entity.Remove();

        _entity = null;
    }

    public void SetText(CCSPlayerController player, string panelId, string value, string variable = "text")
    {
        if (_entity is null || !_entity.IsValid || !player.IsValid)
            return;

        _entity.SetDialogVariableStringForPlayer(player, panelId, variable, value ?? string.Empty);
    }

    public void SetClass(CCSPlayerController player, string panelId, string className, bool has)
    {
        if (_entity is null || !_entity.IsValid || !player.IsValid)
            return;

        _entity.SetHasClassForPlayer(player, panelId, className, has);
    }

    public void Show(CCSPlayerController player, string rootPanelId, string visibleClass = "shown")
    {
        if (_entity is null || !_entity.IsValid)
            Spawn();

        if (_entity is null || !_entity.IsValid || !player.IsValid || player.IsBot)
            return;

        _visible.Add(player.Slot);
        SetClass(player, rootPanelId, visibleClass, true);
        _entity.SetInputCaptureEnabled(player, true);
    }

    public void Hide(CCSPlayerController player, string rootPanelId, string visibleClass = "shown")
    {
        if (_entity is null || !_entity.IsValid || !player.IsValid)
            return;

        _visible.Remove(player.Slot);
        SetClass(player, rootPanelId, visibleClass, false);
        _entity.SetInputCaptureEnabled(player, false);
    }

    public void HideAll()
    {
        foreach (var slot in _visible.ToList())
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            _visible.Remove(slot);

            if (player is null || !player.IsValid || _entity is null || !_entity.IsValid)
                continue;

            _entity.SetInputCaptureEnabled(player, false);
        }
    }

    private void Spawn()
    {
        if (_entity is not null && _entity.IsValid)
            return;

        try
        {
            foreach (var old in Utilities.FindAllEntitiesByDesignerName<CCSCustomHudLayout>("custom_hud_layout"))
            {
                if (old.IsValid)
                    old.Remove();
            }

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

        Clicked?.Invoke(player, buttonId);
    }
}
