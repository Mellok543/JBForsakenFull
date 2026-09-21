using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using JBF.Api;

namespace JBF.SpecialDays.FloorIsLava;

public sealed class JBFFloorIsLava : BasePlugin
{
    private const string AdminPermission = "@jbf/admin";
    private const float DefaultRadius = 220.0f;

    private LavaPointStore? _store;

    public override string ModuleName => "JBF Special Day: Floor Is Lava";
    public override string ModuleVersion => "0.1.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        _store = new LavaPointStore(ModuleDirectory);
    }

    [ConsoleCommand("css_lavapoint", "Manage Floor Is Lava safe-zone points")]
    [RequiresPermissions(AdminPermission)]
    public void OnLavaPoint(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !player.IsValid || player.IsBot)
        {
            command.ReplyToCommand(
                JailbreakChat.Format("Эта команда используется только игроком на карте."));
            return;
        }

        if (_store is null)
        {
            command.ReplyToCommand(
                JailbreakChat.Format("Хранилище точек недоступно."));
            return;
        }

        var action = command.ArgCount >= 2
            ? command.GetArg(1).Trim().ToLowerInvariant()
            : "add";

        var mapName = Server.MapName;
        var config = _store.Load(mapName);

        switch (action)
        {
            case "add":
                AddPoint(player, command, config);
                break;

            case "undo":
                UndoPoint(command, config);
                break;

            case "list":
                ListPoints(command, config);
                break;

            case "clear":
                ClearPoints(command, config);
                break;

            default:
                command.ReplyToCommand(
                    JailbreakChat.Format(
                        "Использование: !lavapoint [add [радиус] | undo | list | clear]"));
                break;
        }
    }

    private void AddPoint(
        CCSPlayerController player,
        CommandInfo command,
        LavaMapConfig config)
    {
        var pawn = player.PlayerPawn.Value;
        var origin = pawn?.AbsOrigin;

        if (pawn is null || !pawn.IsValid || origin is null)
        {
            command.ReplyToCommand(
                JailbreakChat.Format("Не удалось получить вашу позицию."));
            return;
        }

        var radius = DefaultRadius;

        if (command.ArgCount >= 3)
        {
            if (!float.TryParse(
                    command.GetArg(2),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out radius) ||
                radius < 50.0f ||
                radius > 1000.0f)
            {
                command.ReplyToCommand(
                    JailbreakChat.Format(
                        "Радиус должен быть числом от 50 до 1000. Например: !lavapoint add 220"));
                return;
            }
        }

        var nextId = config.Points.Count == 0
            ? 1
            : config.Points.Max(x => x.Id) + 1;

        var point = new LavaPoint
        {
            Id = nextId,
            X = origin.X,
            Y = origin.Y,
            Z = origin.Z,
            Radius = radius
        };

        config.Points.Add(point);
        _store!.Save(config);

        command.ReplyToCommand(
            JailbreakChat.Format(
                $"Точка #{point.Id} сохранена для {config.Map}. " +
                $"X={point.X:F1} Y={point.Y:F1} Z={point.Z:F1} R={point.Radius:F0}. " +
                $"Всего точек: {config.Points.Count}."));
    }

    private void UndoPoint(CommandInfo command, LavaMapConfig config)
    {
        if (config.Points.Count == 0)
        {
            command.ReplyToCommand(JailbreakChat.Format("На этой карте точек ещё нет."));
            return;
        }

        var point = config.Points[^1];
        config.Points.RemoveAt(config.Points.Count - 1);
        _store!.Save(config);

        command.ReplyToCommand(
            JailbreakChat.Format(
                $"Последняя точка #{point.Id} удалена. Осталось: {config.Points.Count}."));
    }

    private static void ListPoints(CommandInfo command, LavaMapConfig config)
    {
        if (config.Points.Count == 0)
        {
            command.ReplyToCommand(
                JailbreakChat.Format($"Для карты {config.Map} точек пока нет."));
            return;
        }

        command.ReplyToCommand(
            JailbreakChat.Format(
                $"Карта {config.Map}: сохранено {config.Points.Count} безопасных точек."));

        foreach (var point in config.Points)
        {
            command.ReplyToCommand(
                JailbreakChat.Format(
                    $"#{point.Id}: X={point.X:F1} Y={point.Y:F1} Z={point.Z:F1} R={point.Radius:F0}"));
        }
    }

    private void ClearPoints(CommandInfo command, LavaMapConfig config)
    {
        var count = config.Points.Count;
        config.Points.Clear();
        _store!.Save(config);

        command.ReplyToCommand(
            JailbreakChat.Format(
                $"Для карты {config.Map} удалено точек: {count}."));
    }
}
