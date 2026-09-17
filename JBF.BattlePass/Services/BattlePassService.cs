using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using JBF.BattlePass.Models;

namespace JBF.BattlePass.Services;

internal sealed class BattlePassService : IBattlePassApi
{
    private const int RewardSlots = 8;
    private const int MissionSlots = 6;
    private const int InventorySlots = 8;

    private readonly BattlePassConfig _config;
    private readonly BattlePassStorage _storage;
    private readonly BattlePassRenderer _renderer;
    private readonly Action<string>? _log;
    private readonly Dictionary<ulong, BattlePassPlayerState> _states;
    private readonly Dictionary<int, string> _tabs = [];
    private readonly Dictionary<int, int> _pages = [];
    private readonly HashSet<int> _usedInventoryThisRound = [];
    private readonly Dictionary<int, DateTime> _playMinuteTicks = [];

    public string SeasonId => _config.SeasonId;
    public string SeasonName => _config.SeasonName;

    public BattlePassService(BattlePassConfig config, BattlePassRenderer renderer, Action<string>? log = null)
    {
        _config = config;
        _renderer = renderer;
        _log = log;
        _storage = new BattlePassStorage(config.Database, config.SeasonId);
        _states = _storage.LoadAll(log);
    }

    public void Open(CCSPlayerController player)
    {
        if (!IsUsable(player)) return;
        _tabs[player.Slot] = _tabs.GetValueOrDefault(player.Slot, "track");
        _pages[player.Slot] = Math.Max(0, _pages.GetValueOrDefault(player.Slot));
        Render(player);
        _renderer.Show(player);
    }

    public void Close(CCSPlayerController player) => _renderer.Hide(player);

    public void SetTab(CCSPlayerController player, string tab)
    {
        if (!IsUsable(player)) return;
        tab = tab.ToLowerInvariant();
        if (tab is not ("track" or "missions" or "inventory")) return;
        _tabs[player.Slot] = tab;
        _pages[player.Slot] = 0;
        Render(player);
    }

    public void ChangePage(CCSPlayerController player, int delta)
    {
        if (!IsUsable(player)) return;
        var page = Math.Max(0, _pages.GetValueOrDefault(player.Slot) + Math.Clamp(delta, -1, 1));
        _pages[player.Slot] = page;
        Render(player);
    }

    public void ClaimSlot(CCSPlayerController player, int slot)
    {
        if (!IsUsable(player) || slot < 0 || slot >= RewardSlots) return;
        var level = _pages.GetValueOrDefault(player.Slot) * RewardSlots + slot + 1;
        ClaimLevel(player, level);
    }

    public void UseInventorySlot(CCSPlayerController player, int slot)
    {
        if (!IsUsable(player) || slot < 0 || slot >= InventorySlots) return;
        var state = GetState(player);
        var items = state.Inventory.Where(x => x.Value > 0).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        var index = _pages.GetValueOrDefault(player.Slot) * InventorySlots + slot;
        if (index < 0 || index >= items.Length) return;
        UseInventoryItem(player, items[index].Key);
    }

    public void AddProgress(CCSPlayerController player, string objectiveId, int amount = 1)
    {
        if (!IsUsable(player) || amount <= 0 || string.IsNullOrWhiteSpace(objectiveId)) return;
        var state = GetState(player);
        var changed = false;

        foreach (var mission in _config.Missions.Where(m => m.ObjectiveId.Equals(objectiveId, StringComparison.OrdinalIgnoreCase)))
        {
            var period = PeriodKey(mission.Period);
            var key = $"{mission.Id}:{period}";
            if (state.CompletedMissionPeriods.Contains(key)) continue;

            var progress = Math.Min(mission.Target, state.MissionProgress.GetValueOrDefault(key) + amount);
            state.MissionProgress[key] = progress;
            changed = true;

            if (progress < mission.Target) continue;
            state.CompletedMissionPeriods.Add(key);
            state.Xp += mission.XpReward;
            UiCapability.Api.Get()?.Notify(player, $"Задание выполнено: {mission.Name} • +{mission.XpReward} XP", UiNotificationType.Success, 5.0f);
        }

        if (changed)
        {
            Save(state);
            if (_tabs.ContainsKey(player.Slot)) Render(player);
        }
    }

