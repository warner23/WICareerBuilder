namespace WICareerBuilder.Desktop.Infrastructure;

public static class AppPaths
{
    private static string? _rootOverride;
    private static bool _demoMode;

    public static bool DemoMode => _demoMode;

    public static void ConfigureFromArgs(IEnumerable<string> args)
    {
        var values = args.ToArray();
        if (values.Contains("--demo", StringComparer.OrdinalIgnoreCase) ||
            values.Contains("--demo-seed", StringComparer.OrdinalIgnoreCase) ||
            values.Contains("--capture-product-media", StringComparer.OrdinalIgnoreCase) ||
            values.Contains("--demo-tour", StringComparer.OrdinalIgnoreCase))
        {
            _demoMode = true;
            _rootOverride = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WICareerBuilder-Demo");
        }
    }

    public static string Root => _rootOverride
        ?? Environment.GetEnvironmentVariable("WICAREERBUILDER_DATA_ROOT")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WICareerBuilder");
    public static string Data => Path.Combine(Root, "Data");
    public static string Logs => Path.Combine(Root, "Logs");
    public static string Backups => Path.Combine(Root, "Backups");
    public static string Updates => Path.Combine(Root, "Updates");
    public static string DatabaseFile => Path.Combine(Data, "WICareerBuilder.db");
    public static string InstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WICareerBuilder");

    public static bool IsInstalledBuild
    {
        get
        {
            var current = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var installed = Path.GetFullPath(InstallDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return current.Equals(installed, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Ensure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Backups);
        Directory.CreateDirectory(Updates);
    }
}
