namespace JBF.Shop.Models;

internal sealed class ShopDatabaseConfig
{
    public string Host { get; set; } = "127.0.0.1";

    public string Database { get; set; } = "jbf";

    public string User { get; set; } = "root";

    public string Password { get; set; } = "";

    public string Port { get; set; } = "3306";

    public string TablePrefix { get; set; } = "jbf_";
}
