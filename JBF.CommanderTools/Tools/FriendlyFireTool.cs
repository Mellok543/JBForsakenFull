using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using JBF.Api;
using JBF.CommanderTools.Services;

namespace JBF.CommanderTools.Tools;

internal sealed class FriendlyFireTool : ICommanderTool
{
    private readonly CommanderToolsState _state;

    public FriendlyFireTool(CommanderToolsState state)
    {
        _state = state;
    }

    public string Id => "friendly-fire";
    public string Text => "Огонь по своим";
    public int Order => 20;

    public void Execute(CCSPlayerController commander)
    {
        _state.FriendlyFireEnabled = !_state.FriendlyFireEnabled;
        ConVar.Find("mp_teammates_are_enemies")?.SetValue(_state.FriendlyFireEnabled);
        commander.PrintToChat(JailbreakChat.Format($"Огонь по своим: {(_state.FriendlyFireEnabled ? "включён" : "выключен")}."));
        CommanderMenuCapability.Api.Get()?.Open(commander);
    }
}
