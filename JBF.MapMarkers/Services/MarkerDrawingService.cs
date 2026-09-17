using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.MapMarkers.Models;

namespace JBF.MapMarkers.Services;

internal sealed class MarkerDrawingService
{
    private readonly List<TemporaryEntity> _entities = [];
    private readonly List<CEntityInstance> _redCircleEntities = [];

    public void DrawLine(
        Vector start,
        Vector end,
        Color color,
        float durationSeconds = 20.0f,
        ICollection<CEntityInstance>? group = null)
    {
        var beam = Utilities.CreateEntityByName<CBeam>("beam");
        if (beam is null)
        {
            return;
        }

        beam.Width = 1.75f;
        beam.Render = color;
        beam.DispatchSpawn();
        beam.Teleport(start, new QAngle(), new Vector());
        beam.EndPos.X = end.X;
        beam.EndPos.Y = end.Y;
        beam.EndPos.Z = end.Z;
        Utilities.SetStateChanged(beam, "CBeam", "m_vecEndPos");
        Track(beam, durationSeconds);
        group?.Add(beam);
    }

    public void DrawRedCircle(Vector center)
    {
        const int pointCount = 32;
        const int radius = 70;
        const float durationSeconds = 18.0f;
        ClearRedCircle();

        Vector? first = null;
        Vector? previous = null;

        for (var index = 0; index < pointCount; index++)
        {
            var angle = (float)index / pointCount * 2 * MathF.PI;
            var point = new Vector(
                center.X + radius * MathF.Cos(angle),
                center.Y + radius * MathF.Sin(angle),
                center.Z);

            first ??= point;
            if (previous is not null)
            {
                DrawLine(previous, point, Color.Red, durationSeconds, _redCircleEntities);
            }

            previous = point;
        }

        if (previous is not null && first is not null)
        {
            DrawLine(previous, first, Color.Red, durationSeconds, _redCircleEntities);
        }
    }

    public void CleanupExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var temporaryEntity in _entities.Where(entity => entity.ExpiresAt <= now).ToArray())
        {
            Remove(temporaryEntity);
        }
    }

    public void Clear()
    {
        foreach (var temporaryEntity in _entities.ToArray())
        {
            Remove(temporaryEntity);
        }

        _redCircleEntities.Clear();
    }

    private void Track(CEntityInstance entity, float durationSeconds)
    {
        _entities.Add(new TemporaryEntity(entity, DateTime.UtcNow.AddSeconds(durationSeconds)));
    }

    private void Remove(TemporaryEntity temporaryEntity)
    {
        if (temporaryEntity.Entity.IsValid)
        {
            temporaryEntity.Entity.Remove();
        }

        _entities.Remove(temporaryEntity);
        _redCircleEntities.Remove(temporaryEntity.Entity);
    }

    private void ClearRedCircle()
    {
        foreach (var entity in _redCircleEntities.ToArray())
        {
            var temporaryEntity = _entities.FirstOrDefault(item => ReferenceEquals(item.Entity, entity));
            if (temporaryEntity is not null)
            {
                Remove(temporaryEntity);
            }
            else if (entity.IsValid)
            {
                entity.Remove();
            }
        }

        _redCircleEntities.Clear();
    }
}
