namespace WICareerBuilder.Desktop.Infrastructure;

public static class SimpleLog
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            AppPaths.Ensure();
            lock (Gate)
            {
                File.AppendAllText(Path.Combine(AppPaths.Logs, "WICareerBuilder.log"),
                    $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch { /* logging must never stop the application */ }
    }
}
