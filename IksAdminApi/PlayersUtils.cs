using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace IksAdminApi;

public static class PlayersUtils
{
    // HTML MESSAGES
    public static Dictionary<CCSPlayerController, string> HtmlMessages = new();
    public static Dictionary<CCSPlayerController, Timer> HtmlMessagesTimer = new();
    public static void HtmlMessage(this CCSPlayerController player, string message, float time = 1)
    {
        ClearHtmlMessage(player);
        if (message == "") return;
        HtmlMessages.Add(player, message);
        HtmlMessagesTimer.Add(player, AdminModule.Api.Plugin.AddTimer(time, () =>
        {
            ClearHtmlMessage(player);
        }));
    }
    public static void ClearHtmlMessage(this CCSPlayerController player)
    {
        HtmlMessages.Remove(player);
        if (HtmlMessagesTimer.TryGetValue(player, out var timer))
        {
            timer.Kill();
            HtmlMessagesTimer.Remove(player);
        }
    }
    public static void CloseMenu(this CCSPlayerController player)
    {
        AdminModule.Api.CloseMenu(player);
    }
    /// <summary>
    /// may cause errors
    /// </summary>
    public static CCSPlayerController? GetControllerBySteamIdUnsafe(string steamId)
    {
        for (int i = 0; i < 72; i++)
        {
            var p = Utilities.GetPlayerFromSlot(i);
            if (p == null) continue;
            if (p.AuthorizedSteamID == null) continue;
            if (p.AuthorizedSteamID.SteamId64.ToString() == steamId) return p;
        }
        return null;
    }
    public static CCSPlayerController? GetControllerBySteamId(string steamId)
    {
        return Utilities.GetPlayers().FirstOrDefault(x => x != null && x.IsValid && x.AuthorizedSteamID != null && x.AuthorizedSteamID.SteamId64.ToString() == steamId);
    }
    public static CCSPlayerController? GetControllerBySteamId(ulong steamId)
    {
        return Utilities.GetPlayers().FirstOrDefault(x => x != null && x.IsValid && x.AuthorizedSteamID != null && x.AuthorizedSteamID.SteamId64 == steamId);
    }
    public static CCSPlayerController? GetControllerByUid(uint userId)
    {
        return Utilities.GetPlayers().FirstOrDefault(x => x != null && x.IsValid && x.Connected == PlayerConnectedState.Connected && x.UserId == userId);
    }
    public static CCSPlayerController? GetControllerByName(string name, bool ignoreRegistry = false)
    {
        return Utilities.GetPlayers().FirstOrDefault(x => x != null && x.IsValid && x.Connected == PlayerConnectedState.Connected && (ignoreRegistry ? x.PlayerName.ToLower().Contains(name) : x.PlayerName.Contains(name)));
    }
    public static CCSPlayerController? GetControllerByIp(string ip)
    {
        return Utilities.GetPlayers().FirstOrDefault(x =>
        {
            if (x == null || !x.IsValid || x.AuthorizedSteamID == null || x.Connected != PlayerConnectedState.Connected)
                return false;

            var rawIp = x.IpAddress;
            if (string.IsNullOrWhiteSpace(rawIp))
                return false;

            var playerIp = rawIp.Split(':')[0];
            if (AdminModule.Api.Config.MirrorsIp.Contains(playerIp))
                return false;

            return string.Equals(playerIp, ip, StringComparison.OrdinalIgnoreCase);
        });
    }
    public static List<CCSPlayerController> GetOnlinePlayers(bool includeBots = false)
    {
        if (includeBots)
            return Utilities.GetPlayers().Where(x => x != null && x.IsValid && x.Connected == PlayerConnectedState.Connected).ToList();
        return Utilities.GetPlayers().Where(x => x != null && x.IsValid && !x.IsBot && x.AuthorizedSteamID != null && x.Connected == PlayerConnectedState.Connected).ToList();
    }
}