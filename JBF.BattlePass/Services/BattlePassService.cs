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
    private const int ProgressSteps = 10;

    private readonly BattlePassConfig _config;
    private readonly BattlePassStorage _storage;
    private readonly BattlePassRenderer _renderer;
    private readonly Action<string>? _log;
    private readonly Dictionary<ulong, BattlePassPlayerState> _states;
    private readonly Dictionary<int, string> _tabs = [];
    private readonly Dictionary<int, int> _pages = [];
    private readonly HashSet<int> _usedInventoryThisRound = [];
    private readonly Dictionary<int, DateTime> _playMinuteTicks = [];
    private readonly HashSet<ulong> _rebelsThisRound = [];

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

    public bool ReloadConfig(BattlePassConfig config, out string error)
    {
        error = string.Empty;

        if (!config.SeasonId.Equals(_config.SeasonId, StringComparison.OrdinalIgnoreCase))
        {
            error = "SeasonId нельзя менять через reload; требуется перезапуск плагина.";
            return false;
        }

        _config.SeasonName = config.SeasonName;
        _config.XpPerLevel = config.XpPerLevel;
        _config.MaxLevel = config.MaxLevel;
        _config.Missions = config.Missions;
        _config.Rewards = config.Rewards;

        foreach (var player in Utilities.GetPlayers().Where(IsUsable))
        {
            _pages[player.Slot] = Math.Min(_pages.GetValueOrDefault(player.Slot), MaxPage(player));
            if (_tabs.ContainsKey(player.Slot))
                Render(player);
        }

        return true;
    }

    public void Open(CCSPlayerController player)
    {
        if (!IsUsable(player)) return;
        _tabs[player.Slot] = _tabs.GetValueOrDefault(player.Slot, "track");
        _pages[player.Slot] = Math.Clamp(_pages.GetValueOrDefault(player.Slot), 0, MaxPage(player));
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
        var current = _pages.GetValueOrDefault(player.Slot);
        var page = Math.Clamp(current + Math.Clamp(delta, -1, 1), 0, MaxPage(player));
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

        foreach (var mission in ActiveMissions(player).Where(m => m.ObjectiveId.Equals(objectiveId, StringComparison.OrdinalIgnoreCase)))
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
            UiCapability.Api.GetOptional()?.Notify(player, $"Задание выполнено: {mission.Name} • +{mission.XpReward} XP", UiNotificationType.Success, 5.0f);
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
        UiCapability.Api.GetOptional()?.Notify(player, $"+{amount} XP{(string.IsNullOrWhiteSpace(reason) ? string.Empty : $" • {reason}")}", UiNotificationType.Success, 3.0f);
        if (_tabs.ContainsKey(player.Slot)) Render(player);
    }

    public int GetXp(CCSPlayerController player) => IsUsable(player) ? GetState(player).Xp : 0;
    public int GetLevel(CCSPlayerController player) => IsUsable(player) ? LevelForXp(GetState(player).Xp) : 0;

    public void OnRoundStart()
    {
        _usedInventoryThisRound.Clear();
        _rebelsThisRound.Clear();
        foreach (var player in Utilities.GetPlayers().Where(IsUsable))
            if (_tabs.ContainsKey(player.Slot)) Render(player);
    }

    public void OnRoundEnd(CsTeam winner)
    {
        foreach (var player in Utilities.GetPlayers().Where(IsUsable))
        {
            AddProgress(player, "round_played", 1);

            if (player.PawnIsAlive)
                AddProgress(player, "round_survived", 1);

            if (player.Team == CsTeam.Terrorist)
            {
                AddProgress(player, "t_round_played", 1);
                if (!_rebelsThisRound.Contains(player.SteamID))
                    AddProgress(player, "lawful_round", 1);
            }
            else if (player.Team == CsTeam.CounterTerrorist)
            {
                AddProgress(player, "ct_round_played", 1);
            }

            if ((winner == CsTeam.Terrorist && player.Team == CsTeam.Terrorist) ||
                (winner == CsTeam.CounterTerrorist && player.Team == CsTeam.CounterTerrorist))
            {
                AddProgress(player, "team_win", 1);
            }
        }
    }

    public void OnKill(CCSPlayerController? victim, CCSPlayerController? attacker, bool headshot, string weapon)
    {
        if (!IsUsable(attacker) || !IsUsable(victim) || attacker!.Slot == victim!.Slot) return;

        AddProgress(attacker, "kill", 1);

        if (headshot)
            AddProgress(attacker, "headshot_kill", 1);

        if (!string.IsNullOrWhiteSpace(weapon) &&
            (weapon.Contains("knife", StringComparison.OrdinalIgnoreCase) ||
             weapon.Equals("bayonet", StringComparison.OrdinalIgnoreCase)))
        {
            AddProgress(attacker, "knife_kill", 1);
        }

        if (attacker.Team == CsTeam.Terrorist && victim.Team == CsTeam.CounterTerrorist)
            AddProgress(attacker, "kill_guard", 1);
        else if (attacker.Team == CsTeam.CounterTerrorist && victim.Team == CsTeam.Terrorist)
            AddProgress(attacker, "kill_inmate", 1);
    }

    public void OnRebelStarted(CCSPlayerController player)
    {
        if (!IsUsable(player)) return;
        _rebelsThisRound.Add(player.SteamID);
        AddProgress(player, "became_rebel", 1);
    }

    public void OnRebelKilled(RebelKilledEvent data)
    {
        if (IsUsable(data.Killer) && data.Killer!.Team == CsTeam.CounterTerrorist)
            AddProgress(data.Killer, "kill_rebel", 1);
    }

    public void OnFreeDayGranted(CCSPlayerController player)
    {
        if (IsUsable(player))
            AddProgress(player, "freeday_received", 1);
    }

    public void OnWardenClaimed(CCSPlayerController player)
    {
        if (IsUsable(player))
            AddProgress(player, "warden_claim", 1);
    }

    public void OnLrEnded(LrMatchEndedEvent match)
    {
        if (IsUsable(match.Inmate)) AddProgress(match.Inmate, "lr_play", 1);
        if (IsUsable(match.Guardian)) AddProgress(match.Guardian, "lr_play", 1);
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

        var player = Utilities.GetPlayerFromSlot(slot);
        if (player is { IsValid: true } && player.SteamID != 0)
            _rebelsThisRound.Remove(player.SteamID);
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
            UiCapability.Api.GetOptional()?.Notify(player, "Этот уровень ещё не открыт.", UiNotificationType.Warning, 3.0f);
            return;
        }
        if (state.ClaimedLevels.Contains(level))
        {
            UiCapability.Api.GetOptional()?.Notify(player, "Награда уже получена.", UiNotificationType.Info, 3.0f);
            return;
        }

        var reward = _config.Rewards.FirstOrDefault(x => x.Level == level);
        if (reward is null) return;

        if (reward.Type == RewardType.Credits)
        {
            var shop = ShopCapability.Api.GetOptional();
            if (shop is null || !shop.TryAddCredits(player, reward.Amount, out var balance))
            {
                UiCapability.Api.GetOptional()?.Notify(player, "Не удалось начислить кредиты. Попробуйте позже.", UiNotificationType.Error, 4.0f);
                return;
            }
            UiCapability.Api.GetOptional()?.Notify(player, $"Получено {reward.Amount} кредитов • Баланс {balance}", UiNotificationType.Success, 4.0f);
        }
        else if (reward.Type == RewardType.Cosmetic)
        {
            if (string.IsNullOrWhiteSpace(reward.ItemId)) return;
            var cosmetics = CosmeticsCapability.Api.GetOptional();
            if (cosmetics is null || !cosmetics.Grant(player, reward.ItemId, "BattlePass"))
            {
                UiCapability.Api.GetOptional()?.Notify(player, "Не удалось выдать косметику. Попробуйте позже.", UiNotificationType.Error, 4.0f);
                return;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(reward.ItemId)) return;
            state.Inventory[reward.ItemId] = state.Inventory.GetValueOrDefault(reward.ItemId) + Math.Max(1, reward.Amount);
            UiCapability.Api.GetOptional()?.Notify(player, $"Получено: {reward.Name}", UiNotificationType.Success, 4.0f);
        }

        state.ClaimedLevels.Add(level);
        Save(state);
        Render(player);
    }

    private void UseInventoryItem(CCSPlayerController player, string itemId)
    {
        var state = GetState(player);
        if (state.Inventory.GetValueOrDefault(itemId) <= 0) return;
        if (!player.PawnIsAlive || JailbreakCapability.Api.GetOptional()?.IsRoundActive != true)
        {
            UiCapability.Api.GetOptional()?.Notify(player, "Предмет можно использовать только живым во время раунда.", UiNotificationType.Warning, 4.0f);
            return;
        }
        if (LrCapability.Api.GetOptional()?.IsActive == true || SpecialDaysCapability.Api.GetOptional()?.IsActive == true)
        {
            UiCapability.Api.GetOptional()?.Notify(player, "Инвентарь Battle Pass недоступен во время LR или игрового дня.", UiNotificationType.Warning, 4.0f);
            return;
        }
        if (_usedInventoryThisRound.Contains(player.Slot))
        {
            UiCapability.Api.GetOptional()?.Notify(player, "Можно использовать только один предмет Battle Pass за раунд.", UiNotificationType.Warning, 4.0f);
            return;
        }

        try
        {
            player.GiveNamedItem(itemId);
        }
        catch
        {
            UiCapability.Api.GetOptional()?.Notify(player, "Не удалось выдать предмет.", UiNotificationType.Error, 3.0f);
            return;
        }

        state.Inventory[itemId]--;
        if (state.Inventory[itemId] <= 0) state.Inventory.Remove(itemId);
        _usedInventoryThisRound.Add(player.Slot);
        _pages[player.Slot] = Math.Min(_pages.GetValueOrDefault(player.Slot), MaxPage(player));
        Save(state);
        UiCapability.Api.GetOptional()?.Notify(player, $"Использовано: {ItemDisplayName(itemId)}", UiNotificationType.Success, 3.0f);
        Render(player);
    }

    private void Render(CCSPlayerController player)
    {
        var state = GetState(player);
        _pages[player.Slot] = Math.Clamp(_pages.GetValueOrDefault(player.Slot), 0, MaxPage(player));

        var level = LevelForXp(state.Xp);
        var currentFloorXp = level >= _config.MaxLevel ? _config.MaxLevel * _config.XpPerLevel : level * _config.XpPerLevel;
        var progress = level >= _config.MaxLevel ? _config.XpPerLevel : Math.Max(0, state.Xp - currentFloorXp);
        var percent = level >= _config.MaxLevel ? 100 : Math.Clamp(progress * 100 / Math.Max(1, _config.XpPerLevel), 0, 100);

        _renderer.Text(player, "jbf_bp_season", _config.SeasonName);
        _renderer.Text(player, "jbf_bp_level", $"LVL {level}");
        _renderer.Text(player, "jbf_bp_xp", level >= _config.MaxLevel ? "MAX LEVEL" : $"{progress} / {_config.XpPerLevel} XP");
        _renderer.Text(player, "jbf_bp_totalxp", $"Сезонный XP: {state.Xp:N0}");
        _renderer.Text(player, "jbf_bp_progress", percent.ToString(CultureInfo.InvariantCulture));
        SetProgressClass(player, "jbf_bp_root", "xp", percent);

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
            SetRewardImage(player, panel, reward);
            var claimed = state.ClaimedLevels.Contains(rewardLevel);
            var available = !claimed && level >= rewardLevel;
            _renderer.Text(player, $"{panel}_state", claimed ? "ПОЛУЧЕНО" : available ? "ЗАБРАТЬ" : "ЗАКРЫТО");
            _renderer.SetClass(player, panel, "claimed", claimed);
            _renderer.SetClass(player, panel, "available", available);
            _renderer.SetClass(player, panel, "locked", level < rewardLevel);
            SetRewardTypeClasses(player, panel, reward.Type);
        }
    }

    private void RenderMissions(CCSPlayerController player, BattlePassPlayerState state)
    {
        var missions = ActiveMissions(player).ToArray();
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
            var percent = Math.Clamp(progress * 100 / Math.Max(1, mission.Target), 0, 100);
            _renderer.Text(player, $"{panel}_name", mission.Name);
            _renderer.Text(player, $"{panel}_type", PeriodName(mission.Period));
            _renderer.Text(player, $"{panel}_progress", $"{progress} / {mission.Target}");
            _renderer.Text(player, $"{panel}_xp", $"+{mission.XpReward} XP");
            _renderer.SetClass(player, panel, "complete", complete);
            SetProgressClass(player, panel, "mission", percent);
        }
    }

    private void RenderInventory(CCSPlayerController player, BattlePassPlayerState state)
    {
        var items = state.Inventory.Where(x => x.Value > 0).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        var page = _pages.GetValueOrDefault(player.Slot);
        var canUse = CanUseInventory(player, out var status);
        _renderer.Text(player, "jbf_bp_inventory_status", status);
        _renderer.SetClass(player, "jbf_bp_inventory_view", "inventory-disabled", !canUse);

        for (var slot = 0; slot < InventorySlots; slot++)
        {
            var index = page * InventorySlots + slot;
            var panel = $"jbf_bp_item_{slot}";
            var visible = index < items.Length;
            _renderer.SetClass(player, panel, "visible", visible);
            _renderer.SetClass(player, panel, "disabled", visible && !canUse);
            if (!visible) continue;

            var item = items[index];
            _renderer.Text(player, $"{panel}_name", ItemDisplayName(item.Key));
            _renderer.Text(player, $"{panel}_count", $"×{item.Value}");
        }
    }

    private bool CanUseInventory(CCSPlayerController player, out string status)
    {
        if (!player.PawnIsAlive || JailbreakCapability.Api.GetOptional()?.IsRoundActive != true)
        {
            status = "Предметы доступны только живым игрокам во время обычного раунда.";
            return false;
        }
        if (LrCapability.Api.GetOptional()?.IsActive == true)
        {
            status = "Инвентарь временно отключён во время LR.";
            return false;
        }
        if (SpecialDaysCapability.Api.GetOptional()?.IsActive == true)
        {
            status = "Инвентарь временно отключён во время игрового дня.";
            return false;
        }
        if (_usedInventoryThisRound.Contains(player.Slot))
        {
            status = "Лимит этого раунда использован: один предмет за раунд.";
            return false;
        }

        status = "Предмет доступен. За один обычный раунд можно использовать одну награду.";
        return true;
    }

    private int MaxPage(CCSPlayerController player)
    {
        var tab = _tabs.GetValueOrDefault(player.Slot, "track");
        var count = tab switch
        {
            "track" => Math.Max(0, _config.MaxLevel),
            "missions" => ActiveMissions(player).Count,
            "inventory" => GetState(player).Inventory.Count(x => x.Value > 0),
            _ => 0
        };
        var pageSize = tab switch
        {
            "track" => RewardSlots,
            "missions" => MissionSlots,
            "inventory" => InventorySlots,
            _ => RewardSlots
        };
        return count <= 0 ? 0 : (count - 1) / pageSize;
    }

    private void SetRewardImage(CCSPlayerController player, string panel, RewardDefinition reward)
    {
        var selected = ResolveRewardImageKey(reward);
        var keys = new[]
        {
            "credits",
            "deagle",
            "hegrenade",
            "smokegrenade",
            "flashbang"
        };

        foreach (var key in keys)
            _renderer.SetClass(player, $"{panel}_image_{key}", "visible", key == selected);
    }

    private static string? ResolveRewardImageKey(RewardDefinition reward)
    {
        if (!string.IsNullOrWhiteSpace(reward.ImageKey))
        {
            var configured = reward.ImageKey.Trim().ToLowerInvariant();
            return configured switch
            {
                "credits" => "credits",
                "deagle" or "weapon_deagle" => "deagle",
                "hegrenade" or "weapon_hegrenade" => "hegrenade",
                "smokegrenade" or "weapon_smokegrenade" => "smokegrenade",
                "flashbang" or "weapon_flashbang" => "flashbang",
                _ => null
            };
        }

        if (reward.Type == RewardType.Credits)
            return "credits";

        return reward.ItemId?.Trim().ToLowerInvariant() switch
        {
            "weapon_deagle" => "deagle",
            "weapon_hegrenade" => "hegrenade",
            "weapon_smokegrenade" => "smokegrenade",
            "weapon_flashbang" => "flashbang",
            _ => null
        };
    }

    private void SetRewardTypeClasses(CCSPlayerController player, string panel, RewardType type)
    {
        foreach (var name in Enum.GetNames<RewardType>())
            _renderer.SetClass(player, panel, $"type-{name.ToLowerInvariant()}", false);
        _renderer.SetClass(player, panel, $"type-{type.ToString().ToLowerInvariant()}", true);
    }

    private void SetProgressClass(CCSPlayerController player, string panel, string prefix, int percent)
    {
        var step = Math.Clamp((int)Math.Ceiling(percent / 10.0), 0, ProgressSteps);
        for (var i = 0; i <= ProgressSteps; i++)
            _renderer.SetClass(player, panel, $"{prefix}-p{i}", i == step);
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

    private IReadOnlyList<MissionDefinition> ActiveMissions(CCSPlayerController player)
    {
        var result = new List<MissionDefinition>();
        result.AddRange(SelectPeriodMissions(player, MissionPeriod.Daily, 3));
        result.AddRange(SelectPeriodMissions(player, MissionPeriod.Weekly, 3));
        result.AddRange(_config.Missions.Where(m => m.Period == MissionPeriod.Season));
        return result;
    }

    private IEnumerable<MissionDefinition> SelectPeriodMissions(CCSPlayerController player, MissionPeriod period, int count)
    {
        var periodKey = PeriodKey(period);
        return _config.Missions
            .Where(m => m.Period == period)
            .OrderBy(m => StableMissionOrder(player.SteamID, periodKey, m.Id))
            .Take(count);
    }

    private static ulong StableMissionOrder(ulong steamId, string periodKey, string missionId)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        var value = $"{steamId}:{periodKey}:{missionId}";
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= prime;
        }
        return hash;
    }

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
