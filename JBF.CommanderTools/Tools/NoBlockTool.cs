using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using JBF.Api;
using JBF.CommanderTools.Services;

namespace JBF.CommanderTools.Tools;

internal sealed class NoBlockTool : ICommanderTool
{
    private readonly CommanderToolsState _state;

    public NoBlockTool(CommanderToolsState state)
    {
        _state = state;
    }

    public string Id => "no-block";
    public string Text => "NoBlock";
    public int Order => 30;

    public void Execute(CCSPlayerController commander)
    {
        _state.NoBlockEnabled = !_state.NoBlockEnabled;
        ConVar.Find("mp_solid_teammates")?.SetValue(_state.NoBlockEnabled ? 0 : 1);
        commander.PrintToChat(JailbreakChat.Format($"NoBlock: {(_state.NoBlockEnabled ? "включён" : "выключен")}."));
        CommanderMenuCapability.Api.Get()?.Open(commander);
    }
}
