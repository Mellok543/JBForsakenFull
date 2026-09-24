using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using JBF.CommanderTools.Extensions;
using JBF.CommanderTools.Services;

namespace JBF.CommanderTools.Tools;

internal sealed class RespawnTool : ICommanderTool
{
    private const float TraceDistance = 16384.0f;
    private const float SpawnOffset = 12.0f;
    private static readonly TraceOptions TraceOptions = new()
    {
        InteractsWith = Masks.SolidBrushOnly,
        InteractsExclude = Contents.Pickup
    };

    private readonly PlayerSelectionService _playerSelection;

    public RespawnTool(PlayerSelectionService playerSelection)
    {
        _playerSelection = playerSelection;
    }

    public string Id => "respawn";
    public string Text => "Возродить игрока";
    public int Order => 70;

    public void Execute(CCSPlayerController commander)
    {
        _playerSelection.Open(
            commander,
            "Кого возродить?",
            Utilities.GetPlayers().Where(player => player.IsValid && !player.PawnIsAlive),
            player =>
            {
                var target = TraceAimPoint(commander);
                if (target is null)
                {
                    commander.PrintToChat(JailbreakChat.Format(
                        "Не удалось определить точку под прицелом. Наведись на поверхность и попробуй ещё раз."));
                    return;
                }

                player.Respawn();

                Server.NextFrame(() =>
                {
                    if (!player.IsUsable() || !player.PawnIsAlive)
                        return;

                    var pawn = player.PlayerPawn.Value!;
                    pawn.Teleport(
                        new Vector(target.X, target.Y, target.Z + SpawnOffset),
                        pawn.AbsRotation ?? new QAngle(),
                        new Vector());

                    commander.PrintToChat(JailbreakChat.Format(
                        $"{player.PlayerName} возрождён в точке прицела."));
                });
            });
    }

    private static Vector? TraceAimPoint(CCSPlayerController commander)
    {
        if (!commander.IsUsable() || !commander.PawnIsAlive)
            return null;

        var pawn = commander.PlayerPawn.Value;
        var origin = pawn?.AbsOrigin;
        var angles = pawn?.EyeAngles ?? pawn?.AbsRotation;
        if (pawn is null || origin is null || angles is null)
            return null;

        var eyePosition = new Vector(origin.X, origin.Y, origin.Z + 56.0f);
        var pitch = angles.X * MathF.PI / 180.0f;
        var yaw = angles.Y * MathF.PI / 180.0f;

        var endPosition = new Vector(
            eyePosition.X + MathF.Cos(pitch) * MathF.Cos(yaw) * TraceDistance,
            eyePosition.Y + MathF.Cos(pitch) * MathF.Sin(yaw) * TraceDistance,
            eyePosition.Z - MathF.Sin(pitch) * TraceDistance);

        var trace = Trace.TraceEndShape(
            eyePosition,
            endPosition,
            pawn,
            TraceOptions);

        if (!trace.DidHit())
            return null;

        var hit = trace.HitPoint;
        var normal = trace.Normal;

        return new Vector(
            hit.X + normal.X * SpawnOffset,
            hit.Y + normal.Y * SpawnOffset,
            hit.Z + normal.Z * SpawnOffset);
    }
}
