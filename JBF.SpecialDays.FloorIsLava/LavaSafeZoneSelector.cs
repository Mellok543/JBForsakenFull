namespace JBF.SpecialDays.FloorIsLava;

internal static class LavaSafeZoneSelector
{
    public static int GetZoneCount(int alivePrisoners, int availablePoints)
    {
        if (availablePoints <= 0)
            return 0;

        var requested = alivePrisoners switch
        {
            <= 4 => 10,
            <= 8 => 12,
            <= 12 => 14,
            <= 16 => 16,
            <= 20 => 18,
            _ => 20
        };

        return Math.Min(requested, availablePoints);
    }

    public static IReadOnlyList<LavaPoint> SelectRandom(
        IReadOnlyList<LavaPoint> points,
        int alivePrisoners)
    {
        var count = GetZoneCount(alivePrisoners, points.Count);
        if (count <= 0)
            return [];

        // Fisher-Yates shuffle so the same point cannot be selected twice
        // in one lava cycle.
        var pool = points.ToArray();

        for (var i = pool.Length - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        return pool.Take(count).ToArray();
    }
}
