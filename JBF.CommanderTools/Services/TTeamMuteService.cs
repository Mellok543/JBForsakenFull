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
    private Timer? _hudTimer;
    private int _manualMuteUses;
    private bool _isMuted;
    private bool _showRoundStartHud;
    private DateTime _muteEndsAt;

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
        _showRoundStartHud = true;
        Mute(
            TimeSpan.FromSeconds(30),
            "Команда T замучена на 30 секунд, командир отдаёт приказ.",
            countManualUse: false);
        StartHudCountdown();
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
        _hudTimer?.Kill();
        _hudTimer = null;

        if (_showRoundStartHud)
        {
            _showRoundStartHud = false;
            ClearHud();
        }

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
        _muteEndsAt = DateTime.UtcNow.Add(duration);
        SetTTeamMuted(true);
        Server.PrintToChatAll(JailbreakChat.Format(message));

        _timer = _plugin.AddTimer((float)duration.TotalSeconds, ClearMute, TimerFlags.STOP_ON_MAPCHANGE);
    }


    private void StartHudCountdown()
    {
        _hudTimer?.Kill();
        UpdateHud();

        _hudTimer = _plugin.AddTimer(
            1.0f,
            UpdateHud,
            TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void UpdateHud()
    {
        if (!_showRoundStartHud || !_isMuted)
            return;

        var secondsLeft = Math.Max(
            0,
            (int)Math.Ceiling((_muteEndsAt - DateTime.UtcNow).TotalSeconds));

        var text = $"МУТ ЗЕКОВ 0:{secondsLeft:00}";
        var ui = UiCapability.Api.GetOptional();

        if (ui is not null)
        {
            foreach (var player in Utilities.GetPlayers().Where(player =>
                         player.IsValid && !player.IsBot))
            {
                ui.SetMuteStatus(player, text);
            }
        }

        if (secondsLeft <= 0)
        {
            _hudTimer?.Kill();
            _hudTimer = null;
        }
    }

    private static void ClearHud()
    {
        var ui = UiCapability.Api.GetOptional();
        if (ui is null)
            return;

        foreach (var player in Utilities.GetPlayers().Where(player =>
                     player.IsValid && !player.IsBot))
        {
            ui.ClearMuteStatus(player);
        }
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
