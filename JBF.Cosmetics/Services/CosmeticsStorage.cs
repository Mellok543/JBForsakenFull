using JBF.Api;
using JBF.Cosmetics.Models;
using MySqlConnector;

namespace JBF.Cosmetics.Services;

internal sealed class CosmeticsStorage
{
    private readonly string _connectionString;
    private readonly string _ownedTable;
    private readonly string _equipTable;

    public CosmeticsStorage(CosmeticsDatabaseConfig config)
    {
        _ownedTable = $"{config.TablePrefix}player_cosmetics";
        _equipTable = $"{config.TablePrefix}player_cosmetic_equips";
        _connectionString = new MySqlConnectionStringBuilder
        {
            Server = config.Host,
            Port = uint.TryParse(config.Port, out var port) ? port : 3306,
            Database = config.Database,
            UserID = config.User,
            Password = config.Password,
            CharacterSet = "utf8mb4"
        }.ConnectionString;
        EnsureSchema();
    }

    public (Dictionary<ulong, HashSet<string>> Owned, Dictionary<ulong, Dictionary<CosmeticCategory, string>> Equipped) LoadAll(Action<string>? log)
    {
        var owned = new Dictionary<ulong, HashSet<string>>();
        var equipped = new Dictionary<ulong, Dictionary<CosmeticCategory, string>>();
        try
        {
            using var db = new MySqlConnection(_connectionString);
            db.Open();

            using (var cmd = new MySqlCommand($"SELECT steam_id, cosmetic_id FROM `{_ownedTable}`", db))
            using (var reader = cmd.ExecuteReader())
                while (reader.Read())
                {
                    var steam = reader.GetUInt64(0);
                    if (!owned.TryGetValue(steam, out var set)) owned[steam] = set = new(StringComparer.OrdinalIgnoreCase);
                    set.Add(reader.GetString(1));
                }

            using (var cmd = new MySqlCommand($"SELECT steam_id, category, cosmetic_id FROM `{_equipTable}`", db))
            using (var reader = cmd.ExecuteReader())
                while (reader.Read())
                {
                    var steam = reader.GetUInt64(0);
                    if (!Enum.TryParse<CosmeticCategory>(reader.GetString(1), true, out var category)) continue;
                    if (!equipped.TryGetValue(steam, out var map)) equipped[steam] = map = [];
                    map[category] = reader.GetString(2);
                }
        }
        catch (Exception ex) { log?.Invoke($"Cosmetics load failed: {ex.Message}"); }
        return (owned, equipped);
    }

    public bool SaveGrant(ulong steamId, string cosmeticId, string source, Action<string>? log)
    {
        try
        {
            using var db = new MySqlConnection(_connectionString);
            db.Open();
            using var cmd = new MySqlCommand($@"INSERT IGNORE INTO `{_ownedTable}`
(steam_id, cosmetic_id, source) VALUES (@steam, @id, @source)", db);
            cmd.Parameters.AddWithValue("@steam", steamId);
            cmd.Parameters.AddWithValue("@id", cosmeticId);
            cmd.Parameters.AddWithValue("@source", source);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex) { log?.Invoke($"Cosmetics grant save failed: {ex.Message}"); return false; }
    }

    public bool SaveEquip(ulong steamId, CosmeticCategory category, string? cosmeticId, Action<string>? log)
    {
        try
        {
            using var db = new MySqlConnection(_connectionString);
            db.Open();
            if (cosmeticId is null)
            {
                using var remove = new MySqlCommand($"DELETE FROM `{_equipTable}` WHERE steam_id=@steam AND category=@category", db);
                remove.Parameters.AddWithValue("@steam", steamId);
                remove.Parameters.AddWithValue("@category", category.ToString());
                remove.ExecuteNonQuery();
            }
            else
            {
                using var cmd = new MySqlCommand($@"INSERT INTO `{_equipTable}`
(steam_id, category, cosmetic_id) VALUES (@steam, @category, @id)
ON DUPLICATE KEY UPDATE cosmetic_id=VALUES(cosmetic_id)", db);
                cmd.Parameters.AddWithValue("@steam", steamId);
                cmd.Parameters.AddWithValue("@category", category.ToString());
                cmd.Parameters.AddWithValue("@id", cosmeticId);
                cmd.ExecuteNonQuery();
            }
            return true;
        }
        catch (Exception ex) { log?.Invoke($"Cosmetics equip save failed: {ex.Message}"); return false; }
    }

    private void EnsureSchema()
    {
        using var db = new MySqlConnection(_connectionString);
        db.Open();
        using var cmd = new MySqlCommand($@"
CREATE TABLE IF NOT EXISTS `{_ownedTable}` (
 steam_id BIGINT UNSIGNED NOT NULL,
 cosmetic_id VARCHAR(96) NOT NULL,
 source VARCHAR(32) NOT NULL,
 acquired_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
 PRIMARY KEY (steam_id, cosmetic_id)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS `{_equipTable}` (
 steam_id BIGINT UNSIGNED NOT NULL,
 category VARCHAR(32) NOT NULL,
 cosmetic_id VARCHAR(96) NOT NULL,
 updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
 PRIMARY KEY (steam_id, category)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;", db);
        cmd.ExecuteNonQuery();
    }
}
