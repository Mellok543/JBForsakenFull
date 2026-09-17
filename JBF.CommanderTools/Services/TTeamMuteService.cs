using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace JBF.CommanderTools.Services;

internal sealed class TTeamMuteService
{
    private const int ManualMuteLimit = 2;

    private readonly BasePlugin _plugin;
    private Timer? _timer;
    private int _manualMuteUses;
    private bool _isMuted;

    public TTeamMuteService(BasePlugin plugin)
    {
        _plugin = plugin;
    }

    public int ManualMuteUsesRemaining => Math.Max(0, ManualMuteLimit - _manualMuteUses);

    public void ResetRound()
    {
        _manualMuteUses = 0;
        ClearMute();
    }

    public void ApplyRoundStartMute()
    {
        Mute(TimeSpan.FromSeconds(30), "Команда T замучена на 30 секунд, командир отдаёт приказ.", countManualUse: false);
    }

    public bool TryApplyManualMute(CCSPlayerController commander)
    {
        if (_manualMuteUses >= ManualMuteLimit)
        {
            commander.PrintToChat(JailbreakChat.Format("Лимит мута T-команды на этот раунд исчерпан."));
            return false;
        }

        _manualMuteUses++;
        Mute(TimeSpan.FromMinutes(1), "Команда T замучена командиром на 1 минуту.", countManualUse: false);
        return true;
    }

    public void ClearMute()
    {
        _timer?.Kill();
        _timer = null;

        if (!_isMuted)
        {
            return;
        }

        _isMuted = false;
        SetTTeamMuted(false);
        Server.PrintToChatAll(JailbreakChat.Format("Мут команды T снят."));
    }

    private void Mute(TimeSpan duration, string message, bool countManualUse)
    {
        if (countManualUse)
        {
            _manualMuteUses++;
        }

        _timer?.Kill();
        _isMuted = true;
        SetTTeamMuted(true);
        Server.PrintToChatAll(JailbreakChat.Format(message));

        _timer = _plugin.AddTimer((float)duration.TotalSeconds, ClearMute, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private static void SetTTeamMuted(bool muted)
    {
        foreach (var player in Utilities.GetPlayers().Where(player =>
                     player.IsValid && !player.IsBot && player.Team == CsTeam.Terrorist))
        {
            if (muted)
            {
                player.VoiceFlags |= VoiceFlags.Muted;
            }
            else
            {
                player.VoiceFlags &= ~VoiceFlags.Muted;
            }
        }
    }
}
