using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Data.Repositories;

public sealed partial class LibraryRepository
{
    public async Task RefreshLatestDiscoveryScanAuditAsync(long sourceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();

        var run = connection.CreateCommand();
        run.Transaction = tx;
        run.CommandText = "SELECT id FROM discovery_scan_runs WHERE library_source_id=$source ORDER BY id DESC LIMIT 1;";
        run.Parameters.AddWithValue("$source", sourceId);
        var runValue = await run.ExecuteScalarAsync(cancellationToken);
        if (runValue is null || runValue is DBNull)
        {
            await tx.CommitAsync(cancellationToken);
            return;
        }
        var runId = Convert.ToInt64(runValue);

        var counts = connection.CreateCommand();
        counts.Transaction = tx;
        counts.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=$source),
                (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=$source AND r.is_available=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.catalogue_lane IN ('Learning','Both')),
                (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=$source AND r.is_available=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.catalogue_lane IN ('Learning','Both') AND r.discovery_decision='Include'),
                (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=$source AND r.is_available=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.catalogue_lane IN ('Learning','Both') AND r.discovery_decision='Review'),
                (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=$source AND r.is_available=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.catalogue_lane='ProjectArtifact'),
                (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=$source AND r.is_available=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.catalogue_lane='Both'),
                (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=$source AND r.is_policy_excluded=1),
                (SELECT COUNT(*) FROM discovery_exclusions de WHERE de.library_source_id=$source),
                (SELECT COUNT(*) FROM learning_collection_candidates lc WHERE lc.library_source_id=$source),
                (SELECT COUNT(*) FROM (
                    SELECT r.relative_path FROM resources r
                    WHERE r.library_source_id=$source AND EXISTS (
                        SELECT 1 FROM learning_collection_candidates lc WHERE lc.library_source_id=r.library_source_id
                        AND (r.relative_path=lc.root_relative_path OR substr(r.relative_path,1,length(lc.root_relative_path)+1)=lc.root_relative_path||'/'))
                    UNION
                    SELECT de.relative_path FROM discovery_exclusions de
                    WHERE de.library_source_id=$source AND EXISTS (
                        SELECT 1 FROM learning_collection_candidates lc WHERE lc.library_source_id=de.library_source_id
                        AND (de.relative_path=lc.root_relative_path OR substr(de.relative_path,1,length(lc.root_relative_path)+1)=lc.root_relative_path||'/'))
                ));
            """;
        counts.Parameters.AddWithValue("$source", sourceId);
        await using var reader = await counts.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        int I(int ordinal) => reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetInt64(ordinal));

        var update = connection.CreateCommand();
        update.Transaction = tx;
        update.CommandText = """
            UPDATE discovery_scan_runs
            SET catalogue_rows_after=$catalogue,
                learning_catalogue_rows_after=$learningCatalogue,
                included_after=$included,
                review_after=$review,
                project_artifact_rows_after=$projectArtifacts,
                dual_lane_rows_after=$dualLane,
                excluded_after=$excluded,
                exclusion_records_after=$exclusions,
                learning_collections_after=$collections,
                learning_collection_items_after=$items
            WHERE id=$run;
            """;
        update.Parameters.AddWithValue("$catalogue", I(0));
        update.Parameters.AddWithValue("$learningCatalogue", I(1));
        update.Parameters.AddWithValue("$included", I(2));
        update.Parameters.AddWithValue("$review", I(3));
        update.Parameters.AddWithValue("$projectArtifacts", I(4));
        update.Parameters.AddWithValue("$dualLane", I(5));
        update.Parameters.AddWithValue("$excluded", I(6));
        update.Parameters.AddWithValue("$exclusions", I(7));
        update.Parameters.AddWithValue("$collections", I(8));
        update.Parameters.AddWithValue("$items", I(9));
        update.Parameters.AddWithValue("$run", runId);
        await update.ExecuteNonQueryAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<DiscoveryScanAuditReport?> GetLatestDiscoveryScanAuditAsync(long sourceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT dsr.id, dsr.library_source_id, ls.name, dsr.status, dsr.started_utc, dsr.completed_utc,
                   dsr.files_inspected, dsr.resources_seen, dsr.policy_excluded_files, dsr.hard_noise_skipped,
                   dsr.persistent_exclusions, dsr.unsupported_files_skipped, dsr.directories_visited,
                   dsr.directories_skipped, dsr.infrastructure_directories_skipped,
                   dsr.context_directories_skipped, dsr.pruned_branch_count, dsr.pruned_catalogue_rows,
                   dsr.reconciled_leakage_removed, dsr.protected_leakage_rows,
                   dsr.policy_restored_resources, dsr.context_role_suggestions, dsr.low_value_project_noise_skipped, dsr.error_count,
                   dsr.new_resources, dsr.modified_resources, dsr.unchanged_resources, dsr.missing_resources,
                   dsr.analysis_needed, dsr.catalogue_rows_after, dsr.learning_catalogue_rows_after, dsr.included_after, dsr.review_after,
                   dsr.project_artifact_rows_after, dsr.dual_lane_rows_after, dsr.excluded_after, dsr.exclusion_records_after, dsr.learning_collections_after,
                   dsr.learning_collection_items_after
            FROM discovery_scan_runs dsr
            JOIN library_sources ls ON ls.id=dsr.library_source_id
            WHERE dsr.library_source_id=$source
            ORDER BY dsr.id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$source", sourceId);

        long runId;
        long librarySourceId;
        string sourceName;
        string status;
        DateTimeOffset startedUtc;
        DateTimeOffset? completedUtc;
        int[] values;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            runId = reader.GetInt64(0);
            librarySourceId = reader.GetInt64(1);
            sourceName = reader.GetString(2);
            status = reader.GetString(3);
            startedUtc = DateTimeOffset.Parse(reader.GetString(4));
            completedUtc = reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5));
            values = Enumerable.Range(6, 33)
                .Select(i => reader.IsDBNull(i) ? 0 : Convert.ToInt32(reader.GetInt64(i)))
                .ToArray();
        }

        var prunedCommand = connection.CreateCommand();
        prunedCommand.CommandText = """
            SELECT id,discovery_scan_run_id,library_source_id,relative_path,reason,
                   existing_catalogue_rows,safely_removed_rows,protected_rows,created_utc
            FROM discovery_scan_pruned_branches
            WHERE discovery_scan_run_id=$run
            ORDER BY relative_path COLLATE NOCASE
            LIMIT 100;
            """;
        prunedCommand.Parameters.AddWithValue("$run", runId);
        var prunedBranches = new List<DiscoveryPrunedBranchAuditItem>();
        await using (var prunedReader = await prunedCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await prunedReader.ReadAsync(cancellationToken))
            {
                prunedBranches.Add(new DiscoveryPrunedBranchAuditItem(
                    prunedReader.GetInt64(0), prunedReader.GetInt64(1), prunedReader.GetInt64(2),
                    prunedReader.GetString(3), prunedReader.GetString(4),
                    Convert.ToInt32(prunedReader.GetInt64(5)), Convert.ToInt32(prunedReader.GetInt64(6)),
                    Convert.ToInt32(prunedReader.GetInt64(7)), DateTimeOffset.Parse(prunedReader.GetString(8))));
            }
        }

        // values[] corresponds to SELECT columns 6..38 above.
        int V(int index) => values[index];
        var notes = new List<string>();
        if (V(3) > 0) notes.Add($"{V(3):N0} file-level software/config/web/unsupported item(s) were skipped without creating noise-only catalogue rows.");
        if (V(10) > 0) notes.Add($"{V(10):N0} context branch(es) were pruned before file enumeration. CareerBuilder intentionally does not count every file inside them because doing so would defeat branch pruning.");
        if (V(11) > 0) notes.Add($"{V(11):N0} existing catalogue row(s) were found beneath pruned branches.");
        if (V(12) > 0) notes.Add($"{V(12):N0} scanner-created leakage row(s) from the preceding rescan were safely removed from CareerBuilder only; original files were untouched.");
        if (V(13) > 0) notes.Add($"{V(13):N0} existing/protected row(s) were retained rather than deleted and were either preserved by an explicit rule or suppressed from active learning.");
        if (V(14) > 0) notes.Add($"{V(14):N0} previously excluded resource(s) were restored by the refined project/reference context policy.");
        if (V(15) > 0) notes.Add($"{V(15):N0} resource-role suggestion(s) were added from path/media context only; these are suggestions, not content-confirmed truth.");
        if (V(16) > 0) notes.Add($"{V(16):N0} low-value project implementation/dependency item(s) were skipped rather than entering Learning Review or the Project Artifact index.");
        if (V(17) > 0) notes.Add($"The rescan completed with {V(17):N0} filesystem access/error item(s); missing-file reconciliation is conservative when scan errors occur.");
        if (V(18) + V(19) + V(21) + V(12) + V(14) > 0) notes.Add("Use What changed to inspect new, modified, missing, newly excluded, restored, lane-changed or reconciled catalogue items from this exact rescan.");
        if (V(26) > 0) notes.Add($"{V(26):N0} learning resource(s) still need an Include/Exclude decision before learning deep scanning.");
        if (V(27) > 0) notes.Add($"{V(27):N0} project artifact(s) are tracked separately and are not automatically sent through learning deep analysis.");

        return new DiscoveryScanAuditReport(
            runId, librarySourceId, sourceName, status, startedUtc, completedUtc,
            V(0), V(1), V(2), V(3), V(4), V(5), V(6), V(7), V(8), V(9), V(10), V(11), V(12), V(13), V(14), V(15), V(16), V(17),
            V(18), V(19), V(20), V(21), V(22), V(23), V(24), V(25), V(26), V(27), V(28), V(29), V(30), V(31), V(32),
            prunedBranches, notes);
    }

    public async Task<IReadOnlyList<DiscoveryScanChangeItem>> GetLatestDiscoveryScanChangesAsync(
        long sourceId, string changeType = "", int limit = 500, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 1000);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT dsc.id, dsc.discovery_scan_run_id, dsc.library_source_id, dsc.relative_path, dsc.file_name,
                   dsc.change_type, dsc.discovery_decision, dsc.discovery_reason, dsc.catalogue_lane, dsc.lane_reason, dsc.created_utc
            FROM discovery_scan_changes dsc
            WHERE dsc.discovery_scan_run_id=(
                SELECT id FROM discovery_scan_runs WHERE library_source_id=$source ORDER BY id DESC LIMIT 1)
              AND ($change='' OR dsc.change_type=$change)
            ORDER BY CASE dsc.change_type
                WHEN 'New' THEN 1 WHEN 'Modified' THEN 2 WHEN 'Missing' THEN 3
                WHEN 'NewlyExcluded' THEN 4 WHEN 'ReconciledRemoved' THEN 5
                WHEN 'Restored' THEN 6 WHEN 'LaneChanged' THEN 7 ELSE 8 END,
                dsc.relative_path COLLATE NOCASE
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$source", sourceId);
        command.Parameters.AddWithValue("$change", changeType?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<DiscoveryScanChangeItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DiscoveryScanChangeItem(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), DateTimeOffset.Parse(reader.GetString(10))));
        }
        return result;
    }
}
