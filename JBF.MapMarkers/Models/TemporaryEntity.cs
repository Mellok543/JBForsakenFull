using CounterStrikeSharp.API.Core;

namespace JBF.MapMarkers.Models;

internal sealed record TemporaryEntity(CEntityInstance Entity, DateTime ExpiresAt);
