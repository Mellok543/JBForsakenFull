using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using JBF.Api;

namespace JBF.Menu.Services;

internal sealed class MenuService : IMenuApi, IUiApi
{
    private const int VisibleOptionCount = 7;

    private readonly Dictionary<int, ActiveMenuState> _activeMenus = new();
    private readonly Dictionary<(int Slot, string Panel), System.Threading.Timer> _hideTimers = new();
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
        foreach (var timer in _hideTimers.Values)
            timer.Dispose();

        _hideTimers.Clear();

        foreach (var state in _activeMenus.Values.ToArray())
        {
            if (state.Player.IsValid)
                _renderer.ClearAll(state.Player);
        }

        _activeMenus.Clear();
        _renderer.Stop(plugin);
    }

    public bool IsOpen(CCSPlayerController player) => _activeMenus.ContainsKey(player.Slot);

    public void Open(CCSPlayerController player, string title, IReadOnlyList<JailbreakMenuOption> options)
    {
        if (!player.IsValid)
            return;

        var items = options.ToArray();
        var state = new ActiveMenuState
        {
            Player = player,
            Title = title,
            Options = items,
            SelectedIndex = FindFirstSelectable(items)
        };

        _activeMenus[player.Slot] = state;

        if (!_renderer.EnsureReady())
        {
            _activeMenus.Remove(player.Slot);
            return;
        }

        _renderer.Show(player);
        Render(state);
    }

    public void Close(CCSPlayerController player)
    {
        if (!_activeMenus.Remove(player.Slot))
            return;

        _renderer.Hide(player);
    }

    public void Notify(CCSPlayerController player, string text, UiNotificationType type = UiNotificationType.Info, float durationSeconds = 4.0f)
    {
        if (!Prepare(player))
            return;

        _renderer.SetText(player, "jbf_notify_text", text);
        ApplyType(player, "jbf_notify_root", type);
        _renderer.ShowPanel(player, "jbf_notify_root");
        ScheduleHide(player, "jbf_notify_root", durationSeconds);
    }

    public void Announce(CCSPlayerController player, string title, string subtitle = "", UiNotificationType type = UiNotificationType.Important, float durationSeconds = 5.0f)
    {
        if (!Prepare(player))
            return;

        _renderer.SetText(player, "jbf_announce_title", title);
        _renderer.SetText(player, "jbf_announce_subtitle", subtitle);
        ApplyType(player, "jbf_announce_root", type);
        _renderer.ShowPanel(player, "jbf_announce_root");
        ScheduleHide(player, "jbf_announce_root", durationSeconds);
    }

    public void SetRoundStatus(CCSPlayerController player, string title, string value = "")
    {
        if (!Prepare(player))
            return;

        _renderer.SetText(player, "jbf_round_title", title);
        _renderer.SetText(player, "jbf_round_value", value);
        _renderer.ShowPanel(player, "jbf_round_root");
    }

    public void ClearRoundStatus(CCSPlayerController player)
    {
        if (_renderer.IsReady && player.IsValid)
            _renderer.HidePanel(player, "jbf_round_root");
    }

    public void SetPlayerStatus(CCSPlayerController player, string primary, string secondary = "")
    {
        if (!Prepare(player))
            return;

        _renderer.SetText(player, "jbf_player_primary", primary);
        _renderer.SetText(player, "jbf_player_secondary", secondary);
        _renderer.ShowPanel(player, "jbf_player_root");
    }

    public void ClearPlayerStatus(CCSPlayerController player)
    {
        if (_renderer.IsReady && player.IsValid)
            _renderer.HidePanel(player, "jbf_player_root");
    }

    public void Clear(CCSPlayerController player)
    {
        if (!player.IsValid)
            return;

        foreach (var key in _hideTimers.Keys.Where(key => key.Slot == player.Slot).ToArray())
        {
            _hideTimers[key].Dispose();
            _hideTimers.Remove(key);
        }

        _activeMenus.Remove(player.Slot);
        _renderer.ClearAll(player);
    }

    public void HandleButtonsChanged(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
    {
        if (!_activeMenus.TryGetValue(player.Slot, out var state))
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
        else if (pressed.HasFlag(PlayerButtons.Use))
        {
            SelectCurrent(state);
        }
    }

    public void HandleClientDisconnect(int playerSlot)
    {
        _activeMenus.Remove(playerSlot);

        foreach (var key in _hideTimers.Keys.Where(key => key.Slot == playerSlot).ToArray())
        {
            _hideTimers[key].Dispose();
            _hideTimers.Remove(key);
        }

        _renderer.ForgetPlayer(playerSlot);
    }

    private bool Prepare(CCSPlayerController player)
    {
        return player.IsValid && !player.IsBot && _renderer.EnsureReady();
    }

    private void ApplyType(CCSPlayerController player, string panelId, UiNotificationType type)
    {
        foreach (var className in new[] { "info", "success", "warning", "error", "important" })
            _renderer.SetClass(player, panelId, className, false);

        _renderer.SetClass(player, panelId, type.ToString().ToLowerInvariant(), true);
    }

    private void ScheduleHide(CCSPlayerController player, string panelId, float durationSeconds)
    {
        var key = (player.Slot, panelId);
        if (_hideTimers.Remove(key, out var existing))
            existing.Dispose();

        var milliseconds = Math.Max(250, (int)(durationSeconds * 1000));
        System.Threading.Timer? timer = null;
        timer = new System.Threading.Timer(_ =>
        {
            Server.NextFrame(() =>
            {
                timer?.Dispose();
                _hideTimers.Remove(key);

                if (!player.IsValid || !_renderer.IsReady)
                    return;

                _renderer.HidePanel(player, panelId);
            });
        }, null, milliseconds, System.Threading.Timeout.Infinite);

        _hideTimers[key] = timer;
    }

    private void SelectCurrent(ActiveMenuState state)
    {
        if (state.SelectedIndex < 0 || state.SelectedIndex >= state.Options.Count)
            return;

        var option = state.Options[state.SelectedIndex];
        if (option.IsDisabled)
        {
            Render(state);
            return;
        }

        var player = state.Player;
        option.OnSelect(player);

        if (_activeMenus.TryGetValue(player.Slot, out var current) && ReferenceEquals(current, state))
            Close(player);
    }

    private static int FindFirstSelectable(IReadOnlyList<JailbreakMenuOption> options)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (!options[i].IsDisabled)
                return i;
        }

        return options.Count > 0 ? 0 : -1;
    }

    private static void MoveSelection(ActiveMenuState state, int delta)
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

        state.SelectedIndex = start;
    }

    private void Render(ActiveMenuState state)
    {
        if (!state.Player.IsValid || !_renderer.IsReady)
            return;

        _renderer.SetText(state.Player, "jbf_menu_title", state.Title);

        var selectedForWindow = Math.Max(0, state.SelectedIndex);
        var firstIndex = Math.Clamp(selectedForWindow - VisibleOptionCount / 2, 0, Math.Max(0, state.Options.Count - VisibleOptionCount));

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

        var pageInfo = state.Options.Count > VisibleOptionCount && state.SelectedIndex >= 0
            ? $"{state.SelectedIndex + 1}/{state.Options.Count}"
            : string.Empty;
        _renderer.SetText(state.Player, "jbf_menu_page", pageInfo);

        var status = "W/S — выбор   E — открыть   R — закрыть";
        if (state.SelectedIndex >= 0 && state.SelectedIndex < state.Options.Count)
        {
            var selected = state.Options[state.SelectedIndex];
            if (selected.IsDisabled)
                status = string.IsNullOrWhiteSpace(selected.DisabledReason)
                    ? "Этот пункт сейчас недоступен"
                    : selected.DisabledReason!;
        }

        _renderer.SetText(state.Player, "jbf_menu_status", status);
    }
}
