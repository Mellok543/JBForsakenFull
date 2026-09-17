using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public interface IMenuApi
{
    bool IsOpen(CCSPlayerController player);

    void Open(
        CCSPlayerController player,
        string title,
        IReadOnlyList<JailbreakMenuOption> options);

    void Close(CCSPlayerController player);
}
