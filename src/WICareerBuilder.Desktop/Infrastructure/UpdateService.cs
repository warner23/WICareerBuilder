using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace WICareerBuilder.Desktop.Infrastructure;

public sealed class UpdateService
{
    public object GetStatus(string currentVersion) => new
    {
        currentVersion,
        isInstalledBuild = AppPaths.IsInstalledBuild,
        installDirectory = AppPaths.InstallDirectory,
        dataDirectory = AppPaths.Root,
        onlineFeedConfigured = false,
        message = AppPaths.IsInstalledBuild
            ? "Manual update packages are ready. Online update-feed checking is not configured yet."
            : "You are running a development/source build. Build/install first; installed builds can apply update packages from this screen."
    };

    public async Task<object> StagePackageAsync(string packagePath, string currentVersion, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath)) throw new FileNotFoundException("Update package not found.", packagePath);
        AppPaths.Ensure();
        using var archive = ZipFile.OpenRead(packagePath);
        var manifestEntry = archive.GetEntry("update-manifest.json") ?? throw new InvalidOperationException("Invalid update package: update-manifest.json is missing.");
        var exeEntry = archive.GetEntry("WICareerBuilder.exe") ?? throw new InvalidOperationException("Invalid update package: WICareerBuilder.exe is missing.");
        var versionEntry = archive.GetEntry("VERSION") ?? throw new InvalidOperationException("Invalid update package: VERSION is missing.");

        string targetVersion;
        await using (var stream = versionEntry.Open())
        using (var reader = new StreamReader(stream))
            targetVersion = (await reader.ReadToEndAsync(cancellationToken)).Trim();

        string? manifestVersion;
        await using (var stream = manifestEntry.Open())
        using (var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken))
        {
            var root = doc.RootElement;
            var product = root.TryGetProperty("product", out var p) ? p.GetString() : null;
            manifestVersion = root.TryGetProperty("version", out var mv) ? mv.GetString() : null;
            if (!string.Equals(product, "WICareerBuilder", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This package is not a WICareerBuilder update.");
        }

        if (!string.Equals(manifestVersion?.Trim(), targetVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Update package VERSION does not match update-manifest.json.");
        if (!Version.TryParse(targetVersion, out var target)) throw new InvalidOperationException("Update package has an invalid version.");
        Version.TryParse(currentVersion, out var current);
        if (current is not null && target <= current)
            throw new InvalidOperationException($"Package v{targetVersion} is not newer than the current v{currentVersion}.");

        var stage = Path.Combine(AppPaths.Updates, "Staged", targetVersion);
        if (Directory.Exists(stage)) Directory.Delete(stage, true);
        Directory.CreateDirectory(stage);
        ZipFile.ExtractToDirectory(packagePath, stage, true);
        var stagedPackage = Path.Combine(AppPaths.Updates, Path.GetFileName(packagePath));
        File.Copy(packagePath, stagedPackage, true);

        var backupRoot = Path.Combine(AppPaths.Backups, "Updates", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-pre-v" + targetVersion);
        Directory.CreateDirectory(Path.Combine(backupRoot, "data"));
        if (File.Exists(AppPaths.DatabaseFile))
            BackupSqliteDatabase(AppPaths.DatabaseFile, Path.Combine(backupRoot, "data", "WICareerBuilder.db"));

        string? script = null;
        if (AppPaths.IsInstalledBuild)
        {
            script = BuildApplyScript(stage, backupRoot, targetVersion);
        }

        return new
        {
            staged = true,
            currentVersion,
            targetVersion,
            packagePath = stagedPackage,
            stageDirectory = stage,
            backupDirectory = backupRoot,
            canApply = AppPaths.IsInstalledBuild,
            applyScript = script,
            message = AppPaths.IsInstalledBuild
                ? $"v{targetVersion} is validated and staged. Apply & restart will back up the installed app, run database migrations, and roll back if migration fails."
                : $"v{targetVersion} is validated and staged, but this session is running from source. Install WICareerBuilder first, then use Update Centre from the installed app."
        };
    }

    public void ApplyStagedUpdate(string scriptPath)
    {
        if (!AppPaths.IsInstalledBuild) throw new InvalidOperationException("Automatic apply is available only from the installed WICareerBuilder build.");
        var full = Path.GetFullPath(scriptPath);
        if (!full.StartsWith(Path.GetFullPath(AppPaths.Updates), StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            throw new InvalidOperationException("Staged update script is invalid.");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{full}\"")
        {
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }


    private static void BackupSqliteDatabase(string sourcePath, string destinationPath)
    {
        // DatabaseService uses WAL mode. A plain file copy while the app is running can miss
        // committed pages that still live in the WAL, so use SQLite's online backup API.
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        if (File.Exists(destinationPath)) File.Delete(destinationPath);
        using var source = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly;Cache=Shared");
        using var destination = new SqliteConnection($"Data Source={destinationPath};Mode=ReadWriteCreate");
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    private static string BuildApplyScript(string stage, string backupRoot, string targetVersion)
    {
        var script = Path.Combine(stage, "apply-update.cmd");
        var install = AppPaths.InstallDirectory;
        var appBackup = Path.Combine(backupRoot, "app");
        Directory.CreateDirectory(appBackup);
        var dbBackup = Path.Combine(backupRoot, "data", "WICareerBuilder.db");
        var lines = new[]
        {
            "@echo off",
            "setlocal",
            "timeout /t 2 /nobreak >nul",
            $"set \"INSTALL={install}\"",
            $"set \"STAGE={stage}\"",
            $"set \"BACKUP={appBackup}\"",
            $"set \"DB={AppPaths.DatabaseFile}\"",
            $"set \"DBBACKUP={dbBackup}\"",
            "if not exist \"%INSTALL%\" exit /b 2",
            "robocopy \"%INSTALL%\" \"%BACKUP%\" /MIR /R:1 /W:1 >nul",
            "robocopy \"%STAGE%\" \"%INSTALL%\" /MIR /XF apply-update.cmd /R:1 /W:1 >nul",
            "\"%INSTALL%\\WICareerBuilder.exe\" --init-db",
            "if errorlevel 1 goto rollback",
            "start \"\" \"%INSTALL%\\WICareerBuilder.exe\"",
            "exit /b 0",
            ":rollback",
            "robocopy \"%BACKUP%\" \"%INSTALL%\" /MIR /R:1 /W:1 >nul",
            "if exist \"%DBBACKUP%\" copy /Y \"%DBBACKUP%\" \"%DB%\" >nul",
            "start \"\" \"%INSTALL%\\WICareerBuilder.exe\"",
            "exit /b 1"
        };
        File.WriteAllLines(script, lines);
        return script;
    }
}
