using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using JBF.Api;
using JBF.MainMenu.Config;
using Microsoft.Extensions.Logging;

namespace JBF.MainMenu;

public sealed class JBFMainMenu : BasePlugin
{
    private MainMenuConfig _config = new();
    private string? _configPath;

    public override string ModuleName => "JBF Main Menu";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        _configPath = Path.Combine(ModuleDirectory, "config", "mainmenu.json");
        _config = MainMenuConfig.LoadOrCreate(
            _configPath,
            message => Logger.LogError("{Message}", message));
    }

    [ConsoleCommand("css_menu", "Open JBF main menu")]
    public void OnMenu(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !player.IsValid || player.IsBot)
        {
            command.ReplyToCommand(JailbreakChat.Format("Команда доступна только игрокам."));
            return;
        }

        var menuApi = MenuCapability.Api.GetOptional();
        if (menuApi is null)
        {
            command.ReplyToCommand(JailbreakChat.Format("Menu API недоступно."));
            return;
        }

        var options = _config.MenuItems
            .Where(item =>
                item.Enabled &&
                !string.IsNullOrWhiteSpace(item.Text) &&
                !string.IsNullOrWhiteSpace(item.Command))
            .Select(item =>
            {
                var text = item.Text;
                var targetCommand = NormalizeServerCommand(item.Command);

                return new JailbreakMenuOption(
                    text,
                    selectedPlayer =>
                    {
                        menuApi.Close(selectedPlayer);

                        try
                        {
                            selectedPlayer.ExecuteClientCommandFromServer(targetCommand);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(
                                ex,
                                "MainMenu failed to execute {Command} for {Player}",
                                targetCommand,
                                selectedPlayer.PlayerName);

                            selectedPlayer.PrintToChat(
                                JailbreakChat.Format($"Не удалось открыть: {text}."));
                        }
                    });
            })
            .ToArray();

        if (options.Length == 0)
        {
            player.PrintToChat(JailbreakChat.Format("Главное меню пока пустое."));
            return;
        }

        menuApi.Open(player, _config.MenuTitle, options);
    }

    [ConsoleCommand("css_help", "Show available JBF commands")]
    public void OnHelp(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !player.IsValid || player.IsBot)
        {
            command.ReplyToCommand(JailbreakChat.Format("Команда доступна только игрокам."));
            return;
        }

        player.PrintToChat(JailbreakChat.Format(_config.HelpHeader));

        foreach (var entry in _config.HelpCommands.Where(x =>
                     x.Enabled && !string.IsNullOrWhiteSpace(x.Command)))
        {
            var description = string.IsNullOrWhiteSpace(entry.Description)
                ? string.Empty
                : $" — {entry.Description}";

            player.PrintToChat(JailbreakChat.Format($"{entry.Command}{description}"));
        }
    }

    [ConsoleCommand("css_menu_reload", "Reload JBF main menu config")]
    [RequiresPermissions("@jbf/admin")]
    public void OnReload(CCSPlayerController? player, CommandInfo command)
    {
        if (string.IsNullOrWhiteSpace(_configPath))
        {
            command.ReplyToCommand(JailbreakChat.Format("Путь к конфигу недоступен."));
            return;
        }

        if (!MainMenuConfig.TryLoad(_configPath, out var loaded, out var error))
        {
            command.ReplyToCommand(JailbreakChat.Format($"Ошибка mainmenu.json: {error}"));
            return;
        }

        _config = loaded;
        command.ReplyToCommand(JailbreakChat.Format("Главное меню и !help перезагружены."));
    }

    private static string NormalizeServerCommand(string command)
    {
        var value = command.Trim();

        if (value.StartsWith("!", StringComparison.Ordinal) ||
            value.StartsWith("/", StringComparison.Ordinal))
        {
            value = value[1..];
        }

        if (!value.StartsWith("css_", StringComparison.OrdinalIgnoreCase))
            value = $"css_{value}";

        return value;
    }
}
