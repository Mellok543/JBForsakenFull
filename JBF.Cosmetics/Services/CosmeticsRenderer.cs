using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;

namespace JBF.Cosmetics.Services;

internal sealed class CosmeticsRenderer
{
    private const string Root = "jbf_cos_root";
    private CCSCustomHudLayout? _entity;
    private readonly HashSet<int> _visible = [];
    private readonly Action<string>? _log;

    public event Action<CCSPlayerController, string>? Clicked;
    public CosmeticsRenderer(Action<string>? log = null) => _log = log;

    public void Start(BasePlugin plugin, bool hotReload)
    {
        plugin.RegisterListener<Listeners.OnCustomHudClicked>(OnClicked);
        plugin.RegisterListener<Listeners.OnMapStart>(_ => Server.NextWorldUpdate(Spawn));
        Server.NextWorldUpdate(Spawn);
    }

    public void Stop(BasePlugin plugin)
    {
        plugin.RemoveListener<Listeners.OnCustomHudClicked>(OnClicked);
        foreach (var slot in _visible.ToArray())
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is { IsValid: true }) Hide(player);
        }
        if (_entity is { IsValid: true }) _entity.Remove();
        _entity = null;
        _visible.Clear();
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

    public void Text(CCSPlayerController player, string id, string text)
    {
        if (!EnsureReady() || !player.IsValid) return;

        try
        {
            _entity!.SetDialogVariableStringForPlayer(player, id, "text", text ?? "");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Cosmetics HUD text update skipped for '{id}': {ex.Message}");
        }
    }

    public void SetClass(CCSPlayerController player, string id, string name, bool value)
    {
        if (!EnsureReady() || !player.IsValid) return;

        try
        {
            _entity!.SetHasClassForPlayer(player, id, name, value);
        }
        catch (Exception ex)
        {
            // During a Workshop UI update, a player can temporarily have an older layout
            // without a newly added panel/category. Missing panel ids must not abort !cos.
            _log?.Invoke($"Cosmetics HUD class update skipped for '{id}.{name}': {ex.Message}");
        }
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
            if (entity is null || !entity.IsValid) return;
            entity.StrLayout = "panorama/layout/custom_game/jbf_cosmetics.xml";
            entity.DispatchSpawn();
            _entity = entity;
        }
        catch (Exception ex) { _log?.Invoke($"Cosmetics HUD spawn failed: {ex.Message}"); }
    }

    private void OnClicked(CCSPlayerController player, CCSCustomHudLayout layout, string buttonId)
    {
        if (!player.IsValid || !_visible.Contains(player.Slot)) return;
        if (!buttonId.StartsWith("jbf_cos_", StringComparison.Ordinal)) return;
        Clicked?.Invoke(player, buttonId);
    }
}
