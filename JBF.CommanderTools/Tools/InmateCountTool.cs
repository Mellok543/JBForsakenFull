using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using JBF.CommanderTools.Extensions;

namespace JBF.CommanderTools.Tools;

internal sealed class InmateCountTool : ICommanderTool
{
    private const float FarDistance = 500.0f;

    public string Id => "inmate-count";
    public string Text => "Посчитать заключённых";
    public int Order => 100;

    public void Execute(CCSPlayerController commander)
    {
        var commanderPosition = commander.PlayerPawn.Value?.AbsOrigin;
        if (commanderPosition is null)
        {
            return;
        }

        var farInmates = Utilities.GetPlayers()
            .Where(player => player.IsUsable() && player.PawnIsAlive && player.Team == CsTeam.Terrorist)
            .Where(player => player.PlayerPawn.Value?.AbsOrigin is { } position &&
                             Distance(commanderPosition, position) > FarDistance)
            .Select(player => player.PlayerName)
            .ToArray();

        commander.PrintToChat(JailbreakChat.Format(farInmates.Length == 0
            ? "Все заключённые рядом с командиром."
            : $"Заключённые далеко: {string.Join(", ", farInmates)}"));
    }

    private static float Distance(Vector first, Vector second)
    {
        var x = second.X - first.X;
        var y = second.Y - first.Y;
        var z = second.Z - first.Z;
        return MathF.Sqrt(x * x + y * y + z * z);
    }
}
