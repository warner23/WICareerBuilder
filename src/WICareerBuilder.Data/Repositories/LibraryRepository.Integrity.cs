using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Data.Repositories;

public sealed partial class LibraryRepository
{
    private const string StructuredAnalysisExtensionsSql = "'.docx','.pptx','.xlsx','.xlsm','.txt','.md','.csv','.xml','.json','.doc','.ppt','.xls'";

    public async Task<LibrarySourceIntegrityReport> VerifyLibrarySourceAsync(long sourceId, CancellationToken cancellationToken = default)
    {
        var source = await GetSourceAsync(sourceId, cancellationToken)
            ?? throw new InvalidOperationException("Library source not found.");

        var pathExists = Directory.Exists(source.RootPath);
        var pathReadable = false;
        if (pathExists)
        {
            try
            {
                _ = Directory.EnumerateFileSystemEntries(source.RootPath).Take(1).ToArray();
                pathReadable = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                pathReadable = false;
            }
        }

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);

        var integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check;";
        var integrityMessage = Convert.ToString(await integrity.ExecuteScalarAsync(cancellationToken)) ?? "unknown";
        var integrityOk = integrityMessage.Equals("ok", StringComparison.OrdinalIgnoreCase);

        var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "SELECT COUNT(*) FROM pragma_foreign_key_check;";
        var foreignKeyProblems = Convert.ToInt32((long)(await foreignKeys.ExecuteScalarAsync(cancellationToken) ?? 0L));

        var counts = connection.CreateCommand();
        counts.CommandText = $"""
            SELECT
                (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=$source),
                (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=$source AND r.is_available=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.catalogue_lane IN ('Learning','Both')),
                (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=$source AND r.is_available=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.catalogue_lane IN ('Learning','Both') AND r.discovery_decision='Include'),
                (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=$source AND r.is_available=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.catalogue_lane IN ('Learning','Both') AND r.discovery_decision='Review'),
                (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=$source AND r.is_available=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.catalogue_lane='ProjectArtifact'),
                (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=$source AND r.is_available=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.catalogue_lane='Both'),
                (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=$source AND r.is_policy_excluded=1),
                (SELECT COUNT(*) FROM discovery_exclusions de WHERE de.library_source_id=$source),
                (SELECT COUNT(*) FROM discovery_exclusions de
                 WHERE de.library_source_id=$source
                   AND NOT EXISTS (SELECT 1 FROM resources r WHERE r.library_source_id=de.library_source_id AND r.relative_path=de.relative_path)
                   AND NOT EXISTS (SELECT 1 FROM discovery_overrides o WHERE o.library_source_id=de.library_source_id AND o.relative_path=de.relative_path)
                   AND NOT EXISTS (SELECT 1 FROM learning_collection_candidates lc WHERE lc.library_source_id=de.library_source_id
                       AND (de.relative_path=lc.root_relative_path OR substr(de.relative_path,1,length(lc.root_relative_path)+1)=lc.root_relative_path||'/'))),
                (SELECT COUNT(*) FROM discovery_overrides o WHERE o.library_source_id=$source AND o.action='Include'),
                (SELECT COUNT(*) FROM discovery_overrides o WHERE o.library_source_id=$source AND o.action='Exclude'),
                (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=$source AND r.inventory_state='Missing'),
                (SELECT COUNT(*) FROM learning_collection_candidates lc WHERE lc.library_source_id=$source),
                (SELECT COUNT(*) FROM (
                    SELECT r.relative_path
                    FROM resources r
                    WHERE r.library_source_id=$source AND EXISTS (
                        SELECT 1 FROM learning_collection_candidates lc
                        WHERE lc.library_source_id=r.library_source_id
                          AND (r.relative_path=lc.root_relative_path OR substr(r.relative_path,1,length(lc.root_relative_path)+1)=lc.root_relative_path||'/'))
                    UNION
                    SELECT de.relative_path
                    FROM discovery_exclusions de
                    WHERE de.library_source_id=$source AND EXISTS (
                        SELECT 1 FROM learning_collection_candidates lc
                        WHERE lc.library_source_id=de.library_source_id
                          AND (de.relative_path=lc.root_relative_path OR substr(de.relative_path,1,length(lc.root_relative_path)+1)=lc.root_relative_path||'/'))
                )),
                (SELECT COUNT(*) FROM resources r
                 WHERE r.library_source_id=$source AND EXISTS (
                    SELECT 1 FROM learning_collection_candidates lc WHERE lc.library_source_id=r.library_source_id
                    AND (r.relative_path=lc.root_relative_path OR substr(r.relative_path,1,length(lc.root_relative_path)+1)=lc.root_relative_path||'/'))),
                (SELECT COUNT(*) FROM discovery_exclusions de
                 WHERE de.library_source_id=$source AND EXISTS (
                    SELECT 1 FROM learning_collection_candidates lc WHERE lc.library_source_id=de.library_source_id
                    AND (de.relative_path=lc.root_relative_path OR substr(de.relative_path,1,length(lc.root_relative_path)+1)=lc.root_relative_path||'/'))),
                (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=$source AND r.is_available=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include' AND r.catalogue_lane IN ('Learning','Both')),
                (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=$source AND r.is_available=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include' AND r.catalogue_lane IN ('Learning','Both') AND r.fingerprint_status IN ('Pending','Failed')),
                (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=$source AND r.is_available=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include' AND r.catalogue_lane IN ('Learning','Both') AND lower(r.extension) IN ({StructuredAnalysisExtensionsSql})),
                (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=$source AND r.is_available=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include' AND r.catalogue_lane IN ('Learning','Both') AND r.analysis_needed=1 AND lower(r.extension) IN ({StructuredAnalysisExtensionsSql})),
                (SELECT COUNT(*) FROM resource_organisation ro LEFT JOIN resources r ON r.id=ro.resource_id WHERE r.id IS NULL),
                (SELECT COUNT(*) FROM resource_subjects rs LEFT JOIN resources r ON r.id=rs.resource_id LEFT JOIN subjects s ON s.id=rs.subject_id WHERE r.id IS NULL OR s.id IS NULL),
                (SELECT COUNT(*) FROM resource_knowledge_links rkl LEFT JOIN resources r ON r.id=rkl.resource_id WHERE r.id IS NULL),
                (SELECT COUNT(*) FROM discovery_scan_runs dsr WHERE dsr.library_source_id=$source AND dsr.status='Running');
            """;
        counts.Parameters.AddWithValue("$source", sourceId);

