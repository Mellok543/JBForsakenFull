using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Achievements.Config;
using JBF.Achievements.Models;
using JBF.Api;

namespace JBF.Achievements.Services;

internal sealed class AchievementService : IAchievementsApi, IDisposable
{
    private readonly BasePlugin _plugin;
    private readonly AchievementsConfig _config;
    private readonly AchievementStorage _storage;
    private readonly Dictionary<ulong, PlayerAchievementState> _players;
    private readonly Dictionary<string, List<AchievementDefinition>> _byStat;

    public AchievementService(BasePlugin plugin, AchievementsConfig config)
    {
        _plugin = plugin;
        _config = config;
        _storage = new AchievementStorage(config.Database);
        _players = _storage.Load();
        _byStat = config.Achievements
            .Where(a => !string.IsNullOrWhiteSpace(a.StatKey) && a.Target > 0)
            .GroupBy(a => a.StatKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    public bool IsUnlocked(CCSPlayerController player, string achievementId) =>
        GetState(player, false)?.Unlocked.Contains(achievementId) == true;

    public long GetStat(CCSPlayerController player, string statKey) =>
        GetState(player, false)?.Stats.GetValueOrDefault(statKey) ?? 0;

    public bool TrySetStat(CCSPlayerController player, string statKey, long value)
    {
        if (string.IsNullOrWhiteSpace(statKey) || value < 0) return false;
        var state = GetState(player, true);
        if (state is null) return false;
        state.Stats[statKey] = value;
        _storage.QueueStat(state.SteamId, statKey, value);
        CheckUnlocks(player, state, statKey, value);
        return true;
    }

    public bool TryUnlock(CCSPlayerController player, string achievementId)
    {
        var achievement = _config.Achievements.FirstOrDefault(a => a.Id.Equals(achievementId, StringComparison.OrdinalIgnoreCase));
        var state = GetState(player, true);
        if (achievement is null || state is null) return false;
        if (state.Unlocked.Add(achievement.Id))
            _storage.QueueUnlock(state.SteamId, achievement.Id);
        return true;
    }

    public bool TryResetAchievement(CCSPlayerController player, string achievementId)
    {
        var achievement = _config.Achievements.FirstOrDefault(a => a.Id.Equals(achievementId, StringComparison.OrdinalIgnoreCase));
        var state = GetState(player, false);
        if (achievement is null || state is null) return false;
        state.Unlocked.Remove(achievement.Id);
        _storage.QueueResetUnlock(state.SteamId, achievement.Id);
        return true;
    }

    public void Increment(CCSPlayerController? player, string statKey, long amount = 1)
    {
        if (player is null || !player.IsValid || player.IsBot || amount <= 0) return;
        var state = GetState(player, true);
        if (state is null) return;
        var current = state.Stats.GetValueOrDefault(statKey);
        var next = current > long.MaxValue - amount ? long.MaxValue : current + amount;
        state.Stats[statKey] = next;
        _storage.QueueStat(state.SteamId, statKey, next);
        CheckUnlocks(player, state, statKey, next);
    }

    public void HandleRoundEnd(CsTeam winner)
    {
        foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist))
        {
            Increment(player, "rounds_played");
            if (winner == CsTeam.CounterTerrorist && player.Team == CsTeam.CounterTerrorist) Increment(player, "ct_round_wins");
            if (winner == CsTeam.Terrorist && player.Team == CsTeam.Terrorist) Increment(player, "t_round_wins");
            if (player.PawnIsAlive && player.PlayerPawn.Value is { IsValid: true, Health: 1 }) Increment(player, "survive_with_1hp");
        }
    }

    public void HandleKill(CCSPlayerController? victim, CCSPlayerController? attacker, string weapon, bool headshot)
    {
        if (victim is null || attacker is null || !victim.IsValid || !attacker.IsValid || victim.Slot == attacker.Slot || attacker.IsBot) return;

        Increment(attacker, "kills_total");
        if (headshot) Increment(attacker, "headshot_kills");
        if (attacker.Team == CsTeam.Terrorist && victim.Team == CsTeam.CounterTerrorist) Increment(attacker, "ct_kills_as_t");

        if (weapon.Contains("knife", StringComparison.OrdinalIgnoreCase) || weapon.Equals("bayonet", StringComparison.OrdinalIgnoreCase))
            Increment(attacker, "knife_kills");

        if (weapon.Contains("hegrenade", StringComparison.OrdinalIgnoreCase) || weapon.Equals("grenade", StringComparison.OrdinalIgnoreCase))
            Increment(attacker, "grenade_kills");

        if (attacker.PlayerPawn.Value is { IsValid: true, Health: 1 })
            Increment(attacker, "kills_with_1hp");
    }

