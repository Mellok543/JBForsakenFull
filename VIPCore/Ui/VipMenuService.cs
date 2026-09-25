using CounterStrikeSharp.API.Core;

namespace VIPCore.Ui;

internal sealed class VipMenuService
{
    private const int VisibleOptionCount = 7;

    private readonly Dictionary<int, MenuState> _menus = [];
    private readonly VipMenuRenderer _renderer;

    public VipMenuService(string layoutResource, Action<string>? log = null)
    {
        _renderer = new VipMenuRenderer(layoutResource, log);
    }

    public void Start(BasePlugin plugin, bool hotReload) => _renderer.Start(plugin, hotReload);

    public void Stop()
    {
        _menus.Clear();
        _renderer.Stop();
    }

    public void Open(
        CCSPlayerController player,
        string title,
        string subtitle,
        IReadOnlyList<VipMenuOption> options)
    {
        if (!player.IsValid)
            return;

        var items = options.ToArray();
        var state = new MenuState
        {
            Player = player,
            Title = title,
            Subtitle = subtitle,
            Options = items,
            SelectedIndex = FindFirstSelectable(items)
        };

        _menus[player.Slot] = state;

        if (!_renderer.EnsureReady())
        {
            _menus.Remove(player.Slot);
            return;
        }

        _renderer.Show(player);
        Render(state);
    }

    public void Close(CCSPlayerController player)
    {
        if (_menus.Remove(player.Slot))
            _renderer.Hide(player);
    }

    public void HandleButtonsChanged(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
    {
        if (!_menus.TryGetValue(player.Slot, out var state))
            return;

        if (pressed.HasFlag(PlayerButtons.Reload))
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
        else if (pressed.HasFlag(PlayerButtons.Moveleft))
        {
            MovePage(state, -1);
            Render(state);
        }
        else if (pressed.HasFlag(PlayerButtons.Moveright))
        {
            MovePage(state, 1);
            Render(state);
        }
        else if (pressed.HasFlag(PlayerButtons.Use))
        {
            SelectCurrent(state);
        }
    }

    public void HandleDisconnect(int slot)
    {
        _menus.Remove(slot);
        _renderer.ForgetPlayer(slot);
    }

    private void SelectCurrent(MenuState state)
    {
        if (state.SelectedIndex < 0 || state.SelectedIndex >= state.Options.Count)
            return;

        var option = state.Options[state.SelectedIndex];
        if (option.IsDisabled)
        {
            Render(state);
            return;
        }

        option.OnSelect(state.Player);

        if (_menus.TryGetValue(state.Player.Slot, out var current) && ReferenceEquals(current, state))
            Close(state.Player);
    }

    private void Render(MenuState state)
    {
        if (!state.Player.IsValid || !_renderer.IsReady)
            return;

        _renderer.SetText(state.Player, "vipcore_menu_title", state.Title);
        _renderer.SetText(state.Player, "vipcore_menu_subtitle", state.Subtitle);

        var selected = Math.Max(0, state.SelectedIndex);
        var first = Math.Clamp(
            selected - VisibleOptionCount / 2,
            0,
            Math.Max(0, state.Options.Count - VisibleOptionCount));

        for (var row = 0; row < VisibleOptionCount; row++)
        {
            var rowId = $"vipcore_menu_row_{row}";
            var textId = $"{rowId}_text";
            var optionIndex = first + row;

            if (optionIndex >= state.Options.Count)
            {
                _renderer.SetText(state.Player, textId, string.Empty);
                _renderer.SetClass(state.Player, rowId, "hidden", true);
                _renderer.SetClass(state.Player, rowId, "selected", false);
                _renderer.SetClass(state.Player, rowId, "disabled", false);
                continue;
            }

            var option = state.Options[optionIndex];
            _renderer.SetText(state.Player, textId, option.Text);
            _renderer.SetClass(state.Player, rowId, "hidden", false);
            _renderer.SetClass(state.Player, rowId, "selected", optionIndex == state.SelectedIndex);
            _renderer.SetClass(state.Player, rowId, "disabled", option.IsDisabled);
        }

        _renderer.SetText(
            state.Player,
            "vipcore_menu_page",
            state.Options.Count > VisibleOptionCount && state.SelectedIndex >= 0
                ? $"{state.SelectedIndex + 1}/{state.Options.Count}"
                : string.Empty);

        var status = "W/S — выбор   A/D — страница   E — открыть   R — закрыть";
        if (state.SelectedIndex >= 0 && state.SelectedIndex < state.Options.Count)
        {
            var option = state.Options[state.SelectedIndex];
            if (option.IsDisabled)
                status = string.IsNullOrWhiteSpace(option.DisabledReason)
                    ? "Эта функция сейчас недоступна"
                    : option.DisabledReason!;
        }

        _renderer.SetText(state.Player, "vipcore_menu_status", status);
    }

    private static int FindFirstSelectable(IReadOnlyList<VipMenuOption> options)
    {
        for (var i = 0; i < options.Count; i++)
            if (!options[i].IsDisabled)
                return i;

        return options.Count > 0 ? 0 : -1;
    }

    private static void MoveSelection(MenuState state, int delta)
    {
        if (state.Options.Count == 0)
            return;

        var start = state.SelectedIndex < 0 ? 0 : state.SelectedIndex;
        var index = start;

        for (var i = 0; i < state.Options.Count; i++)
        {
            index = (index + delta + state.Options.Count) % state.Options.Count;
            if (!state.Options[index].IsDisabled)
            {
                state.SelectedIndex = index;
                return;
            }
        }
    }

    private static void MovePage(MenuState state, int direction)
    {
        if (state.Options.Count == 0)
            return;

        var target = Math.Clamp(
            Math.Max(0, state.SelectedIndex) + direction * VisibleOptionCount,
            0,
            state.Options.Count - 1);

        state.SelectedIndex = target;
        if (!state.Options[target].IsDisabled)
            return;

        MoveSelection(state, direction >= 0 ? 1 : -1);
    }

    private sealed class MenuState
    {
        public required CCSPlayerController Player { get; init; }
        public required string Title { get; init; }
        public required string Subtitle { get; init; }
        public required IReadOnlyList<VipMenuOption> Options { get; init; }
        public int SelectedIndex { get; set; }
    }
}
