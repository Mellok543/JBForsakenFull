using CounterStrikeSharp.API.Core;
using JBF.Api;
using JBF.CommanderTools.Services;

namespace JBF.CommanderTools.Tools;

internal sealed class BhopTool : ICommanderTool
{
    private readonly CommanderToolsState _state;

    public BhopTool(CommanderToolsState state)
    {
        _state = state;
    }

    public string Id => "bhop";
    public string Text => "Bhop";
    public int Order => 40;

    public void Execute(CCSPlayerController commander)
    {
        _state.BhopEnabled = !_state.BhopEnabled;
        commander.PrintToChat(JailbreakChat.Format($"Bhop: {(_state.BhopEnabled ? "включён" : "выключен")}."));
        CommanderMenuCapability.Api.Get()?.Open(commander);
    }
}
