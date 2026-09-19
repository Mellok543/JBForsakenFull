using MySqlConnector;

namespace JBF.TeamBalance;

internal sealed class TeamBalanceDatabase
{
    private readonly DatabaseConnectionConfig _config;

    public TeamBalanceDatabase(DatabaseConnectionConfig config) => _config = config;

    private MySqlConnection CreateConnection()
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = _config.Host,
            Port = (uint)Math.Max(1, _config.Port),
            Database = _config.Database,
            UserID = _config.User,
            Password = _config.Password,
            CharacterSet = "utf8mb4"
        };

        return new MySqlConnection(builder.ConnectionString);
    }

    public async Task InitializeAsync()
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS jbf_ct_bans
            (
                steam_id BIGINT UNSIGNED NOT NULL PRIMARY KEY,
                ban_until DATETIME NOT NULL
            );

            CREATE TABLE IF NOT EXISTS jbf_ct_access
            (
                steam_id BIGINT UNSIGNED NOT NULL PRIMARY KEY,
                passed_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """;

        await command.ExecuteNonQueryAsync();
    }

    public async Task<Dictionary<ulong, DateTime>> LoadBansAsync()
    {
        var result = new Dictionary<ulong, DateTime>();

        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT steam_id, ban_until FROM jbf_ct_bans WHERE ban_until > UTC_TIMESTAMP();";
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
            result[reader.GetFieldValue<ulong>(0)] = DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc);

        return result;
    }

    public async Task<HashSet<ulong>> LoadAccessAsync()
    {
        var result = new HashSet<ulong>();

        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT steam_id FROM jbf_ct_access;";
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
            result.Add(reader.GetFieldValue<ulong>(0));

        return result;
    }

    public async Task SetBanAsync(ulong steamId, DateTime banUntil)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO jbf_ct_bans (steam_id, ban_until)
            VALUES (@steamId, @banUntil)
            ON DUPLICATE KEY UPDATE ban_until = @banUntil;
            """;
        command.Parameters.AddWithValue("@steamId", steamId);
        command.Parameters.AddWithValue("@banUntil", banUntil);
        await command.ExecuteNonQueryAsync();
    }

    public async Task RemoveBanAsync(ulong steamId)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM jbf_ct_bans WHERE steam_id = @steamId;";
        command.Parameters.AddWithValue("@steamId", steamId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task GrantAccessAsync(ulong steamId)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO jbf_ct_access (steam_id, passed_at)
            VALUES (@steamId, UTC_TIMESTAMP())
            ON DUPLICATE KEY UPDATE passed_at = passed_at;
            """;
        command.Parameters.AddWithValue("@steamId", steamId);
        await command.ExecuteNonQueryAsync();
    }
}
