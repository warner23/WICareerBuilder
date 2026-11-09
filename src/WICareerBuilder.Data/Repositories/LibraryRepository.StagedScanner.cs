using Microsoft.Data.Sqlite;
using WICareerBuilder.Core.Discovery;
using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Data.Repositories;

public sealed partial class LibraryRepository
{
    public async Task<ScannerSummary> GetScannerSummaryAsync(long? sourceId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var where = sourceId.HasValue ? "AND r.library_source_id = $source" : string.Empty;
        var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                COUNT(*),
                SUM(CASE WHEN r.inventory_state='New' AND r.is_available=1 THEN 1 ELSE 0 END),
                SUM(CASE WHEN r.inventory_state='Modified' AND r.is_available=1 THEN 1 ELSE 0 END),
                SUM(CASE WHEN r.inventory_state='Unchanged' AND r.is_available=1 THEN 1 ELSE 0 END),
                SUM(CASE WHEN r.inventory_state='Missing' THEN 1 ELSE 0 END),
                SUM(CASE WHEN r.analysis_needed=1 AND r.is_available=1 AND lower(r.extension) IN ('.docx','.pptx','.xlsx','.xlsm','.txt','.md','.csv','.xml','.json','.doc','.ppt','.xls') THEN 1 ELSE 0 END),
                SUM(CASE WHEN r.fingerprint_status IN ('Pending','Failed') AND r.is_available=1 THEN 1 ELSE 0 END),
                SUM(CASE WHEN r.fingerprint_status='Complete' AND r.is_available=1 THEN 1 ELSE 0 END),
                (SELECT MAX(ls.last_discovered_utc) FROM library_sources ls WHERE ls.lifecycle_status='Active' {(sourceId.HasValue ? "AND ls.id=$source" : string.Empty)})
            FROM resources r
            JOIN library_sources active_ls ON active_ls.id=r.library_source_id
            WHERE r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include' AND r.catalogue_lane IN ('Learning','Both')
              AND active_ls.is_enabled=1 AND COALESCE(active_ls.lifecycle_status,'Active')='Active' {where};
            """;
        if (sourceId.HasValue) command.Parameters.AddWithValue("$source", sourceId.Value);

        int total = 0, @new = 0, modified = 0, unchanged = 0, missing = 0, analysis = 0, pending = 0, fingerprinted = 0;
        DateTimeOffset? lastScan = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                total = ReadInt(reader, 0);
                @new = ReadInt(reader, 1);
                modified = ReadInt(reader, 2);
                unchanged = ReadInt(reader, 3);
                missing = ReadInt(reader, 4);
                analysis = ReadInt(reader, 5);
                pending = ReadInt(reader, 6);
                fingerprinted = ReadInt(reader, 7);
                if (!reader.IsDBNull(8) && DateTimeOffset.TryParse(reader.GetString(8), out var parsed)) lastScan = parsed;
            }
        }

        var (duplicateGroups, duplicateResources) = await GetDuplicateFingerprintCountsAsync(connection, sourceId, cancellationToken);
        return new ScannerSummary(sourceId, total, @new, modified, unchanged, missing, analysis, pending, fingerprinted, duplicateGroups, duplicateResources, lastScan);
    }

    public async Task<IReadOnlyList<FingerprintCandidate>> GetFingerprintCandidatesAsync(long? sourceId = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 250);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT r.id, r.library_source_id, ls.root_path, r.relative_path, r.size_bytes, r.last_write_utc
            FROM resources r
            JOIN library_sources ls ON ls.id=r.library_source_id
            WHERE r.is_available=1
              AND r.is_policy_excluded=0
              AND r.is_ignored=0
              AND ls.is_enabled=1
              AND COALESCE(ls.lifecycle_status,'Active')='Active'
              AND r.discovery_decision='Include'
              AND r.catalogue_lane IN ('Learning','Both')
              AND r.fingerprint_status IN ('Pending','Failed')
              {(sourceId.HasValue ? "AND r.library_source_id=$source" : string.Empty)}
            ORDER BY CASE r.fingerprint_status WHEN 'Pending' THEN 0 ELSE 1 END,
                     CASE r.inventory_state WHEN 'New' THEN 0 WHEN 'Modified' THEN 1 WHEN 'Existing' THEN 2 ELSE 3 END,
                     r.size_bytes ASC, r.id ASC
            LIMIT $limit;
            """;
        if (sourceId.HasValue) command.Parameters.AddWithValue("$source", sourceId.Value);
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<FingerprintCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new FingerprintCandidate(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4), DateTimeOffset.Parse(reader.GetString(5))));
        }
        return result;
    }

    public async Task SaveQuickFingerprintAsync(long resourceId, string fingerprint, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE resources
            SET quick_fingerprint=$fingerprint,
                fingerprint_kind=$kind,
                fingerprint_status='Complete',
                fingerprinted_utc=$now
            WHERE id=$resource;
            """;
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue("$kind", ContentFingerprintService.FingerprintKind);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$resource", resourceId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkFingerprintFailedAsync(long resourceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE resources SET fingerprint_status='Failed', fingerprinted_utc=$now WHERE id=$resource;";
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$resource", resourceId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<long>> GetStagedAnalysisCandidateIdsAsync(long? sourceId = null, int limit = 50, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT r.id
            FROM resources r
            JOIN library_sources ls ON ls.id=r.library_source_id
            WHERE r.analysis_needed=1
              AND r.is_available=1
              AND r.is_policy_excluded=0
              AND r.is_ignored=0
              AND ls.is_enabled=1
              AND COALESCE(ls.lifecycle_status,'Active')='Active'
              AND r.discovery_decision='Include'
              AND r.catalogue_lane IN ('Learning','Both')
              AND r.extension IN ('.docx','.pptx','.xlsx','.xlsm','.txt','.md','.csv','.xml','.json','.doc','.ppt','.xls')
              {(sourceId.HasValue ? "AND r.library_source_id=$source" : string.Empty)}
            ORDER BY CASE r.inventory_state WHEN 'New' THEN 0 WHEN 'Modified' THEN 1 WHEN 'Existing' THEN 2 ELSE 3 END,
                     r.size_bytes ASC, r.id ASC
            LIMIT $limit;
            """;
        if (sourceId.HasValue) command.Parameters.AddWithValue("$source", sourceId.Value);
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetInt64(0));
        return result;
    }

    public async Task<int> GetStagedAnalysisRemainingAsync(long? sourceId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT(*) FROM resources r
            JOIN library_sources ls ON ls.id=r.library_source_id
            WHERE r.analysis_needed=1 AND r.is_available=1 AND r.is_policy_excluded=0 AND r.is_ignored=0
              AND ls.is_enabled=1 AND COALESCE(ls.lifecycle_status,'Active')='Active'
              AND r.discovery_decision='Include'
              AND r.catalogue_lane IN ('Learning','Both')
              AND r.extension IN ('.docx','.pptx','.xlsx','.xlsm','.txt','.md','.csv','.xml','.json','.doc','.ppt','.xls')
              {(sourceId.HasValue ? "AND r.library_source_id=$source" : string.Empty)};
            """;
        if (sourceId.HasValue) command.Parameters.AddWithValue("$source", sourceId.Value);
        return Convert.ToInt32((long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L));
    }

    private static int ReadInt(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetInt64(ordinal));

    private static async Task<(int Groups, int Resources)> GetDuplicateFingerprintCountsAsync(SqliteConnection connection, long? sourceId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"""
            WITH duplicate_fingerprints AS (
                SELECT quick_fingerprint
                FROM resources
                WHERE quick_fingerprint<>'' AND fingerprint_status='Complete' AND is_available=1 AND is_policy_excluded=0 AND is_ignored=0 AND discovery_decision='Include' AND catalogue_lane IN ('Learning','Both')
                GROUP BY quick_fingerprint
                HAVING COUNT(*)>1
            ), scoped AS (
                SELECT r.quick_fingerprint, r.id
                FROM resources r
                JOIN duplicate_fingerprints d ON d.quick_fingerprint=r.quick_fingerprint
                WHERE r.is_available=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include' AND r.catalogue_lane IN ('Learning','Both')
                  {(sourceId.HasValue ? "AND r.library_source_id=$source" : string.Empty)}
            )
            SELECT COUNT(DISTINCT quick_fingerprint), COUNT(*) FROM scoped;
            """;
        if (sourceId.HasValue) command.Parameters.AddWithValue("$source", sourceId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return (0, 0);
        return (ReadInt(reader, 0), ReadInt(reader, 1));
    }
}
