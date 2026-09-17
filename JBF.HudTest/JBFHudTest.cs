using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace JBF.HudTest;

public sealed class JBFHudTest : BasePlugin
{
    private const string RootPanelId = "jbf_test_root";
    private HudPanel? _panel;

    public override string ModuleName => "JBF HUD Test";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        _panel = new HudPanel(
            "panorama/layout/custom_game/jbf_hud_test.xml",
            message => Logger.LogInformation("{Message}", message));

        _panel.Clicked += OnClicked;
        _panel.Start(this);
    }

    public override void Unload(bool hotReload)
    {
        if (_panel is null)
            return;

        _panel.Clicked -= OnClicked;
        _panel.Stop(this);
        _panel = null;
    }

    [ConsoleCommand("css_hudtest", "Open the JBF clickable HUD test")]
    public void OnHudTestCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !player.IsValid)
        {
            command.ReplyToCommand("Command is available only to players.");
            return;
        }

        Open(player);
    }

    [GameEventHandler]
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        if (@event.Userid is { IsValid: true } player)
            _panel?.Hide(player, RootPanelId);

        return HookResult.Continue;
    }

    private void Open(CCSPlayerController player)
    {
        if (_panel is null)
            return;

        _panel.SetText(player, "jbf_test_title", "JBFORSAKEN HUD TEST");
        _panel.SetText(player, "jbf_test_subtitle", "Кликабельное Panorama-меню");
        _panel.SetText(player, "jbf_btn_1_text", "Проверить кнопку");
        _panel.SetText(player, "jbf_btn_2_text", "Сменить текст");
        _panel.SetText(player, "jbf_btn_3_text", "Выделить пункт");
        _panel.SetText(player, "jbf_test_status", "Нажми любую кнопку мышкой");

        _panel.SetClass(player, "jbf_btn_1", "selected", false);
        _panel.SetClass(player, "jbf_btn_2", "selected", false);
        _panel.SetClass(player, "jbf_btn_3", "selected", false);

        _panel.Show(player, RootPanelId);
    }

    private void OnClicked(CCSPlayerController player, string buttonId)
    {
        if (_panel is null)
            return;

        switch (buttonId)
        {
            case "jbf_btn_1":
                Select(player, buttonId);
                _panel.SetText(player, "jbf_test_status", "Клик дошёл до C# плагина");
                break;

            case "jbf_btn_2":
                Select(player, buttonId);
                _panel.SetText(player, "jbf_test_title", "ТЕКСТ ИЗ C# ИЗМЕНЁН");
                _panel.SetText(player, "jbf_test_status", "SetDialogVariableStringForPlayer работает");
                break;

            case "jbf_btn_3":
                Select(player, buttonId);
                _panel.SetText(player, "jbf_test_status", "CSS-класс selected включён");
                break;

            case "jbf_test_close":
                _panel.Hide(player, RootPanelId);
                break;
        }
    }

    private void Select(CCSPlayerController player, string selectedId)
    {
        if (_panel is null)
            return;

        foreach (var id in new[] { "jbf_btn_1", "jbf_btn_2", "jbf_btn_3" })
            _panel.SetClass(player, id, "selected", id == selectedId);
    }
}
