using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public interface ICommanderMenuApi
{
    void Open(CCSPlayerController player);

    IDisposable RegisterItem(CommanderMenuItem item);
}
