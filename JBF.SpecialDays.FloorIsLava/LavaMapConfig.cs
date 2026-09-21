namespace JBF.SpecialDays.FloorIsLava;

internal sealed class LavaMapConfig
{
    public string Map { get; set; } = string.Empty;
    public List<LavaPoint> Points { get; set; } = [];
}

internal sealed class LavaPoint
{
    public int Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Radius { get; set; } = 220.0f;
}
