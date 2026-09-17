using System.Threading.Channels;
using CounterStrikeSharp.API;
using JBF.Achievements.Config;
using JBF.Achievements.Models;
using MySqlConnector;

namespace JBF.Achievements.Services;

internal sealed class AchievementStorage : IDisposable
{
    private readonly string _connectionString;
    private readonly string _statsTable;
    private readonly string _unlocksTable;
    private readonly Channel<StorageWrite> _writes = Channel.CreateUnbounded<StorageWrite>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;

    public AchievementStorage(AchievementDatabaseConfig config)
    {
        var prefix = NormalizePrefix(config.TablePrefix);
        _statsTable = $"{prefix}achievement_stats";
        _unlocksTable = $"{prefix}achievement_unlocks";
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

        Initialize();
        _worker = Task.Run(ProcessWritesAsync);
    }

    public Dictionary<ulong, PlayerAchievementState> Load()
    {
        var result = new Dictionary<ulong, PlayerAchievementState>();
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT steam_id, stat_key, value FROM `{_statsTable}`";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var steamId = reader.GetFieldValue<ulong>(0);
                    GetOrCreate(result, steamId).Stats[reader.GetString(1)] = Math.Max(0, reader.GetInt64(2));
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT steam_id, achievement_id FROM `{_unlocksTable}`";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    GetOrCreate(result, reader.GetFieldValue<ulong>(0)).Unlocked.Add(reader.GetString(1));
            }
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[JBF] Achievements MySQL load failed: {ex.Message}");
        }
        return result;
    }

    public void QueueStat(ulong steamId, string statKey, long value) =>
        _writes.Writer.TryWrite(new StatWrite(steamId, statKey, Math.Max(0, value)));

    public void QueueUnlock(ulong steamId, string achievementId) =>
        _writes.Writer.TryWrite(new UnlockWrite(steamId, achievementId));

    public void QueueResetUnlock(ulong steamId, string achievementId) =>
        _writes.Writer.TryWrite(new ResetUnlockWrite(steamId, achievementId));

    public void Dispose()
    {
        _writes.Writer.TryComplete();
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!_worker.IsCompleted && DateTime.UtcNow < deadline) Thread.Sleep(20);
        _shutdown.Cancel();
        try { _worker.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _shutdown.Dispose();
    }

    private void Initialize()
    {
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                CREATE TABLE IF NOT EXISTS `{_statsTable}` (
                    steam_id BIGINT UNSIGNED NOT NULL,
                    stat_key VARCHAR(64) NOT NULL,
                    value BIGINT NOT NULL DEFAULT 0,
                    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    PRIMARY KEY (steam_id, stat_key)
                ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
                CREATE TABLE IF NOT EXISTS `{_unlocksTable}` (
                    steam_id BIGINT UNSIGNED NOT NULL,
                    achievement_id VARCHAR(96) NOT NULL,
                    unlocked_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY (steam_id, achievement_id)
                ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
                """;
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[JBF] Achievements MySQL init failed: {ex.Message}");
        }
    }

    private async Task ProcessWritesAsync()
    {
        try
        {
            await foreach (var write in _writes.Reader.ReadAllAsync(_shutdown.Token))
            {
                while (!_shutdown.IsCancellationRequested)
                {
                    if (await TryWriteAsync(write, _shutdown.Token)) break;
                    await Task.Delay(TimeSpan.FromSeconds(2), _shutdown.Token);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task<bool> TryWriteAsync(StorageWrite write, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            switch (write)
            {
                case StatWrite stat:
                    command.CommandText = $"INSERT INTO `{_statsTable}` (steam_id, stat_key, value) VALUES (@steamId,@key,@value) ON DUPLICATE KEY UPDATE value=VALUES(value);";
                    command.Parameters.AddWithValue("@steamId", stat.SteamId);
                    command.Parameters.AddWithValue("@key", stat.StatKey);
                    command.Parameters.AddWithValue("@value", stat.Value);
                    break;
                case UnlockWrite unlock:
                    command.CommandText = $"INSERT IGNORE INTO `{_unlocksTable}` (steam_id, achievement_id) VALUES (@steamId,@achievementId);";
                    command.Parameters.AddWithValue("@steamId", unlock.SteamId);
                    command.Parameters.AddWithValue("@achievementId", unlock.AchievementId);
                    break;
                case ResetUnlockWrite reset:
                    command.CommandText = $"DELETE FROM `{_unlocksTable}` WHERE steam_id=@steamId AND achievement_id=@achievementId;";
                    command.Parameters.AddWithValue("@steamId", reset.SteamId);
                    command.Parameters.AddWithValue("@achievementId", reset.AchievementId);
                    break;
            }
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return false; }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[JBF] Achievements MySQL write failed: {ex.Message}");
            return false;
        }
    }

    private static PlayerAchievementState GetOrCreate(Dictionary<ulong, PlayerAchievementState> states, ulong steamId)
    {
        if (!states.TryGetValue(steamId, out var state))
        {
            state = new PlayerAchievementState { SteamId = steamId };
            states[steamId] = state;
        }
        return state;
    }

    private static string NormalizePrefix(string prefix) =>
        !string.IsNullOrWhiteSpace(prefix) && prefix.All(ch => char.IsLetterOrDigit(ch) || ch == '_') ? prefix : "jbf_";

    private abstract record StorageWrite;
    private sealed record StatWrite(ulong SteamId, string StatKey, long Value) : StorageWrite;
    private sealed record UnlockWrite(ulong SteamId, string AchievementId) : StorageWrite;
    private sealed record ResetUnlockWrite(ulong SteamId, string AchievementId) : StorageWrite;
}
