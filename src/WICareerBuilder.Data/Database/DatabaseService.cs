using System.Reflection;
using Microsoft.Data.Sqlite;

namespace WICareerBuilder.Data.Database;

public sealed class DatabaseService
{
    private readonly string _databasePath;

    public DatabaseService(string databasePath) => _databasePath = databasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var createMigrations = connection.CreateCommand();
        createMigrations.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                migration_name TEXT PRIMARY KEY,
                applied_utc TEXT NOT NULL
            );
            """;
        await createMigrations.ExecuteNonQueryAsync(cancellationToken);

        var assembly = typeof(DatabaseService).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(n => n.Contains(".Migrations.", StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        foreach (var resourceName in resources)
        {
            var migrationName = resourceName[(resourceName.LastIndexOf(".Migrations.", StringComparison.Ordinal) + 12)..];
            if (await IsAppliedAsync(connection, migrationName, cancellationToken)) continue;

            await using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Missing migration resource: {resourceName}");
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync(cancellationToken);

            using var transaction = connection.BeginTransaction();
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);

            var record = connection.CreateCommand();
            record.Transaction = transaction;
            record.CommandText = "INSERT INTO schema_migrations(migration_name, applied_utc) VALUES ($name, $utc);";
            record.Parameters.AddWithValue("$name", migrationName);
            record.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
            await record.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadWriteCreate;Cache=Shared");
        await connection.OpenAsync(cancellationToken);
        var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task<bool> IsAppliedAsync(SqliteConnection connection, string migrationName, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM schema_migrations WHERE migration_name = $name LIMIT 1;";
        command.Parameters.AddWithValue("$name", migrationName);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }
}
