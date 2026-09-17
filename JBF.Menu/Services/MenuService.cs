using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using JBF.Api;

namespace JBF.Menu.Services;

internal sealed class MenuService : IMenuApi
{
    private const int VisibleOptionCount = 7;
    private readonly Dictionary<int, ActiveMenuState> _activeMenus = new();

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
        {
            return;
        }

        _activeMenus[player.Slot] = new ActiveMenuState
        {
            Player = player,
            Title = title,
            Options = options.ToArray(),
            SelectedIndex = 0
        };
    }

    public void Close(CCSPlayerController player)
    {
        if (_activeMenus.Remove(player.Slot))
        {
            player.PrintToCenterHtml(" ");
        }
    }

    public void HandleButtonsChanged(
        CCSPlayerController player,
        PlayerButtons pressed,
        PlayerButtons released)
    {
        if (!_activeMenus.TryGetValue(player.Slot, out var state))
        {
            return;
        }

        if (pressed.HasFlag(PlayerButtons.Scoreboard))
        {
            Close(player);
            return;
        }

        if (state.Options.Count == 0)
        {
            return;
        }

        if (pressed.HasFlag(PlayerButtons.Forward))
        {
            MoveSelection(state, -1);
        }
        else if (pressed.HasFlag(PlayerButtons.Back))
        {
            MoveSelection(state, 1);
        }
        else if (pressed.HasFlag(PlayerButtons.Use))
        {
            SelectCurrent(state);
        }
    }

    public void HandleClientDisconnect(int playerSlot)
    {
        _activeMenus.Remove(playerSlot);
    }

    public void Render()
    {
        foreach (var (slot, state) in _activeMenus.ToArray())
        {
            if (!state.Player.IsValid)
            {
                _activeMenus.Remove(slot);
                continue;
            }

            state.Player.PrintToCenterHtml(BuildHtml(state));
        }
    }

    private void SelectCurrent(ActiveMenuState state)
    {
        var option = state.Options[state.SelectedIndex];
        if (option.IsDisabled)
        {
            return;
        }

        Close(state.Player);
        option.OnSelect(state.Player);
    }

    private static void MoveSelection(ActiveMenuState state, int delta)
    {
        state.SelectedIndex = (state.SelectedIndex + delta + state.Options.Count) % state.Options.Count;
    }

    private static string BuildHtml(ActiveMenuState state)
    {
        var builder = new StringBuilder();
        builder.Append("<b><font color='#f5c451'>");
        builder.Append(Encode(state.Title));
        builder.AppendLine("</font></b><br>");

        var firstIndex = Math.Clamp(
            state.SelectedIndex - VisibleOptionCount / 2,
            0,
            Math.Max(0, state.Options.Count - VisibleOptionCount));
        var lastIndex = Math.Min(firstIndex + VisibleOptionCount, state.Options.Count);

        for (var index = firstIndex; index < lastIndex; index++)
        {
            var option = state.Options[index];
            var color = option.IsDisabled
                ? "#777777"
                : index == state.SelectedIndex ? "#62d48f" : "#ffffff";
            var marker = index == state.SelectedIndex ? "&#9658; " : "&nbsp;&nbsp;";

            builder.Append("<font color='");
            builder.Append(color);
            builder.Append("'>");
            builder.Append(marker);
            builder.Append(Encode(option.Text));
            builder.AppendLine("</font><br>");
        }

        builder.Append("<br><font color='#aeb4bd'>W/S: навигация  E: выбрать  TAB: закрыть</font>");
        return builder.ToString();
    }

    private static string Encode(string value)
    {
        var builder = new StringBuilder(value.Length * 2);
        foreach (var character in value)
        {
            if (character > 127)
            {
                builder.Append("&#x");
                builder.Append(((int)character).ToString("X"));
                builder.Append(';');
                continue;
            }

            builder.Append(character switch
            {
                '<' => "&lt;",
                '>' => "&gt;",
                '&' => "&amp;",
                '\'' => "&#39;",
                '"' => "&quot;",
                _ => character.ToString()
            });
        }

        return builder.ToString();
    }
}