    public void AddXp(CCSPlayerController player, int amount, string reason = "")
    {
        if (!IsUsable(player) || amount <= 0) return;
        var state = GetState(player);
        state.Xp = Math.Max(0, state.Xp + amount);
        Save(state);
        UiCapability.Api.Get()?.Notify(player, $"+{amount} XP{(string.IsNullOrWhiteSpace(reason) ? string.Empty : $" • {reason}")}", UiNotificationType.Success, 3.0f);
        if (_tabs.ContainsKey(player.Slot)) Render(player);
    }

    public int GetXp(CCSPlayerController player) => IsUsable(player) ? GetState(player).Xp : 0;
    public int GetLevel(CCSPlayerController player) => IsUsable(player) ? LevelForXp(GetState(player).Xp) : 0;

    public void OnRoundStart()
    {
        _usedInventoryThisRound.Clear();
    }

    public void OnRoundEnd()
    {
        foreach (var player in Utilities.GetPlayers().Where(IsUsable)) AddProgress(player, "round_played", 1);
    }

    public void OnKill(CCSPlayerController? victim, CCSPlayerController? attacker)
    {
        if (!IsUsable(attacker) || victim is null || attacker.Slot == victim.Slot) return;
        AddProgress(attacker, "kill", 1);
    }

    public void OnLrEnded(LrMatchEndedEvent match)
    {
        if (IsUsable(match.Winner)) AddProgress(match.Winner!, "lr_win", 1);
    }

    public void TickPlayTime()
    {
        var now = DateTime.UtcNow;
        foreach (var player in Utilities.GetPlayers().Where(IsUsable))
        {
            if (!_playMinuteTicks.TryGetValue(player.Slot, out var next))
            {
                _playMinuteTicks[player.Slot] = now.AddMinutes(1);
                continue;
            }
            if (now < next) continue;
            _playMinuteTicks[player.Slot] = now.AddMinutes(1);
            AddProgress(player, "play_minute", 1);
        }
    }

    public void Disconnect(int slot)
    {
        _tabs.Remove(slot);
        _pages.Remove(slot);
        _usedInventoryThisRound.Remove(slot);
        _playMinuteTicks.Remove(slot);
        _renderer.Forget(slot);
    }

    public void Shutdown()
    {
        foreach (var state in _states.Values) Save(state);
        _tabs.Clear();
        _pages.Clear();
    }

    private void ClaimLevel(CCSPlayerController player, int level)
    {
        var state = GetState(player);
        if (level < 1 || level > _config.MaxLevel) return;
        if (LevelForXp(state.Xp) < level)
        {
            UiCapability.Api.Get()?.Notify(player, "Этот уровень ещё не открыт.", UiNotificationType.Warning, 3.0f);
            return;
        }
        if (state.ClaimedLevels.Contains(level))
        {
            UiCapability.Api.Get()?.Notify(player, "Награда уже получена.", UiNotificationType.Info, 3.0f);
            return;
        }

        var reward = _config.Rewards.FirstOrDefault(x => x.Level == level);
        if (reward is null) return;

        if (reward.Type == RewardType.Credits)
        {
            var shop = ShopCapability.Api.Get();
            if (shop is null || !shop.TryAddCredits(player, reward.Amount, out var balance))
            {
                UiCapability.Api.Get()?.Notify(player, "Не удалось начислить кредиты. Попробуйте позже.", UiNotificationType.Error, 4.0f);
                return;
            }
            UiCapability.Api.Get()?.Notify(player, $"Получено {reward.Amount} кредитов • Баланс {balance}", UiNotificationType.Success, 4.0f);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(reward.ItemId)) return;
            state.Inventory[reward.ItemId] = state.Inventory.GetValueOrDefault(reward.ItemId) + Math.Max(1, reward.Amount);
            UiCapability.Api.Get()?.Notify(player, $"Получено: {reward.Name}", UiNotificationType.Success, 4.0f);
        }

