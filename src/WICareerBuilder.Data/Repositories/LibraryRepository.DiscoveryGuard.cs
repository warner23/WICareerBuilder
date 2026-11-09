using Microsoft.Data.Sqlite;
using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Data.Repositories;

public sealed partial class LibraryRepository
{
    public async Task<IReadOnlyList<string>> GetDiscoveryProtectedPathsAsync(long sourceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT root_relative_path
            FROM learning_collection_candidates
            WHERE library_source_id=$source
            UNION
            SELECT relative_path
            FROM discovery_overrides
            WHERE library_source_id=$source AND action='Include'
            ORDER BY 1 COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$source", sourceId);
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var value = reader.GetString(0).Trim();
            if (value.Length > 0) result.Add(value);
        }
        return result;
    }


    private static async Task<int> UpsertDiscoveryRoleSuggestionAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        long sourceId,
        string relativePath,
        string roleKey,
        string now,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT OR IGNORE INTO resource_role_assignments(
                resource_id,role_id,assignment_source,confidence,status,notes,created_utc,updated_utc)
            SELECT r.id,rr.id,'v0.8.5 discovery lane',0.68,'Suggested',
                   'Suggested from discovery lane/path/media context only; content analysis has not confirmed this role.',
                   $now,$now
            FROM resources r
            JOIN resource_roles rr ON rr.role_key=$role
            WHERE r.library_source_id=$source AND r.relative_path=$relative;
            """;
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$role", roleKey);
        command.Parameters.AddWithValue("$source", sourceId);
        command.Parameters.AddWithValue("$relative", relativePath);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> GetPreviousDiscoveryScanRunIdAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        long sourceId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT id
            FROM discovery_scan_runs
            WHERE library_source_id=$source
              AND status IN ('Completed','CompletedWithWarnings')
            ORDER BY id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$source", sourceId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }

    private static async Task<bool> WasNewInScanAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        long scanRunId,
        long sourceId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        if (scanRunId <= 0) return false;
        var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT 1
            FROM discovery_scan_changes
            WHERE discovery_scan_run_id=$run
              AND library_source_id=$source
              AND relative_path=$relative
              AND change_type='New'
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$run", scanRunId);
        command.Parameters.AddWithValue("$source", sourceId);
        command.Parameters.AddWithValue("$relative", relativePath);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    // Resource_subjects and resource_relationships are intentionally not protection signals here:
    // both can be generated automatically by discovery/relationship refresh. Everything below is
    // user-authored, analysis-derived, learning-composition or workbench metadata that we must not
    // silently discard just because a later policy is stricter.
    private static async Task<bool> HasProtectedResourceDataAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        long resourceId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT CASE WHEN
                EXISTS(SELECT 1 FROM resource_organisation WHERE resource_id=$id)
                OR EXISTS(SELECT 1 FROM resource_analysis WHERE resource_id=$id)
                OR EXISTS(SELECT 1 FROM resource_knowledge_links WHERE resource_id=$id)
                OR EXISTS(SELECT 1 FROM resource_role_assignments WHERE resource_id=$id AND NOT (status='Suggested' AND assignment_source IN ('v0.8.4 discovery context','v0.8.5 discovery lane')))
                OR EXISTS(SELECT 1 FROM source_analysis_runs WHERE resource_id=$id)
                OR EXISTS(SELECT 1 FROM source_assets WHERE resource_id=$id)
                OR EXISTS(SELECT 1 FROM learning_source_refs WHERE resource_id=$id)
                OR EXISTS(SELECT 1 FROM learning_module_sources WHERE resource_id=$id)
                OR EXISTS(SELECT 1 FROM learning_candidate_sources WHERE resource_id=$id)
                OR EXISTS(SELECT 1 FROM output_suggestion_sources WHERE resource_id=$id)
                OR EXISTS(SELECT 1 FROM learning_modules WHERE primary_resource_id=$id)
            THEN 1 ELSE 0 END;
            """;
        command.Parameters.AddWithValue("$id", resourceId);
        return Convert.ToInt32((long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L)) == 1;
    }

    private static async Task<bool> IsLearningCollectionManagedPathAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        long sourceId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT 1
            FROM learning_collection_candidates lc
            WHERE lc.library_source_id=$source
              AND ($relative=lc.root_relative_path OR substr($relative,1,length(lc.root_relative_path)+1)=lc.root_relative_path||'/')
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$source", sourceId);
        command.Parameters.AddWithValue("$relative", relativePath);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task DeleteScannerLeakageResourceAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        long scanRunId,
        long sourceId,
        long resourceId,
        string relativePath,
        string fileName,
        string reason,
        string now,
        CancellationToken cancellationToken)
    {
        var deleteExclusion = connection.CreateCommand();
        deleteExclusion.Transaction = tx;
        deleteExclusion.CommandText = "DELETE FROM discovery_exclusions WHERE library_source_id=$source AND relative_path=$relative;";
        deleteExclusion.Parameters.AddWithValue("$source", sourceId);
        deleteExclusion.Parameters.AddWithValue("$relative", relativePath);
        await deleteExclusion.ExecuteNonQueryAsync(cancellationToken);

        var deleteResource = connection.CreateCommand();
        deleteResource.Transaction = tx;
        deleteResource.CommandText = "DELETE FROM resources WHERE id=$id;";
        deleteResource.Parameters.AddWithValue("$id", resourceId);
        await deleteResource.ExecuteNonQueryAsync(cancellationToken);

        await InsertDiscoveryScanChangeAsync(
            connection, tx, scanRunId, sourceId, relativePath, fileName,
            "ReconciledRemoved", "Exclude", reason, "None", reason, now, cancellationToken);

        var audit = connection.CreateCommand();
        audit.Transaction = tx;
        audit.CommandText = """
            INSERT INTO discovery_reconciliation_log(
                discovery_scan_run_id,library_source_id,relative_path,action,reason,created_utc)
            VALUES($run,$source,$relative,'RemovedScannerLeakage',$reason,$now);
            """;
        audit.Parameters.AddWithValue("$run", scanRunId);
        audit.Parameters.AddWithValue("$source", sourceId);
        audit.Parameters.AddWithValue("$relative", relativePath);
        audit.Parameters.AddWithValue("$reason", reason);
        audit.Parameters.AddWithValue("$now", now);
        await audit.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task LogRetainedLeakageAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        long scanRunId,
        long sourceId,
        string relativePath,
        string action,
        string reason,
        string now,
        CancellationToken cancellationToken)
    {
        var audit = connection.CreateCommand();
        audit.Transaction = tx;
        audit.CommandText = """
            INSERT INTO discovery_reconciliation_log(
                discovery_scan_run_id,library_source_id,relative_path,action,reason,created_utc)
            VALUES($run,$source,$relative,$action,$reason,$now);
            """;
        audit.Parameters.AddWithValue("$run", scanRunId);
        audit.Parameters.AddWithValue("$source", sourceId);
        audit.Parameters.AddWithValue("$relative", relativePath);
        audit.Parameters.AddWithValue("$action", action);
        audit.Parameters.AddWithValue("$reason", reason);
        audit.Parameters.AddWithValue("$now", now);
        await audit.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(int CatalogueRows, int RemovedRows, int RetainedRows)> ReconcilePrunedBranchesAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        LibrarySource source,
        long scanRunId,
        long previousScanRunId,
        IReadOnlyList<PrunedDiscoveryDirectory> prunedDirectories,
        IReadOnlyDictionary<string, string> savedOverrides,
        string scanToken,
        string now,
        CancellationToken cancellationToken)
    {
        var totalCatalogueRows = 0;
        var totalRemoved = 0;
        var totalRetained = 0;

        foreach (var branch in prunedDirectories)
        {
            var relative = branch.RelativePath.Trim('/');
            if (relative.Length == 0) continue;
            var prefix = relative + "/";

            var read = connection.CreateCommand();
            read.Transaction = tx;
            read.CommandText = """
                SELECT id,relative_path,file_name,extension,is_policy_excluded,discovery_decision
                FROM resources
                WHERE library_source_id=$source
                  AND (relative_path=$relative OR substr(relative_path,1,length($prefix))=$prefix)
                ORDER BY relative_path COLLATE NOCASE;
                """;
            read.Parameters.AddWithValue("$source", source.Id);
            read.Parameters.AddWithValue("$relative", relative);
            read.Parameters.AddWithValue("$prefix", prefix);

            var rows = new List<(long Id,string RelativePath,string FileName,string Extension,bool WasExcluded,string Decision)>();
            await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add((
                        reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                        reader.GetInt64(4) == 1, reader.GetString(5)));
                }
            }

            var removed = 0;
            var retained = 0;
            totalCatalogueRows += rows.Count;

            foreach (var row in rows)
            {
                if (savedOverrides.TryGetValue(row.RelativePath, out var overrideAction) &&
                    overrideAction.Equals("Include", StringComparison.OrdinalIgnoreCase))
                {
                    retained++;
                    await LogRetainedLeakageAsync(connection, tx, scanRunId, source.Id, row.RelativePath,
                        "ManualIncludeProtected", branch.Reason, now, cancellationToken);
                    continue;
                }

                if (await IsLearningCollectionManagedPathAsync(connection, tx, source.Id, row.RelativePath, cancellationToken))
                {
                    retained++;
                    var touchCollectionRow = connection.CreateCommand();
                    touchCollectionRow.Transaction = tx;
                    touchCollectionRow.CommandText = "UPDATE resources SET last_scan_token=$token,last_seen_utc=$now WHERE id=$id;";
                    touchCollectionRow.Parameters.AddWithValue("$token", scanToken);
                    touchCollectionRow.Parameters.AddWithValue("$now", now);
                    touchCollectionRow.Parameters.AddWithValue("$id", row.Id);
                    await touchCollectionRow.ExecuteNonQueryAsync(cancellationToken);
                    await LogRetainedLeakageAsync(connection, tx, scanRunId, source.Id, row.RelativePath,
                        "LearningCollectionProtected", branch.Reason, now, cancellationToken);
                    continue;
                }

                var wasPreviousScanNew = await WasNewInScanAsync(
                    connection, tx, previousScanRunId, source.Id, row.RelativePath, cancellationToken);
                var protectedData = await HasProtectedResourceDataAsync(connection, tx, row.Id, cancellationToken);

                if (wasPreviousScanNew && !protectedData && !savedOverrides.ContainsKey(row.RelativePath))
                {
                    await DeleteScannerLeakageResourceAsync(
                        connection, tx, scanRunId, source.Id, row.Id, row.RelativePath, row.FileName,
                        $"Safe reconciliation: {branch.Reason}", now, cancellationToken);
                    removed++;
                    continue;
                }

                retained++;
                var suppress = connection.CreateCommand();
                suppress.Transaction = tx;
                suppress.CommandText = """
                    UPDATE resources
                    SET discovery_status='Excluded',discovery_decision='Exclude',discovery_reason=$reason,
                        is_policy_excluded=1,catalogue_lane='None',catalogue_lane_reason=$reason,last_scan_token=$token,last_seen_utc=$now,
                        fingerprint_status='Deferred',analysis_needed=0
                    WHERE id=$id;
                    """;
                suppress.Parameters.AddWithValue("$reason", branch.Reason);
                suppress.Parameters.AddWithValue("$token", scanToken);
                suppress.Parameters.AddWithValue("$now", now);
                suppress.Parameters.AddWithValue("$id", row.Id);
                await suppress.ExecuteNonQueryAsync(cancellationToken);

                await UpsertDiscoveryExclusionAsync(
                    connection, tx, source.Id, row.RelativePath, row.FileName, row.Extension,
                    branch.Reason, now, cancellationToken);

                if (!row.WasExcluded || !row.Decision.Equals("Exclude", StringComparison.OrdinalIgnoreCase))
                {
                    await InsertDiscoveryScanChangeAsync(
                        connection, tx, scanRunId, source.Id, row.RelativePath, row.FileName,
                        "NewlyExcluded", "Exclude", branch.Reason, "None", branch.Reason, now, cancellationToken);
                }

                await LogRetainedLeakageAsync(connection, tx, scanRunId, source.Id, row.RelativePath,
                    protectedData ? "ProtectedDataRetainedExcluded" : "ExistingCatalogueRetainedExcluded",
                    branch.Reason, now, cancellationToken);
            }

            totalRemoved += removed;
            totalRetained += retained;

            var audit = connection.CreateCommand();
            audit.Transaction = tx;
            audit.CommandText = """
                INSERT INTO discovery_scan_pruned_branches(
                    discovery_scan_run_id,library_source_id,relative_path,reason,
                    existing_catalogue_rows,safely_removed_rows,protected_rows,created_utc)
                VALUES($run,$source,$relative,$reason,$existing,$removed,$retained,$now)
                ON CONFLICT(discovery_scan_run_id,relative_path) DO UPDATE SET
                    reason=excluded.reason,
                    existing_catalogue_rows=excluded.existing_catalogue_rows,
                    safely_removed_rows=excluded.safely_removed_rows,
                    protected_rows=excluded.protected_rows;
                """;
            audit.Parameters.AddWithValue("$run", scanRunId);
            audit.Parameters.AddWithValue("$source", source.Id);
            audit.Parameters.AddWithValue("$relative", relative);
            audit.Parameters.AddWithValue("$reason", branch.Reason);
            audit.Parameters.AddWithValue("$existing", rows.Count);
            audit.Parameters.AddWithValue("$removed", removed);
            audit.Parameters.AddWithValue("$retained", retained);
            audit.Parameters.AddWithValue("$now", now);
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }

        return (totalCatalogueRows, totalRemoved, totalRetained);
    }
}
