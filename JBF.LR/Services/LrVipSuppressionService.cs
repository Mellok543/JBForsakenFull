using System.Collections;
using System.Reflection;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;

namespace JBF.LR.Services;

internal sealed class LrVipSuppressionService
{
    private sealed record SavedFeature(string Name, object State);

    private readonly Dictionary<int, List<SavedFeature>> _savedStates = [];
    private object? _api;
    private Type? _apiType;
    private Type? _featureStateType;
    private MethodInfo? _getAllRegisteredFeatures;
    private MethodInfo? _isClientVip;
    private MethodInfo? _playerHasFeature;
    private MethodInfo? _getPlayerFeatureState;
    private MethodInfo? _setPlayerFeatureState;
    private object? _disabledState;

    public void Suppress(CCSPlayerController player)
    {
        if (!player.IsValid || _savedStates.ContainsKey(player.Slot)) return;
        if (!EnsureApi()) return;

        try
        {
            if (_isClientVip?.Invoke(_api, [player]) is not true) return;
            if (_getAllRegisteredFeatures?.Invoke(_api, null) is not IEnumerable features) return;

            var saved = new List<SavedFeature>();
            foreach (var entry in features)
            {
                var feature = ReadFeatureName(entry);
                if (string.IsNullOrWhiteSpace(feature)) continue;
                if (_playerHasFeature?.Invoke(_api, [player, feature]) is not true) continue;

                var state = _getPlayerFeatureState?.Invoke(_api, [player, feature]);
                if (state is null) continue;

                // Only change enabled features. Disabled/NoAccess remain untouched.
                if (!string.Equals(state.ToString(), "Enabled", StringComparison.OrdinalIgnoreCase)) continue;

                saved.Add(new SavedFeature(feature, state));
                _setPlayerFeatureState?.Invoke(_api, [player, feature, _disabledState]);
            }

            _savedStates[player.Slot] = saved;
        }
        catch
        {
            // VIP Core is optional. LR must continue even if its API changes.
            _savedStates.Remove(player.Slot);
        }
    }

    public void Restore(CCSPlayerController? player)
    {
        if (player is null || !_savedStates.Remove(player.Slot, out var saved)) return;
        if (!EnsureApi()) return;

        foreach (var feature in saved)
        {
            try
            {
                _setPlayerFeatureState?.Invoke(_api, [player, feature.Name, feature.State]);
            }
            catch
            {
                // Restore remaining features even if one module disappeared during LR.
            }
        }
    }

    public void Forget(int slot) => _savedStates.Remove(slot);

    public void RestoreAll(IEnumerable<CCSPlayerController> players)
    {
        foreach (var player in players.ToArray()) Restore(player);
        _savedStates.Clear();
    }

    private bool EnsureApi()
    {
        if (_api is not null) return true;

        try
        {
            _apiType = Type.GetType("VipCoreApi.IVipCoreApi, VipCoreApi", throwOnError: false);
            if (_apiType is null) return false;

            var capabilityType = typeof(PluginCapability<>).MakeGenericType(_apiType);
            var capability = Activator.CreateInstance(capabilityType, "vipcore:core");
            if (capability is null) return false;

            _api = capabilityType.GetMethod("Get", Type.EmptyTypes)?.Invoke(capability, null);
            if (_api is null) return false;

            _featureStateType = _apiType.GetNestedType("FeatureState");
            if (_featureStateType is null) return false;

            _disabledState = Enum.Parse(_featureStateType, "Disabled");
            _getAllRegisteredFeatures = _apiType.GetMethod("GetAllRegisteredFeatures");
            _isClientVip = _apiType.GetMethod("IsClientVip");
            _playerHasFeature = _apiType.GetMethod("PlayerHasFeature");
            _getPlayerFeatureState = _apiType.GetMethod("GetPlayerFeatureState");
            _setPlayerFeatureState = _apiType.GetMethod("SetPlayerFeatureState");

            return _getAllRegisteredFeatures is not null && _isClientVip is not null &&
                   _playerHasFeature is not null && _getPlayerFeatureState is not null &&
                   _setPlayerFeatureState is not null;
        }
        catch
        {
            _api = null;
            return false;
        }
    }

    private static string? ReadFeatureName(object? entry)
    {
        if (entry is null) return null;
        var type = entry.GetType();
        return type.GetField("Item1")?.GetValue(entry)?.ToString() ??
               type.GetProperty("Item1")?.GetValue(entry)?.ToString();
    }
}
