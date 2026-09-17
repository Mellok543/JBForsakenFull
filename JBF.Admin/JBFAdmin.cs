using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using JBF.Api;

namespace JBF.Admin;

public sealed class JBFAdmin : BasePlugin
{
    private const string AdminPermission = "@jbf/admin";
    private ILrApi? _lrApi;
    private IShopApi? _shopApi;
    private IAchievementsApi? _achievementsApi;

    public override string ModuleName => "JBF Admin";
    public override string ModuleVersion => "1.1.0";
    public override string ModuleAuthor => "Mell";

    public override void OnAllPluginsLoaded(bool hotReload) => RefreshApis();

    [ConsoleCommand("css_jbf_lrstop", "Stop the active JBF Last Request")]
    [RequiresPermissions(AdminPermission)]
    public void OnLrStopCommand(CCSPlayerController? player, CommandInfo command)
    {
        RefreshApis();
        if (_lrApi is null) { Reply(command, "LR API недоступно."); return; }
        if (!_lrApi.TryEndActive()) { Reply(command, "Активного LR сейчас нет."); return; }
        Reply(command, "LR остановлен администратором.");
        LogAction(player, "остановил активный LR");
    }

    [ConsoleCommand("css_jbf_daystop", "Stop or cancel the current JBF special day")]
    [RequiresPermissions(AdminPermission)]
    public void OnSpecialDayStopCommand(CCSPlayerController? player, CommandInfo command)
    {
        var specialDays = SpecialDaysCapability.Api.Get();
        if (specialDays is null) { Reply(command, "Special Days API недоступно."); return; }
        if (!specialDays.TryStop()) { Reply(command, "Активного или назначенного игрового дня нет."); return; }
        Reply(command, "Игровой день остановлен или отменён.");
        LogAction(player, "остановил или отменил игровой день");
    }

    [ConsoleCommand("css_jbf_warden_remove", "Remove the current JBF warden")]
    [RequiresPermissions(AdminPermission)]
    public void OnWardenRemoveCommand(CCSPlayerController? player, CommandInfo command)
    {
        var api = WardenCapability.Api.Get();
        var warden = api?.Warden;
        if (api is null) { Reply(command, "Warden API недоступно."); return; }
        if (warden is null || !api.TryResign(warden)) { Reply(command, "Командира сейчас нет."); return; }
        Reply(command, $"Командир {warden.PlayerName} снят с поста.");
        LogAction(player, $"снял командира {warden.PlayerName} с поста");
    }

    [ConsoleCommand("css_jbf_shopreload", "Reload the JBF shop config")]
    [RequiresPermissions(AdminPermission)]
    public void OnShopReloadCommand(CCSPlayerController? player, CommandInfo command)
    {
        RefreshApis();
        if (_shopApi is null) { Reply(command, "Shop API недоступно."); return; }
        _shopApi.ReloadConfig();
        Reply(command, "Конфиг магазина перезагружен.");
        LogAction(player, "перезагрузил конфиг магазина");
    }

