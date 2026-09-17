using System.Text.Json;
using JBF.BattlePass.Models;
using MySqlConnector;

namespace JBF.BattlePass.Services;

internal sealed class BattlePassStorage
{
    private readonly string _connectionString;
    private readonly string _table;
    private readonly string _seasonId;
    private bool _ready;

    public BattlePassStorage(DatabaseConfig config, string seasonId)
    {
        _seasonId = seasonId;
        _table = $"{NormalizePrefix(config.TablePrefix)}battlepass_players";
        _connectionString = new MySqlConnectionStringBuilder
        {
            Server = config.Host,
            Database = config.Database,
            UserID = config.User,
            Password = config.Password,
            Port = ushort.TryParse(config.Port, out var port) ? port : (ushort)3306,
            CharacterSet = "utf8mb4",
            ConnectionTimeout = 5
        }.ConnectionString;
    }

    public Dictionary<ulong, BattlePassPlayerState> LoadAll(Action<string>? log = null)
    {
        EnsureReady(log);
        if (!_ready) return [];

        var result = new Dictionary<ulong, BattlePassPlayerState>();
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT steam_id, player_name, xp, claimed_levels, mission_progress, completed_missions, inventory FROM `{_table}` WHERE season_id=@season";
            command.Parameters.AddWithValue("@season", _seasonId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var state = new BattlePassPlayerState
                {
                    SteamId = reader.GetFieldValue<ulong>(0),
                    PlayerName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Xp = reader.IsDBNull(2) ? 0 : Math.Max(0, reader.GetInt32(2))
                };
                DeserializeInto(reader.IsDBNull(3) ? "[]" : reader.GetString(3), state.ClaimedLevels);
                DeserializeDictionary(reader.IsDBNull(4) ? "{}" : reader.GetString(4), state.MissionProgress);
                DeserializeInto(reader.IsDBNull(5) ? "[]" : reader.GetString(5), state.CompletedMissionPeriods);
                DeserializeDictionary(reader.IsDBNull(6) ? "{}" : reader.GetString(6), state.Inventory);
                result[state.SteamId] = state;
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"BattlePass load failed: {ex.Message}");
        }
        return result;
    }

    public void Save(BattlePassPlayerState state, Action<string>? log = null)
    {
        EnsureReady(log);
        if (!_ready) return;

        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $@"INSERT INTO `{_table}`
(steam_id, season_id, player_name, xp, claimed_levels, mission_progress, completed_missions, inventory)
VALUES (@steam,@season,@name,@xp,@claimed,@progress,@completed,@inventory)
ON DUPLICATE KEY UPDATE player_name=@name, xp=@xp, claimed_levels=@claimed, mission_progress=@progress, completed_missions=@completed, inventory=@inventory";
            command.Parameters.AddWithValue("@steam", state.SteamId);
            command.Parameters.AddWithValue("@season", _seasonId);
            command.Parameters.AddWithValue("@name", state.PlayerName);
            command.Parameters.AddWithValue("@xp", state.Xp);
            command.Parameters.AddWithValue("@claimed", JsonSerializer.Serialize(state.ClaimedLevels));
            command.Parameters.AddWithValue("@progress", JsonSerializer.Serialize(state.MissionProgress));
            command.Parameters.AddWithValue("@completed", JsonSerializer.Serialize(state.CompletedMissionPeriods));
            command.Parameters.AddWithValue("@inventory", JsonSerializer.Serialize(state.Inventory));
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            log?.Invoke($"BattlePass save failed for {state.SteamId}: {ex.Message}");
        }
    }

    private void EnsureReady(Action<string>? log)
    {
        if (_ready) return;
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $@"CREATE TABLE IF NOT EXISTS `{_table}` (
steam_id BIGINT UNSIGNED NOT NULL,
season_id VARCHAR(64) NOT NULL,
player_name VARCHAR(128) NOT NULL DEFAULT '',
xp INT NOT NULL DEFAULT 0,
claimed_levels LONGTEXT NOT NULL,
mission_progress LONGTEXT NOT NULL,
completed_missions LONGTEXT NOT NULL,
inventory LONGTEXT NOT NULL,
updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
PRIMARY KEY (steam_id, season_id)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            command.ExecuteNonQuery();
            _ready = true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"BattlePass database unavailable: {ex.Message}");
        }
    }

    private static void DeserializeInto<T>(string json, ICollection<T> target)
    {
        try
        {
            foreach (var item in JsonSerializer.Deserialize<List<T>>(json) ?? []) target.Add(item);
        }
        catch { }
    }

    private static void DeserializeDictionary(string json, IDictionary<string, int> target)
    {
        try
        {
            foreach (var pair in JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? []) target[pair.Key] = pair.Value;
        }
        catch { }
    }

    private static string NormalizePrefix(string prefix)
    {
        var safe = new string((prefix ?? string.Empty).Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        return safe.Length == 0 ? "jbf_" : safe;
    }
}
