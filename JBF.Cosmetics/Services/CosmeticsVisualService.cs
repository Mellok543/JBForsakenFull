using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using JBF.Api;
using JBF.Cosmetics.Models;

namespace JBF.Cosmetics.Services;

internal sealed class CosmeticsVisualService
{
    private readonly Action<string>? _log;
    private readonly Dictionary<int, Dictionary<CosmeticCategory, CDynamicProp>> _entities = [];

    public CosmeticsVisualService(Action<string>? log = null) => _log = log;

    public void Apply(CCSPlayerController player, CosmeticDefinition item)
    {
        if (!CanRender(player) || string.IsNullOrWhiteSpace(item.AssetPath)) return;
        if (item.Category is not (CosmeticCategory.Head or CosmeticCategory.Back or CosmeticCategory.ShoulderPet)) return;

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

            // Position the prop in world space first, then parent it.
            // SetParent keeps the current transform as a local offset, so the cosmetic
            // follows the pawn without per-tick Teleport jitter.
            ApplyInitialTransform(prop, pawn, item);
            prop.AcceptInput("SetParent", pawn, pawn, "!activator");
            ApplyScale(prop, item);

            if (!_entities.TryGetValue(player.Slot, out var map))
                _entities[player.Slot] = map = [];
            map[item.Category] = prop;

            _log?.Invoke($"Cosmetics: attached {item.Id} to {player.PlayerName} with fixed parent offset.");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Cosmetics: failed to attach {item.Id} to {player.PlayerName}: {ex.Message}");
            Remove(player.Slot, item.Category);
        }
    }

    public void Remove(CCSPlayerController player, CosmeticCategory category) => Remove(player.Slot, category);

    public void RemoveAll(CCSPlayerController player) => RemoveAll(player.Slot);

    public void RemoveAll(int slot)
    {
        if (!_entities.TryGetValue(slot, out var map)) return;
        foreach (var entity in map.Values.ToArray())
            SafeRemove(entity);
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
        if (map.Remove(category, out var entity))
            SafeRemove(entity);
        if (map.Count == 0)
            _entities.Remove(slot);
    }

    private static void ApplyInitialTransform(CDynamicProp prop, CCSPlayerPawn pawn, CosmeticDefinition item)
    {
        var origin = pawn.AbsOrigin;
        var angles = pawn.AbsRotation;
        if (origin is null || angles is null) return;

        var offset = Normalize(item.Offset);
        var rotation = Normalize(item.Rotation);

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

        prop.Teleport(position, finalAngles, null);
    }

    private static void ApplyScale(CDynamicProp prop, CosmeticDefinition item)
    {
        var node = prop.CBodyComponent?.SceneNode;
        if (node is null) return;

        node.Scale = Math.Clamp(item.Scale, 0.01f, 10.0f);
        Utilities.SetStateChanged(prop, "CBaseEntity", "m_CBodyComponent");
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
}
