using System.Text.Json;
using Microsoft.Data.Sqlite;
using WICareerBuilder.Core.Models;
using WICareerBuilder.Core.Discovery;
using WICareerBuilder.Data.Database;

namespace WICareerBuilder.Data.Repositories;

public sealed partial class LibraryRepository
{
    private readonly DatabaseService _database;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public LibraryRepository(DatabaseService database) => _database = database;

    public async Task<long> AddSourceAsync(string name, string rootPath, string subject, string courseLevel, IReadOnlyList<string> exclusions, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(rootPath);
        var root = Path.GetPathRoot(fullPath);
        if (!string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException(fullPath);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow.ToString("O");

        var source = connection.CreateCommand();
        source.Transaction = tx;
        source.CommandText = """
            INSERT INTO library_sources(name, root_path, course_level, exclusions_json, created_utc)
            VALUES ($name, $path, $courseLevel, $exclusions, $utc)
            ON CONFLICT(root_path) DO UPDATE SET
                name = excluded.name,
                course_level = excluded.course_level,
                exclusions_json = excluded.exclusions_json
            RETURNING id;
            """;
        source.Parameters.AddWithValue("$name", name.Trim());
        source.Parameters.AddWithValue("$path", fullPath);
        source.Parameters.AddWithValue("$courseLevel", courseLevel.Trim());
        source.Parameters.AddWithValue("$exclusions", JsonSerializer.Serialize(exclusions, JsonOptions));
        source.Parameters.AddWithValue("$utc", now);
        var sourceId = Convert.ToInt64(await source.ExecuteScalarAsync(cancellationToken));

        var ensureSubject = connection.CreateCommand();
        ensureSubject.Transaction = tx;
        ensureSubject.CommandText = """
            INSERT INTO subjects(name, created_utc) VALUES ($name, $utc)
            ON CONFLICT(name) DO UPDATE SET name = excluded.name
            RETURNING id;
            """;
        ensureSubject.Parameters.AddWithValue("$name", subject.Trim());
        ensureSubject.Parameters.AddWithValue("$utc", now);
        var subjectId = Convert.ToInt64(await ensureSubject.ExecuteScalarAsync(cancellationToken));

        var clear = connection.CreateCommand();
        clear.Transaction = tx;
        clear.CommandText = "DELETE FROM source_subjects WHERE library_source_id = $source;";
        clear.Parameters.AddWithValue("$source", sourceId);
        await clear.ExecuteNonQueryAsync(cancellationToken);

        var link = connection.CreateCommand();
        link.Transaction = tx;
        link.CommandText = "INSERT OR IGNORE INTO source_subjects(library_source_id, subject_id) VALUES ($source, $subject);";
        link.Parameters.AddWithValue("$source", sourceId);
        link.Parameters.AddWithValue("$subject", subjectId);
        await link.ExecuteNonQueryAsync(cancellationToken);

        // Foundation behaviour: resources inherit their source subject. If the source subject is edited,
        // keep already-discovered resources consistent immediately.
        var clearResourceSubjects = connection.CreateCommand();
        clearResourceSubjects.Transaction = tx;
        clearResourceSubjects.CommandText = """
            DELETE FROM resource_subjects
            WHERE resource_id IN (SELECT id FROM resources WHERE library_source_id = $source);
            """;
        clearResourceSubjects.Parameters.AddWithValue("$source", sourceId);
        await clearResourceSubjects.ExecuteNonQueryAsync(cancellationToken);

        var relinkResources = connection.CreateCommand();
        relinkResources.Transaction = tx;
        relinkResources.CommandText = """
            INSERT OR IGNORE INTO resource_subjects(resource_id, subject_id)
            SELECT id, $subject FROM resources WHERE library_source_id = $source;
            """;
        relinkResources.Parameters.AddWithValue("$source", sourceId);
        relinkResources.Parameters.AddWithValue("$subject", subjectId);
        await relinkResources.ExecuteNonQueryAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
        await EnsureSourceCourseLinkAsync(sourceId, subject.Trim(), courseLevel.Trim(), cancellationToken);
        return sourceId;
    }

    public async Task<IReadOnlyList<LibrarySource>> GetSourcesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ls.id, ls.name, ls.root_path, ls.course_level, ls.exclusions_json, ls.is_enabled, ls.last_discovered_utc,
                   COALESCE(GROUP_CONCAT(s.name, ', '), '') AS subject,
                   COALESCE(ls.lifecycle_status, 'Active'), ls.removed_utc
            FROM library_sources ls
            LEFT JOIN source_subjects ss ON ss.library_source_id = ls.id
            LEFT JOIN subjects s ON s.id = ss.subject_id
            GROUP BY ls.id
            ORDER BY ls.name COLLATE NOCASE;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<LibrarySource>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var exclusions = JsonSerializer.Deserialize<List<string>>(reader.GetString(4), JsonOptions) ?? [];
            result.Add(new LibrarySource(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(7), reader.GetString(3), exclusions,
                reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)), reader.GetInt64(5) == 1,
                reader.GetString(8), reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9))));
        }
        return result;
    }

    public async Task<LibrarySource?> GetSourceAsync(long id, CancellationToken cancellationToken = default)
        => (await GetSourcesAsync(cancellationToken)).FirstOrDefault(x => x.Id == id);

    public async Task<DashboardSummary> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM library_sources WHERE is_enabled = 1 AND lifecycle_status = 'Active'),
              COUNT(*),
              SUM(CASE WHEN r.media_kind IN ('Document','Presentation','Spreadsheet') THEN 1 ELSE 0 END),
              SUM(CASE WHEN r.media_kind = 'Video' THEN 1 ELSE 0 END),
              SUM(CASE WHEN r.media_kind = 'Image' THEN 1 ELSE 0 END),
              SUM(CASE WHEN r.media_kind = 'Archive' THEN 1 ELSE 0 END),
              SUM(CASE WHEN r.analysis_status = 'NotAnalysed' THEN 1 ELSE 0 END)
            FROM resources r
            JOIN library_sources ls ON ls.id = r.library_source_id
            WHERE r.is_ignored = 0
              AND r.is_policy_excluded = 0
              AND r.discovery_decision = 'Include'
              AND r.catalogue_lane IN ('Learning','Both')
              AND ls.lifecycle_status = 'Active'
              AND ls.is_enabled = 1
              AND NOT EXISTS (
                  SELECT 1 FROM learning_collection_candidates lc
                  WHERE lc.library_source_id = r.library_source_id
                    AND (r.relative_path = lc.root_relative_path OR substr(r.relative_path, 1, length(lc.root_relative_path) + 1) = lc.root_relative_path || '/')
              )
              AND NOT EXISTS (
                  SELECT 1 FROM resource_relationships rr
                  WHERE rr.from_resource_id = r.id AND rr.relationship_type = 'ArchiveCompanion' AND rr.confidence >= 0.95
              );
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        int I(int ordinal) => reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetInt64(ordinal));
        return new DashboardSummary(I(0), I(1), I(2), I(3), I(4), I(5), I(6));
    }

    public async Task<IReadOnlyList<ResourceItem>> SearchResourcesAsync(string? search, string? mediaKind, string? studyStatus, string? libraryKey = null, int limit = 500, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.id, r.library_source_id, ls.name,
                   COALESCE(GROUP_CONCAT(DISTINCT s.name), '') AS subject,
                   ls.course_level,
                   r.relative_path, r.file_name, r.extension, r.size_bytes, r.last_write_utc,
                   r.media_kind, r.resource_type, r.analysis_status, r.is_available, r.is_ignored,
                   COALESCE(ro.learning_subject, ''), COALESCE(ro.topic, ''), COALESCE(ro.subtopic, ''), COALESCE(ro.study_status, 'Unsorted'),
                   (SELECT COUNT(*) FROM resources d
                    WHERE d.library_source_id = r.library_source_id AND d.id <> r.id
                      AND d.file_name = r.file_name COLLATE NOCASE AND d.size_bytes = r.size_bytes
                      AND d.is_policy_excluded = 0 AND d.is_ignored = 0 AND d.discovery_decision = 'Include') AS related_copy_count,
                   CASE WHEN EXISTS (
                       SELECT 1 FROM resource_relationships rr
                       WHERE rr.to_resource_id = r.id AND rr.relationship_type = 'ArchiveCompanion' AND rr.confidence >= 0.95
                   ) THEN 1 ELSE 0 END AS has_archive_companion,
                   r.catalogue_lane, r.catalogue_lane_reason
            FROM resources r
            JOIN library_sources ls ON ls.id = r.library_source_id
            LEFT JOIN resource_subjects rs ON rs.resource_id = r.id
            LEFT JOIN subjects s ON s.id = rs.subject_id
            LEFT JOIN resource_organisation ro ON ro.resource_id = r.id
            WHERE r.is_ignored = 0
              AND r.is_policy_excluded = 0
              AND r.discovery_decision = 'Include'
              AND (r.catalogue_lane IN ('Learning','Both') OR $library = 'projects')
              AND ls.lifecycle_status = 'Active'
              AND ls.is_enabled = 1
              AND NOT EXISTS (
                  SELECT 1 FROM learning_collection_candidates lc
                  WHERE lc.library_source_id = r.library_source_id
                    AND (r.relative_path = lc.root_relative_path OR substr(r.relative_path, 1, length(lc.root_relative_path) + 1) = lc.root_relative_path || '/')
              )
              AND NOT EXISTS (
                  SELECT 1 FROM resource_relationships rr
                  WHERE rr.from_resource_id = r.id AND rr.relationship_type = 'ArchiveCompanion' AND rr.confidence >= 0.95
              )
              AND ($search = '' OR r.file_name LIKE '%' || $search || '%'
                   OR r.relative_path LIKE '%' || $search || '%'
                   OR COALESCE(ro.learning_subject, '') LIKE '%' || $search || '%'
                   OR COALESCE(ro.topic, '') LIKE '%' || $search || '%'
                   OR COALESCE(ro.subtopic, '') LIKE '%' || $search || '%'
                   OR COALESCE(ro.notes, '') LIKE '%' || $search || '%')
              AND ($kind = '' OR r.media_kind = $kind)
              AND ($status = '' OR COALESCE(ro.study_status, 'Unsorted') = $status)
              AND (
                    $library = ''
                    OR ($library = 'images' AND r.media_kind = 'Image')
                    OR ($library = 'references' AND (COALESCE(ro.study_status,'Unsorted')='Reference' OR r.resource_type IN ('Reference Document','Reference Material','Manual / Technical Reference')))
                    OR ($library = 'datasheets' AND (r.resource_type IN ('Datasheet','Manual / Technical Reference') OR lower(r.relative_path) LIKE '%datasheet%' OR lower(r.relative_path) LIKE '%data sheet%'))
                    OR ($library = 'assignments' AND (r.resource_type='Old Assignment' OR lower(r.relative_path) LIKE '%old assignments%' OR lower(r.relative_path) LIKE '%completed assignments%'))
                    OR ($library = 'projects' AND (r.catalogue_lane IN ('ProjectArtifact','Both') OR r.resource_type='Project Material' OR lower(r.relative_path) LIKE 'projects/%' OR lower(r.relative_path) LIKE '%/projects/%'))
                    OR ($library = 'books' AND (r.extension='.pdf' OR r.resource_type LIKE 'Book%'))
                  )
            GROUP BY r.id
            ORDER BY r.file_name COLLATE NOCASE
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$search", search?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$kind", mediaKind?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$status", studyStatus?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$library", libraryKey?.Trim().ToLowerInvariant() ?? string.Empty);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 2000));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<ResourceItem>();
        while (await reader.ReadAsync(cancellationToken)) items.Add(ReadResource(reader));
        return items;
    }

    public async Task<(ResourceItem Resource, string RootPath)?> GetResourceAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.id, r.library_source_id, ls.name,
                   COALESCE(GROUP_CONCAT(DISTINCT s.name), '') AS subject,
                   ls.course_level,
                   r.relative_path, r.file_name, r.extension, r.size_bytes, r.last_write_utc,
                   r.media_kind, r.resource_type, r.analysis_status, r.is_available, r.is_ignored,
                   COALESCE(ro.learning_subject, ''), COALESCE(ro.topic, ''), COALESCE(ro.subtopic, ''), COALESCE(ro.study_status, 'Unsorted'),
                   (SELECT COUNT(*) FROM resources d
                    WHERE d.library_source_id = r.library_source_id AND d.id <> r.id
                      AND d.file_name = r.file_name COLLATE NOCASE AND d.size_bytes = r.size_bytes
                      AND d.is_policy_excluded = 0 AND d.is_ignored = 0 AND d.discovery_decision = 'Include') AS related_copy_count,
                   CASE WHEN EXISTS (
                       SELECT 1 FROM resource_relationships rr
                       WHERE rr.to_resource_id = r.id AND rr.relationship_type = 'ArchiveCompanion' AND rr.confidence >= 0.95
                   ) THEN 1 ELSE 0 END AS has_archive_companion,
                   r.catalogue_lane, r.catalogue_lane_reason,
                   ls.root_path
            FROM resources r
            JOIN library_sources ls ON ls.id = r.library_source_id
            LEFT JOIN resource_subjects rs ON rs.resource_id = r.id
            LEFT JOIN subjects s ON s.id = rs.subject_id
            LEFT JOIN resource_organisation ro ON ro.resource_id = r.id
            WHERE r.id = $id
            GROUP BY r.id;
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return (ReadResource(reader), reader.GetString(23));
    }

    public async Task<DiscoveryResult> SaveDiscoveryAsync(LibrarySource source, DiscoveryScan scan, CancellationToken cancellationToken = default)
    {
        var scanToken = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow.ToString("O");
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();

        // Capture the prior run before inserting this one. The reconciliation guard uses this only to identify
        // scanner-created rows from the immediately preceding scan that are safe to reconcile.
        var previousScanRunId = await GetPreviousDiscoveryScanRunIdAsync(connection, tx, source.Id, cancellationToken);

        var existingMetadata = new Dictionary<string, (long Id, long SizeBytes, string LastWriteUtc, string DiscoveryDecision, bool IsPolicyExcluded, bool IsAvailable, string CatalogueLane)>(StringComparer.OrdinalIgnoreCase);
        var existing = connection.CreateCommand();
        existing.Transaction = tx;
        existing.CommandText = "SELECT relative_path, id, size_bytes, last_write_utc, discovery_decision, is_policy_excluded, is_available, catalogue_lane FROM resources WHERE library_source_id = $source;";
        existing.Parameters.AddWithValue("$source", source.Id);
        await using (var existingReader = await existing.ExecuteReaderAsync(cancellationToken))
        {
            while (await existingReader.ReadAsync(cancellationToken))
                existingMetadata[existingReader.GetString(0)] = (
                    existingReader.GetInt64(1), existingReader.GetInt64(2), existingReader.GetString(3), existingReader.GetString(4),
                    existingReader.GetInt64(5) == 1, existingReader.GetInt64(6) == 1, existingReader.GetString(7));
        }

        // Saved user decisions are loaded before reconciliation so the scanner knows which otherwise
        // noisy paths need a durable catalogue record. Manual decisions always win later in the scan.
        var overrideCommand = connection.CreateCommand();
        overrideCommand.Transaction = tx;
        overrideCommand.CommandText = "SELECT relative_path, action FROM discovery_overrides WHERE library_source_id = $source;";
        overrideCommand.Parameters.AddWithValue("$source", source.Id);
        var savedOverrides = new List<(string RelativePath, string Action)>();
        await using (var overrideReader = await overrideCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await overrideReader.ReadAsync(cancellationToken))
                savedOverrides.Add((overrideReader.GetString(0), overrideReader.GetString(1)));
        }
        var overridePaths = savedOverrides.Select(x => x.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overrideActions = savedOverrides.ToDictionary(x => x.RelativePath, x => x.Action, StringComparer.OrdinalIgnoreCase);

        var filesInspected = scan.Files.Count + scan.ExcludedFiles.Count + scan.UnsupportedFilesSkipped;
        var startRun = connection.CreateCommand();
        startRun.Transaction = tx;
        startRun.CommandText = """
            INSERT INTO discovery_scan_runs(library_source_id,scan_token,status,resources_seen,policy_excluded_files,directories_visited,directories_skipped,infrastructure_directories_skipped,unsupported_files_skipped,error_count,started_utc,files_inspected)
            VALUES($source,$token,'Running',$resources,$excluded,$visited,$skipped,$infra,$unsupported,$errors,$now,$inspected);
            """;
        startRun.Parameters.AddWithValue("$source", source.Id);
        startRun.Parameters.AddWithValue("$token", scanToken);
        startRun.Parameters.AddWithValue("$resources", scan.Files.Count);
        startRun.Parameters.AddWithValue("$excluded", scan.ExcludedFiles.Count);
        startRun.Parameters.AddWithValue("$visited", scan.DirectoriesVisited);
        startRun.Parameters.AddWithValue("$skipped", scan.DirectoriesSkipped);
        startRun.Parameters.AddWithValue("$infra", scan.InfrastructureDirectoriesSkipped);
        startRun.Parameters.AddWithValue("$unsupported", scan.UnsupportedFilesSkipped);
        startRun.Parameters.AddWithValue("$errors", scan.Errors.Count);
        startRun.Parameters.AddWithValue("$now", now);
        startRun.Parameters.AddWithValue("$inspected", filesInspected);
        await startRun.ExecuteNonQueryAsync(cancellationToken);

        var runIdCommand = connection.CreateCommand();
        runIdCommand.Transaction = tx;
        runIdCommand.CommandText = "SELECT last_insert_rowid();";
        var scanRunId = Convert.ToInt64(await runIdCommand.ExecuteScalarAsync(cancellationToken) ?? 0L);

        var restoredByRefinedContext = 0;
        var contextRoleSuggestions = 0;
        var lowValueProjectNoiseSkipped = scan.ExcludedFiles.Count(x => x.Reason.Equals("Low-value project implementation/dependency asset", StringComparison.OrdinalIgnoreCase));

        foreach (var file in scan.Files)
        {
            var lastWrite = file.LastWriteUtc.ToString("O");
            var inventoryState = !existingMetadata.TryGetValue(file.RelativePath, out var oldMetadata)
                ? "New"
                : oldMetadata.SizeBytes == file.SizeBytes && oldMetadata.LastWriteUtc == lastWrite
                    ? "Unchanged"
                    : "Modified";
            var learningEligible = file.DiscoveryDecision.Equals("Include", StringComparison.OrdinalIgnoreCase)
                                   && (file.CatalogueLane.Equals("Learning", StringComparison.OrdinalIgnoreCase)
                                       || file.CatalogueLane.Equals("Both", StringComparison.OrdinalIgnoreCase));
            var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = """
                INSERT INTO resources(
                    library_source_id, relative_path, file_name, extension, size_bytes, last_write_utc,
                    media_kind, resource_type, discovery_status, analysis_status, is_available, is_ignored,
                    last_scan_token, discovered_utc, last_seen_utc,
                    discovery_decision, discovery_reason, is_policy_excluded, catalogue_lane, catalogue_lane_reason,
                    inventory_state, quick_fingerprint, fingerprint_kind, fingerprint_status, fingerprinted_utc, analysis_needed)
                VALUES ($source, $relative, $name, $extension, $size, $modified,
                    $kind, 'Unknown', 'Discovered', 'NotAnalysed', 1, 0, $token, $now, $now,
                    $decision, $reason, 0, $lane, $laneReason,
                    $inventoryState, '', '', $fingerprintStatus, NULL, $analysisNeeded)
                ON CONFLICT(library_source_id, relative_path) DO UPDATE SET
                    file_name = excluded.file_name,
                    extension = excluded.extension,
                    size_bytes = excluded.size_bytes,
                    last_write_utc = excluded.last_write_utc,
                    media_kind = excluded.media_kind,
                    is_available = 1,
                    last_scan_token = excluded.last_scan_token,
                    last_seen_utc = excluded.last_seen_utc,
                    discovery_status = 'Discovered',
                    discovery_decision = excluded.discovery_decision,
                    discovery_reason = excluded.discovery_reason,
                    is_policy_excluded = 0,
                    catalogue_lane = excluded.catalogue_lane,
                    catalogue_lane_reason = excluded.catalogue_lane_reason,
                    inventory_state = $inventoryState,
                    quick_fingerprint = CASE WHEN $learningEligible = 1 AND $inventoryState = 'Unchanged' AND resources.fingerprint_status = 'Complete' THEN resources.quick_fingerprint ELSE '' END,
                    fingerprint_kind = CASE WHEN $learningEligible = 1 AND $inventoryState = 'Unchanged' AND resources.fingerprint_status = 'Complete' THEN resources.fingerprint_kind ELSE '' END,
                    fingerprint_status = CASE
                        WHEN $learningEligible = 0 THEN 'Deferred'
                        WHEN $inventoryState = 'Unchanged' AND resources.fingerprint_status = 'Complete' THEN 'Complete'
                        ELSE 'Pending' END,
                    fingerprinted_utc = CASE WHEN $learningEligible = 1 AND $inventoryState = 'Unchanged' AND resources.fingerprint_status = 'Complete' THEN resources.fingerprinted_utc ELSE NULL END,
                    analysis_needed = CASE
                        WHEN $learningEligible = 0 THEN 0
                        WHEN $inventoryState = 'Unchanged' AND resources.analysis_status IN ('Analysed','Limited') THEN 0
                        ELSE 1 END,
                    analysis_status = CASE WHEN $learningEligible = 1 AND $inventoryState <> 'Unchanged' THEN 'NotAnalysed' ELSE resources.analysis_status END;
                """;
            command.Parameters.AddWithValue("$source", source.Id);
            command.Parameters.AddWithValue("$relative", file.RelativePath);
            command.Parameters.AddWithValue("$name", file.FileName);
            command.Parameters.AddWithValue("$extension", file.Extension);
            command.Parameters.AddWithValue("$size", file.SizeBytes);
            command.Parameters.AddWithValue("$modified", lastWrite);
            command.Parameters.AddWithValue("$kind", file.MediaKind);
            command.Parameters.AddWithValue("$inventoryState", inventoryState);
            command.Parameters.AddWithValue("$decision", file.DiscoveryDecision);
            command.Parameters.AddWithValue("$reason", file.DiscoveryReason);
            command.Parameters.AddWithValue("$lane", file.CatalogueLane);
            command.Parameters.AddWithValue("$laneReason", file.LaneReason);
            command.Parameters.AddWithValue("$learningEligible", learningEligible ? 1 : 0);
            command.Parameters.AddWithValue("$fingerprintStatus", learningEligible ? "Pending" : "Deferred");
            command.Parameters.AddWithValue("$analysisNeeded", learningEligible ? 1 : 0);
            command.Parameters.AddWithValue("$token", scanToken);
            command.Parameters.AddWithValue("$now", now);
            await command.ExecuteNonQueryAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(file.SuggestedRoleKey))
                contextRoleSuggestions += await UpsertDiscoveryRoleSuggestionAsync(
                    connection, tx, source.Id, file.RelativePath, file.SuggestedRoleKey!, now, cancellationToken);

            if (inventoryState is "New" or "Modified")
            {
                await InsertDiscoveryScanChangeAsync(connection, tx, scanRunId, source.Id, file.RelativePath, file.FileName,
                    inventoryState, file.DiscoveryDecision, file.DiscoveryReason, file.CatalogueLane, file.LaneReason, now, cancellationToken);
            }
            else if (existingMetadata.TryGetValue(file.RelativePath, out var previous) &&
                     (previous.IsPolicyExcluded || previous.DiscoveryDecision.Equals("Exclude", StringComparison.OrdinalIgnoreCase)))
            {
                restoredByRefinedContext++;
                await InsertDiscoveryScanChangeAsync(connection, tx, scanRunId, source.Id, file.RelativePath, file.FileName,
                    "Restored", file.DiscoveryDecision, file.DiscoveryReason, file.CatalogueLane, file.LaneReason, now, cancellationToken);
            }
            else if (existingMetadata.TryGetValue(file.RelativePath, out var lanePrevious)
                     && !lanePrevious.CatalogueLane.Equals(file.CatalogueLane, StringComparison.OrdinalIgnoreCase))
            {
                await InsertDiscoveryScanChangeAsync(connection, tx, scanRunId, source.Id, file.RelativePath, file.FileName,
                    "LaneChanged", file.DiscoveryDecision, file.DiscoveryReason, file.CatalogueLane, file.LaneReason, now, cancellationToken);
            }

            var removeOldExclusion = connection.CreateCommand();
            removeOldExclusion.Transaction = tx;
            removeOldExclusion.CommandText = "DELETE FROM discovery_exclusions WHERE library_source_id = $source AND relative_path = $relative;";
            removeOldExclusion.Parameters.AddWithValue("$source", source.Id);
            removeOldExclusion.Parameters.AddWithValue("$relative", file.RelativePath);
            await removeOldExclusion.ExecuteNonQueryAsync(cancellationToken);
        }

        var hardNoiseSkipped = 0;
        var persistentExclusionsEncountered = 0;
        var reconciledLeakageRemoved = 0;
        var retainedLeakageRows = 0;

        foreach (var file in scan.ExcludedFiles)
        {
            var hasExistingResource = existingMetadata.TryGetValue(file.RelativePath, out var previous);
            var collectionCandidate = DiscoveryPolicy.IsLearningCollectionCandidate(file.RelativePath, file.Reason);
            var hasSavedOverride = overridePaths.Contains(file.RelativePath);

            // If the immediately preceding scan created this row and the corrected context-first
            // policy now rejects it, remove only when no user/analysis/learning metadata has attached
            // to it. Original filesystem content is never touched.
            if (hasExistingResource &&
                !hasSavedOverride &&
                !collectionCandidate &&
                await WasNewInScanAsync(connection, tx, previousScanRunId, source.Id, file.RelativePath, cancellationToken) &&
                !await HasProtectedResourceDataAsync(connection, tx, previous.Id, cancellationToken))
            {
                await DeleteScannerLeakageResourceAsync(
                    connection, tx, scanRunId, source.Id, previous.Id, file.RelativePath,
                    Path.GetFileName(file.RelativePath),
                    $"Safe reconciliation: {file.Reason}", now, cancellationToken);
                reconciledLeakageRemoved++;
                hardNoiseSkipped++;
                continue;
            }

            var shouldPersist = hasExistingResource || collectionCandidate || hasSavedOverride;

            if (shouldPersist)
            {
                persistentExclusionsEncountered++;
                var exclusion = connection.CreateCommand();
                exclusion.Transaction = tx;
                exclusion.CommandText = """
                    INSERT INTO discovery_exclusions(
                        library_source_id, relative_path, file_name, extension, reason, last_scan_token, last_seen_utc)
                    VALUES ($source, $relative, $name, $extension, $reason, $token, $now)
                    ON CONFLICT(library_source_id, relative_path) DO UPDATE SET
                        file_name = excluded.file_name,
                        extension = excluded.extension,
                        reason = excluded.reason,
                        last_scan_token = excluded.last_scan_token,
                        last_seen_utc = excluded.last_seen_utc;
                    """;
                exclusion.Parameters.AddWithValue("$source", source.Id);
                exclusion.Parameters.AddWithValue("$relative", file.RelativePath);
                exclusion.Parameters.AddWithValue("$name", Path.GetFileName(file.RelativePath));
                exclusion.Parameters.AddWithValue("$extension", file.Extension);
                exclusion.Parameters.AddWithValue("$reason", file.Reason);
                exclusion.Parameters.AddWithValue("$token", scanToken);
                exclusion.Parameters.AddWithValue("$now", now);
                await exclusion.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                // Raw application/source/config/web noise that never formed part of the educational
                // catalogue is counted in the durable scan report but is not persisted path-by-path.
                hardNoiseSkipped++;
            }

            if (!hasExistingResource) continue;

            // A resource may have been catalogued by an older version or may now have attached
            // workbench data. Preserve the catalogue row, but suppress it from active learning.
            var suppress = connection.CreateCommand();
            suppress.Transaction = tx;
            suppress.CommandText = """
                UPDATE resources
                SET discovery_status = 'Excluded',
                    discovery_decision = 'Exclude',
                    discovery_reason = $reason,
                    is_policy_excluded = 1,
                    catalogue_lane = 'None',
                    catalogue_lane_reason = $reason,
                    last_scan_token = $token,
                    last_seen_utc = $now,
                    fingerprint_status = 'Deferred',
                    analysis_needed = 0
                WHERE library_source_id = $source AND relative_path = $relative;
                """;
            suppress.Parameters.AddWithValue("$source", source.Id);
            suppress.Parameters.AddWithValue("$relative", file.RelativePath);
            suppress.Parameters.AddWithValue("$reason", file.Reason);
            suppress.Parameters.AddWithValue("$token", scanToken);
            suppress.Parameters.AddWithValue("$now", now);
            await suppress.ExecuteNonQueryAsync(cancellationToken);
            retainedLeakageRows++;

            if (!previous.IsPolicyExcluded || !previous.DiscoveryDecision.Equals("Exclude", StringComparison.OrdinalIgnoreCase))
            {
                await InsertDiscoveryScanChangeAsync(connection, tx, scanRunId, source.Id, file.RelativePath, Path.GetFileName(file.RelativePath),
                    "NewlyExcluded", "Exclude", file.Reason, "None", file.Reason, now, cancellationToken);
            }
        }

        // Directory-level pruning prevents v0.8.2's Software/Web leak before any file in that
        // branch is enumerated. Existing catalogue rows underneath a newly-pruned branch are
        // reconciled here so they do not become false Missing items.
        var prunedReconciliation = await ReconcilePrunedBranchesAsync(
            connection, tx, source, scanRunId, previousScanRunId, scan.PrunedDirectories,
            overrideActions, scanToken, now, cancellationToken);
        reconciledLeakageRemoved += prunedReconciliation.RemovedRows;
        retainedLeakageRows += prunedReconciliation.RetainedRows;

        // Manual review decisions always win over a later discovery scan. This keeps the user's
        // Include/Exclude choices stable even when the policy is recalibrated in a future version.
        foreach (var savedOverride in savedOverrides)
        {
            if (savedOverride.Action.Equals("Include", StringComparison.OrdinalIgnoreCase))
            {
                var fullPath = ResolveSourcePath(source, savedOverride.RelativePath);
                if (!File.Exists(fullPath)) continue;
                await EnsureManualResourceAsync(connection, tx, source, savedOverride.RelativePath, now, cancellationToken);

                var include = connection.CreateCommand();
                include.Transaction = tx;
                include.CommandText = """
                    UPDATE resources
                    SET discovery_status = 'Discovered', discovery_decision = 'Include', discovery_reason = 'Manual include override',
                        media_kind = $kind, is_policy_excluded = 0, catalogue_lane='Learning', catalogue_lane_reason='Manual include override into learning catalogue',
                        is_available = 1, last_scan_token = $token, last_seen_utc = $now,
                        fingerprint_status = CASE WHEN quick_fingerprint<>'' THEN 'Complete' ELSE 'Pending' END,
                        analysis_needed = CASE WHEN analysis_status IN ('Analysed','Limited') THEN 0 ELSE 1 END
                    WHERE library_source_id = $source AND relative_path = $relative;
                    """;
                include.Parameters.AddWithValue("$kind", DiscoveryPolicy.ManualIncludeMediaKind(savedOverride.RelativePath));
                include.Parameters.AddWithValue("$now", now);
                include.Parameters.AddWithValue("$token", scanToken);
                include.Parameters.AddWithValue("$source", source.Id);
                include.Parameters.AddWithValue("$relative", savedOverride.RelativePath);
                await include.ExecuteNonQueryAsync(cancellationToken);

                var remove = connection.CreateCommand();
                remove.Transaction = tx;
                remove.CommandText = "DELETE FROM discovery_exclusions WHERE library_source_id = $source AND relative_path = $relative;";
                remove.Parameters.AddWithValue("$source", source.Id);
                remove.Parameters.AddWithValue("$relative", savedOverride.RelativePath);
                await remove.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                var exclude = connection.CreateCommand();
                exclude.Transaction = tx;
                exclude.CommandText = """
                    UPDATE resources
                    SET discovery_status = 'Excluded', discovery_decision = 'Exclude', discovery_reason = 'Manual exclusion override',
                        is_policy_excluded = 1, catalogue_lane='None', catalogue_lane_reason='Manual exclusion override',
                        last_seen_utc = $now, fingerprint_status='Deferred', analysis_needed=0
                    WHERE library_source_id = $source AND relative_path = $relative;
                    """;
                exclude.Parameters.AddWithValue("$now", now);
                exclude.Parameters.AddWithValue("$source", source.Id);
                exclude.Parameters.AddWithValue("$relative", savedOverride.RelativePath);
                await exclude.ExecuteNonQueryAsync(cancellationToken);

                await UpsertDiscoveryExclusionAsync(
                    connection, tx, source.Id, savedOverride.RelativePath,
                    Path.GetFileName(savedOverride.RelativePath), Path.GetExtension(savedOverride.RelativePath).ToLowerInvariant(),
                    "Manual exclusion override", now, cancellationToken);
            }
        }

        var inheritedSubjects = connection.CreateCommand();
        inheritedSubjects.Transaction = tx;
        inheritedSubjects.CommandText = """
            INSERT OR IGNORE INTO resource_subjects(resource_id, subject_id)
            SELECT r.id, ss.subject_id
            FROM resources r
            JOIN source_subjects ss ON ss.library_source_id = r.library_source_id
            WHERE r.library_source_id = $source
              AND r.is_policy_excluded = 0
              AND r.discovery_decision = 'Include';
            """;
        inheritedSubjects.Parameters.AddWithValue("$source", source.Id);
        await inheritedSubjects.ExecuteNonQueryAsync(cancellationToken);

        // Only reconcile unseen items after a clean scan. If any directory was inaccessible,
        // preserving the previous state is safer than assuming resources disappeared.
        if (scan.Errors.Count == 0)
        {
            var newlyMissing = connection.CreateCommand();
            newlyMissing.Transaction = tx;
            newlyMissing.CommandText = """
                SELECT relative_path, file_name, discovery_decision, discovery_reason
                FROM resources
                WHERE library_source_id = $source
                  AND is_policy_excluded = 0
                  AND is_available = 1
                  AND (last_scan_token IS NULL OR last_scan_token <> $token);
                """;
            newlyMissing.Parameters.AddWithValue("$source", source.Id);
            newlyMissing.Parameters.AddWithValue("$token", scanToken);
            var missingChanges = new List<(string RelativePath,string FileName,string Decision,string Reason)>();
            await using (var missingReader = await newlyMissing.ExecuteReaderAsync(cancellationToken))
            {
                while (await missingReader.ReadAsync(cancellationToken))
                    missingChanges.Add((missingReader.GetString(0), missingReader.GetString(1), missingReader.GetString(2), missingReader.GetString(3)));
            }
            foreach (var missingChange in missingChanges)
                await InsertDiscoveryScanChangeAsync(connection, tx, scanRunId, source.Id, missingChange.RelativePath, missingChange.FileName,
                    "Missing", missingChange.Decision, missingChange.Reason, "", "", now, cancellationToken);

            var unavailable = connection.CreateCommand();
            unavailable.Transaction = tx;
            unavailable.CommandText = """
                UPDATE resources
                SET is_available = 0,
                    inventory_state = 'Missing',
                    fingerprint_status = 'Unavailable',
                    analysis_needed = 0
                WHERE library_source_id = $source
                  AND is_policy_excluded = 0
                  AND (last_scan_token IS NULL OR last_scan_token <> $token);
                """;
            unavailable.Parameters.AddWithValue("$source", source.Id);
            unavailable.Parameters.AddWithValue("$token", scanToken);
            await unavailable.ExecuteNonQueryAsync(cancellationToken);

            var staleExclusions = connection.CreateCommand();
            staleExclusions.Transaction = tx;
            staleExclusions.CommandText = """
                DELETE FROM discovery_exclusions
                WHERE library_source_id = $source
                  AND (last_scan_token IS NULL OR last_scan_token <> $token)
                  AND NOT EXISTS (
                      SELECT 1 FROM discovery_overrides o
                      WHERE o.library_source_id = discovery_exclusions.library_source_id
                        AND o.relative_path = discovery_exclusions.relative_path
                  )
                  AND NOT EXISTS (
                      SELECT 1 FROM resources r
                      WHERE r.library_source_id = discovery_exclusions.library_source_id
                        AND r.relative_path = discovery_exclusions.relative_path
                        AND r.is_policy_excluded = 1
                  )
                  AND NOT EXISTS (
                      SELECT 1 FROM learning_collection_candidates lc
                      WHERE lc.library_source_id = discovery_exclusions.library_source_id
                        AND (discovery_exclusions.relative_path = lc.root_relative_path
                             OR substr(discovery_exclusions.relative_path,1,length(lc.root_relative_path)+1) = lc.root_relative_path || '/')
                  );
                """;
            staleExclusions.Parameters.AddWithValue("$source", source.Id);
            staleExclusions.Parameters.AddWithValue("$token", scanToken);
            await staleExclusions.ExecuteNonQueryAsync(cancellationToken);
        }

        await DetectLearningCollectionCandidatesAsync(connection, tx, source, now, cancellationToken);

        // Keep the exclusion catalogue compact. A durable path-level exclusion is useful only when it
        // corresponds to an actual catalogue resource, a manual decision, or content managed by a
        // Learning Collection. Everything else is raw filesystem noise and belongs only in scan totals.
        var pruneNoiseOnlyExclusions = connection.CreateCommand();
        pruneNoiseOnlyExclusions.Transaction = tx;
        pruneNoiseOnlyExclusions.CommandText = """
            DELETE FROM discovery_exclusions
            WHERE library_source_id = $source
              AND NOT EXISTS (
                  SELECT 1 FROM resources r
                  WHERE r.library_source_id = discovery_exclusions.library_source_id
                    AND r.relative_path = discovery_exclusions.relative_path)
              AND NOT EXISTS (
                  SELECT 1 FROM discovery_overrides o
                  WHERE o.library_source_id = discovery_exclusions.library_source_id
                    AND o.relative_path = discovery_exclusions.relative_path)
              AND NOT EXISTS (
                  SELECT 1 FROM learning_collection_candidates lc
                  WHERE lc.library_source_id = discovery_exclusions.library_source_id
                    AND (discovery_exclusions.relative_path = lc.root_relative_path
                         OR substr(discovery_exclusions.relative_path,1,length(lc.root_relative_path)+1) = lc.root_relative_path || '/'));
            """;
        pruneNoiseOnlyExclusions.Parameters.AddWithValue("$source", source.Id);
        await pruneNoiseOnlyExclusions.ExecuteNonQueryAsync(cancellationToken);

        var updateSource = connection.CreateCommand();
        updateSource.Transaction = tx;
        updateSource.CommandText = "UPDATE library_sources SET last_discovered_utc = $now WHERE id = $source;";
        updateSource.Parameters.AddWithValue("$source", source.Id);
        updateSource.Parameters.AddWithValue("$now", now);
        await updateSource.ExecuteNonQueryAsync(cancellationToken);

        var scanCounts = connection.CreateCommand();
        scanCounts.Transaction = tx;
        scanCounts.CommandText = """
            SELECT
                SUM(CASE WHEN inventory_state='New' AND is_available=1 AND is_policy_excluded=0 THEN 1 ELSE 0 END),
                SUM(CASE WHEN inventory_state='Modified' AND is_available=1 AND is_policy_excluded=0 THEN 1 ELSE 0 END),
                SUM(CASE WHEN inventory_state='Unchanged' AND is_available=1 AND is_policy_excluded=0 THEN 1 ELSE 0 END),
                SUM(CASE WHEN inventory_state='Missing' AND is_policy_excluded=0 THEN 1 ELSE 0 END),
                SUM(CASE WHEN analysis_needed=1 AND is_available=1 AND is_policy_excluded=0 AND is_ignored=0 AND discovery_decision='Include' AND catalogue_lane IN ('Learning','Both') THEN 1 ELSE 0 END)
            FROM resources WHERE library_source_id=$source;
            """;
        scanCounts.Parameters.AddWithValue("$source", source.Id);
        var newFiles = 0; var modifiedFiles = 0; var unchangedFiles = 0; var missingFiles = 0; var analysisNeeded = 0;
        await using (var countReader = await scanCounts.ExecuteReaderAsync(cancellationToken))
        {
            if (await countReader.ReadAsync(cancellationToken))
            {
                newFiles = countReader.IsDBNull(0) ? 0 : Convert.ToInt32(countReader.GetInt64(0));
                modifiedFiles = countReader.IsDBNull(1) ? 0 : Convert.ToInt32(countReader.GetInt64(1));
                unchangedFiles = countReader.IsDBNull(2) ? 0 : Convert.ToInt32(countReader.GetInt64(2));
                missingFiles = countReader.IsDBNull(3) ? 0 : Convert.ToInt32(countReader.GetInt64(3));
                analysisNeeded = countReader.IsDBNull(4) ? 0 : Convert.ToInt32(countReader.GetInt64(4));
            }
        }

        var auditCounts = connection.CreateCommand();
        auditCounts.Transaction = tx;
        auditCounts.CommandText = """
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
        auditCounts.Parameters.AddWithValue("$source", source.Id);
        var catalogueRowsAfter = 0; var learningCatalogueRowsAfter = 0; var includedAfter = 0; var reviewAfter = 0;
        var projectArtifactRowsAfter = 0; var dualLaneRowsAfter = 0; var excludedAfter = 0;
        var exclusionRecordsAfter = 0; var learningCollectionsAfter = 0; var learningCollectionItemsAfter = 0;
        await using (var auditReader = await auditCounts.ExecuteReaderAsync(cancellationToken))
        {
            if (await auditReader.ReadAsync(cancellationToken))
            {
                int AI(int ordinal) => auditReader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(auditReader.GetInt64(ordinal));
                catalogueRowsAfter = AI(0); learningCatalogueRowsAfter = AI(1); includedAfter = AI(2); reviewAfter = AI(3);
                projectArtifactRowsAfter = AI(4); dualLaneRowsAfter = AI(5); excludedAfter = AI(6);
                exclusionRecordsAfter = AI(7); learningCollectionsAfter = AI(8); learningCollectionItemsAfter = AI(9);
            }
        }

        var finishRun = connection.CreateCommand();
        finishRun.Transaction = tx;
        finishRun.CommandText = """
            UPDATE discovery_scan_runs
            SET status=$status,new_resources=$new,modified_resources=$modified,unchanged_resources=$unchanged,missing_resources=$missing,analysis_needed=$analysis,completed_utc=$now,
                files_inspected=$inspected,hard_noise_skipped=$noise,persistent_exclusions=$persisted,
                context_directories_skipped=$contextDirs,pruned_branch_count=$prunedBranches,pruned_catalogue_rows=$prunedRows,
                reconciled_leakage_removed=$reconciledRemoved,protected_leakage_rows=$retainedRows,
                policy_restored_resources=$restoredByContext,context_role_suggestions=$roleSuggestions,low_value_project_noise_skipped=$lowValueProjectNoise,
                catalogue_rows_after=$catalogue,learning_catalogue_rows_after=$learningCatalogue,project_artifact_rows_after=$projectArtifacts,dual_lane_rows_after=$dualLane,
                included_after=$includedAfter,review_after=$reviewAfter,excluded_after=$excludedAfter,
                exclusion_records_after=$exclusionRecords,learning_collections_after=$collections,learning_collection_items_after=$collectionItems
            WHERE library_source_id=$source AND scan_token=$token;
            """;
        finishRun.Parameters.AddWithValue("$status", scan.Errors.Count == 0 ? "Completed" : "CompletedWithWarnings");
        finishRun.Parameters.AddWithValue("$new", newFiles);
        finishRun.Parameters.AddWithValue("$modified", modifiedFiles);
        finishRun.Parameters.AddWithValue("$unchanged", unchangedFiles);
        finishRun.Parameters.AddWithValue("$missing", missingFiles);
        finishRun.Parameters.AddWithValue("$analysis", analysisNeeded);
        finishRun.Parameters.AddWithValue("$inspected", filesInspected);
        finishRun.Parameters.AddWithValue("$noise", hardNoiseSkipped + scan.UnsupportedFilesSkipped);
        finishRun.Parameters.AddWithValue("$persisted", persistentExclusionsEncountered);
        finishRun.Parameters.AddWithValue("$contextDirs", scan.ContextDirectoriesSkipped);
        finishRun.Parameters.AddWithValue("$prunedBranches", scan.PrunedDirectories.Count);
        finishRun.Parameters.AddWithValue("$prunedRows", prunedReconciliation.CatalogueRows);
        finishRun.Parameters.AddWithValue("$reconciledRemoved", reconciledLeakageRemoved);
        finishRun.Parameters.AddWithValue("$retainedRows", retainedLeakageRows);
        finishRun.Parameters.AddWithValue("$restoredByContext", restoredByRefinedContext);
        finishRun.Parameters.AddWithValue("$roleSuggestions", contextRoleSuggestions);
        finishRun.Parameters.AddWithValue("$lowValueProjectNoise", lowValueProjectNoiseSkipped);
        finishRun.Parameters.AddWithValue("$catalogue", catalogueRowsAfter);
        finishRun.Parameters.AddWithValue("$learningCatalogue", learningCatalogueRowsAfter);
        finishRun.Parameters.AddWithValue("$projectArtifacts", projectArtifactRowsAfter);
        finishRun.Parameters.AddWithValue("$dualLane", dualLaneRowsAfter);
        finishRun.Parameters.AddWithValue("$includedAfter", includedAfter);
        finishRun.Parameters.AddWithValue("$reviewAfter", reviewAfter);
        finishRun.Parameters.AddWithValue("$excludedAfter", excludedAfter);
        finishRun.Parameters.AddWithValue("$exclusionRecords", exclusionRecordsAfter);
        finishRun.Parameters.AddWithValue("$collections", learningCollectionsAfter);
        finishRun.Parameters.AddWithValue("$collectionItems", learningCollectionItemsAfter);
        finishRun.Parameters.AddWithValue("$now", now);
        finishRun.Parameters.AddWithValue("$source", source.Id);
        finishRun.Parameters.AddWithValue("$token", scanToken);
        await finishRun.ExecuteNonQueryAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
        return new DiscoveryResult(
            scan.Files.Count,
            scan.ExcludedFiles.Count,
            scan.DirectoriesVisited,
            scan.DirectoriesSkipped,
            scan.InfrastructureDirectoriesSkipped,
            scan.ContextDirectoriesSkipped,
            scan.UnsupportedFilesSkipped,
            scan.Errors.Count,
            newFiles, modifiedFiles, unchangedFiles, missingFiles, analysisNeeded,
            hardNoiseSkipped + scan.UnsupportedFilesSkipped, persistentExclusionsEncountered,
            scan.PrunedDirectories.Count, prunedReconciliation.CatalogueRows,
            reconciledLeakageRemoved, retainedLeakageRows, restoredByRefinedContext, contextRoleSuggestions,
            learningCatalogueRowsAfter, projectArtifactRowsAfter, dualLaneRowsAfter, lowValueProjectNoiseSkipped, scanRunId);
    }

    public async Task<LibraryCleanupResult> CleanLibraryIndexAsync(long? sourceId = null, CancellationToken cancellationToken = default)
    {
        var sources = await GetSourcesAsync(cancellationToken);
        if (sourceId.HasValue)
            sources = sources.Where(s => s.Id == sourceId.Value).ToArray();
        if (sourceId.HasValue && sources.Count == 0)
            throw new InvalidOperationException("Library source not found.");

        var examined = 0;
        var excluded = 0;
        var restored = 0;
        var review = 0;
        var included = 0;
        var now = DateTimeOffset.UtcNow.ToString("O");

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();

        foreach (var source in sources)
        {
            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var overrideRead = connection.CreateCommand();
            overrideRead.Transaction = tx;
            overrideRead.CommandText = "SELECT relative_path, action FROM discovery_overrides WHERE library_source_id = $source;";
            overrideRead.Parameters.AddWithValue("$source", source.Id);
            await using (var overrideReader = await overrideRead.ExecuteReaderAsync(cancellationToken))
            {
                while (await overrideReader.ReadAsync(cancellationToken))
                    overrides[overrideReader.GetString(0)] = overrideReader.GetString(1);
            }

            var read = connection.CreateCommand();
            read.Transaction = tx;
            read.CommandText = """
                SELECT id, relative_path, file_name, extension, is_policy_excluded
                FROM resources
                WHERE library_source_id = $source;
                """;
            read.Parameters.AddWithValue("$source", source.Id);

            var rows = new List<(long Id, string RelativePath, string FileName, string Extension, bool WasExcluded)>();
            await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                    rows.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4) == 1));
            }

            var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                processedPaths.Add(row.RelativePath);
                examined++;
                var policyAssessment = DiscoveryPolicy.Evaluate(row.RelativePath, source.Exclusions);
                var hasOverride = overrides.TryGetValue(row.RelativePath, out var overrideAction);
                var assessment = hasOverride
                    ? new DiscoveryAssessment(overrideAction!, overrideAction!.Equals("Include", StringComparison.OrdinalIgnoreCase) ? "Manual include override" : "Manual exclusion override", DiscoveryPolicy.ManualIncludeMediaKind(row.RelativePath))
                    : policyAssessment;
                var isExcluded = assessment.Decision.Equals("Exclude", StringComparison.OrdinalIgnoreCase);

                if (isExcluded) excluded++;
                else if (assessment.Decision.Equals("Review", StringComparison.OrdinalIgnoreCase)) review++;
                else included++;
                if (row.WasExcluded && !isExcluded) restored++;

                var update = connection.CreateCommand();
                update.Transaction = tx;
                update.CommandText = """
                    UPDATE resources
                    SET discovery_status = $status,
                        discovery_decision = $decision,
                        discovery_reason = $reason,
                        media_kind = CASE WHEN $refreshKind = 1 THEN $mediaKind ELSE media_kind END,
                        is_policy_excluded = $excluded,
                        catalogue_lane = $lane,
                        catalogue_lane_reason = $laneReason,
                        fingerprint_status = CASE
                            WHEN $decision <> 'Include' OR $learningLane = 0 THEN 'Deferred'
                            WHEN quick_fingerprint<>'' THEN 'Complete'
                            ELSE 'Pending' END,
                        analysis_needed = CASE
                            WHEN $decision <> 'Include' OR $learningLane = 0 OR is_ignored=1 OR is_available=0 THEN 0
                            WHEN analysis_status IN ('Analysed','Limited') THEN 0
                            ELSE 1 END
                    WHERE id = $id;
                    """;
                update.Parameters.AddWithValue("$status", isExcluded ? "Excluded" : "Discovered");
                update.Parameters.AddWithValue("$decision", assessment.Decision);
                update.Parameters.AddWithValue("$reason", assessment.Reason);
                update.Parameters.AddWithValue("$mediaKind", assessment.MediaKind ?? DiscoveryPolicy.ManualIncludeMediaKind(row.RelativePath));
                update.Parameters.AddWithValue("$refreshKind", assessment.MediaKind is not null || (hasOverride && overrideAction!.Equals("Include", StringComparison.OrdinalIgnoreCase)) ? 1 : 0);
                update.Parameters.AddWithValue("$excluded", isExcluded ? 1 : 0);
                update.Parameters.AddWithValue("$lane", isExcluded ? "None" : assessment.CatalogueLane);
                update.Parameters.AddWithValue("$laneReason", isExcluded ? assessment.Reason : assessment.LaneReason);
                update.Parameters.AddWithValue("$learningLane", !isExcluded && (assessment.CatalogueLane.Equals("Learning", StringComparison.OrdinalIgnoreCase) || assessment.CatalogueLane.Equals("Both", StringComparison.OrdinalIgnoreCase)) ? 1 : 0);
                update.Parameters.AddWithValue("$id", row.Id);
                await update.ExecuteNonQueryAsync(cancellationToken);

                if (isExcluded)
                {
                    await UpsertDiscoveryExclusionAsync(connection, tx, source.Id, row.RelativePath, row.FileName, row.Extension, assessment.Reason, now, cancellationToken);
                }
                else
                {
                    var remove = connection.CreateCommand();
                    remove.Transaction = tx;
                    remove.CommandText = "DELETE FROM discovery_exclusions WHERE library_source_id = $source AND relative_path = $relative;";
                    remove.Parameters.AddWithValue("$source", source.Id);
                    remove.Parameters.AddWithValue("$relative", row.RelativePath);
                    await remove.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            // v0.4.4 also recalibrates rows that exist only in discovery_exclusions. Older releases
            // could not restore a false-positive exclusion without rescanning the drive.
            var exclusionRead = connection.CreateCommand();
            exclusionRead.Transaction = tx;
            exclusionRead.CommandText = """
                SELECT relative_path, file_name, extension, reason
                FROM discovery_exclusions
                WHERE library_source_id = $source;
                """;
            exclusionRead.Parameters.AddWithValue("$source", source.Id);
            var exclusionRows = new List<(string RelativePath, string FileName, string Extension, string Reason)>();
            await using (var exclusionReader = await exclusionRead.ExecuteReaderAsync(cancellationToken))
            {
                while (await exclusionReader.ReadAsync(cancellationToken))
                    exclusionRows.Add((exclusionReader.GetString(0), exclusionReader.GetString(1), exclusionReader.GetString(2), exclusionReader.GetString(3)));
            }

            foreach (var row in exclusionRows)
            {
                if (processedPaths.Contains(row.RelativePath)) continue;
                examined++;

                var policyAssessment = DiscoveryPolicy.Evaluate(row.RelativePath, source.Exclusions);
                var hasOverride = overrides.TryGetValue(row.RelativePath, out var overrideAction);
                var assessment = hasOverride
                    ? new DiscoveryAssessment(overrideAction!, overrideAction!.Equals("Include", StringComparison.OrdinalIgnoreCase) ? "Manual include override" : "Manual exclusion override", DiscoveryPolicy.ManualIncludeMediaKind(row.RelativePath))
                    : policyAssessment;

                if (assessment.Decision.Equals("Exclude", StringComparison.OrdinalIgnoreCase))
                {
                    excluded++;
                    await UpsertDiscoveryExclusionAsync(connection, tx, source.Id, row.RelativePath, row.FileName, row.Extension, assessment.Reason, now, cancellationToken);
                    continue;
                }

                // If the file is still available, restore it into the active/review catalogue using the
                // new policy decision. Missing drive items remain safely recorded as exclusions.
                if (!File.Exists(ResolveSourcePath(source, row.RelativePath)))
                {
                    excluded++;
                    continue;
                }

                if (assessment.Decision.Equals("Review", StringComparison.OrdinalIgnoreCase)) review++;
                else included++;
                restored++;

                await EnsurePolicyResourceAsync(connection, tx, source, row.RelativePath, assessment, now, cancellationToken);

                var remove = connection.CreateCommand();
                remove.Transaction = tx;
                remove.CommandText = "DELETE FROM discovery_exclusions WHERE library_source_id = $source AND relative_path = $relative;";
                remove.Parameters.AddWithValue("$source", source.Id);
                remove.Parameters.AddWithValue("$relative", row.RelativePath);
                await remove.ExecuteNonQueryAsync(cancellationToken);
            }

            await DetectLearningCollectionCandidatesAsync(connection, tx, source, now, cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return new LibraryCleanupResult(sourceId, examined, excluded, restored, review, included);
    }

    public async Task<IReadOnlyList<DiscoveryReviewItem>> GetDiscoveryReviewItemsAsync(long? sourceId = null, string state = "Pending", int limit = 500, CancellationToken cancellationToken = default)
    {
        var normalizedState = string.IsNullOrWhiteSpace(state) ? "Pending" : state.Trim();
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            WITH review_items AS (
                SELECT de.library_source_id, de.relative_path, de.file_name, de.extension, de.reason,
                       'AutoExcluded' AS record_kind
                FROM discovery_exclusions de
                UNION ALL
                SELECT r.library_source_id, r.relative_path, r.file_name, r.extension, r.discovery_reason,
                       'Review' AS record_kind
                FROM resources r
                WHERE r.discovery_decision = 'Review'
                  AND r.catalogue_lane IN ('Learning','Both')
                  AND NOT EXISTS (
                      SELECT 1 FROM discovery_exclusions de
                      WHERE de.library_source_id = r.library_source_id AND de.relative_path = r.relative_path
                  )
                UNION ALL
                SELECT r.library_source_id, r.relative_path, r.file_name, r.extension, r.discovery_reason,
                       'ManualInclude' AS record_kind
                FROM resources r
                JOIN discovery_overrides oi
                  ON oi.library_source_id = r.library_source_id AND oi.relative_path = r.relative_path AND oi.action = 'Include'
                WHERE r.discovery_decision <> 'Review'
                  AND NOT EXISTS (
                      SELECT 1 FROM discovery_exclusions de
                      WHERE de.library_source_id = r.library_source_id AND de.relative_path = r.relative_path
                  )
            )
            SELECT ri.library_source_id, ls.name, ri.relative_path, ri.file_name, ri.extension, ri.reason,
                   COALESCE(o.action, ''), CASE WHEN r.id IS NULL THEN 0 ELSE 1 END
            FROM review_items ri
            JOIN library_sources ls ON ls.id = ri.library_source_id
            LEFT JOIN discovery_overrides o
              ON o.library_source_id = ri.library_source_id AND o.relative_path = ri.relative_path
            LEFT JOIN resources r
              ON r.library_source_id = ri.library_source_id AND r.relative_path = ri.relative_path
            WHERE ($source = 0 OR ri.library_source_id = $source)
              AND (
                   $state = 'All'
                   OR ($state = 'Pending' AND ri.record_kind = 'Review' AND o.action IS NULL
                       AND NOT EXISTS (
                           SELECT 1 FROM learning_collection_candidates lc
                           WHERE lc.library_source_id = ri.library_source_id
                             AND (ri.relative_path = lc.root_relative_path OR substr(ri.relative_path, 1, length(lc.root_relative_path) + 1) = lc.root_relative_path || '/')
                       ))
                   OR ($state = 'Archives' AND ri.record_kind = 'Review' AND o.action IS NULL
                       AND lower(ri.extension) IN ('.zip','.rar','.7z')
                       AND NOT EXISTS (
                           SELECT 1 FROM learning_collection_candidates lc
                           WHERE lc.library_source_id = ri.library_source_id
                             AND (ri.relative_path = lc.root_relative_path OR substr(ri.relative_path, 1, length(lc.root_relative_path) + 1) = lc.root_relative_path || '/')
                       ))
                   OR ($state = 'AutoExcluded' AND ri.record_kind = 'AutoExcluded' AND o.action IS NULL
                       AND NOT EXISTS (
                           SELECT 1 FROM learning_collection_candidates lc
                           WHERE lc.library_source_id = ri.library_source_id
                             AND (ri.relative_path = lc.root_relative_path OR substr(ri.relative_path, 1, length(lc.root_relative_path) + 1) = lc.root_relative_path || '/')
                       ))
                   OR ($state = 'KeptExcluded' AND o.action = 'Exclude')
                   OR ($state = 'Included' AND o.action = 'Include')
                   OR ($state = 'Collections' AND 0)
              )
            ORDER BY ls.name COLLATE NOCASE, ri.relative_path COLLATE NOCASE
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$source", sourceId ?? 0);
        command.Parameters.AddWithValue("$state", normalizedState);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 2000));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<DiscoveryReviewItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var relative = reader.GetString(2);
            var reason = reader.GetString(5);
            result.Add(new DiscoveryReviewItem(
                reader.GetInt64(0), reader.GetString(1), relative, reader.GetString(3), reader.GetString(4), reason,
                reader.GetString(6), reader.GetInt64(7) == 1,
                DiscoveryPolicy.IsLearningCollectionCandidate(relative, reason)));
        }
        return result;
    }

    public async Task<DiscoveryReviewSummary> GetDiscoveryReviewSummaryAsync(long? sourceId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            WITH review_items AS (
                SELECT de.library_source_id, de.relative_path, 'AutoExcluded' AS record_kind
                FROM discovery_exclusions de
                UNION ALL
                SELECT r.library_source_id, r.relative_path, 'Review' AS record_kind
                FROM resources r
                WHERE r.discovery_decision = 'Review'
                  AND r.catalogue_lane IN ('Learning','Both')
                  AND NOT EXISTS (
                      SELECT 1 FROM discovery_exclusions de
                      WHERE de.library_source_id = r.library_source_id AND de.relative_path = r.relative_path
                  )
                UNION ALL
                SELECT r.library_source_id, r.relative_path, 'ManualInclude' AS record_kind
                FROM resources r
                JOIN discovery_overrides oi
                  ON oi.library_source_id = r.library_source_id AND oi.relative_path = r.relative_path AND oi.action = 'Include'
                WHERE r.discovery_decision <> 'Review'
                  AND NOT EXISTS (
                      SELECT 1 FROM discovery_exclusions de
                      WHERE de.library_source_id = r.library_source_id AND de.relative_path = r.relative_path
                  )
            ),
            filtered AS (
                SELECT ri.library_source_id, ri.relative_path, ri.record_kind, COALESCE(o.action, '') AS override_action,
                       CASE WHEN EXISTS (
                           SELECT 1 FROM learning_collection_candidates lc
                           WHERE lc.library_source_id = ri.library_source_id
                             AND (ri.relative_path = lc.root_relative_path OR substr(ri.relative_path, 1, length(lc.root_relative_path) + 1) = lc.root_relative_path || '/')
                       ) THEN 1 ELSE 0 END AS collection_managed
                FROM review_items ri
                LEFT JOIN discovery_overrides o
                  ON o.library_source_id = ri.library_source_id AND o.relative_path = ri.relative_path
                WHERE ($source = 0 OR ri.library_source_id = $source)
            ),
            collection_paths AS (
                SELECT r.library_source_id, r.relative_path
                FROM resources r
                WHERE ($source = 0 OR r.library_source_id = $source)
                  AND EXISTS (
                      SELECT 1 FROM learning_collection_candidates lc
                      WHERE lc.library_source_id = r.library_source_id
                        AND (r.relative_path = lc.root_relative_path OR substr(r.relative_path, 1, length(lc.root_relative_path) + 1) = lc.root_relative_path || '/')
                  )
                UNION
                SELECT de.library_source_id, de.relative_path
                FROM discovery_exclusions de
                WHERE ($source = 0 OR de.library_source_id = $source)
                  AND EXISTS (
                      SELECT 1 FROM learning_collection_candidates lc
                      WHERE lc.library_source_id = de.library_source_id
                        AND (de.relative_path = lc.root_relative_path OR substr(de.relative_path, 1, length(lc.root_relative_path) + 1) = lc.root_relative_path || '/')
                  )
            )
            SELECT
                COALESCE(SUM(CASE WHEN record_kind = 'Review' AND override_action = '' AND collection_managed = 0 THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN record_kind = 'AutoExcluded' AND override_action = '' AND collection_managed = 0 THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN override_action = 'Exclude' THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN override_action = 'Include' THEN 1 ELSE 0 END), 0),
                COUNT(*),
                (SELECT COUNT(*) FROM learning_collection_candidates lc WHERE ($source = 0 OR lc.library_source_id = $source)),
                (SELECT COUNT(*) FROM collection_paths),
                COALESCE(SUM(CASE WHEN record_kind = 'Review' AND override_action = '' AND collection_managed = 0
                                      AND (lower(relative_path) GLOB '*.zip' OR lower(relative_path) GLOB '*.rar' OR lower(relative_path) GLOB '*.7z')
                                 THEN 1 ELSE 0 END), 0)
            FROM filtered;
            """;
        command.Parameters.AddWithValue("$source", sourceId ?? 0);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return new DiscoveryReviewSummary(0, 0, 0, 0, 0, 0, 0, 0);
        return new DiscoveryReviewSummary(
            Convert.ToInt32(reader.GetInt64(0)),
            Convert.ToInt32(reader.GetInt64(1)),
            Convert.ToInt32(reader.GetInt64(2)),
            Convert.ToInt32(reader.GetInt64(3)),
            Convert.ToInt32(reader.GetInt64(4)),
            Convert.ToInt32(reader.GetInt64(5)),
            Convert.ToInt32(reader.GetInt64(6)),
            Convert.ToInt32(reader.GetInt64(7)));
    }

    public async Task<DiscoveryBulkActionResult> ApplyDiscoveryOverrideAsync(long sourceId, IReadOnlyList<string> relativePaths, string action, CancellationToken cancellationToken = default)
    {
        if (!action.Equals("Include", StringComparison.OrdinalIgnoreCase) && !action.Equals("Exclude", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Discovery override must be Include or Exclude.");

        var source = await GetSourceAsync(sourceId, cancellationToken) ?? throw new InvalidOperationException("Library source not found.");
        var uniquePaths = relativePaths.Select(DiscoveryPolicy.Normalize).Select(x => x.Trim('/')).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var now = DateTimeOffset.UtcNow.ToString("O");
        var created = 0;

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();

        foreach (var relativePath in uniquePaths)
        {
            var overrideCommand = connection.CreateCommand();
            overrideCommand.Transaction = tx;
            overrideCommand.CommandText = """
                INSERT INTO discovery_overrides(library_source_id, relative_path, action, note, updated_utc)
                VALUES ($source, $relative, $action, $note, $now)
                ON CONFLICT(library_source_id, relative_path) DO UPDATE SET
                    action = excluded.action,
                    note = excluded.note,
                    updated_utc = excluded.updated_utc;
                """;
            overrideCommand.Parameters.AddWithValue("$source", sourceId);
            overrideCommand.Parameters.AddWithValue("$relative", relativePath);
            overrideCommand.Parameters.AddWithValue("$action", action.Equals("Include", StringComparison.OrdinalIgnoreCase) ? "Include" : "Exclude");
            overrideCommand.Parameters.AddWithValue("$note", action.Equals("Include", StringComparison.OrdinalIgnoreCase) ? "User included from discovery review" : "User kept excluded from discovery review");
            overrideCommand.Parameters.AddWithValue("$now", now);
            await overrideCommand.ExecuteNonQueryAsync(cancellationToken);

            if (action.Equals("Include", StringComparison.OrdinalIgnoreCase))
            {
                if (await EnsureManualResourceAsync(connection, tx, source, relativePath, now, cancellationToken)) created++;
                var include = connection.CreateCommand();
                include.Transaction = tx;
                include.CommandText = """
                    UPDATE resources
                    SET discovery_status = 'Discovered', discovery_decision = 'Include', discovery_reason = 'Manual include override',
                        media_kind = $kind, is_policy_excluded = 0, is_available = 1, last_seen_utc = $now,
                        catalogue_lane='Learning', catalogue_lane_reason='Manual include override into learning catalogue',
                        fingerprint_status = CASE WHEN quick_fingerprint<>'' THEN 'Complete' ELSE 'Pending' END,
                        analysis_needed = CASE WHEN analysis_status IN ('Analysed','Limited') THEN 0 ELSE 1 END
                    WHERE library_source_id = $source AND relative_path = $relative;
                    """;
                include.Parameters.AddWithValue("$kind", DiscoveryPolicy.ManualIncludeMediaKind(relativePath));
                include.Parameters.AddWithValue("$now", now);
                include.Parameters.AddWithValue("$source", sourceId);
                include.Parameters.AddWithValue("$relative", relativePath);
                await include.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                var exclude = connection.CreateCommand();
                exclude.Transaction = tx;
                exclude.CommandText = """
                    UPDATE resources
                    SET discovery_status = 'Excluded', discovery_decision = 'Exclude', discovery_reason = 'Manual exclusion override', is_policy_excluded = 1,
                        catalogue_lane='None', catalogue_lane_reason='Manual exclusion override', fingerprint_status='Deferred', analysis_needed=0
                    WHERE library_source_id = $source AND relative_path = $relative;
                    """;
                exclude.Parameters.AddWithValue("$source", sourceId);
                exclude.Parameters.AddWithValue("$relative", relativePath);
                await exclude.ExecuteNonQueryAsync(cancellationToken);

                _ = ResolveSourcePath(source, relativePath); // path-safety validation only
                var fileName = Path.GetFileName(relativePath);
                var extension = Path.GetExtension(relativePath).ToLowerInvariant();
                await UpsertDiscoveryExclusionAsync(connection, tx, sourceId, relativePath, fileName, extension, "Manual exclusion override", now, cancellationToken);
            }
        }

        await tx.CommitAsync(cancellationToken);
        return new DiscoveryBulkActionResult(uniquePaths.Length, uniquePaths.Length, created, 0, 0);
    }

    public async Task<DiscoveryBulkActionResult> PurgeDiscoveryPathsAsync(long sourceId, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
    {
        var source = await GetSourceAsync(sourceId, cancellationToken) ?? throw new InvalidOperationException("Library source not found.");
        var uniquePaths = relativePaths.Select(DiscoveryPolicy.Normalize).Select(x => x.Trim('/')).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var now = DateTimeOffset.UtcNow.ToString("O");
        var purged = 0;
        var protectedCount = 0;

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();

        foreach (var relativePath in uniquePaths)
        {
            var overrideCommand = connection.CreateCommand();
            overrideCommand.Transaction = tx;
            overrideCommand.CommandText = """
                INSERT INTO discovery_overrides(library_source_id, relative_path, action, note, updated_utc)
                VALUES ($source, $relative, 'Exclude', 'Purged from active catalogue; keep excluded on future scans', $now)
                ON CONFLICT(library_source_id, relative_path) DO UPDATE SET
                    action = 'Exclude', note = excluded.note, updated_utc = excluded.updated_utc;
                """;
            overrideCommand.Parameters.AddWithValue("$source", sourceId);
            overrideCommand.Parameters.AddWithValue("$relative", relativePath);
            overrideCommand.Parameters.AddWithValue("$now", now);
            await overrideCommand.ExecuteNonQueryAsync(cancellationToken);

            var resourceIdCommand = connection.CreateCommand();
            resourceIdCommand.Transaction = tx;
            resourceIdCommand.CommandText = "SELECT id FROM resources WHERE library_source_id = $source AND relative_path = $relative LIMIT 1;";
            resourceIdCommand.Parameters.AddWithValue("$source", sourceId);
            resourceIdCommand.Parameters.AddWithValue("$relative", relativePath);
            var resourceScalar = await resourceIdCommand.ExecuteScalarAsync(cancellationToken);

            if (resourceScalar is not null)
            {
                var resourceId = Convert.ToInt64(resourceScalar);
                var protectedCommand = connection.CreateCommand();
                protectedCommand.Transaction = tx;
                protectedCommand.CommandText = """
                    SELECT CASE WHEN
                        EXISTS(SELECT 1 FROM learning_modules WHERE primary_resource_id = $resource)
                        OR EXISTS(SELECT 1 FROM learning_source_refs WHERE resource_id = $resource)
                        OR EXISTS(SELECT 1 FROM learning_module_sources WHERE resource_id = $resource)
                        OR EXISTS(SELECT 1 FROM learning_candidate_sources WHERE resource_id = $resource)
                    THEN 1 ELSE 0 END;
                    """;
                protectedCommand.Parameters.AddWithValue("$resource", resourceId);
                var isProtected = Convert.ToInt64(await protectedCommand.ExecuteScalarAsync(cancellationToken) ?? 0) == 1;

                if (isProtected)
                {
                    protectedCount++;
                    var suppress = connection.CreateCommand();
                    suppress.Transaction = tx;
                    suppress.CommandText = "UPDATE resources SET discovery_status = 'Excluded', discovery_decision = 'Exclude', discovery_reason = 'Manual exclusion override', is_policy_excluded = 1 WHERE id = $id;";
                    suppress.Parameters.AddWithValue("$id", resourceId);
                    await suppress.ExecuteNonQueryAsync(cancellationToken);
                }
                else
                {
                    var delete = connection.CreateCommand();
                    delete.Transaction = tx;
                    delete.CommandText = "DELETE FROM resources WHERE id = $id;";
                    delete.Parameters.AddWithValue("$id", resourceId);
                    purged += await delete.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            _ = ResolveSourcePath(source, relativePath); // path-safety validation only
            await UpsertDiscoveryExclusionAsync(connection, tx, sourceId, relativePath, Path.GetFileName(relativePath), Path.GetExtension(relativePath).ToLowerInvariant(), "Kept excluded / purged from active catalogue", now, cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return new DiscoveryBulkActionResult(uniquePaths.Length, uniquePaths.Length, 0, purged, protectedCount);
    }

    public async Task<LearningCollectionCandidate> AddLearningCollectionCandidateAsync(long sourceId, string rootRelativePath, string? displayName = null, CancellationToken cancellationToken = default)
    {
        var source = await GetSourceAsync(sourceId, cancellationToken) ?? throw new InvalidOperationException("Library source not found.");
        var relative = DiscoveryPolicy.Normalize(rootRelativePath).Trim('/');
        if (relative.Length == 0) throw new InvalidOperationException("Choose a folder inside the library source for the Learning Collection.");
        var fullPath = ResolveSourcePath(source, relative);
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException($"Learning Collection folder is unavailable: {fullPath}");

        var name = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileName(fullPath) : displayName.Trim();
        var prefix = relative + "/%";
        var now = DateTimeOffset.UtcNow.ToString("O");

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();

        var countCommand = connection.CreateCommand();
        countCommand.Transaction = tx;
        countCommand.CommandText = """
            SELECT COUNT(*) FROM (
                SELECT relative_path FROM resources WHERE library_source_id = $source AND (relative_path = $relative OR relative_path LIKE $prefix)
                UNION
                SELECT relative_path FROM discovery_exclusions WHERE library_source_id = $source AND (relative_path = $relative OR relative_path LIKE $prefix)
            );
            """;
        countCommand.Parameters.AddWithValue("$source", sourceId);
        countCommand.Parameters.AddWithValue("$relative", relative);
        countCommand.Parameters.AddWithValue("$prefix", prefix);
        var itemCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken) ?? 0);

        var upsert = connection.CreateCommand();
        upsert.Transaction = tx;
        upsert.CommandText = """
            INSERT INTO learning_collection_candidates(library_source_id, root_relative_path, display_name, status, item_count, created_utc, updated_utc)
            VALUES ($source, $relative, $name, 'Candidate', $count, $now, $now)
            ON CONFLICT(library_source_id, root_relative_path) DO UPDATE SET
                display_name = excluded.display_name,
                status = 'Candidate',
                item_count = excluded.item_count,
                updated_utc = excluded.updated_utc;
            """;
        upsert.Parameters.AddWithValue("$source", sourceId);
        upsert.Parameters.AddWithValue("$relative", relative);
        upsert.Parameters.AddWithValue("$name", name);
        upsert.Parameters.AddWithValue("$count", itemCount);
        upsert.Parameters.AddWithValue("$now", now);
        await upsert.ExecuteNonQueryAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var candidates = await GetLearningCollectionCandidatesAsync(sourceId, cancellationToken);
        return candidates.First(x => x.RootRelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<LearningCollectionCandidate>> GetLearningCollectionCandidatesAsync(long? sourceId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT lc.id, lc.library_source_id, ls.name, lc.root_relative_path, lc.display_name, lc.status,
                   lc.item_count, lc.created_utc, lc.updated_utc
            FROM learning_collection_candidates lc
            JOIN library_sources ls ON ls.id = lc.library_source_id
            WHERE ($source = 0 OR lc.library_source_id = $source)
            ORDER BY lc.display_name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$source", sourceId ?? 0);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<LearningCollectionCandidate>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new LearningCollectionCandidate(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                Convert.ToInt32(reader.GetInt64(6)), DateTimeOffset.Parse(reader.GetString(7)), DateTimeOffset.Parse(reader.GetString(8))));
        }
        return result;
    }

    private static async Task EnsurePolicyResourceAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        LibrarySource source,
        string relativePath,
        DiscoveryAssessment assessment,
        string now,
        CancellationToken cancellationToken)
    {
        var fullPath = ResolveSourcePath(source, relativePath);
        if (!File.Exists(fullPath)) return;
        var info = new FileInfo(fullPath);
        var kind = assessment.MediaKind ?? DiscoveryPolicy.ManualIncludeMediaKind(relativePath);

        var upsert = connection.CreateCommand();
        upsert.Transaction = tx;
        upsert.CommandText = """
            INSERT INTO resources(
                library_source_id, relative_path, file_name, extension, size_bytes, last_write_utc,
                media_kind, resource_type, discovery_status, analysis_status, is_available, is_ignored,
                last_scan_token, discovered_utc, last_seen_utc, discovery_decision, discovery_reason, is_policy_excluded,
                catalogue_lane, catalogue_lane_reason, fingerprint_status, analysis_needed)
            VALUES ($source, $relative, $name, $extension, $size, $modified,
                $kind, 'Unknown', 'Discovered', 'NotAnalysed', 1, 0,
                NULL, $now, $now, $decision, $reason, 0, $lane, $laneReason, $fingerprintStatus, $analysisNeeded)
            ON CONFLICT(library_source_id, relative_path) DO UPDATE SET
                file_name = excluded.file_name,
                extension = excluded.extension,
                size_bytes = excluded.size_bytes,
                last_write_utc = excluded.last_write_utc,
                media_kind = excluded.media_kind,
                discovery_status = 'Discovered',
                is_available = 1,
                last_seen_utc = excluded.last_seen_utc,
                discovery_decision = excluded.discovery_decision,
                discovery_reason = excluded.discovery_reason,
                is_policy_excluded = 0,
                catalogue_lane = excluded.catalogue_lane,
                catalogue_lane_reason = excluded.catalogue_lane_reason,
                fingerprint_status = excluded.fingerprint_status,
                analysis_needed = excluded.analysis_needed;
            """;
        upsert.Parameters.AddWithValue("$source", source.Id);
        upsert.Parameters.AddWithValue("$relative", relativePath);
        upsert.Parameters.AddWithValue("$name", info.Name);
        upsert.Parameters.AddWithValue("$extension", info.Extension.ToLowerInvariant());
        upsert.Parameters.AddWithValue("$size", info.Length);
        upsert.Parameters.AddWithValue("$modified", info.LastWriteTimeUtc.ToString("O"));
        upsert.Parameters.AddWithValue("$kind", kind);
        upsert.Parameters.AddWithValue("$now", now);
        upsert.Parameters.AddWithValue("$decision", assessment.Decision);
        upsert.Parameters.AddWithValue("$reason", assessment.Reason);
        upsert.Parameters.AddWithValue("$lane", assessment.CatalogueLane);
        upsert.Parameters.AddWithValue("$laneReason", assessment.LaneReason);
        var learningEligible = assessment.Decision.Equals("Include", StringComparison.OrdinalIgnoreCase)
                               && (assessment.CatalogueLane.Equals("Learning", StringComparison.OrdinalIgnoreCase) || assessment.CatalogueLane.Equals("Both", StringComparison.OrdinalIgnoreCase));
        upsert.Parameters.AddWithValue("$fingerprintStatus", learningEligible ? "Pending" : "Deferred");
        upsert.Parameters.AddWithValue("$analysisNeeded", learningEligible ? 1 : 0);
        await upsert.ExecuteNonQueryAsync(cancellationToken);

        var subjects = connection.CreateCommand();
        subjects.Transaction = tx;
        subjects.CommandText = """
            INSERT OR IGNORE INTO resource_subjects(resource_id, subject_id)
            SELECT r.id, ss.subject_id
            FROM resources r
            JOIN source_subjects ss ON ss.library_source_id = r.library_source_id
            WHERE r.library_source_id = $source AND r.relative_path = $relative;
            """;
        subjects.Parameters.AddWithValue("$source", source.Id);
        subjects.Parameters.AddWithValue("$relative", relativePath);
        await subjects.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DetectLearningCollectionCandidatesAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        LibrarySource source,
        string now,
        CancellationToken cancellationToken)
    {
        // Auto-detected candidates are recalculated; user-saved Candidate rows are preserved.
        var clearDetected = connection.CreateCommand();
        clearDetected.Transaction = tx;
        clearDetected.CommandText = "DELETE FROM learning_collection_candidates WHERE library_source_id = $source AND status = 'Detected';";
        clearDetected.Parameters.AddWithValue("$source", source.Id);
        await clearDetected.ExecuteNonQueryAsync(cancellationToken);

        var read = connection.CreateCommand();
        read.Transaction = tx;
        read.CommandText = """
            SELECT de.relative_path
            FROM discovery_exclusions de
            LEFT JOIN discovery_overrides o
              ON o.library_source_id = de.library_source_id AND o.relative_path = de.relative_path
            WHERE de.library_source_id = $source
              AND de.reason LIKE '%learning collection candidate%'
              AND COALESCE(o.action, '') <> 'Include';
            """;
        read.Parameters.AddWithValue("$source", source.Id);

        var grouped = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var relativePath = reader.GetString(0);
                var groupingPrefix = DiscoveryPolicy.GetLearningCollectionGroupingPrefix(relativePath);
                if (string.IsNullOrWhiteSpace(groupingPrefix)) continue;
                if (!grouped.TryGetValue(groupingPrefix, out var paths))
                {
                    paths = [];
                    grouped[groupingPrefix] = paths;
                }
                paths.Add(relativePath);
            }
        }

        foreach (var (groupingPrefix, paths) in grouped)
        {
            // A handful of HTML files can simply be reviewed normally. Larger groups are where a
            // package-level Learning Collection is materially more useful than file-by-file review.
            if (paths.Count < 5) continue;

            var rootRelativePath = CommonDirectory(paths, groupingPrefix);
            if (string.IsNullOrWhiteSpace(rootRelativePath)) continue;
            var fullRoot = ResolveSourcePath(source, rootRelativePath);
            if (!Directory.Exists(fullRoot)) continue;

            var prefix = rootRelativePath + "/%";
            var count = connection.CreateCommand();
            count.Transaction = tx;
            count.CommandText = """
                SELECT COUNT(*) FROM (
                    SELECT relative_path FROM resources
                    WHERE library_source_id = $source AND (relative_path = $relative OR relative_path LIKE $prefix)
                    UNION
                    SELECT relative_path FROM discovery_exclusions
                    WHERE library_source_id = $source AND (relative_path = $relative OR relative_path LIKE $prefix)
                );
                """;
            count.Parameters.AddWithValue("$source", source.Id);
            count.Parameters.AddWithValue("$relative", rootRelativePath);
            count.Parameters.AddWithValue("$prefix", prefix);
            var itemCount = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken) ?? 0);

            var groupingName = Path.GetFileName(groupingPrefix.Replace('/', Path.DirectorySeparatorChar));
            var rootName = Path.GetFileName(rootRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var displayName = rootName.Equals(groupingName, StringComparison.OrdinalIgnoreCase)
                ? rootName
                : $"{groupingName} / {rootName}";

            var upsert = connection.CreateCommand();
            upsert.Transaction = tx;
            upsert.CommandText = """
                INSERT INTO learning_collection_candidates(
                    library_source_id, root_relative_path, display_name, status, item_count, created_utc, updated_utc)
                VALUES ($source, $relative, $name, 'Detected', $count, $now, $now)
                ON CONFLICT(library_source_id, root_relative_path) DO UPDATE SET
                    item_count = excluded.item_count,
                    updated_utc = excluded.updated_utc;
                """;
            upsert.Parameters.AddWithValue("$source", source.Id);
            upsert.Parameters.AddWithValue("$relative", rootRelativePath);
            upsert.Parameters.AddWithValue("$name", displayName);
            upsert.Parameters.AddWithValue("$count", itemCount);
            upsert.Parameters.AddWithValue("$now", now);
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string CommonDirectory(IReadOnlyList<string> relativePaths, string minimumPrefix)
    {
        if (relativePaths.Count == 0) return minimumPrefix;

        var directorySegments = relativePaths
            .Select(path => DiscoveryPolicy.Normalize(path).Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).SkipLast(1).ToArray())
            .ToArray();
        var minLength = directorySegments.Min(x => x.Length);
        var common = new List<string>();
        for (var i = 0; i < minLength; i++)
        {
            var value = directorySegments[0][i];
            if (directorySegments.Any(x => !x[i].Equals(value, StringComparison.OrdinalIgnoreCase))) break;
            common.Add(value);
        }

        var minimumSegments = DiscoveryPolicy.Normalize(minimumPrefix).Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (common.Count < minimumSegments.Length)
            return string.Join('/', minimumSegments);
        return string.Join('/', common);
    }

    private static async Task InsertDiscoveryScanChangeAsync(
        SqliteConnection connection, SqliteTransaction tx, long scanRunId, long sourceId, string relativePath,
        string fileName, string changeType, string discoveryDecision, string discoveryReason, string catalogueLane, string laneReason, string now,
        CancellationToken cancellationToken)
    {
        if (scanRunId <= 0) return;
        var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT OR IGNORE INTO discovery_scan_changes(
                discovery_scan_run_id, library_source_id, relative_path, file_name, change_type,
                discovery_decision, discovery_reason, catalogue_lane, lane_reason, created_utc)
            VALUES ($run,$source,$relative,$name,$change,$decision,$reason,$lane,$laneReason,$now);
            """;
        command.Parameters.AddWithValue("$run", scanRunId);
        command.Parameters.AddWithValue("$source", sourceId);
        command.Parameters.AddWithValue("$relative", relativePath);
        command.Parameters.AddWithValue("$name", fileName);
        command.Parameters.AddWithValue("$change", changeType);
        command.Parameters.AddWithValue("$decision", discoveryDecision ?? string.Empty);
        command.Parameters.AddWithValue("$reason", discoveryReason ?? string.Empty);
        command.Parameters.AddWithValue("$lane", catalogueLane ?? string.Empty);
        command.Parameters.AddWithValue("$laneReason", laneReason ?? string.Empty);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertDiscoveryExclusionAsync(SqliteConnection connection, SqliteTransaction tx, long sourceId, string relativePath, string fileName, string extension, string reason, string now, CancellationToken cancellationToken)
    {
        var exclusion = connection.CreateCommand();
        exclusion.Transaction = tx;
        exclusion.CommandText = """
            INSERT INTO discovery_exclusions(library_source_id, relative_path, file_name, extension, reason, last_scan_token, last_seen_utc)
            VALUES ($source, $relative, $name, $extension, $reason, NULL, $now)
            ON CONFLICT(library_source_id, relative_path) DO UPDATE SET
                file_name = excluded.file_name,
                extension = excluded.extension,
                reason = excluded.reason,
                last_seen_utc = excluded.last_seen_utc;
            """;
        exclusion.Parameters.AddWithValue("$source", sourceId);
        exclusion.Parameters.AddWithValue("$relative", relativePath);
        exclusion.Parameters.AddWithValue("$name", fileName);
        exclusion.Parameters.AddWithValue("$extension", extension);
        exclusion.Parameters.AddWithValue("$reason", reason);
        exclusion.Parameters.AddWithValue("$now", now);
        await exclusion.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ResolveSourcePath(LibrarySource source, string relativePath)
    {
        var root = Path.GetFullPath(source.RootPath);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Resource path is outside its library source.");
        return fullPath;
    }

    private static async Task<bool> EnsureManualResourceAsync(SqliteConnection connection, SqliteTransaction tx, LibrarySource source, string relativePath, string now, CancellationToken cancellationToken)
    {
        var existing = connection.CreateCommand();
        existing.Transaction = tx;
        existing.CommandText = "SELECT id FROM resources WHERE library_source_id = $source AND relative_path = $relative LIMIT 1;";
        existing.Parameters.AddWithValue("$source", source.Id);
        existing.Parameters.AddWithValue("$relative", relativePath);
        if (await existing.ExecuteScalarAsync(cancellationToken) is not null) return false;

        var fullPath = ResolveSourcePath(source, relativePath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The selected item is no longer available on the drive.", fullPath);
        var info = new FileInfo(fullPath);

        var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO resources(
                library_source_id, relative_path, file_name, extension, size_bytes, last_write_utc,
                media_kind, resource_type, discovery_status, analysis_status, is_available, is_ignored,
                last_scan_token, discovered_utc, last_seen_utc, discovery_decision, discovery_reason, is_policy_excluded,
                inventory_state, quick_fingerprint, fingerprint_kind, fingerprint_status, fingerprinted_utc, analysis_needed)
            VALUES ($source, $relative, $name, $extension, $size, $modified,
                $kind, 'Unknown', 'Discovered', 'NotAnalysed', 1, 0,
                NULL, $now, $now, 'Include', 'Manual include override', 0,
                'Existing', '', '', 'Pending', NULL, 1);
            """;
        insert.Parameters.AddWithValue("$source", source.Id);
        insert.Parameters.AddWithValue("$relative", relativePath);
        insert.Parameters.AddWithValue("$name", info.Name);
        insert.Parameters.AddWithValue("$extension", info.Extension.ToLowerInvariant());
        insert.Parameters.AddWithValue("$size", info.Length);
        insert.Parameters.AddWithValue("$modified", info.LastWriteTimeUtc.ToString("O"));
        insert.Parameters.AddWithValue("$kind", DiscoveryPolicy.ManualIncludeMediaKind(relativePath));
        insert.Parameters.AddWithValue("$now", now);
        await insert.ExecuteNonQueryAsync(cancellationToken);

        var subjects = connection.CreateCommand();
        subjects.Transaction = tx;
        subjects.CommandText = """
            INSERT OR IGNORE INTO resource_subjects(resource_id, subject_id)
            SELECT r.id, ss.subject_id
            FROM resources r
            JOIN source_subjects ss ON ss.library_source_id = r.library_source_id
            WHERE r.library_source_id = $source AND r.relative_path = $relative;
            """;
        subjects.Parameters.AddWithValue("$source", source.Id);
        subjects.Parameters.AddWithValue("$relative", relativePath);
        await subjects.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    public async Task<ResourceOrganisation?> GetResourceOrganisationAsync(long resourceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.id,
                   COALESCE(GROUP_CONCAT(DISTINCT s.name), '') AS subject,
                   ls.course_level, COALESCE(ro.learning_subject, ''),
                   COALESCE(ro.topic, ''), COALESCE(ro.subtopic, ''), r.resource_type,
                   COALESCE(ro.study_status, 'Unsorted'), COALESCE(ro.notes, ''), ro.updated_utc
            FROM resources r
            JOIN library_sources ls ON ls.id = r.library_source_id
            LEFT JOIN resource_subjects rs ON rs.resource_id = r.id
            LEFT JOIN subjects s ON s.id = rs.subject_id
            LEFT JOIN resource_organisation ro ON ro.resource_id = r.id
            WHERE r.id = $resource
            GROUP BY r.id;
            """;
        command.Parameters.AddWithValue("$resource", resourceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new ResourceOrganisation(
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
            reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8),
            reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)));
    }

    public async Task<ResourceOrganisation> SaveResourceOrganisationAsync(
        long resourceId,
        string learningSubject,
        string topic,
        string subtopic,
        string resourceType,
        string studyStatus,
        string notes,
        CancellationToken cancellationToken = default,
        string organisationSource = "Manual",
        double organisationConfidence = 1.0)
    {
        var allowedStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Unsorted", "Useful", "Study later", "Reference", "Ignore"
        };
        var status = string.IsNullOrWhiteSpace(studyStatus) ? "Unsorted" : studyStatus.Trim();
        if (!allowedStatuses.Contains(status)) throw new InvalidOperationException("Unknown organisation status.");
        var type = string.IsNullOrWhiteSpace(resourceType) ? "Unknown" : resourceType.Trim();
        var now = DateTimeOffset.UtcNow.ToString("O");

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();

        var updateResource = connection.CreateCommand();
        updateResource.Transaction = tx;
        updateResource.CommandText = "UPDATE resources SET resource_type = $type WHERE id = $resource;";
        updateResource.Parameters.AddWithValue("$type", type);
        updateResource.Parameters.AddWithValue("$resource", resourceId);
        if (await updateResource.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new InvalidOperationException("Resource not found.");

        var organisation = connection.CreateCommand();
        organisation.Transaction = tx;
        organisation.CommandText = """
            INSERT INTO resource_organisation(resource_id, learning_subject, topic, subtopic, study_status, notes, updated_utc, organisation_source, organisation_confidence)
            VALUES ($resource, $learningSubject, $topic, $subtopic, $status, $notes, $updated, $organisationSource, $organisationConfidence)
            ON CONFLICT(resource_id) DO UPDATE SET
                learning_subject = excluded.learning_subject,
                topic = excluded.topic,
                subtopic = excluded.subtopic,
                study_status = excluded.study_status,
                notes = excluded.notes,
                updated_utc = excluded.updated_utc,
                organisation_source = excluded.organisation_source,
                organisation_confidence = excluded.organisation_confidence;
            """;
        organisation.Parameters.AddWithValue("$resource", resourceId);
        organisation.Parameters.AddWithValue("$learningSubject", learningSubject.Trim());
        organisation.Parameters.AddWithValue("$topic", topic.Trim());
        organisation.Parameters.AddWithValue("$subtopic", subtopic.Trim());
        organisation.Parameters.AddWithValue("$status", status);
        organisation.Parameters.AddWithValue("$notes", notes.Trim());
        organisation.Parameters.AddWithValue("$updated", now);
        organisation.Parameters.AddWithValue("$organisationSource", string.IsNullOrWhiteSpace(organisationSource) ? "Manual" : organisationSource.Trim());
        organisation.Parameters.AddWithValue("$organisationConfidence", Math.Clamp(organisationConfidence, 0.0, 1.0));
        await organisation.ExecuteNonQueryAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        await SyncPrimaryKnowledgeLinkAsync(resourceId, learningSubject, topic, subtopic, cancellationToken);

        return await GetResourceOrganisationAsync(resourceId, cancellationToken)
            ?? throw new InvalidOperationException("Resource organisation could not be reloaded.");
    }

    public async Task SaveResourceAnalysisAsync(ResourceAnalysis analysis, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();

        var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO resource_analysis(
                resource_id, analysis_status, scanner_kind, scanner_version, scanned_utc,
                suggested_resource_type, suggested_topic, suggested_subtopics_json, document_title, author, page_count, pages_sampled,
                extracted_characters, has_searchable_text, headings_json, keywords_json,
                content_indicators_json, text_preview, error_message, source_size_bytes, source_last_write_utc, analysis_profile, extracted_text)
            VALUES ($resource, $status, $kind, $version, $scanned,
                $suggested, $topic, $subtopics, $title, $author, $pages, $sampled,
                $characters, $searchable, $headings, $keywords,
                $indicators, $preview, $error,
                (SELECT size_bytes FROM resources WHERE id=$resource),
                (SELECT last_write_utc FROM resources WHERE id=$resource),
                'Selected action', $extractedText)
            ON CONFLICT(resource_id) DO UPDATE SET
                analysis_status = excluded.analysis_status,
                scanner_kind = excluded.scanner_kind,
                scanner_version = excluded.scanner_version,
                scanned_utc = excluded.scanned_utc,
                suggested_resource_type = excluded.suggested_resource_type,
                suggested_topic = excluded.suggested_topic,
                suggested_subtopics_json = excluded.suggested_subtopics_json,
                document_title = excluded.document_title,
                author = excluded.author,
                page_count = excluded.page_count,
                pages_sampled = excluded.pages_sampled,
                extracted_characters = excluded.extracted_characters,
                has_searchable_text = excluded.has_searchable_text,
                headings_json = excluded.headings_json,
                keywords_json = excluded.keywords_json,
                content_indicators_json = excluded.content_indicators_json,
                text_preview = excluded.text_preview,
                error_message = excluded.error_message,
                source_size_bytes = excluded.source_size_bytes,
                source_last_write_utc = excluded.source_last_write_utc,
                analysis_profile = excluded.analysis_profile,
                extracted_text = excluded.extracted_text;
            """;
        command.Parameters.AddWithValue("$resource", analysis.ResourceId);
        command.Parameters.AddWithValue("$status", analysis.AnalysisStatus);
        command.Parameters.AddWithValue("$kind", analysis.ScannerKind);
        command.Parameters.AddWithValue("$version", analysis.ScannerVersion);
        command.Parameters.AddWithValue("$scanned", analysis.ScannedUtc.ToString("O"));
        command.Parameters.AddWithValue("$suggested", analysis.SuggestedResourceType);
        command.Parameters.AddWithValue("$topic", analysis.SuggestedTopic);
        command.Parameters.AddWithValue("$subtopics", JsonSerializer.Serialize(analysis.SuggestedSubtopics, JsonOptions));
        command.Parameters.AddWithValue("$title", (object?)analysis.DocumentTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$author", (object?)analysis.Author ?? DBNull.Value);
        command.Parameters.AddWithValue("$pages", (object?)analysis.PageCount ?? DBNull.Value);
        command.Parameters.AddWithValue("$sampled", analysis.PagesSampled);
        command.Parameters.AddWithValue("$characters", analysis.ExtractedCharacters);
        command.Parameters.AddWithValue("$searchable", analysis.HasSearchableText ? 1 : 0);
        command.Parameters.AddWithValue("$headings", JsonSerializer.Serialize(analysis.Headings, JsonOptions));
        command.Parameters.AddWithValue("$keywords", JsonSerializer.Serialize(analysis.Keywords, JsonOptions));
        command.Parameters.AddWithValue("$indicators", JsonSerializer.Serialize(analysis.ContentIndicators, JsonOptions));
        command.Parameters.AddWithValue("$preview", analysis.TextPreview);
        command.Parameters.AddWithValue("$error", (object?)analysis.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$extractedText", analysis.ExtractedText);
        await command.ExecuteNonQueryAsync(cancellationToken);

        var updateResource = connection.CreateCommand();
        updateResource.Transaction = tx;
        updateResource.CommandText = "UPDATE resources SET analysis_status = $status, analysis_needed = CASE WHEN $status = 'Failed' THEN 1 ELSE 0 END WHERE id = $resource;";
        updateResource.Parameters.AddWithValue("$status", analysis.AnalysisStatus);
        updateResource.Parameters.AddWithValue("$resource", analysis.ResourceId);
        await updateResource.ExecuteNonQueryAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }

    public async Task<ResourceAnalysis?> GetResourceAnalysisAsync(long resourceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT resource_id, analysis_status, scanner_kind, scanner_version, scanned_utc,
                   suggested_resource_type, suggested_topic, suggested_subtopics_json, document_title, author, page_count, pages_sampled,
                   extracted_characters, has_searchable_text, headings_json, keywords_json,
                   content_indicators_json, text_preview, error_message, extracted_text
            FROM resource_analysis
            WHERE resource_id = $resource;
            """;
        command.Parameters.AddWithValue("$resource", resourceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        string? OptionalString(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        var subtopics = JsonSerializer.Deserialize<List<string>>(reader.GetString(7), JsonOptions) ?? [];
        var headings = JsonSerializer.Deserialize<List<string>>(reader.GetString(14), JsonOptions) ?? [];
        var keywords = JsonSerializer.Deserialize<List<string>>(reader.GetString(15), JsonOptions) ?? [];
        var indicators = JsonSerializer.Deserialize<List<string>>(reader.GetString(16), JsonOptions) ?? [];

        return new ResourceAnalysis(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            DateTimeOffset.Parse(reader.GetString(4)),
            reader.GetString(5),
            reader.GetString(6),
            subtopics,
            OptionalString(8),
            OptionalString(9),
            reader.IsDBNull(10) ? null : reader.GetInt32(10),
            reader.GetInt32(11),
            reader.GetInt64(12),
            reader.GetInt64(13) == 1,
            headings,
            keywords,
            indicators,
            reader.GetString(17),
            OptionalString(18),
            reader.IsDBNull(19) ? string.Empty : reader.GetString(19));
    }

    private static ResourceItem ReadResource(SqliteDataReader reader) => new(
        reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
        reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetInt64(8), DateTimeOffset.Parse(reader.GetString(9)),
        reader.GetString(10), reader.GetString(11), reader.GetString(12), reader.GetInt64(13) == 1, reader.GetInt64(14) == 1,
        reader.GetString(15), reader.GetString(16), reader.GetString(17), reader.GetString(18),
        reader.FieldCount > 19 && !reader.IsDBNull(19) ? reader.GetInt32(19) : 0,
        reader.FieldCount > 20 && !reader.IsDBNull(20) && reader.GetInt64(20) == 1,
        reader.FieldCount > 21 && !reader.IsDBNull(21) ? reader.GetString(21) : "Learning",
        reader.FieldCount > 22 && !reader.IsDBNull(22) ? reader.GetString(22) : string.Empty);
}
