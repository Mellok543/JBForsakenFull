using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using IksAdminApi;
using JBF.Api;
using CounterStrikeSharp.API.Modules.Utils;
using MenuType = IksAdminApi.MenuType;
using CounterStrikeSharp.API.Core.Translations;
using System.Diagnostics;
using IMenu = CounterStrikeSharp.API.Modules.Menu.IMenu;

namespace IksAdmin.Menu;

public class DynamicMenu : IDynamicMenu
{
    public string Id {get; set;}

    public string Title {get; set;} = "Dynamic Menu";
    public MenuColors TitleColor {get; set;}
    public MenuType Type {get; set;} = MenuType.Default;
    public Action<CCSPlayerController>? BackAction {get; set;} = null;
    public List<ChatMenuOption> MenuOptions { get; }
    public PostSelectAction PostSelectAction {get; set;} = PostSelectAction.Nothing;
    public bool ExitButton { get; set; }
    public List<IDynamicMenuOption> Options {get; set;} = new();
    private bool _backOptionRendered = false;
    public DynamicMenu(string id, string title, MenuType type = (MenuType)3, MenuColors titleColor = MenuColors.Default, PostSelectAction postSelectAction = PostSelectAction.Nothing, Action<CCSPlayerController>? backAction = null, IDynamicMenu? backMenu = null)
    {
        Id = id;
        Title = title;
        TitleColor = titleColor;
        Type = type;
        PostSelectAction = postSelectAction;
        BackAction = backAction;
        if (backMenu != null)
        {
            BackAction = player => backMenu.Open(player);
        }
        
        AdminUtils.LogDebug($@"
            Menu created:
            Id: {Id}
            Title: {Title}
            Type: {Type}
        ");
    }

    public void Open(CCSPlayerController player, bool useSortMenu = true)
    {
        var menuApi = JBF.Api.MenuCapability.Api.GetOptional();
        if (menuApi is null)
        {
            player.PrintToChat(JailbreakChat.Format("JBF Menu API недоступно."));
            return;
        }

        var pAdmin = player.Admin();
        var adminFlags = pAdmin?.CurrentFlags.ToCharArray() ?? [];
        var oldOptions = Options.ToList();

        // Keep the IksAdmin menu lifecycle/events intact for third-party admin modules.
        IMenu gameMenu = this;
        if (!Main.AdminApi.OnMenuOpenPre(player, this, gameMenu))
            return;

        var options = oldOptions.ToList();
        if (BackAction is not null)
        {
            options.Insert(
                0,
                new DynamicMenuOption(
                    "back_btn",
                    Main.AdminApi.Localizer["MenuOption.Other.Back"],
                    (p, _) => BackAction.Invoke(p),
                    null,
                    false));
        }

        var ordered = new List<IDynamicMenuOption>();

        if (useSortMenu && Main.AdminApi.SortMenus.TryGetValue(Id, out var sortMenu))
        {
            var remaining = options.ToList();

            foreach (var sort in sortMenu)
            {
                var option = remaining.FirstOrDefault(x => x.Id == sort.Id);
                if (option is null)
                    continue;

                remaining.Remove(option);

                if (!sort.View)
                    continue;

                var viewFlags = sort.ViewFlags.ToLowerInvariant() == "not override"
                    ? option.ViewFlags
                    : sort.ViewFlags;

                if (!CanView(viewFlags, pAdmin, adminFlags))
                    continue;

                ordered.Add(option);
            }

            foreach (var option in remaining)
            {
                if (CanView(option.ViewFlags, pAdmin, adminFlags))
                    ordered.Add(option);
            }
        }
        else
        {
            ordered.AddRange(options.Where(option => CanView(option.ViewFlags, pAdmin, adminFlags)));
        }

        var rendered = new List<JailbreakMenuOption>();

        foreach (var option in ordered)
        {
            if (!Main.AdminApi.OnOptionRenderPre(player, this, gameMenu, option))
                continue;

            var captured = option;
            rendered.Add(
                new JailbreakMenuOption(
                    OptionTitle(player, captured),
                    selected =>
                    {
                        if (!Main.AdminApi.OnOptionExecutedPre(selected, this, gameMenu, captured))
                            return;

                        captured.OnExecute(selected, captured);
                        Main.AdminApi.OnOptionExecutedPost(selected, this, gameMenu, captured);
                    },
                    captured.Disabled));

            Main.AdminApi.OnOptionRenderPost(player, this, gameMenu, option);
        }

        menuApi.Open(player, MenuTitle(player), rendered);
        Main.AdminApi.OnMenuOpenPost(player, this, gameMenu);
        Options = oldOptions;
    }

    private static bool CanView(string viewFlags, Admin? admin, char[] adminFlags)
    {
        if (viewFlags.Contains("*"))
            return true;

        if (admin is null)
            return false;

        return adminFlags.Contains('z') || adminFlags.Any(viewFlags.Contains);
    }

    private string MenuTitle(CCSPlayerController player)
    {
        string colorString = GetMenuColorString(player, TitleColor);
        var fullTitleString = colorString.Replace("{value}", RemoveDangerSymbols(player, Title));
        AdminUtils.LogDebug($"Full title string: {fullTitleString}");
        return fullTitleString;
    }

    public string OptionTitle(CCSPlayerController player, IDynamicMenuOption option)
    {
        string colorString = GetMenuColorString(player, option.Color);
        var fullOptionString = colorString.Replace("{value}", RemoveDangerSymbols(player, option.Title));
        AdminUtils.LogDebug($"Full option string: {fullOptionString}");
        return fullOptionString;
    }

    private string RemoveDangerSymbols(CCSPlayerController player, string str)
    {
        return str.Replace("<", "").Replace(">", "");
    }

    public MenuType GetThisMenuType(CCSPlayerController player)
    {
        // IksAdmin now renders through the shared JBF Panorama menu.
        return Type == MenuType.Default ? MenuType.CenterMenu : Type;
    }

    private string GetMenuColorString(CCSPlayerController player, MenuColors color)
    {
        // char[] chatColors = new char[] {
        //     ChatColors.Default, ChatColors.White, ChatColors.DarkRed, ChatColors.Green, ChatColors.LightYellow, ChatColors.LightBlue, ChatColors.Olive, ChatColors.Lime, ChatColors.Red, ChatColors.LightPurple, ChatColors.Purple, ChatColors.Grey, ChatColors.Yellow, ChatColors.Gold, ChatColors.Silver, ChatColors.Blue, ChatColors.DarkBlue, ChatColors.BlueGrey, ChatColors.Magenta, ChatColors.LightRed, ChatColors.Orange, ChatColors.DarkRed
        // };
        // var menuType = GetThisMenuType(player);
        // if (menuType == MenuType.ChatMenu)
        // {
        //     return $"{chatColors[(int)color]}" + "{value}";
        // } else if (menuType == MenuType.ButtonMenu)
        // {
        //     string[] htmlColors = new string[] {
        //         "<font color='white'>",
        //         "<font color='white'>",
        //         "<font color='darkred'>",
        //         "<font color='green'>",
        //         "<font color='lightyellow'>",
        //         "<font color='lightblue'>",
        //         "<font color='olive'>",
        //         "<font color='lime'>",
        //         "<font color='red'>",
        //         "<font color='lightpurple'>",
        //         "<font color='purple'>",
        //         "<font color='grey'>",
        //         "<font color='yellow'>",
        //         "<font color='gold'>",
        //         "<font color='silver'>",
        //         "<font color='blue'>",
        //         "<font color='darkblue'>",
        //         "<font color='lightred'>",
        //         "<font color='orange'>",
        //         "<font color='darkred'>"
        //     };
        //     return $"{htmlColors[(int)color]}" + "{value}</font>";
        // }
        
        return "{value}";
    }

    private string GetOnlyColorString(MenuColors color)
    {
        string[] colors = new string[] {
                "white",
                "white",
                "darkred",
                "green",
                "lightyellow",
                "lightblue",
                "olive",
                "lime",
                "red",
                "lightpurple",
                "purple",
                "grey",
                "yellow",
                "gold",
                "silver",
                "blue",
                "darkblue",
                "lightred",
                "orange",
                "darkred"
            };
        return colors[(int)color];
    }
    public void AddMenuOption(string id, string title, Action<CCSPlayerController, IDynamicMenuOption> onExecute, MenuColors? color = null, bool disabled = false, string viewFlags = "*")
    {
        if (Options.Any(x => x.Id == id))
        {
            AdminUtils.LogDebug($"Option \"{id}\" already exists.");
        }
        var option = new DynamicMenuOption(id, title, onExecute, color, disabled, viewFlags);
        Options.Add(option);
    }
    
    [Obsolete("AddMenuOption(string display, Action<CCSPlayerController, ChatMenuOption> onSelect, bool disabled) is deprecated, please use AddMenuOption(string id, string title, Action<CCSPlayerController, IDynamicMenuOption> onExecute, MenuColors? color, bool disabled, string viewFlags) instead.")]
    public ChatMenuOption AddMenuOption(string display, Action<CCSPlayerController, ChatMenuOption> onSelect, bool disabled = false)
    {
        var menuOption = new ChatMenuOption(display, disabled, onSelect);
        menuOption.Disabled = disabled;
        
        AddMenuOption(display, display, (controller, option) =>
        {
            onSelect(controller, menuOption);
        });
        
        return menuOption;
    }
    
    public void Open(CCSPlayerController player)
    {
        Open(player, true);
    }
    
    public void OpenToAll()
    {
        foreach (var player in PlayersUtils.GetOnlinePlayers())
        {
            Open(player, true);
        }
    }
}

public class DynamicMenuOption : IDynamicMenuOption
{
    public string Id {get; set;}
    public string Title { get; set; } = "Option";
    public MenuColors Color { get; set; }
    public Action<CCSPlayerController, IDynamicMenuOption> OnExecute {get; set;}
    public bool Disabled { get; set; }
    public string ViewFlags { get; set; } = "*";

    public DynamicMenuOption(string id, string title, Action<CCSPlayerController, IDynamicMenuOption> onExecute, MenuColors? color = null, bool disabled = false, string viewFlags = "*")
    {
        Id = id;
        Title = title;
        Color = color ?? MenuColors.Default;
        OnExecute = onExecute;
        Disabled = disabled;
        ViewFlags = viewFlags;

        AdminUtils.LogDebug($@"
            Option created:
            Id: {id}
            Title: {title}
            Color: {color}
            ViewFlags: {viewFlags}
            Disabled: {disabled}
        ");
    }
}