        state.ClaimedLevels.Add(level);
        Save(state);
        Render(player);
    }

    private void UseInventoryItem(CCSPlayerController player, string itemId)
    {
        var state = GetState(player);
        if (state.Inventory.GetValueOrDefault(itemId) <= 0) return;
        if (!player.PawnIsAlive || JailbreakCapability.Api.Get()?.IsRoundActive != true)
        {
            UiCapability.Api.Get()?.Notify(player, "Предмет можно использовать только живым во время раунда.", UiNotificationType.Warning, 4.0f);
            return;
        }
        if (LrCapability.Api.Get()?.IsActive == true || SpecialDaysCapability.Api.Get()?.IsActive == true)
        {
            UiCapability.Api.Get()?.Notify(player, "Инвентарь Battle Pass недоступен во время LR или игрового дня.", UiNotificationType.Warning, 4.0f);
            return;
        }
        if (_usedInventoryThisRound.Contains(player.Slot))
        {
            UiCapability.Api.Get()?.Notify(player, "Можно использовать только один предмет Battle Pass за раунд.", UiNotificationType.Warning, 4.0f);
            return;
        }

        try
        {
            player.GiveNamedItem(itemId);
        }
        catch
        {
            UiCapability.Api.Get()?.Notify(player, "Не удалось выдать предмет.", UiNotificationType.Error, 3.0f);
            return;
        }

        state.Inventory[itemId]--;
        if (state.Inventory[itemId] <= 0) state.Inventory.Remove(itemId);
        _usedInventoryThisRound.Add(player.Slot);
        Save(state);
        UiCapability.Api.Get()?.Notify(player, $"Использовано: {ItemDisplayName(itemId)}", UiNotificationType.Success, 3.0f);
        Render(player);
    }

    private void Render(CCSPlayerController player)
    {
        var state = GetState(player);
        var level = LevelForXp(state.Xp);
        var currentFloorXp = level >= _config.MaxLevel ? _config.MaxLevel * _config.XpPerLevel : level * _config.XpPerLevel;
        var nextXp = Math.Min(_config.MaxLevel * _config.XpPerLevel, currentFloorXp + _config.XpPerLevel);
        var progress = level >= _config.MaxLevel ? _config.XpPerLevel : Math.Max(0, state.Xp - currentFloorXp);

        _renderer.Text(player, "jbf_bp_season", _config.SeasonName);
        _renderer.Text(player, "jbf_bp_level", $"LVL {level}");
        _renderer.Text(player, "jbf_bp_xp", level >= _config.MaxLevel ? "MAX LEVEL" : $"{progress} / {_config.XpPerLevel} XP");
        _renderer.Text(player, "jbf_bp_totalxp", $"Сезонный XP: {state.Xp:N0}");
        _renderer.Text(player, "jbf_bp_progress", level >= _config.MaxLevel ? "100" : $"{Math.Clamp(progress * 100 / Math.Max(1, _config.XpPerLevel), 0, 100)}");

        var tab = _tabs.GetValueOrDefault(player.Slot, "track");
        _renderer.SetClass(player, "jbf_bp_track", "active", tab == "track");
        _renderer.SetClass(player, "jbf_bp_missions", "active", tab == "missions");
        _renderer.SetClass(player, "jbf_bp_inventory", "active", tab == "inventory");
        _renderer.SetClass(player, "jbf_bp_track_view", "shown", tab == "track");
        _renderer.SetClass(player, "jbf_bp_missions_view", "shown", tab == "missions");
        _renderer.SetClass(player, "jbf_bp_inventory_view", "shown", tab == "inventory");

        RenderRewards(player, state, level);
        RenderMissions(player, state);
        RenderInventory(player, state);
    }

    private void RenderRewards(CCSPlayerController player, BattlePassPlayerState state, int level)
    {
        var page = _pages.GetValueOrDefault(player.Slot);
        var start = page * RewardSlots + 1;
        _renderer.Text(player, "jbf_bp_page", $"Уровни {start}–{Math.Min(_config.MaxLevel, start + RewardSlots - 1)}");
        for (var slot = 0; slot < RewardSlots; slot++)
        {
            var rewardLevel = start + slot;
            var panel = $"jbf_bp_reward_{slot}";
            var reward = _config.Rewards.FirstOrDefault(x => x.Level == rewardLevel);
            var visible = reward is not null && rewardLevel <= _config.MaxLevel;
            _renderer.SetClass(player, panel, "visible", visible);
            if (!visible) continue;
            _renderer.Text(player, $"{panel}_level", $"LVL {rewardLevel}");
            _renderer.Text(player, $"{panel}_name", reward!.Name);
            var claimed = state.ClaimedLevels.Contains(rewardLevel);
            _renderer.Text(player, $"{panel}_state", claimed ? "ПОЛУЧЕНО" : level >= rewardLevel ? "ЗАБРАТЬ" : "ЗАКРЫТО");
            _renderer.SetClass(player, panel, "claimed", claimed);
            _renderer.SetClass(player, panel, "locked", level < rewardLevel);
        }
    }

    private void RenderMissions(CCSPlayerController player, BattlePassPlayerState state)
    {
        var missions = _config.Missions.ToArray();
        var page = _pages.GetValueOrDefault(player.Slot);
        for (var slot = 0; slot < MissionSlots; slot++)
        {
            var index = page * MissionSlots + slot;
            var panel = $"jbf_bp_mission_{slot}";
            var visible = index < missions.Length;
            _renderer.SetClass(player, panel, "visible", visible);
            if (!visible) continue;
            var mission = missions[index];
            var period = PeriodKey(mission.Period);
            var key = $"{mission.Id}:{period}";
            var progress = Math.Min(mission.Target, state.MissionProgress.GetValueOrDefault(key));
            var complete = state.CompletedMissionPeriods.Contains(key);
            _renderer.Text(player, $"{panel}_name", mission.Name);
            _renderer.Text(player, $"{panel}_type", PeriodName(mission.Period));
            _renderer.Text(player, $"{panel}_progress", $"{progress} / {mission.Target}");
            _renderer.Text(player, $"{panel}_xp", $"+{mission.XpReward} XP");
            _renderer.SetClass(player, panel, "complete", complete);
        }
    }

    private void RenderInventory(CCSPlayerController player, BattlePassPlayerState state)
    {
        var items = state.Inventory.Where(x => x.Value > 0).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        var page = _pages.GetValueOrDefault(player.Slot);
        for (var slot = 0; slot < InventorySlots; slot++)
        {
            var index = page * InventorySlots + slot;
            var panel = $"jbf_bp_item_{slot}";
            var visible = index < items.Length;
            _renderer.SetClass(player, panel, "visible", visible);
            if (!visible) continue;
            var item = items[index];
            _renderer.Text(player, $"{panel}_name", ItemDisplayName(item.Key));
            _renderer.Text(player, $"{panel}_count", $"×{item.Value}");
        }
    }

    private BattlePassPlayerState GetState(CCSPlayerController player)
    {
        if (_states.TryGetValue(player.SteamID, out var existing))
        {
            existing.PlayerName = player.PlayerName;
            return existing;
        }
        var created = new BattlePassPlayerState { SteamId = player.SteamID, PlayerName = player.PlayerName };
        _states[player.SteamID] = created;
        return created;
    }

    private void Save(BattlePassPlayerState state) => _storage.Save(state, _log);

    private int LevelForXp(int xp) => Math.Clamp(xp / Math.Max(1, _config.XpPerLevel), 0, _config.MaxLevel);

    private string PeriodKey(MissionPeriod period)
    {
        var now = DateTime.UtcNow;
        return period switch
        {
            MissionPeriod.Daily => now.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            MissionPeriod.Weekly => $"{ISOWeek.GetYear(now)}W{ISOWeek.GetWeekOfYear(now):00}",
            _ => _config.SeasonId
        };
    }

    private static string PeriodName(MissionPeriod period) => period switch
    {
        MissionPeriod.Daily => "ЕЖЕДНЕВНОЕ",
        MissionPeriod.Weekly => "ЕЖЕНЕДЕЛЬНОЕ",
        _ => "СЕЗОННОЕ"
    };

    private static string ItemDisplayName(string itemId) => itemId switch
    {
        "weapon_hegrenade" => "HE Grenade",
        "weapon_flashbang" => "Flashbang",
        "weapon_smokegrenade" => "Smoke",
        "weapon_decoy" => "Decoy",
        "weapon_deagle" => "Desert Eagle",
        _ => itemId.Replace("weapon_", string.Empty, StringComparison.OrdinalIgnoreCase)
    };

    private static bool IsUsable(CCSPlayerController? player) => player is { IsValid: true, IsBot: false } && player.SteamID != 0;
}
