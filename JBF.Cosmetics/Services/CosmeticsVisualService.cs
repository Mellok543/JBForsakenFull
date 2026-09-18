using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using JBF.Api;
using JBF.Cosmetics.Models;

namespace JBF.Cosmetics.Services;

internal sealed class CosmeticsVisualService
{
    private readonly Action<string>? _log;
    private readonly Dictionary<int, Dictionary<CosmeticCategory, VisualEntry>> _entities = [];

    public CosmeticsVisualService(Action<string>? log = null) => _log = log;

    public void Apply(CCSPlayerController player, CosmeticDefinition item)
    {
        if (!CanRender(player) || string.IsNullOrWhiteSpace(item.AssetPath)) return;
        if (item.Category is not (CosmeticCategory.Head or CosmeticCategory.Back)) return;

        Remove(player.Slot, item.Category);

        var pawn = player.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid) return;

        try
        {
            var prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (prop is null || !prop.IsValid)
            {
                _log?.Invoke($"Cosmetics: failed to create prop_dynamic for {item.Id}.");
                return;
            }

            prop.SetModel(item.AssetPath);
            prop.DispatchSpawn();

            var entry = new VisualEntry(prop, item);
            ApplyScale(entry);
            UpdateEntry(player, entry);

            if (!_entities.TryGetValue(player.Slot, out var map))
                _entities[player.Slot] = map = [];
            map[item.Category] = entry;

            _log?.Invoke($"Cosmetics: attached {item.Id} to {player.PlayerName} using world-follow offset.");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Cosmetics: failed to attach {item.Id} to {player.PlayerName}: {ex.Message}");
            Remove(player.Slot, item.Category);
        }
    }

    public void Update()
    {
        if (_entities.Count == 0) return;

        var players = Utilities.GetPlayers()
            .Where(CanRender)
            .ToDictionary(player => player.Slot);

        foreach (var (slot, map) in _entities.ToArray())
        {
            if (!players.TryGetValue(slot, out var player))
            {
                RemoveAll(slot);
                continue;
            }

            foreach (var entry in map.Values.ToArray())
            {
                if (entry.Entity is not { IsValid: true }) continue;
                UpdateEntry(player, entry);
            }
        }
    }

    public void Remove(CCSPlayerController player, CosmeticCategory category) => Remove(player.Slot, category);

    public void RemoveAll(CCSPlayerController player) => RemoveAll(player.Slot);

    public void RemoveAll(int slot)
    {
        if (!_entities.TryGetValue(slot, out var map)) return;
        foreach (var entry in map.Values.ToArray())
            SafeRemove(entry.Entity);
        _entities.Remove(slot);
    }

    public void Reset()
    {
        foreach (var slot in _entities.Keys.ToArray())
            RemoveAll(slot);
        _entities.Clear();
    }

    private void Remove(int slot, CosmeticCategory category)
    {
        if (!_entities.TryGetValue(slot, out var map)) return;
        if (map.Remove(category, out var entry))
            SafeRemove(entry.Entity);
        if (map.Count == 0)
            _entities.Remove(slot);
    }

    private static void UpdateEntry(CCSPlayerController player, VisualEntry entry)
    {
        var pawn = player.PlayerPawn.Value;
        var origin = pawn?.AbsOrigin;
        var angles = pawn?.AbsRotation;
        if (pawn is null || origin is null || angles is null || !entry.Entity.IsValid) return;

        var offset = Normalize(entry.Item.Offset);
        var rotation = Normalize(entry.Item.Rotation);

        // Rotate local XY offset by the player's yaw so forward/back/side offsets follow the model.
        var yawRad = angles.Y * MathF.PI / 180.0f;
        var cos = MathF.Cos(yawRad);
        var sin = MathF.Sin(yawRad);
        var worldX = offset[0] * cos - offset[1] * sin;
        var worldY = offset[0] * sin + offset[1] * cos;

        var position = new CounterStrikeSharp.API.Modules.Utils.Vector(
            origin.X + worldX,
            origin.Y + worldY,
            origin.Z + offset[2]);

        var finalAngles = new CounterStrikeSharp.API.Modules.Utils.QAngle(
            angles.X + rotation[0],
            angles.Y + rotation[1],
            angles.Z + rotation[2]);

        entry.Entity.Teleport(position, finalAngles, null);
    }

    private static void ApplyScale(VisualEntry entry)
    {
        var node = entry.Entity.CBodyComponent?.SceneNode;
        if (node is null) return;
        node.Scale = Math.Clamp(entry.Item.Scale, 0.01f, 10.0f);
        Utilities.SetStateChanged(entry.Entity, "CBaseEntity", "m_CBodyComponent");
    }

    private static float[] Normalize(float[]? values)
        => values is { Length: >= 3 } ? values : [0.0f, 0.0f, 0.0f];

    private static void SafeRemove(CDynamicProp? entity)
    {
        try
        {
            if (entity is { IsValid: true })
                entity.Remove();
        }
        catch
        {
            // Entity may already have been destroyed by map cleanup.
        }
    }

    private static bool CanRender(CCSPlayerController? player)
        => player is { IsValid: true, IsBot: false, PawnIsAlive: true }
           && player.PlayerPawn.Value is { IsValid: true };

    private sealed record VisualEntry(CDynamicProp Entity, CosmeticDefinition Item);
}
