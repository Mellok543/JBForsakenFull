namespace JBF.Shop.Services;

internal static class ShopConfigPath
{
    public static string Get(string moduleDirectory)
    {
        var directory = new DirectoryInfo(moduleDirectory);
        while (directory is not null &&
               !string.Equals(directory.Name, "counterstrikesharp", StringComparison.OrdinalIgnoreCase))
        {
            directory = directory.Parent;
        }

        var configsDirectory = directory is not null
            ? Path.Combine(directory.FullName, "configs", "plugins")
            : Path.GetFullPath(Path.Combine(moduleDirectory, "..", "..", "configs", "plugins"));

        return Path.Combine(configsDirectory, "JBF.Shop.json");
    }
}
