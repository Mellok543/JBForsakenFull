using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace JBF.SpecialDays.FloorIsLava;

internal sealed class LavaZoneRenderer
{
    private const int Segments = 10;
    private const float WallHeight = 140.0f;
    private const float BeamWidth = 5.0f;

    private readonly List<CBeam> _beams = [];

    public void Show(IEnumerable<LavaPoint> zones)
    {
        Clear();

        foreach (var zone in zones)
            DrawZone(zone);
    }

    public void Clear()
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

    private void DrawZone(LavaPoint zone)
    {
        var bottom = new Vector[Segments];
        var top = new Vector[Segments];

        for (var i = 0; i < Segments; i++)
        {
            var angle = (float)i / Segments * 2.0f * MathF.PI;
            var x = zone.X + zone.Radius * MathF.Cos(angle);
            var y = zone.Y + zone.Radius * MathF.Sin(angle);

            bottom[i] = new Vector(x, y, zone.Z + 4.0f);
            top[i] = new Vector(x, y, zone.Z + WallHeight);
        }

        for (var i = 0; i < Segments; i++)
        {
            var next = (i + 1) % Segments;

            DrawBeam(bottom[i], bottom[next]);
            DrawBeam(top[i], top[next]);
            DrawBeam(bottom[i], top[i]);
        }

        // Tall center beacon so the island can be found from farther away.
        DrawBeam(
            new Vector(zone.X, zone.Y, zone.Z + 4.0f),
            new Vector(zone.X, zone.Y, zone.Z + 260.0f),
            7.0f);
    }

    private void DrawBeam(Vector start, Vector end, float width = BeamWidth)
    {
        var beam = Utilities.CreateEntityByName<CBeam>("beam");
        if (beam is null || !beam.IsValid)
            return;

        beam.Width = width;
        beam.Render = Color.LimeGreen;
        beam.DispatchSpawn();
        beam.Teleport(start, new QAngle(), new Vector());
        beam.EndPos.X = end.X;
        beam.EndPos.Y = end.Y;
        beam.EndPos.Z = end.Z;
        Utilities.SetStateChanged(beam, "CBeam", "m_vecEndPos");

        _beams.Add(beam);
    }
}
