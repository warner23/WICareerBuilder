using Microsoft.Data.Sqlite;
using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Data.Repositories;

public sealed partial class LibraryRepository
{
    private const string ContentPlacementAnalyserVersion = "0.9.2";
    public async Task<IReadOnlyList<ContentPlacementCandidate>> GetContentPlacementCandidatesAsync(
        long? sourceId = null,
        string focus = "",
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        focus = (focus ?? string.Empty).Trim();
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT r.id,r.library_source_id,ls.root_path,r.relative_path,r.file_name,r.extension,r.media_kind,r.fingerprint_status
            FROM resources r
            JOIN library_sources ls ON ls.id=r.library_source_id
            LEFT JOIN resource_classification_proposals p ON p.resource_id=r.id
            WHERE r.is_available=1
              AND r.is_policy_excluded=0
              AND r.is_ignored=0
              AND ls.is_enabled=1
              AND COALESCE(ls.lifecycle_status,'Active')='Active'
              AND r.discovery_decision='Include'
              AND r.catalogue_lane IN ('Learning','Both')
              AND lower(r.extension) IN ('.docx','.pptx','.xlsx','.xlsm','.txt','.md','.csv','.xml','.json','.doc','.ppt','.xls')
              AND (p.resource_id IS NULL OR p.analyser_version<>$placementVersion)
              {(sourceId.HasValue ? "AND r.library_source_id=$source" : string.Empty)}
              {(focus.Length > 0 ? "AND (lower(r.file_name) LIKE $focus OR lower(r.relative_path) LIKE $focus)" : string.Empty)}
            ORDER BY
              CASE WHEN lower(r.media_kind)='presentation' THEN 0 WHEN lower(r.media_kind)='document' THEN 1 WHEN lower(r.media_kind)='spreadsheet' THEN 2 ELSE 3 END,
              CASE WHEN lower(r.file_name) LIKE '%dc motor%' OR lower(r.relative_path) LIKE '%dc motor%' THEN 0 ELSE 1 END,
              CASE r.analysis_needed WHEN 1 THEN 0 ELSE 1 END,
              r.size_bytes ASC,r.id ASC
            LIMIT $limit;
            """;
        if (sourceId.HasValue) command.Parameters.AddWithValue("$source", sourceId.Value);
        if (focus.Length > 0) command.Parameters.AddWithValue("$focus", "%" + focus.ToLowerInvariant() + "%");
        command.Parameters.AddWithValue("$placementVersion", ContentPlacementAnalyserVersion);
        command.Parameters.AddWithValue("$limit", limit);

        var result = new List<ContentPlacementCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ContentPlacementCandidate(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6), reader.GetString(7)));
        }
        return result;
    }

    public async Task<int> GetContentPlacementRemainingAsync(long? sourceId = null, string focus = "", CancellationToken cancellationToken = default)
    {
        focus = (focus ?? string.Empty).Trim();
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT(*)
            FROM resources r
            JOIN library_sources ls ON ls.id=r.library_source_id
            LEFT JOIN resource_classification_proposals p ON p.resource_id=r.id
            WHERE r.is_available=1 AND r.is_policy_excluded=0 AND r.is_ignored=0
              AND ls.is_enabled=1 AND COALESCE(ls.lifecycle_status,'Active')='Active'
              AND r.discovery_decision='Include' AND r.catalogue_lane IN ('Learning','Both')
              AND lower(r.extension) IN ('.docx','.pptx','.xlsx','.xlsm','.txt','.md','.csv','.xml','.json','.doc','.ppt','.xls')
              AND (p.resource_id IS NULL OR p.analyser_version<>$placementVersion)
              {(sourceId.HasValue ? "AND r.library_source_id=$source" : string.Empty)}
              {(focus.Length > 0 ? "AND (lower(r.file_name) LIKE $focus OR lower(r.relative_path) LIKE $focus)" : string.Empty)};
            """;
        if (sourceId.HasValue) command.Parameters.AddWithValue("$source", sourceId.Value);
        if (focus.Length > 0) command.Parameters.AddWithValue("$focus", "%" + focus.ToLowerInvariant() + "%");
        command.Parameters.AddWithValue("$placementVersion", ContentPlacementAnalyserVersion);
        return Convert.ToInt32((long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L));
    }

