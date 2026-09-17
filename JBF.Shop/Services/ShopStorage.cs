using System.Collections.Concurrent;
using CounterStrikeSharp.API;
using JBF.Shop.Models;
using MySqlConnector;

namespace JBF.Shop.Services;

internal sealed class ShopStorage : IDisposable
{
    private readonly ShopDatabaseConfig _config;
    private readonly string _connectionString;
    private readonly string _balancesTable;
    private readonly ConcurrentDictionary<ulong, PendingSave> _pending = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;

    private bool _isReady;
    private long _version;
    private DateTimeOffset _nextReconnectAttempt = DateTimeOffset.MinValue;

    public ShopStorage(ShopDatabaseConfig config)
    {
        _config = config;
        _balancesTable = $"{NormalizePrefix(_config.TablePrefix)}shop_balances";
        _connectionString = new MySqlConnectionStringBuilder
        {
            Server = _config.Host,
            Database = _config.Database,
            UserID = _config.User,
            Password = _config.Password,
            Port = ushort.TryParse(_config.Port, out var port) ? port : (ushort)3306,
            CharacterSet = "utf8mb4",
            ConnectionTimeout = 5
        }.ConnectionString;

        _worker = Task.Run(ProcessQueueAsync);
    }

    public Dictionary<ulong, ShopPlayerState> Load()
    {
        if (!Initialize())
        {
            return [];
        }

        var players = new Dictionary<ulong, ShopPlayerState>();

        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT steam_id, player_name, credits FROM `{_balancesTable}`";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var steamId = reader.GetFieldValue<ulong>(0);
                players[steamId] = new ShopPlayerState
                {
                    SteamId = steamId,
                    PlayerName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Credits = Math.Max(0, reader.IsDBNull(2) ? 0 : reader.GetInt32(2))
                };
            }
        }
        catch (Exception exception)
        {
            MarkDisconnected($"load failed: {exception.Message}");
        }

        return players;
    }

    public void QueueSave(ShopPlayerState state)
    {
        var snapshot = new PendingSave(
            state.SteamId,
            state.PlayerName,
            Math.Max(0, state.Credits),
            Interlocked.Increment(ref _version));

        _pending[state.SteamId] = snapshot;
        TrySignal();
    }

    public void QueueSave(IEnumerable<ShopPlayerState> states)
    {
        foreach (var state in states)
        {
            QueueSave(state);
        }
    }

    public void Dispose()
    {
        // Give pending writes a short chance to reach MySQL during plugin unload.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!_pending.IsEmpty && DateTime.UtcNow < deadline)
        {
            TrySignal();
            Thread.Sleep(25);
        }

        _shutdown.Cancel();
        TrySignal();

        try
        {
            _worker.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Shutdown must never block/unload the game server.
        }

        _shutdown.Dispose();
        _signal.Dispose();
        _initializeLock.Dispose();
    }

    private async Task ProcessQueueAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(_shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Small debounce window: multiple balance changes for one player collapse
            // into a single latest snapshot in _pending.
            try
            {
                await Task.Delay(100, _shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var failed = false;
            foreach (var pair in _pending.ToArray())
            {
                if (_shutdown.IsCancellationRequested)
                {
                    return;
                }

                var snapshot = pair.Value;
                if (!await SaveSnapshotAsync(snapshot, _shutdown.Token))
                {
                    failed = true;
                    break;
                }

                if (_pending.TryGetValue(snapshot.SteamId, out var current) &&
                    current.Version == snapshot.Version)
                {
                    _pending.TryRemove(snapshot.SteamId, out _);
                }
            }

            if (!failed || _shutdown.IsCancellationRequested)
            {
                continue;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), _shutdown.Token);
                TrySignal();
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<bool> SaveSnapshotAsync(PendingSave state, CancellationToken cancellationToken)
    {
        if (!await EnsureReadyAsync(cancellationToken))
        {
            return false;
        }

        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                                  INSERT INTO `{_balancesTable}` (steam_id, player_name, credits)
                                  VALUES (@steamId, @playerName, @credits)
                                  ON DUPLICATE KEY UPDATE
                                      player_name = VALUES(player_name),
                                      credits = VALUES(credits);
                                  """;
            command.Parameters.AddWithValue("@steamId", state.SteamId);
            command.Parameters.AddWithValue("@playerName", state.PlayerName);
            command.Parameters.AddWithValue("@credits", state.Credits);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            MarkDisconnected($"save failed: {exception.Message}");
            return false;
        }
    }

    private bool Initialize()
    {
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = CreateTableSql();
            command.ExecuteNonQuery();

            MarkConnected();
            return true;
        }
        catch (Exception exception)
        {
            MarkDisconnected($"init failed: {exception.Message}");
            return false;
        }
    }

    private async Task<bool> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (_isReady)
        {
            return true;
        }

        if (DateTimeOffset.UtcNow < _nextReconnectAttempt)
        {
            return false;
        }

        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (_isReady)
            {
                return true;
            }

            if (DateTimeOffset.UtcNow < _nextReconnectAttempt)
            {
                return false;
            }

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);

                await using var command = connection.CreateCommand();
                command.CommandText = CreateTableSql();
                await command.ExecuteNonQueryAsync(cancellationToken);

                MarkConnected();
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception exception)
            {
                MarkDisconnected($"reconnect failed: {exception.Message}");
                return false;
            }
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    private string CreateTableSql()
    {
        return $"""
               CREATE TABLE IF NOT EXISTS `{_balancesTable}` (
                   steam_id BIGINT UNSIGNED NOT NULL PRIMARY KEY,
                   player_name VARCHAR(128) NOT NULL,
                   credits INT NOT NULL DEFAULT 0,
                   updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
               ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
               """;
    }

    private void MarkConnected()
    {
        var wasReady = _isReady;
        _isReady = true;
        _nextReconnectAttempt = DateTimeOffset.MinValue;

        if (!wasReady)
        {
            Server.PrintToConsole($"[JBF] Shop connected to MySQL table `{_balancesTable}`.");
        }
    }

    private void MarkDisconnected(string message)
    {
        _isReady = false;
        _nextReconnectAttempt = DateTimeOffset.UtcNow.AddSeconds(2);
        Server.PrintToConsole($"[JBF] Shop MySQL {message}");
    }

    private void TrySignal()
    {
        try
        {
            _signal.Release();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private static string NormalizePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return "jbf_";
        }

        return prefix.All(character => char.IsLetterOrDigit(character) || character == '_')
            ? prefix
            : "jbf_";
    }

    private sealed record PendingSave(
        ulong SteamId,
        string PlayerName,
        int Credits,
        long Version);
}
