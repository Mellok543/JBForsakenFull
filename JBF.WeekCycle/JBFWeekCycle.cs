using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using JBF.Api;

namespace JBF.WeekCycle;

public sealed class JBFWeekCycle : BasePlugin, IWeekCycleApi
{
    private int _roundIndex = -1;

    public override string ModuleName => "JBF Week Cycle";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Mell";

    public JailbreakWeekDay CurrentDay =>
        _roundIndex < 0
            ? JailbreakWeekDay.Monday
            : (JailbreakWeekDay)(_roundIndex % 7);

    public string CurrentDayName => GetRussianDayName(CurrentDay);

    public int RoundIndex => Math.Max(0, _roundIndex);

    public override void Load(bool hotReload)
    {
        Capabilities.RegisterPluginCapability(WeekCycleCapability.Api, () => this);
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _roundIndex++;

        var ui = UiCapability.Api.GetOptional();

        foreach (var player in Utilities.GetPlayers())
        {
            if (player is not { IsValid: true, IsBot: false })
                continue;

            if (ui is not null)
                ui.Announce(player, CurrentDayName, $"Раунд {RoundIndex + 1}", UiNotificationType.Important, 5.0f);
            else
                player.PrintToChat(JailbreakChat.Format($"Сегодня: {CurrentDayName}"));
        }

        return HookResult.Continue;
    }


    [ConsoleCommand("css_day", "Show current Jailbreak week day")]
    [ConsoleCommand("css_weekday", "Show current Jailbreak week day")]
    public void OnDayCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !player.IsValid || player.IsBot)
        {
            command.ReplyToCommand(JailbreakChat.Format($"Сегодня: {CurrentDayName} • раунд {RoundIndex + 1}"));
            return;
        }

        player.PrintToChat(JailbreakChat.Format($"Сегодня: {CurrentDayName} • раунд {RoundIndex + 1}"));
    }

    private static string GetRussianDayName(JailbreakWeekDay day) => day switch
    {
        JailbreakWeekDay.Monday => "ПОНЕДЕЛЬНИК",
        JailbreakWeekDay.Tuesday => "ВТОРНИК",
        JailbreakWeekDay.Wednesday => "СРЕДА",
        JailbreakWeekDay.Thursday => "ЧЕТВЕРГ",
        JailbreakWeekDay.Friday => "ПЯТНИЦА",
        JailbreakWeekDay.Saturday => "СУББОТА",
        JailbreakWeekDay.Sunday => "ВОСКРЕСЕНЬЕ",
        _ => "ПОНЕДЕЛЬНИК"
    };
}
