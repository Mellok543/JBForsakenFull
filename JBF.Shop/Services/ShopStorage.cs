using CounterStrikeSharp.API;
using JBF.Shop.Models;
using MySqlConnector;

namespace JBF.Shop.Services;

internal sealed class ShopStorage
{
    private readonly ShopDatabaseConfig _config;
    private readonly string _connectionString;
    private readonly string _balancesTable;
    private bool _isReady;

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
            CharacterSet = "utf8mb4"
        }.ConnectionString;
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
                    Credits = reader.IsDBNull(2) ? 0 : reader.GetInt32(2)
                };
            }
        }
        catch (Exception exception)
        {
            _isReady = false;
            Server.PrintToConsole($"[JBF] Shop MySQL load failed: {exception.Message}");
        }

        return players;
    }

    public void Save(IReadOnlyDictionary<ulong, ShopPlayerState> states)
    {
        if (!_isReady)
        {
            return;
        }

        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            foreach (var state in states.Values)
            {
                using var command = connection.CreateCommand();
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
                command.ExecuteNonQuery();
            }
        }
        catch (Exception exception)
        {
            _isReady = false;
            Server.PrintToConsole($"[JBF] Shop MySQL save failed: {exception.Message}");
        }
    }

    private bool Initialize()
    {
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = $"""
                                  CREATE TABLE IF NOT EXISTS `{_balancesTable}` (
                                      steam_id BIGINT UNSIGNED NOT NULL PRIMARY KEY,
                                      player_name VARCHAR(128) NOT NULL,
                                      credits INT NOT NULL DEFAULT 0,
                                      updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
                                  ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
                                  """;
            command.ExecuteNonQuery();

            _isReady = true;
            Server.PrintToConsole($"[JBF] Shop connected to MySQL table `{_balancesTable}`.");
            return true;
        }
        catch (Exception exception)
        {
            _isReady = false;
            Server.PrintToConsole($"[JBF] Shop MySQL init failed: {exception.Message}");
            return false;
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
}
