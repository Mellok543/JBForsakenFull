using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using JBF.MapMarkers.Models;

namespace JBF.MapMarkers.Services;

internal sealed class MapMarkerService
{
    private const float DrawDistanceStep = 14.0f;
    private const int MaxSegmentsPerSample = 6;
    private const float SurfaceTraceDistance = 16384.0f;
    private static readonly TimeSpan DrawInterval = TimeSpan.FromMilliseconds(75);
    private static readonly TraceOptions SurfaceTraceOptions = new()
    {
        InteractsWith = Masks.SolidBrushOnly,
        InteractsExclude = Contents.Pickup
    };

    private readonly Dictionary<int, FreeDrawState> _drawStates = [];
    private readonly MarkerDrawingService _drawingService = new();

    public HookResult HandlePlayerPing(EventPlayerPing @event)
    {
        var player = @event.Userid;
        if (player is null || !player.IsValid || WardenCapability.Api.Get()?.IsWarden(player) != true)
            return HookResult.Continue;

        var position = new Vector(@event.X, @event.Y, @event.Z + 1.0f);
        _drawingService.DrawRedCircle(position);
        Server.PrintToChatAll(JailbreakChat.Format("Командир установил красную метку."));
        return HookResult.Handled;
    }

    public void Tick()
    {
        _drawingService.CleanupExpired();

        var warden = WardenCapability.Api.Get()?.Warden;
        if (warden is null || !warden.IsValid || !warden.PawnIsAlive)
            return;

        var state = GetOrCreateState(warden.Slot);
        if (!warden.Buttons.HasFlag(PlayerButtons.Use))
        {
            state.LastPosition = null;
            return;
        }

        var now = DateTime.UtcNow;
        if (now - state.LastDrawTime < DrawInterval)
            return;

        var position = TraceSurface(warden);
        if (position is null)
        {
            state.LastPosition = null;
            return;
        }

        state.LastDrawTime = now;
        DrawSmoothLine(state, position);
    }

    public void Reset()
    {
        _drawStates.Clear();
        _drawingService.Clear();
    }

    private FreeDrawState GetOrCreateState(int playerSlot)
    {
        if (_drawStates.TryGetValue(playerSlot, out var state))
            return state;

        state = new FreeDrawState();
        _drawStates[playerSlot] = state;
        return state;
    }

    private void DrawSmoothLine(FreeDrawState state, Vector position)
    {
        if (state.LastPosition is null)
        {
            state.LastPosition = position;
            return;
        }

        var distance = Distance(state.LastPosition, position);
        if (distance < 5.0f)
            return;

        var segmentCount = Math.Clamp((int)Math.Ceiling(distance / DrawDistanceStep), 1, MaxSegmentsPerSample);
        var previous = state.LastPosition;

        for (var index = 1; index <= segmentCount; index++)
        {
            var amount = (float)index / segmentCount;
            var next = Lerp(state.LastPosition, position, amount);
            _drawingService.DrawLine(previous, next, Color.Red, 12.0f);
            previous = next;
        }

        state.LastPosition = position;
    }

    private static Vector? TraceSurface(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        var origin = pawn?.AbsOrigin;
        var angles = pawn?.EyeAngles ?? pawn?.AbsRotation;
        if (pawn is null || origin is null || angles is null)
            return null;

        var eyePosition = new Vector(origin.X, origin.Y, origin.Z + 56.0f);
        var pitch = angles.X * MathF.PI / 180.0f;
        var yaw = angles.Y * MathF.PI / 180.0f;
        var endPosition = new Vector(
            eyePosition.X + MathF.Cos(pitch) * MathF.Cos(yaw) * SurfaceTraceDistance,
            eyePosition.Y + MathF.Cos(pitch) * MathF.Sin(yaw) * SurfaceTraceDistance,
            eyePosition.Z - MathF.Sin(pitch) * SurfaceTraceDistance);

        var trace = Trace.TraceEndShape(eyePosition, endPosition, pawn, SurfaceTraceOptions);
        if (!trace.DidHit())
            return null;

        var hitPoint = trace.HitPoint;
        var normal = trace.Normal;
        return new Vector(hitPoint.X + normal.X, hitPoint.Y + normal.Y, hitPoint.Z + normal.Z);
    }

    private static Vector Lerp(Vector start, Vector end, float amount) => new(
        start.X + (end.X - start.X) * amount,
        start.Y + (end.Y - start.Y) * amount,
        start.Z + (end.Z - start.Z) * amount);

    private static float Distance(Vector start, Vector end)
    {
        var x = end.X - start.X;
        var y = end.Y - start.Y;
        var z = end.Z - start.Z;
        return MathF.Sqrt(x * x + y * y + z * z);
    }
}
