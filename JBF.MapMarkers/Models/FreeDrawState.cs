using CounterStrikeSharp.API.Modules.Utils;

namespace JBF.MapMarkers.Models;

internal sealed class FreeDrawState
{
    public Vector? LastPosition { get; set; }

    public DateTime LastDrawTime { get; set; }
}
