using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public interface IUiApi
{
    void Notify(CCSPlayerController player, string text, UiNotificationType type = UiNotificationType.Info, float durationSeconds = 4.0f);
    void Announce(CCSPlayerController player, string title, string subtitle = "", UiNotificationType type = UiNotificationType.Important, float durationSeconds = 5.0f);
    void SetQuestion(CCSPlayerController player, string progress, string question, string timer = "");
    void ClearQuestion(CCSPlayerController player);
    void SetMapVote(CCSPlayerController player, string title, IReadOnlyList<MapVoteHudEntry> entries, string timer = "");
    void ClearMapVote(CCSPlayerController player);
    void SetRoundStatus(CCSPlayerController player, string title, string value = "");
    void ClearRoundStatus(CCSPlayerController player);
    void SetPlayerStatus(CCSPlayerController player, string primary, string secondary = "");
    void ClearPlayerStatus(CCSPlayerController player);
    void Clear(CCSPlayerController player);
}