    public async Task<long> StartContentPlacementRunAsync(long? sourceId, string focus, int requested, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO content_placement_runs(library_source_id,focus_text,requested,status,started_utc)
            VALUES($source,$focus,$requested,'Running',$now);
            """;
        command.Parameters.AddWithValue("$source", (object?)sourceId ?? DBNull.Value);
        command.Parameters.AddWithValue("$focus", focus ?? string.Empty);
        command.Parameters.AddWithValue("$requested", requested);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        var idCommand = connection.CreateCommand();
        idCommand.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt64(await idCommand.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    public async Task SaveClassificationProposalAsync(
        long resourceId,
        string learningSubject,
        string topic,
        string subtopic,
        string resourceType,
        double confidence,
        double subjectConfidence,
        double topicConfidence,
        double subtopicConfidence,
        string status,
        string reason,
        string analyserVersion,
        string sourceFingerprint,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO resource_classification_proposals(
              resource_id,learning_subject,topic,subtopic,resource_type,confidence,subject_confidence,topic_confidence,subtopic_confidence,
              proposal_status,reason,analyser_version,source_fingerprint,created_utc,updated_utc,applied_utc)
            VALUES($resource,$subject,$topic,$subtopic,$type,$confidence,$subjectConfidence,$topicConfidence,$subtopicConfidence,
              $status,$reason,$version,$fingerprint,$now,$now,CASE WHEN $status IN ('Applied','PartiallyApplied') THEN $now ELSE NULL END)
            ON CONFLICT(resource_id) DO UPDATE SET
              learning_subject=excluded.learning_subject,
              topic=excluded.topic,
              subtopic=excluded.subtopic,
              resource_type=excluded.resource_type,
              confidence=excluded.confidence,
              subject_confidence=excluded.subject_confidence,
              topic_confidence=excluded.topic_confidence,
              subtopic_confidence=excluded.subtopic_confidence,
              proposal_status=excluded.proposal_status,
              reason=excluded.reason,
              analyser_version=excluded.analyser_version,
              source_fingerprint=excluded.source_fingerprint,
              updated_utc=excluded.updated_utc,
              applied_utc=CASE WHEN excluded.proposal_status IN ('Applied','PartiallyApplied') THEN excluded.updated_utc ELSE resource_classification_proposals.applied_utc END;
            """;
        command.Parameters.AddWithValue("$resource", resourceId);
        command.Parameters.AddWithValue("$subject", learningSubject ?? string.Empty);
        command.Parameters.AddWithValue("$topic", topic ?? string.Empty);
        command.Parameters.AddWithValue("$subtopic", subtopic ?? string.Empty);
        command.Parameters.AddWithValue("$type", resourceType ?? "Unknown");
        command.Parameters.AddWithValue("$confidence", Math.Clamp(confidence, 0, 1));
        command.Parameters.AddWithValue("$subjectConfidence", Math.Clamp(subjectConfidence, 0, 1));
        command.Parameters.AddWithValue("$topicConfidence", Math.Clamp(topicConfidence, 0, 1));
        command.Parameters.AddWithValue("$subtopicConfidence", Math.Clamp(subtopicConfidence, 0, 1));
        command.Parameters.AddWithValue("$status", status ?? "Suggested");
        command.Parameters.AddWithValue("$reason", reason ?? string.Empty);
        command.Parameters.AddWithValue("$version", analyserVersion ?? string.Empty);
        command.Parameters.AddWithValue("$fingerprint", sourceFingerprint ?? string.Empty);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveContentPlacementItemAsync(long runId, ContentPlacementItem item, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO content_placement_items(
              run_id,resource_id,learning_subject,topic,subtopic,resource_type,confidence,subject_confidence,topic_confidence,subtopic_confidence,
              disposition,reason,fingerprinted_this_run,analysis_status,created_utc)
            VALUES($run,$resource,$subject,$topic,$subtopic,$type,$confidence,$subjectConfidence,$topicConfidence,$subtopicConfidence,
              $disposition,$reason,$fingerprinted,$analysis,$now);
            """;
        command.Parameters.AddWithValue("$run", runId);
        command.Parameters.AddWithValue("$resource", item.ResourceId);
        command.Parameters.AddWithValue("$subject", item.LearningSubject);
        command.Parameters.AddWithValue("$topic", item.Topic);
        command.Parameters.AddWithValue("$subtopic", item.Subtopic);
        command.Parameters.AddWithValue("$type", item.ResourceType);
        command.Parameters.AddWithValue("$confidence", item.Confidence);
        command.Parameters.AddWithValue("$subjectConfidence", item.SubjectConfidence);
        command.Parameters.AddWithValue("$topicConfidence", item.TopicConfidence);
        command.Parameters.AddWithValue("$subtopicConfidence", item.SubtopicConfidence);
        command.Parameters.AddWithValue("$disposition", item.Disposition);
        command.Parameters.AddWithValue("$reason", item.Reason);
        command.Parameters.AddWithValue("$fingerprinted", item.FingerprintedThisRun ? 1 : 0);
        command.Parameters.AddWithValue("$analysis", item.AnalysisStatus);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CompleteContentPlacementRunAsync(
        long runId,int examined,int fingerprinted,int analysed,int cached,int autoOrganised,int partiallyOrganised,int needsReview,int protectedExisting,int limited,int failed,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE content_placement_runs SET examined=$examined,fingerprinted=$fingerprinted,analysed=$analysed,cached=$cached,
              auto_organised=$auto,partially_organised=$partial,needs_review=$review,protected_existing=$protected,limited=$limited,failed=$failed,
              status='Completed',completed_utc=$now WHERE id=$id;
            """;
        command.Parameters.AddWithValue("$examined", examined);
        command.Parameters.AddWithValue("$fingerprinted", fingerprinted);
        command.Parameters.AddWithValue("$analysed", analysed);
        command.Parameters.AddWithValue("$cached", cached);
        command.Parameters.AddWithValue("$auto", autoOrganised);
        command.Parameters.AddWithValue("$partial", partiallyOrganised);
        command.Parameters.AddWithValue("$review", needsReview);
        command.Parameters.AddWithValue("$protected", protectedExisting);
        command.Parameters.AddWithValue("$limited", limited);
        command.Parameters.AddWithValue("$failed", failed);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", runId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string> GetQuickFingerprintAsync(long resourceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(quick_fingerprint,'') FROM resources WHERE id=$id;";
        command.Parameters.AddWithValue("$id", resourceId);
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? string.Empty;
    }

    public async Task<(string Source, double Confidence)> GetResourceOrganisationProvenanceAsync(long resourceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(organisation_source,'LegacyUnknown'), COALESCE(organisation_confidence,0.0) FROM resource_organisation WHERE resource_id=$resource LIMIT 1;";
        command.Parameters.AddWithValue("$resource", resourceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return ("", 0.0);
        return (reader.GetString(0), reader.GetDouble(1));
    }

    public async Task<bool> ClearPriorContentAnalysisOrganisationAsync(long resourceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();

        var clearOrganisation = connection.CreateCommand();
        clearOrganisation.Transaction = tx;
        clearOrganisation.CommandText = "DELETE FROM resource_organisation WHERE resource_id=$resource AND organisation_source LIKE 'ContentAnalysis%';";
        clearOrganisation.Parameters.AddWithValue("$resource", resourceId);
        var changed = await clearOrganisation.ExecuteNonQueryAsync(cancellationToken) > 0;

        if (changed)
        {
            var clearKnowledge = connection.CreateCommand();
            clearKnowledge.Transaction = tx;
            clearKnowledge.CommandText = "DELETE FROM resource_knowledge_links WHERE resource_id=$resource AND link_source='Primary organisation';";
            clearKnowledge.Parameters.AddWithValue("$resource", resourceId);
            await clearKnowledge.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return changed;
    }

    public async Task<ContentPlacementBatchResult?> GetLatestContentPlacementRunAsync(long? sourceId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var run = connection.CreateCommand();
        run.CommandText = $"""
            SELECT id,focus_text,requested,examined,fingerprinted,analysed,cached,auto_organised,partially_organised,needs_review,protected_existing,limited,failed,completed_utc
            FROM content_placement_runs
            WHERE status='Completed' {(sourceId.HasValue ? "AND library_source_id=$source" : string.Empty)}
            ORDER BY id DESC LIMIT 1;
            """;
        if (sourceId.HasValue) run.Parameters.AddWithValue("$source", sourceId.Value);

        long runId;
        string focus;
        int requested, examined, fingerprinted, analysed, cached, autoOrganised, partiallyOrganised, needsReview, protectedExisting, limited, failed;
        DateTimeOffset completedUtc;
        await using (var reader = await run.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            runId = reader.GetInt64(0);
            focus = reader.GetString(1);
            requested = ReadPlacementInt(reader, 2);
            examined = ReadPlacementInt(reader, 3);
            fingerprinted = ReadPlacementInt(reader, 4);
            analysed = ReadPlacementInt(reader, 5);
            cached = ReadPlacementInt(reader, 6);
            autoOrganised = ReadPlacementInt(reader, 7);
            partiallyOrganised = ReadPlacementInt(reader, 8);
            needsReview = ReadPlacementInt(reader, 9);
            protectedExisting = ReadPlacementInt(reader, 10);
            limited = ReadPlacementInt(reader, 11);
            failed = ReadPlacementInt(reader, 12);
            completedUtc = reader.IsDBNull(13) ? DateTimeOffset.UtcNow : DateTimeOffset.Parse(reader.GetString(13));
        }

        var itemCommand = connection.CreateCommand();
        itemCommand.CommandText = """
            SELECT i.resource_id,r.file_name,r.relative_path,r.media_kind,i.learning_subject,i.topic,i.subtopic,i.resource_type,
                   i.confidence,i.subject_confidence,i.topic_confidence,i.subtopic_confidence,
                   i.disposition,i.reason,i.fingerprinted_this_run,i.analysis_status
            FROM content_placement_items i
            JOIN resources r ON r.id=i.resource_id
            WHERE i.run_id=$run
            ORDER BY CASE i.disposition WHEN 'AutoOrganised' THEN 0 WHEN 'NeedsReview' THEN 1 WHEN 'ProtectedExisting' THEN 2 ELSE 3 END,
                     i.confidence DESC,r.file_name COLLATE NOCASE
            LIMIT 100;
            """;
        itemCommand.Parameters.AddWithValue("$run", runId);
        var items = new List<ContentPlacementItem>();
        await using (var reader = await itemCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new ContentPlacementItem(
                    reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                    reader.GetDouble(8), reader.GetDouble(9), reader.GetDouble(10), reader.GetDouble(11),
                    reader.GetString(12), reader.GetString(13), reader.GetInt64(14) == 1, reader.GetString(15)));
            }
        }

        var remaining = await GetContentPlacementRemainingAsync(sourceId, focus, cancellationToken);
        return new ContentPlacementBatchResult(runId, focus, requested, examined, fingerprinted, analysed, cached, autoOrganised,
            partiallyOrganised, needsReview, protectedExisting, limited, failed, remaining, items, completedUtc);
    }

    private static int ReadPlacementInt(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetInt64(ordinal));

}