    public void HandleLrEnded(LrMatchEndedEvent match)
    {
        Increment(match.Inmate, "lr_participations");
        Increment(match.Guardian, "lr_participations");
        if (match.Winner is not null) Increment(match.Winner, "lr_wins");
    }

    public void ApplySpawnBonuses(CCSPlayerController player)
    {
        if (!player.IsValid || player.IsBot) return;
        _plugin.AddTimer(0.20f, () =>
        {
            if (!player.IsValid || !player.PawnIsAlive || player.PlayerPawn.Value is not { IsValid: true } pawn) return;
            var bonuses = GetBonuses(player);
            if (bonuses.BonusHealth > 0)
            {
                pawn.Health += bonuses.BonusHealth;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
            }
            if (bonuses.BonusArmor > 0)
            {
                pawn.ArmorValue = Math.Min(200, pawn.ArmorValue + bonuses.BonusArmor);
                Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_ArmorValue");
            }
            if (bonuses.SpeedMultiplier > 1.0001f)
            {
                pawn.VelocityModifier *= bonuses.SpeedMultiplier;
                Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_flVelocityModifier");
            }
            if (bonuses.GravityMultiplier < 0.9999f)
            {
                pawn.GravityScale *= bonuses.GravityMultiplier;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_flGravityScale");
            }
            foreach (var item in bonuses.SpawnItems.Distinct(StringComparer.OrdinalIgnoreCase)) player.GiveNamedItem(item);
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    public void OpenMenu(CCSPlayerController player)
    {
        var menu = MenuCapability.Api.GetOptional();
        var state = GetState(player, true);
        if (menu is null || state is null) return;

        var categories = _config.Achievements.Select(a => a.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        var options = categories.Select(category =>
        {
            var categoryAchievements = _config.Achievements.Where(a => a.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToArray();
            var unlocked = categoryAchievements.Count(a => state.Unlocked.Contains(a.Id));
            return new JailbreakMenuOption($"{category} [{unlocked}/{categoryAchievements.Length}]", p => OpenCategory(p, category));
        }).ToArray();

        menu.Open(player, $"Достижения | {state.Unlocked.Count}/{_config.Achievements.Count}", options);
    }

    public void OpenStats(CCSPlayerController player)
    {
        var menu = MenuCapability.Api.GetOptional();
        var state = GetState(player, true);
        if (menu is null || state is null) return;

        var rows = new (string Name, string Key)[]
        {
            ("Сыграно раундов", "rounds_played"), ("Побед за КТ", "ct_round_wins"), ("Побед за T", "t_round_wins"),
            ("Убийств", "kills_total"), ("Убийств КТ за T", "ct_kills_as_t"), ("Headshot", "headshot_kills"),
            ("Убийств ножом", "knife_kills"), ("Убийств HE", "grenade_kills"), ("Стать КМД", "warden_claims"),
            ("Побед LR", "lr_wins"), ("Участий LR", "lr_participations"),
            ("Начал бунт", "times_became_rebel"), ("Убито бунтарей", "rebels_killed"), ("Получено FreeDay", "freedays_received")
        };
        var options = rows.Select(row => new JailbreakMenuOption($"{row.Name}: {state.Stats.GetValueOrDefault(row.Key)}", _ => { }, true)).ToArray();
        menu.Open(player, "Статистика", options);
    }

    private void OpenCategory(CCSPlayerController player, string category)
    {
        var menu = MenuCapability.Api.GetOptional();
        var state = GetState(player, true);
        if (menu is null || state is null) return;

        var options = _config.Achievements.Where(a => a.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Secret).ThenBy(a => a.Name)
            .Select(a =>
            {
                var unlocked = state.Unlocked.Contains(a.Id);
                var title = a.Secret && !unlocked ? "? Секретное достижение" : $"{(unlocked ? "✓" : "○")} {a.Name}";
                return new JailbreakMenuOption(title, p => OpenDetails(p, a));
            }).Append(new JailbreakMenuOption("Назад", OpenMenu)).ToArray();

        menu.Open(player, category, options);
    }

    private void OpenDetails(CCSPlayerController player, AchievementDefinition achievement)
    {
        var menu = MenuCapability.Api.GetOptional();
        var state = GetState(player, true);
        if (menu is null || state is null) return;
        var unlocked = state.Unlocked.Contains(achievement.Id);
        var hidden = achievement.Secret && !unlocked;
        var progress = state.Stats.GetValueOrDefault(achievement.StatKey);
        var options = new List<JailbreakMenuOption>
        {
            new(hidden ? "Условие: ???" : achievement.Description, _ => { }, true),
            new(hidden ? "Награда: ???" : $"Награда: {FormatReward(achievement.Reward)}", _ => { }, true),
            new(hidden ? "Прогресс: ???" : $"Прогресс: {Math.Min(progress, achievement.Target)}/{achievement.Target}", _ => { }, true),
            new("Назад", p => OpenCategory(p, achievement.Category))
        };
        menu.Open(player, hidden ? "Секретное достижение" : achievement.Name, options);
    }

    private void CheckUnlocks(CCSPlayerController player, PlayerAchievementState state, string statKey, long value)
    {
        if (!_byStat.TryGetValue(statKey, out var achievements)) return;
        foreach (var achievement in achievements)
        {
            if (value < achievement.Target || !state.Unlocked.Add(achievement.Id)) continue;
            _storage.QueueUnlock(state.SteamId, achievement.Id);
            player.PrintToChat(JailbreakChat.Format($"★ ДОСТИЖЕНИЕ: {achievement.Name} | {FormatReward(achievement.Reward)}"));
            player.PrintToCenterHtml($"<font color='#FFD700'><b>ДОСТИЖЕНИЕ ПОЛУЧЕНО</b></font><br>{achievement.Name}<br><font color='#A0FFA0'>{FormatReward(achievement.Reward)}</font>");
        }
    }

    private AchievementBonuses GetBonuses(CCSPlayerController player)
    {
        var state = GetState(player, false);
        if (state is null) return new AchievementBonuses(0, 0, 1f, 1f, Array.Empty<string>());
        var health = 0; var armor = 0; var speed = 1f; var gravity = 1f; var items = new List<string>();
        foreach (var achievement in _config.Achievements.Where(a => state.Unlocked.Contains(a.Id)))
        {
            health += achievement.Reward.BonusHealth;
            armor += achievement.Reward.BonusArmor;
            speed *= Math.Max(0.01f, achievement.Reward.SpeedMultiplier);
            gravity *= Math.Max(0.01f, achievement.Reward.GravityMultiplier);
            if (!string.IsNullOrWhiteSpace(achievement.Reward.SpawnItem)) items.Add(achievement.Reward.SpawnItem);
        }
        return new AchievementBonuses(Math.Clamp(health, 0, _config.Caps.MaxBonusHealth), Math.Clamp(armor, 0, _config.Caps.MaxBonusArmor), Math.Clamp(speed, 1f, _config.Caps.MaxSpeedMultiplier), Math.Clamp(gravity, _config.Caps.MinGravityMultiplier, 1f), items);
    }

    private PlayerAchievementState? GetState(CCSPlayerController player, bool create)
    {
        var steamId = player.AuthorizedSteamID?.SteamId64;
        if (steamId is null) return null;
        if (!_players.TryGetValue(steamId.Value, out var state) && create)
        {
            state = new PlayerAchievementState { SteamId = steamId.Value };
            _players[steamId.Value] = state;
        }
        return state;
    }

    private static string FormatReward(AchievementReward reward)
    {
        var parts = new List<string>();
        if (reward.BonusHealth != 0) parts.Add($"+{reward.BonusHealth} HP");
        if (reward.BonusArmor != 0) parts.Add($"+{reward.BonusArmor} Armor");
        if (reward.SpeedMultiplier > 1.0001f) parts.Add($"+{(reward.SpeedMultiplier - 1f) * 100:0.#}% Speed");
        if (reward.GravityMultiplier < 0.9999f) parts.Add($"{(reward.GravityMultiplier - 1f) * 100:0.#}% Gravity");
        if (!string.IsNullOrWhiteSpace(reward.SpawnItem)) parts.Add(reward.SpawnItem);
        return parts.Count == 0 ? "без бонуса" : string.Join(", ", parts);
    }

    public void Dispose() => _storage.Dispose();
}
