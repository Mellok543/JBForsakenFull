using CounterStrikeSharp.API.Core;
using JBF.Api;

namespace JBF.Menu.Services;

internal sealed class MenuService : IMenuApi
{
    private const int VisibleOptionCount = 7;

    private readonly Dictionary<int, ActiveMenuState> _activeMenus = new();
    private readonly PanoramaMenuRenderer _renderer;

    public MenuService(string layoutResource, Action<string>? log = null)
    {
        _renderer = new PanoramaMenuRenderer(layoutResource, log);
    }

    public void Start(BasePlugin plugin, bool hotReload)
    {
        _renderer.Start(plugin, hotReload);
    }

    public void Stop(BasePlugin plugin)
    {
        foreach (var state in _activeMenus.Values.ToArray())
        {
            if (state.Player.IsValid)
                _renderer.Hide(state.Player);
        }

        _activeMenus.Clear();
        _renderer.Stop(plugin);
    }

    public bool IsOpen(CCSPlayerController player)
    {
        return _activeMenus.ContainsKey(player.Slot);
    }

    public void Open(
        CCSPlayerController player,
        string title,
        IReadOnlyList<JailbreakMenuOption> options)
    {
        if (!player.IsValid)
            return;

        var state = new ActiveMenuState
        {
            Player = player,
            Title = title,
            Options = options.ToArray(),
            SelectedIndex = 0
        };

        _activeMenus[player.Slot] = state;

        if (!_renderer.EnsureReady())
            return;

        _renderer.Show(player);
        Render(state);
    }

    public void Close(CCSPlayerController player)
    {
        if (!_activeMenus.Remove(player.Slot))
            return;

        _renderer.Hide(player);
    }

    public void HandleButtonsChanged(
        CCSPlayerController player,
        PlayerButtons pressed,
        PlayerButtons released)
    {
        if (!_activeMenus.TryGetValue(player.Slot, out var state))
            return;

        if (pressed.HasFlag(PlayerButtons.Scoreboard))
        {
            Close(player);
            return;
        }

        if (state.Options.Count == 0)
            return;

        if (pressed.HasFlag(PlayerButtons.Forward))
        {
            MoveSelection(state, -1);
            Render(state);
        }
        else if (pressed.HasFlag(PlayerButtons.Back))
        {
            MoveSelection(state, 1);
            Render(state);
        }
        else if (pressed.HasFlag(PlayerButtons.Use))
        {
            SelectCurrent(state);
        }
    }

    public void HandleClientDisconnect(int playerSlot)
    {
        _activeMenus.Remove(playerSlot);
        _renderer.ForgetPlayer(playerSlot);
    }

    private void SelectCurrent(ActiveMenuState state)
    {
        var option = state.Options[state.SelectedIndex];
        if (option.IsDisabled)
            return;

        Close(state.Player);
        option.OnSelect(state.Player);
    }

    private static void MoveSelection(ActiveMenuState state, int delta)
    {
        state.SelectedIndex = (state.SelectedIndex + delta + state.Options.Count) % state.Options.Count;
    }

    private void Render(ActiveMenuState state)
    {
        if (!state.Player.IsValid || !_renderer.IsReady)
            return;

        _renderer.SetText(state.Player, "jbf_menu_title", state.Title);

        var firstIndex = Math.Clamp(
            state.SelectedIndex - VisibleOptionCount / 2,
            0,
            Math.Max(0, state.Options.Count - VisibleOptionCount));

        for (var row = 0; row < VisibleOptionCount; row++)
        {
            var panelId = $"jbf_menu_row_{row}";
            var textId = $"jbf_menu_row_{row}_text";
            var optionIndex = firstIndex + row;

            if (optionIndex >= state.Options.Count)
            {
                _renderer.SetText(state.Player, textId, string.Empty);
                _renderer.SetClass(state.Player, panelId, "hidden", true);
                _renderer.SetClass(state.Player, panelId, "selected", false);
                _renderer.SetClass(state.Player, panelId, "disabled", false);
                continue;
            }

            var option = state.Options[optionIndex];
            _renderer.SetText(state.Player, textId, option.Text);
            _renderer.SetClass(state.Player, panelId, "hidden", false);
            _renderer.SetClass(state.Player, panelId, "selected", optionIndex == state.SelectedIndex);
            _renderer.SetClass(state.Player, panelId, "disabled", option.IsDisabled);
        }

        var pageInfo = state.Options.Count > VisibleOptionCount
            ? $"{state.SelectedIndex + 1}/{state.Options.Count}"
            : string.Empty;

        _renderer.SetText(state.Player, "jbf_menu_page", pageInfo);
    }
}