        await using var reader = await counts.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        int I(int ordinal) => reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetInt64(ordinal));

        var catalogueRows = I(0);
        var learningCatalogueRows = I(1);
        var included = I(2);
        var review = I(3);
        var projectArtifacts = I(4);
        var dualLane = I(5);
        var policyExcluded = I(6);
        var exclusionRecords = I(7);
        var noiseOnlyExclusions = I(8);
        var manualInclude = I(9);
        var manualExclude = I(10);
        var missing = I(11);
        var collections = I(12);
        var collectionItems = I(13);
        var collectionCataloguePaths = I(14);
        var collectionExclusionPaths = I(15);
        var fingerprintEligible = I(16);
        var fingerprintPending = I(17);
        var analysisEligible = I(18);
        var analysisPending = I(19);
        var orphanOrganisation = I(20);
        var orphanSubjects = I(21);
        var orphanKnowledge = I(22);
        var interruptedRuns = I(23);

        var warnings = new List<string>();
        if (!pathExists) warnings.Add("Source folder is unavailable. Do not rescan until the drive/path is reconnected.");
        else if (!pathReadable) warnings.Add("Source folder exists but could not be read.");
        if (!source.IsEnabled || !source.LifecycleStatus.Equals("Active", StringComparison.OrdinalIgnoreCase))
            warnings.Add($"Source lifecycle is {source.LifecycleStatus}; deep scanning is disabled while it is inactive.");
        if (review > 0) warnings.Add($"{review:N0} resource(s) still need a discovery Include/Exclude decision before deep scanning.");
        if (noiseOnlyExclusions > 0) warnings.Add($"{noiseOnlyExclusions:N0} raw noise-only exclusion record(s) remain; rescan reconciliation should keep this at zero.");
        if (missing > 0) warnings.Add($"{missing:N0} previously catalogued resource(s) are currently marked Missing.");
        if (!integrityOk) warnings.Add($"SQLite integrity_check returned: {integrityMessage}");
        if (foreignKeyProblems > 0) warnings.Add($"SQLite reports {foreignKeyProblems:N0} foreign-key problem(s).");
        if (orphanOrganisation + orphanSubjects + orphanKnowledge > 0)
            warnings.Add("Orphaned catalogue/knowledge rows were detected; inspect before rebuilding knowledge organisation.");
        if (interruptedRuns > 0) warnings.Add($"{interruptedRuns:N0} discovery scan run(s) are still marked Running.");

        return new LibrarySourceIntegrityReport(
            source.Id, source.Name, source.RootPath, source.LifecycleStatus, source.IsEnabled,
            pathExists, pathReadable, source.LastDiscoveredUtc,
            catalogueRows, learningCatalogueRows, included, review, projectArtifacts, dualLane, policyExcluded, exclusionRecords, noiseOnlyExclusions,
            manualInclude, manualExclude, missing, collections, collectionItems, collectionCataloguePaths, collectionExclusionPaths,
            fingerprintEligible, fingerprintPending, analysisEligible, analysisPending,
            orphanOrganisation, orphanSubjects, orphanKnowledge, interruptedRuns,
            integrityOk, foreignKeyProblems, warnings);
    }

    public async Task<DatabaseIntegritySummary> VerifyDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);

        var integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check;";
        var integrityMessage = Convert.ToString(await integrity.ExecuteScalarAsync(cancellationToken)) ?? "unknown";
        var integrityOk = integrityMessage.Equals("ok", StringComparison.OrdinalIgnoreCase);

        var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "SELECT COUNT(*) FROM pragma_foreign_key_check;";
        var foreignKeyProblems = Convert.ToInt32((long)(await foreignKeys.ExecuteScalarAsync(cancellationToken) ?? 0L));

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM library_sources WHERE is_enabled=1 AND lifecycle_status='Active'),
                (SELECT COUNT(*) FROM library_sources WHERE lifecycle_status='Removed'),
                (SELECT COUNT(*) FROM resources),
                (SELECT COUNT(*) FROM resources WHERE is_available=1 AND is_policy_excluded=0 AND is_ignored=0 AND catalogue_lane IN ('Learning','Both')),
                (SELECT COUNT(*) FROM resources WHERE is_available=1 AND is_policy_excluded=0 AND is_ignored=0 AND catalogue_lane IN ('Learning','Both') AND discovery_decision='Include'),
                (SELECT COUNT(*) FROM resources WHERE is_available=1 AND is_policy_excluded=0 AND is_ignored=0 AND catalogue_lane IN ('Learning','Both') AND discovery_decision='Review'),
                (SELECT COUNT(*) FROM resources WHERE is_available=1 AND is_policy_excluded=0 AND is_ignored=0 AND catalogue_lane='ProjectArtifact'),
                (SELECT COUNT(*) FROM resources WHERE is_available=1 AND is_policy_excluded=0 AND is_ignored=0 AND catalogue_lane='Both'),
                (SELECT COUNT(*) FROM resources WHERE is_policy_excluded=1),
                (SELECT COUNT(*) FROM discovery_exclusions),
                (SELECT COUNT(*) FROM discovery_exclusions de
                 WHERE NOT EXISTS (SELECT 1 FROM resources r WHERE r.library_source_id=de.library_source_id AND r.relative_path=de.relative_path)
                   AND NOT EXISTS (SELECT 1 FROM discovery_overrides o WHERE o.library_source_id=de.library_source_id AND o.relative_path=de.relative_path)
                   AND NOT EXISTS (SELECT 1 FROM learning_collection_candidates lc WHERE lc.library_source_id=de.library_source_id
                       AND (de.relative_path=lc.root_relative_path OR substr(de.relative_path,1,length(lc.root_relative_path)+1)=lc.root_relative_path||'/'))),
                (SELECT COUNT(*) FROM resources WHERE inventory_state='Missing'),
                (SELECT COUNT(*) FROM learning_collection_candidates),
                (SELECT COUNT(*) FROM resource_organisation ro LEFT JOIN resources r ON r.id=ro.resource_id WHERE r.id IS NULL),
                (SELECT COUNT(*) FROM resource_subjects rs LEFT JOIN resources r ON r.id=rs.resource_id LEFT JOIN subjects s ON s.id=rs.subject_id WHERE r.id IS NULL OR s.id IS NULL),
                (SELECT COUNT(*) FROM resource_knowledge_links rkl LEFT JOIN resources r ON r.id=rkl.resource_id WHERE r.id IS NULL),
                (SELECT COUNT(*) FROM discovery_scan_runs WHERE status='Running');
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        int I(int ordinal) => reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetInt64(ordinal));

        var warnings = new List<string>();
        if (!integrityOk) warnings.Add($"SQLite integrity_check returned: {integrityMessage}");
        if (foreignKeyProblems > 0) warnings.Add($"SQLite reports {foreignKeyProblems:N0} foreign-key problem(s).");
        if (I(10) > 0) warnings.Add($"{I(10):N0} raw noise-only exclusion record(s) remain; rescan reconciliation should keep this at zero.");
        if (I(13) + I(14) + I(15) > 0) warnings.Add("Orphaned catalogue/knowledge rows were detected.");
        if (I(16) > 0) warnings.Add($"{I(16):N0} discovery scan run(s) are still marked Running.");

        return new DatabaseIntegritySummary(
            integrityOk, integrityMessage, foreignKeyProblems,
            I(0), I(1), I(2), I(3), I(4), I(5), I(6), I(7), I(8), I(9), I(10), I(11), I(12), I(13), I(14), I(15), I(16), warnings);
    }
}