    [ConsoleCommand("css_jbf_credits", "Show a player's JBF credit balance")]
    [RequiresPermissions(AdminPermission)]
    [CommandHelper(1, "<игрок>", CommandUsage.CLIENT_AND_SERVER)]
    public void OnCreditsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryGetShopAndTarget(player, command, out var shop, out var target)) return;
        if (!shop.TryGetCredits(target, out var credits)) { Reply(command, "Не удалось получить SteamID игрока."); return; }
        Reply(command, $"Баланс {target.PlayerName}: {credits} кредитов.");
    }

    [ConsoleCommand("css_jbf_credits_give", "Give JBF credits to a player")]
    [RequiresPermissions(AdminPermission)]
    [CommandHelper(2, "<игрок> <количество>", CommandUsage.CLIENT_AND_SERVER)]
    public void OnCreditsGiveCommand(CCSPlayerController? player, CommandInfo command) => ChangeCredits(player, command, CreditOperation.Give);

    [ConsoleCommand("css_jbf_credits_take", "Take JBF credits from a player")]
    [RequiresPermissions(AdminPermission)]
    [CommandHelper(2, "<игрок> <количество>", CommandUsage.CLIENT_AND_SERVER)]
    public void OnCreditsTakeCommand(CCSPlayerController? player, CommandInfo command) => ChangeCredits(player, command, CreditOperation.Take);

    [ConsoleCommand("css_jbf_credits_set", "Set a player's JBF credit balance")]
    [RequiresPermissions(AdminPermission)]
    [CommandHelper(2, "<игрок> <количество>", CommandUsage.CLIENT_AND_SERVER)]
    public void OnCreditsSetCommand(CCSPlayerController? player, CommandInfo command) => ChangeCredits(player, command, CreditOperation.Set);

    [ConsoleCommand("css_jbf_ach_unlock", "Unlock achievement for a player")]
    [RequiresPermissions(AdminPermission)]
    [CommandHelper(2, "<игрок> <achievement_id>", CommandUsage.CLIENT_AND_SERVER)]
    public void OnAchievementUnlock(CCSPlayerController? caller, CommandInfo command)
    {
        if (!TryGetAchievementsAndTarget(caller, command, out var api, out var target)) return;
        var id = command.GetArg(2);
        if (!api.TryUnlock(target, id)) { Reply(command, "Достижение не найдено или SteamID недоступен."); return; }
        Reply(command, $"Достижение {id} выдано игроку {target.PlayerName}.");
        LogAction(caller, $"выдал достижение {id} игроку {target.PlayerName}");
    }

    [ConsoleCommand("css_jbf_ach_reset", "Reset achievement for a player")]
    [RequiresPermissions(AdminPermission)]
    [CommandHelper(2, "<игрок> <achievement_id>", CommandUsage.CLIENT_AND_SERVER)]
    public void OnAchievementReset(CCSPlayerController? caller, CommandInfo command)
    {
        if (!TryGetAchievementsAndTarget(caller, command, out var api, out var target)) return;
        var id = command.GetArg(2);
        if (!api.TryResetAchievement(target, id)) { Reply(command, "Достижение не найдено или SteamID недоступен."); return; }
        Reply(command, $"Достижение {id} сброшено у {target.PlayerName}.");
        LogAction(caller, $"сбросил достижение {id} игроку {target.PlayerName}");
    }

    [ConsoleCommand("css_jbf_stat_set", "Set achievement statistic")]
    [RequiresPermissions(AdminPermission)]
    [CommandHelper(3, "<игрок> <stat_key> <value>", CommandUsage.CLIENT_AND_SERVER)]
    public void OnStatSet(CCSPlayerController? caller, CommandInfo command)
    {
        if (!TryGetAchievementsAndTarget(caller, command, out var api, out var target)) return;
        if (!long.TryParse(command.GetArg(3), out var value) || value < 0) { Reply(command, "Значение должно быть неотрицательным числом."); return; }
        var key = command.GetArg(2);
        if (!api.TrySetStat(target, key, value)) { Reply(command, "Не удалось изменить статистику."); return; }
        Reply(command, $"{target.PlayerName}: {key} = {value}.");
        LogAction(caller, $"установил {key}={value} игроку {target.PlayerName}");
    }

    [ConsoleCommand("css_jbf_stat_get", "Show achievement statistic")]
    [RequiresPermissions(AdminPermission)]
    [CommandHelper(2, "<игрок> <stat_key>", CommandUsage.CLIENT_AND_SERVER)]
    public void OnStatGet(CCSPlayerController? caller, CommandInfo command)
    {
        if (!TryGetAchievementsAndTarget(caller, command, out var api, out var target)) return;
        var key = command.GetArg(2);
        Reply(command, $"{target.PlayerName}: {key} = {api.GetStat(target, key)}.");
    }

    [ConsoleCommand("css_jbf_admin_status", "Show JBF module status")]
    [RequiresPermissions(AdminPermission)]
    public void OnStatusCommand(CCSPlayerController? player, CommandInfo command)
    {
        RefreshApis();
        var core = JailbreakCapability.Api.Get();
        var specialDays = SpecialDaysCapability.Api.Get();
        Reply(command, $"Core: {FormatAvailability(core)} | Раунд: {core?.RoundState.ToString() ?? "нет API"} | LR: {FormatActivity(_lrApi?.IsActive, _lrApi?.ActiveGameName)} | Игровой день: {FormatActivity(specialDays?.IsActive, specialDays?.ActiveDayName)} | Shop: {FormatAvailability(_shopApi)} | Achievements: {FormatAvailability(_achievementsApi)}.");
    }

    private void ChangeCredits(CCSPlayerController? caller, CommandInfo command, CreditOperation operation)
    {
        if (!TryGetShopAndTarget(caller, command, out var shop, out var target)) return;
        if (!int.TryParse(command.GetArg(2), out var amount) || amount < 0) { Reply(command, "Количество должно быть целым неотрицательным числом."); return; }
        var balance = 0;
        var success = operation switch
        {
            CreditOperation.Give => shop.TryAddCredits(target, amount, out balance),
            CreditOperation.Take => shop.TryAddCredits(target, -amount, out balance),
            CreditOperation.Set => shop.TrySetCredits(target, amount, out balance),
            _ => false
        };
        if (!success) { Reply(command, "Не удалось изменить баланс: SteamID игрока недоступен."); return; }
        Reply(command, $"Баланс {target.PlayerName}: {balance} кредитов.");
        LogAction(caller, $"изменил баланс игроку {target.PlayerName}; новый баланс: {balance}");
    }

    private bool TryGetShopAndTarget(CCSPlayerController? caller, CommandInfo command, out IShopApi shop, out CCSPlayerController target)
    {
        RefreshApis();
        if (_shopApi is null) { Reply(command, "Shop API недоступно."); shop = null!; target = null!; return false; }
        if (!TryGetTarget(caller, command, out target)) { shop = null!; return false; }
        shop = _shopApi;
        return true;
    }

    private bool TryGetAchievementsAndTarget(CCSPlayerController? caller, CommandInfo command, out IAchievementsApi api, out CCSPlayerController target)
    {
        RefreshApis();
        if (_achievementsApi is null) { Reply(command, "Achievements API недоступно."); api = null!; target = null!; return false; }
        if (!TryGetTarget(caller, command, out target)) { api = null!; return false; }
        api = _achievementsApi;
        return true;
    }

    private static bool TryGetTarget(CCSPlayerController? caller, CommandInfo command, out CCSPlayerController target)
    {
        var targets = command.GetArgTargetResult(1).Players.Where(candidate => candidate.IsValid && !candidate.IsBot).ToArray();
        if (targets.Length != 1)
        {
            command.ReplyToCommand(JailbreakChat.Format(targets.Length == 0 ? "Игрок не найден." : "Найдено несколько игроков. Укажите имя точнее или используйте #userid."));
            target = null!;
            return false;
        }
        target = targets[0];
        if (caller is not null && !AdminManager.CanPlayerTarget(caller, target))
        {
            command.ReplyToCommand(JailbreakChat.Format("У вас недостаточно иммунитета для управления этим игроком."));
            target = null!;
            return false;
        }
        return true;
    }

    private void RefreshApis()
    {
        _lrApi = LrCapability.Api.Get();
        _shopApi = ShopCapability.Api.Get();
        _achievementsApi = AchievementsCapability.Api.Get();
    }

    private static string FormatAvailability(object? api) => api is null ? "недоступен" : "доступен";
    private static string FormatActivity(bool? active, string? name) => active switch { true => name ?? "активен", false => "не активен", null => "нет API" };
    private static void Reply(CommandInfo command, string message) => command.ReplyToCommand(JailbreakChat.Format(message));
    private static void LogAction(CCSPlayerController? caller, string action)
    {
        var actor = caller is null ? "Server Console" : $"{caller.PlayerName} ({GetSteamId(caller)})";
        Server.PrintToConsole($"[JBF] Admin: {actor} {action}.");
    }
    private static string GetSteamId(CCSPlayerController player) => player.AuthorizedSteamID?.SteamId64.ToString() ?? "SteamID unavailable";
    private enum CreditOperation { Give, Take, Set }
}
