using CounterStrikeSharp.API.Core;
using JBF.Api;

namespace JBF.CommanderTools.Services;

internal sealed class CommanderMenuService : ICommanderMenuApi
{
    private readonly Dictionary<string, RegisteredCommanderMenuItem> _items = new(StringComparer.Ordinal);

    public void Open(CCSPlayerController player)
    {
        var wardenApi = WardenCapability.Api.Get();
        if (wardenApi is null)
        {
            player.PrintToChat(JailbreakChat.Format("Warden API недоступно."));
            return;
        }

        if (!wardenApi.IsWarden(player))
        {
            player.PrintToChat(JailbreakChat.Format("Меню доступно только командиру."));
            return;
        }

        var menuApi = MenuCapability.Api.Get();
        if (menuApi is null)
        {
            player.PrintToChat(JailbreakChat.Format("Menu API недоступно."));
            return;
        }

        var options = _items.Values
            .Select(entry => entry.Item)
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Text, StringComparer.Ordinal)
            .Select(item => new JailbreakMenuOption(item.Text, item.OnSelect))
            .Append(new JailbreakMenuOption("Покинуть пост", Resign))
            .ToArray();

        menuApi.Open(player, "Меню командира", options);
    }

    public IDisposable RegisterItem(CommanderMenuItem item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Text);

        var registration = new RegisteredCommanderMenuItem(Guid.NewGuid(), item);
        _items[item.Id] = registration;

        return new ActionDisposable(() => Remove(item.Id, registration.Token));
    }

    private void Resign(CCSPlayerController player)
    {
        if (WardenCapability.Api.Get()?.TryResign(player) == true)
        {
            player.PrintToChat(JailbreakChat.Format("Вы покинули пост командира."));
        }
    }

    private void Remove(string id, Guid token)
    {
        if (_items.TryGetValue(id, out var registration) && registration.Token == token)
        {
            _items.Remove(id);
        }
    }

}
