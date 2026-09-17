using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace JBF.HudTest;

public sealed class JBFHudTest : BasePlugin
{
    private const string RootPanelId = "jbf_test_root";
    private static readonly string[] RowIds = ["jbf_btn_1", "jbf_btn_2", "jbf_btn_3"];

    private readonly Dictionary<int, int> _selectedBySlot = [];
    private HudPanel? _panel;

    public override string ModuleName => "JBF HUD Test";
    public override string ModuleVersion => "1.1.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        _panel = new HudPanel(
            "panorama/layout/custom_game/jbf_hud_test.xml",
            message => Logger.LogInformation("{Message}", message));

        _panel.Start(this, hotReload);
        RegisterListener<Listeners.OnPlayerButtonsChanged>(OnPlayerButtonsChanged);
        Logger.LogInformation("JBF HUD Test loaded. Use !hudtest in game.");
    }

    public override void Unload(bool hotReload)
    {
        RemoveListener<Listeners.OnPlayerButtonsChanged>(OnPlayerButtonsChanged);
        _selectedBySlot.Clear();

        if (_panel is null)
            return;

        _panel.Stop(this);
        _panel = null;
    }

    [ConsoleCommand("css_hudtest", "Open the JBF keyboard HUD test")]
    public void OnHudTestCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !player.IsValid)
        {
            command.ReplyToCommand("[HudTest] Команда доступна только игрокам.");
            return;
        }

        if (_panel is null || !_panel.EnsureReady())
        {
            command.ReplyToCommand("[HudTest] Ошибка: custom_hud_layout не удалось создать.");
            return;
        }

        Open(player);
        command.ReplyToCommand("[HudTest] W/S — выбор, E — действие, R — закрыть.");
    }

    [GameEventHandler]
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        if (@event.Userid is { IsValid: true } player)
            Close(player);

        return HookResult.Continue;
    }

    private void Open(CCSPlayerController player)
    {
        if (_panel is null)
            return;

        _selectedBySlot[player.Slot] = 0;

        _panel.SetText(player, "jbf_test_title", "JBFORSAKEN");
        _panel.SetText(player, "jbf_test_subtitle", "Тест клавиатурного HUD");
        _panel.SetText(player, "jbf_btn_1_text", "Проверить действие");
        _panel.SetText(player, "jbf_btn_2_text", "Сменить текст");
        _panel.SetText(player, "jbf_btn_3_text", "Тест выделения");
        _panel.SetText(player, "jbf_test_status", "W/S — выбор • E — действие • R — закрыть");

        UpdateSelection(player);
        _panel.Show(player, RootPanelId);
    }

    private void Close(CCSPlayerController player)
    {
        _selectedBySlot.Remove(player.Slot);
        _panel?.Hide(player, RootPanelId);
    }

    private void OnPlayerButtonsChanged(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
    {
        if (_panel is null || !_panel.IsVisible(player.Slot))
            return;

        if ((pressed & PlayerButtons.Forward) != 0)
        {
            MoveSelection(player, -1);
            return;
        }

        if ((pressed & PlayerButtons.Back) != 0)
        {
            MoveSelection(player, 1);
            return;
        }

        if ((pressed & PlayerButtons.Use) != 0)
        {
            ActivateSelected(player);
            return;
        }

        if ((pressed & PlayerButtons.Reload) != 0)
            Close(player);
    }

    private void MoveSelection(CCSPlayerController player, int direction)
    {
        var current = _selectedBySlot.GetValueOrDefault(player.Slot, 0);
        current = (current + direction + RowIds.Length) % RowIds.Length;
        _selectedBySlot[player.Slot] = current;
        UpdateSelection(player);
    }

    private void UpdateSelection(CCSPlayerController player)
    {
        if (_panel is null)
            return;

        var selected = _selectedBySlot.GetValueOrDefault(player.Slot, 0);

        for (var i = 0; i < RowIds.Length; i++)
            _panel.SetClass(player, RowIds[i], "selected", i == selected);
    }

    private void ActivateSelected(CCSPlayerController player)
    {
        if (_panel is null)
            return;

        switch (_selectedBySlot.GetValueOrDefault(player.Slot, 0))
        {
            case 0:
                _panel.SetText(player, "jbf_test_status", "E дошла до C# — управление работает");
                break;

            case 1:
                _panel.SetText(player, "jbf_test_title", "ТЕКСТ ИЗ C# ИЗМЕНЁН");
                _panel.SetText(player, "jbf_test_status", "Текст изменён без открытия курсора");
                break;

            case 2:
                _panel.SetText(player, "jbf_test_status", "Выделение управляется клавиатурой");
                break;
        }
    }
}
