using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using JBF.Api;

namespace JBF.CommanderTools.Tools;

internal sealed class DoorsTool : ICommanderTool
{
    private static readonly string[] DoorEntityNames =
    [
        "func_door",
        "func_movelinear",
        "func_door_rotating",
        "prop_door_rotating"
    ];

    public string Id => "doors";
    public string Text => "Открыть/закрыть двери";
    public int Order => 10;

    public void Execute(CCSPlayerController commander)
    {
        var menuApi = MenuCapability.Api.Get();
        if (menuApi is null)
        {
            commander.PrintToChat(JailbreakChat.Format("Menu API недоступно."));
            return;
        }

        JailbreakMenuOption[] options =
        [
            new("Открыть все", player => Apply(player, "Open", true)),
            new("Закрыть все", player => Apply(player, "Close", false))
        ];

        menuApi.Open(commander, "Управление дверями", options);
    }

    private static void Apply(CCSPlayerController commander, string input, bool breakBreakables)
    {
        ApplyToAllDoors(input, breakBreakables);

        commander.PrintToChat(JailbreakChat.Format(input == "Open" ? "Двери открыты." : "Двери закрыты."));
        CommanderMenuCapability.Api.Get()?.Open(commander);
    }

    internal static int OpenJailDoors()
    {
        var opened = 0;

        foreach (var entityName in DoorEntityNames)
        {
            foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(entityName))
            {
                if (!entity.IsValid || !IsJailDoor(entity))
                    continue;

                entity.AcceptInput("Open");
                opened++;
            }
        }

        return opened;
    }

    private static void ApplyToAllDoors(string input, bool breakBreakables)
    {
        foreach (var entityName in DoorEntityNames)
        {
            foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(entityName))
            {
                if (entity.IsValid)
                    entity.AcceptInput(input);
            }
        }

        if (!breakBreakables)
            return;

        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("func_breakable"))
        {
            if (entity.IsValid)
                entity.AcceptInput("Break");
        }
    }

    private static bool IsJailDoor(CBaseEntity entity)
    {
        // Targetname is map-dependent. Reflection keeps this compatible across
        // CounterStrikeSharp schema changes while supporting common JB names
        // such as cell_door, cells, jail_door and prison_door.
        var identity = entity.GetType().GetProperty("Entity")?.GetValue(entity);
        var name = identity?.GetType().GetProperty("Name")?.GetValue(identity)?.ToString();

        if (string.IsNullOrWhiteSpace(name))
            return false;

        return name.Contains("cell", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("jail", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("prison", StringComparison.OrdinalIgnoreCase);
    }
}
