using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using JBF.CommanderTools.Extensions;

namespace JBF.CommanderTools.Tools;

internal sealed class InmateCountTool : ICommanderTool
{
    private const float CircleRadius = 500.0f;
    private const float CircleLifetimeSeconds = 5.0f;
    private const int CircleSegments = 28;
    private const float BeamWidth = 3.0f;

    private readonly BasePlugin _plugin;
    private readonly List<CBeam> _beams = [];

    public InmateCountTool(BasePlugin plugin)
    {
        _plugin = plugin;
    }

    public string Id => "inmate-count";
    public string Text => "Посчитать заключённых";
    public int Order => 100;

    public void Execute(CCSPlayerController commander)
    {
        var commanderPosition = commander.PlayerPawn.Value?.AbsOrigin;
        if (commanderPosition is null)
            return;

        ClearCircle();
        DrawCircle(commanderPosition);

        commander.PrintToChat(JailbreakChat.Format(
            $"Круг подсчёта активен {CircleLifetimeSeconds:0} сек. Заключённые должны находиться внутри круга."));

        _plugin.AddTimer(
            CircleLifetimeSeconds,
            () => FinishCount(commander),
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void FinishCount(CCSPlayerController commander)
    {
        ClearCircle();

        if (!commander.IsUsable() || !commander.PawnIsAlive)
            return;

        var center = commander.PlayerPawn.Value?.AbsOrigin;
        if (center is null)
            return;

        var aliveInmates = Utilities.GetPlayers()
            .Where(player =>
                player.IsUsable() &&
                player.PawnIsAlive &&
                player.Team == CsTeam.Terrorist)
            .ToArray();

        var inside = new List<CCSPlayerController>();
        var outside = new List<CCSPlayerController>();

        foreach (var inmate in aliveInmates)
        {
            var position = inmate.PlayerPawn.Value?.AbsOrigin;
            if (position is not null && Distance2D(center, position) <= CircleRadius)
                inside.Add(inmate);
            else
                outside.Add(inmate);
        }

        Server.PrintToChatAll(JailbreakChat.Format(
            $"В круге КМД: {inside.Count}/{aliveInmates.Length} заключённых."));

        if (outside.Count == 0)
        {
            Server.PrintToChatAll(JailbreakChat.Format("Все живые заключённые находятся внутри круга."));
            return;
        }

        Server.PrintToChatAll(JailbreakChat.Format(
            $"Вне круга: {string.Join(", ", outside.Select(player => player.PlayerName))}."));
    }

    private void DrawCircle(Vector center)
    {
        var points = new Vector[CircleSegments];

        for (var i = 0; i < CircleSegments; i++)
        {
            var angle = (float)i / CircleSegments * MathF.PI * 2.0f;
            points[i] = new Vector(
                center.X + CircleRadius * MathF.Cos(angle),
                center.Y + CircleRadius * MathF.Sin(angle),
                center.Z + 5.0f);
        }

        for (var i = 0; i < CircleSegments; i++)
            DrawBeam(points[i], points[(i + 1) % CircleSegments]);
    }

    private void DrawBeam(Vector start, Vector end)
    {
        var beam = Utilities.CreateEntityByName<CBeam>("beam");
        if (beam is null || !beam.IsValid)
            return;

        beam.Width = BeamWidth;
        beam.Render = Color.LimeGreen;
        beam.DispatchSpawn();
        beam.Teleport(start, new QAngle(), new Vector());

        beam.EndPos.X = end.X;
        beam.EndPos.Y = end.Y;
        beam.EndPos.Z = end.Z;
        Utilities.SetStateChanged(beam, "CBeam", "m_vecEndPos");

        _beams.Add(beam);
    }

    private void ClearCircle()
    {
        foreach (var beam in _beams.ToArray())
        {
            try
            {
                if (beam.IsValid)
                    beam.Remove();
            }
            catch
            {
            }
        }

        _beams.Clear();
    }

    private static float Distance2D(Vector first, Vector second)
    {
        var x = second.X - first.X;
        var y = second.Y - first.Y;
        return MathF.Sqrt(x * x + y * y);
    }
}
