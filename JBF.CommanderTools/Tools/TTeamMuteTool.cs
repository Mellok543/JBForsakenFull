using CounterStrikeSharp.API.Core;
using JBF.Api;
using JBF.CommanderTools.Services;

namespace JBF.CommanderTools.Tools;

internal sealed class TTeamMuteTool : ICommanderTool
{
    private readonly TTeamMuteService _muteService;

    public TTeamMuteTool(TTeamMuteService muteService)
    {
        _muteService = muteService;
    }

    public string Id => "mute-t-team";
    public string Text => "Замутить T на 1 минуту";
    public int Order => 115;

    public void Execute(CCSPlayerController commander)
    {
        if (_muteService.TryApplyManualMute(commander))
        {
            commander.PrintToChat(JailbreakChat.Format($"Осталось мутов T-команды: {_muteService.ManualMuteUsesRemaining}/2."));
        }

        CommanderMenuCapability.Api.Get()?.Open(commander);
    }
}
