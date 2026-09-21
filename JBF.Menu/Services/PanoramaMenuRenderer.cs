using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;

namespace JBF.Menu.Services;

internal sealed class PanoramaMenuRenderer
{
    private const string MenuRootPanelId = "jbf_menu_root";

    private readonly string _layoutResource;
    private readonly Action<string>? _log;
    private readonly HashSet<int> _visiblePlayers = [];

    private CCSCustomHudLayout? _entity;

    public bool IsReady => _entity is { IsValid: true };

    public PanoramaMenuRenderer(string layoutResource, Action<string>? log = null)
    {
        _layoutResource = layoutResource;
        _log = log;
    }

    public void Start(BasePlugin plugin, bool hotReload)
    {
        plugin.RegisterListener<Listeners.OnMapStart>(_ => Server.NextWorldUpdate(Spawn));

        if (hotReload)
            Server.NextWorldUpdate(Spawn);
    }

    public void Stop(BasePlugin plugin)
    {
        try
        {
            foreach (var slot in _visiblePlayers.ToArray())
            {
                var player = Utilities.GetPlayerFromSlot(slot);
                if (player is { IsValid: true })
                    ClearAll(player);
            }

            if (_entity is { IsValid: true })
                _entity.Remove();
        }
        catch (Exception ex)
        {
            _log?.Invoke($"JBF.Menu: HUD cleanup skipped: {ex.Message}");
        }

        _visiblePlayers.Clear();
        _entity = null;
    }

    public bool EnsureReady()
    {
        if (IsReady)
            return true;

        Spawn();
        return IsReady;
    }

    public void Show(CCSPlayerController player) => ShowPanel(player, MenuRootPanelId);

    public void Hide(CCSPlayerController player) => HidePanel(player, MenuRootPanelId);

    public void ShowPanel(CCSPlayerController player, string panelId)
    {
        if (!IsReady || !player.IsValid || player.IsBot)
            return;

        _visiblePlayers.Add(player.Slot);
        SetClass(player, panelId, "shown", true);
    }

    public void HidePanel(CCSPlayerController player, string panelId)
    {
        if (!IsReady || !player.IsValid)
            return;

        SetClass(player, panelId, "shown", false);
    }

    public void ClearAll(CCSPlayerController player)
    {
        if (!IsReady || !player.IsValid)
            return;

        foreach (var panelId in new[]
                 {
                     "jbf_menu_root",
                     "jbf_question_root",
                     "jbf_mapvote_root",
                     "jbf_notify_root",
                     "jbf_announce_root",
                     "jbf_round_root",
                     "jbf_player_root",
                     "jbf_event_root"
                 })
        {
            SetClass(player, panelId, "shown", false);
        }

        _visiblePlayers.Remove(player.Slot);
    }

    public void ForgetPlayer(int playerSlot)
    {
        _visiblePlayers.Remove(playerSlot);
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

    private void Spawn()
    {
        if (IsReady)
            return;

        try
        {
            var entity = Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout");
            if (entity is null || !entity.IsValid)
            {
                _log?.Invoke("JBF.Menu: failed to create custom_hud_layout entity.");
                return;
            }

            entity.StrLayout = _layoutResource;
            entity.DispatchSpawn();
            _entity = entity;

            _log?.Invoke($"JBF.Menu: Panorama HUD ready: {_layoutResource}");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"JBF.Menu: HUD spawn failed: {ex.Message}");
        }
    }
}
