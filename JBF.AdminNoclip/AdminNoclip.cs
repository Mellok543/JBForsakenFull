using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;
using IksAdminApi;
using Microsoft.Extensions.Logging;

namespace JBF.AdminNoclip;

public sealed class AdminNoclip : BasePlugin
{
    private const string Permission = "jbf_fun.noclip";
    private const string RequiredFlag = "z";
    private const string MainMenuId = "iksadmin:menu:main";
    private const string FunMenuId = "jbf:admin:fun";
    private const string FunOptionId = "jbf:admin:fun:open";

    private static readonly PluginCapability<IIksAdminApi> AdminCapability =
        new("iksadmin:core");

    private IIksAdminApi? _adminApi;

    public override string ModuleName => "JBF Admin Noclip";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Mell";

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _adminApi = AdminCapability.Get();

        if (_adminApi is null)
        {
            Logger.LogError("IksAdmin API недоступно. JBF Admin Noclip не загружен.");
            return;
        }

        _adminApi.RegisterPermission(Permission, RequiredFlag);
        _adminApi.MenuOpenPre += OnMenuOpenPre;

        _adminApi.AddNewCommand(
            command: "noclip",
            description: "Включить или выключить noclip игроку",
            permission: Permission,
            usage: "css_noclip <игрок>",
            onExecute: OnNoclipCommand,
            whoCanExecute: CommandUsage.CLIENT_ONLY,
            minArgs: 1
        );

        Logger.LogInformation("JBF Admin Noclip загружен. Permission: {Permission}, flag: {Flag}", Permission, RequiredFlag);
    }

    public override void Unload(bool hotReload)
    {
        if (_adminApi is not null)
            _adminApi.MenuOpenPre -= OnMenuOpenPre;
    }

    private HookResult OnMenuOpenPre(
        CCSPlayerController player,
        IDynamicMenu menu,
        IMenu gameMenu)
    {
        if (_adminApi is null || menu.Id != MainMenuId)
            return HookResult.Continue;

        if (menu.Options.Any(option => option.Id == FunOptionId))
            return HookResult.Continue;

        menu.AddMenuOption(
            id: FunOptionId,
            title: "Фан",
            onExecute: (caller, _) => OpenFunMenu(caller, menu),
            viewFlags: RequiredFlag
        );

        return HookResult.Continue;
    }

    private void OpenFunMenu(CCSPlayerController player, IDynamicMenu backMenu)
    {
        if (_adminApi is null)
            return;

        var menu = _adminApi.CreateMenu(
            id: FunMenuId,
            title: "Фан",
            backMenu: backMenu
        );

        menu.AddMenuOption(
            id: "jbf:admin:fun:noclip",
            title: "Noclip",
            onExecute: (caller, _) => OpenNoclipPlayersMenu(caller, menu),
            viewFlags: RequiredFlag
        );

        menu.Open(player);
    }

    private void OpenNoclipPlayersMenu(CCSPlayerController player, IDynamicMenu backMenu)
    {
        if (_adminApi is null)
            return;

        var menu = _adminApi.CreateMenu(
            id: "jbf:admin:fun:noclip:players",
            title: "Noclip — выберите игрока",
            backMenu: backMenu
        );

        foreach (var target in Utilities.GetPlayers()
                     .Where(p => p is { IsValid: true, IsBot: false })
                     .OrderBy(p => p.PlayerName))
        {
            var selectedTarget = target;
            var enabled = IsNoclipEnabled(selectedTarget);

            menu.AddMenuOption(
                id: $"jbf:admin:fun:noclip:{selectedTarget.Slot}",
                title: $"{selectedTarget.PlayerName} [{(enabled ? "ON" : "OFF")}]",
                onExecute: (caller, _) =>
                {
                    ToggleNoclip(caller, selectedTarget);
                    OpenNoclipPlayersMenu(caller, backMenu);
                },
                viewFlags: RequiredFlag
            );
        }

        menu.Open(player);
    }

    private void OnNoclipCommand(
        CCSPlayerController? caller,
        List<string> args,
        CommandInfo command)
    {
        if (caller is null || !caller.IsValid)
            return;

        var targets = command.GetArgTargetResult(1)
            .Players
            .Where(target => target is { IsValid: true, IsBot: false })
            .ToArray();

        if (targets.Length == 0)
        {
            command.ReplyToCommand("Игрок не найден.");
            return;
        }

        if (targets.Length > 1)
        {
            command.ReplyToCommand("Найдено несколько игроков. Укажите имя точнее или используйте #userid.");
            return;
        }

        ToggleNoclip(caller, targets[0]);
    }

    private static void ToggleNoclip(
        CCSPlayerController admin,
        CCSPlayerController target)
    {
        if (!target.IsValid || !target.PawnIsAlive)
        {
            admin.PrintToChat($"[JBF] Игрок {target.PlayerName} мёртв.");
            return;
        }

        var pawn = target.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid)
        {
            admin.PrintToChat($"[JBF] Pawn игрока {target.PlayerName} недоступен.");
            return;
        }

        var enable = pawn.MoveType != MoveType_t.MOVETYPE_NOCLIP;

        pawn.MoveType = enable
            ? MoveType_t.MOVETYPE_NOCLIP
            : MoveType_t.MOVETYPE_WALK;

        pawn.ActualMoveType = enable
            ? MoveType_t.MOVETYPE_OBSERVER
            : MoveType_t.MOVETYPE_WALK;

        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");

        var state = enable ? "включён" : "выключен";

        admin.PrintToChat($"[JBF] Noclip {state} для {target.PlayerName}.");

        if (target != admin)
            target.PrintToChat($"[JBF] Администратор {admin.PlayerName}: noclip {state}.");
    }

    private static bool IsNoclipEnabled(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        return pawn is { IsValid: true } &&
               pawn.MoveType == MoveType_t.MOVETYPE_NOCLIP;
    }
}
