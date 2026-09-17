using CounterStrikeSharp.API.Core;
using JBF.Api;

namespace JBF.CommanderTools.Services;

internal sealed class PlayerSelectionService
{
    public void Open(
        CCSPlayerController commander,
        string title,
        IEnumerable<CCSPlayerController> players,
        Action<CCSPlayerController> onSelect)
    {
        var menuApi = MenuCapability.Api.Get();
        if (menuApi is null)
        {
            commander.PrintToChat(JailbreakChat.Format("Menu API недоступно."));
            return;
        }

        var options = players
            .Where(player => player.IsValid)
            .Select(player => new JailbreakMenuOption(
                player.PlayerName,
                _ =>
                {
                    onSelect(player);
                    CommanderMenuCapability.Api.Get()?.Open(commander);
                }))
            .ToArray();

        if (options.Length == 0)
        {
            commander.PrintToChat(JailbreakChat.Format("Нет подходящих игроков."));
            CommanderMenuCapability.Api.Get()?.Open(commander);
            return;
        }

        menuApi.Open(commander, title, options);
    }
}
