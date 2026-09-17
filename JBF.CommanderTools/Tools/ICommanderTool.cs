using CounterStrikeSharp.API.Core;

namespace JBF.CommanderTools.Tools;

internal interface ICommanderTool
{
    string Id { get; }

    string Text { get; }

    int Order { get; }

    void Execute(CCSPlayerController commander);
}
