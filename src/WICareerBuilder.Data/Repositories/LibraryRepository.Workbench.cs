using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Data.Repositories;

public sealed partial class LibraryRepository
{
    public async Task<WorkbenchSummary> GetWorkbenchSummaryAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM resources r JOIN library_sources ls ON ls.id=r.library_source_id
               WHERE ls.lifecycle_status='Active' AND ls.is_enabled=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include' AND r.catalogue_lane IN ('Learning','Both')),
              (SELECT COUNT(*) FROM resources r JOIN library_sources ls ON ls.id=r.library_source_id
               LEFT JOIN resource_organisation ro ON ro.resource_id=r.id
               WHERE ls.lifecycle_status='Active' AND ls.is_enabled=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include' AND r.catalogue_lane IN ('Learning','Both')
                 AND COALESCE(ro.study_status,'Unsorted')='Unsorted'),
              (SELECT COUNT(*) FROM resources r JOIN library_sources ls ON ls.id=r.library_source_id
               WHERE ls.lifecycle_status='Active' AND ls.is_enabled=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include' AND r.catalogue_lane IN ('Learning','Both')
                 AND r.analysis_status='NotAnalysed'),
              (SELECT COUNT(*) FROM resources r JOIN library_sources ls ON ls.id=r.library_source_id
               WHERE ls.lifecycle_status='Active' AND ls.is_enabled=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include' AND r.catalogue_lane IN ('Learning','Both') AND r.media_kind='Image'),
              (SELECT COUNT(DISTINCT rra.resource_id) FROM resource_role_assignments rra
               JOIN resource_roles rr ON rr.id=rra.role_id
               JOIN resources r ON r.id=rra.resource_id
               JOIN library_sources ls ON ls.id=r.library_source_id
               WHERE rr.role_key IN ('OldCoursework','Assignment') AND rra.status IN ('Suggested','Accepted')
                 AND ls.lifecycle_status='Active' AND ls.is_enabled=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include' AND r.catalogue_lane IN ('Learning','Both')),
              (SELECT COUNT(*) FROM resource_knowledge_links rkl
               JOIN resources r ON r.id=rkl.resource_id
               JOIN library_sources ls ON ls.id=r.library_source_id
               WHERE rkl.status IN ('Suggested','Accepted') AND r.catalogue_lane IN ('Learning','Both')
                 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include'
                 AND ls.lifecycle_status='Active' AND ls.is_enabled=1),
              (SELECT COUNT(*) FROM source_analysis_runs),
              (SELECT COUNT(*) FROM source_fragments WHERE review_status IN ('Raw','NeedsReview')),
              (SELECT COUNT(*) FROM learning_modules WHERE composition_state='PrototypeNeedsRecomposition' AND status<>'Archived'),
              (SELECT COUNT(*) FROM output_suggestions WHERE status='Suggested'),
              (SELECT COUNT(DISTINCT rra.resource_id) FROM resource_role_assignments rra
               JOIN resource_roles rr ON rr.id=rra.role_id
               JOIN resources r ON r.id=rra.resource_id
               JOIN library_sources ls ON ls.id=r.library_source_id
               WHERE rr.role_key IN ('CareerEvidenceCandidate','CvEvidenceCandidate') AND rra.status IN ('Suggested','Accepted')
                 AND ls.lifecycle_status='Active' AND ls.is_enabled=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include'),
              (SELECT COUNT(*) FROM career_paths WHERE status<>'Archived');
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        int I(int ordinal) => reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetInt64(ordinal));
        return new WorkbenchSummary(I(0), I(1), I(2), I(3), I(4), I(5), I(6), I(7), I(8), I(9), I(10), I(11));
    }

    public async Task<KnowledgeWorkbenchOverview> GetKnowledgeWorkbenchOverviewAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);

        var areas = new List<KnowledgeAreaOverviewItem>();
        var areaCommand = connection.CreateCommand();
        areaCommand.CommandText = """
            SELECT ka.id,ka.name,ka.description,
                   (SELECT COUNT(*) FROM courses c WHERE c.knowledge_area_id=ka.id AND c.is_active=1),
                   (SELECT COUNT(DISTINCT rkl.resource_id) FROM resource_knowledge_links rkl
                    JOIN resources r ON r.id=rkl.resource_id
                    JOIN library_sources ls ON ls.id=r.library_source_id
                    WHERE rkl.knowledge_area_id=ka.id AND rkl.status IN ('Suggested','Accepted')
                      AND ls.lifecycle_status='Active' AND ls.is_enabled=1
                      AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include'
                      AND r.catalogue_lane IN ('Learning','Both'))
            FROM knowledge_areas ka
            WHERE ka.is_active=1
            ORDER BY ka.sort_order,ka.name COLLATE NOCASE;
            """;
        await using (var reader = await areaCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                areas.Add(new KnowledgeAreaOverviewItem(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), Convert.ToInt32(reader.GetInt64(3)), Convert.ToInt32(reader.GetInt64(4))));
        }

        var courses = new List<KnowledgeCourseOverviewItem>();
        var courseCommand = connection.CreateCommand();
        courseCommand.CommandText = """
            SELECT c.id,c.knowledge_area_id,ka.name,c.name,c.level_code,
                   (SELECT COUNT(*) FROM curriculum_subjects cs WHERE cs.course_id=c.id AND cs.is_active=1),
                   (SELECT COUNT(DISTINCT rkl.resource_id) FROM resource_knowledge_links rkl
                    JOIN resources r ON r.id=rkl.resource_id
                    JOIN library_sources ls ON ls.id=r.library_source_id
                    WHERE rkl.course_id=c.id AND rkl.status IN ('Suggested','Accepted')
                      AND ls.lifecycle_status='Active' AND ls.is_enabled=1
                      AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include'
                      AND r.catalogue_lane IN ('Learning','Both'))
            FROM courses c
            JOIN knowledge_areas ka ON ka.id=c.knowledge_area_id
            WHERE c.is_active=1 AND ka.is_active=1
            ORDER BY ka.sort_order,c.sort_order,c.name COLLATE NOCASE;
            """;
        await using (var reader = await courseCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                courses.Add(new KnowledgeCourseOverviewItem(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), Convert.ToInt32(reader.GetInt64(5)), Convert.ToInt32(reader.GetInt64(6))));
        }

        var subjects = new List<KnowledgeSubjectOverviewItem>();
        var subjectCommand = connection.CreateCommand();
        subjectCommand.CommandText = """
            SELECT cs.id,cs.course_id,cs.name,
                   (SELECT COUNT(*) FROM curriculum_topics ct WHERE ct.curriculum_subject_id=cs.id AND ct.is_active=1),
                   (SELECT COUNT(DISTINCT rkl.resource_id) FROM resource_knowledge_links rkl
                    JOIN resources r ON r.id=rkl.resource_id
                    JOIN library_sources ls ON ls.id=r.library_source_id
                    WHERE rkl.curriculum_subject_id=cs.id AND rkl.status IN ('Suggested','Accepted')
                      AND ls.lifecycle_status='Active' AND ls.is_enabled=1
                      AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include'
                      AND r.catalogue_lane IN ('Learning','Both')),
                   (SELECT COUNT(*) FROM learning_modules lm JOIN courses c2 ON c2.id=cs.course_id
                     WHERE lm.course_level=c2.level_code AND lm.learning_subject=cs.name COLLATE NOCASE
                       AND lm.composition_state='PrototypeNeedsRecomposition' AND lm.status<>'Archived')
            FROM curriculum_subjects cs
            JOIN courses c ON c.id=cs.course_id
            JOIN knowledge_areas ka ON ka.id=c.knowledge_area_id
            WHERE cs.is_active=1 AND c.is_active=1 AND ka.is_active=1
            ORDER BY ka.sort_order,c.sort_order,cs.sort_order,cs.name COLLATE NOCASE;
            """;
        await using (var reader = await subjectCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                subjects.Add(new KnowledgeSubjectOverviewItem(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), Convert.ToInt32(reader.GetInt64(3)), Convert.ToInt32(reader.GetInt64(4)), Convert.ToInt32(reader.GetInt64(5))));
        }

        var topics = new List<KnowledgeTopicOverviewItem>();
        var topicCommand = connection.CreateCommand();
        topicCommand.CommandText = """
            SELECT ct.id,ct.curriculum_subject_id,ct.parent_topic_id,ct.name,
                   (SELECT COUNT(DISTINCT rkl.resource_id) FROM resource_knowledge_links rkl
                    JOIN resources r ON r.id=rkl.resource_id
                    JOIN library_sources ls ON ls.id=r.library_source_id
                    WHERE rkl.topic_id=ct.id AND rkl.status IN ('Suggested','Accepted')
                      AND ls.lifecycle_status='Active' AND ls.is_enabled=1
                      AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include'
                      AND r.catalogue_lane IN ('Learning','Both'))
            FROM curriculum_topics ct
            JOIN curriculum_subjects cs ON cs.id=ct.curriculum_subject_id
            JOIN courses c ON c.id=cs.course_id
            JOIN knowledge_areas ka ON ka.id=c.knowledge_area_id
            WHERE ct.is_active=1 AND cs.is_active=1 AND c.is_active=1 AND ka.is_active=1
            ORDER BY ka.sort_order,c.sort_order,cs.sort_order,
                     CASE WHEN ct.parent_topic_id IS NULL THEN 0 ELSE 1 END,ct.sort_order,ct.name COLLATE NOCASE;
            """;
        await using (var reader = await topicCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                topics.Add(new KnowledgeTopicOverviewItem(
                    reader.GetInt64(0),
                    reader.GetInt64(1),
                    reader.IsDBNull(2) ? null : reader.GetInt64(2),
                    reader.GetString(3),
                    Convert.ToInt32(reader.GetInt64(4))));
        }

        async Task<int> ScalarCountAsync(string sql)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32((long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L));
        }

        var topicCount = topics.Count;
        var multiLinked = await ScalarCountAsync("""
            SELECT COUNT(*) FROM (
              SELECT rkl.resource_id
              FROM resource_knowledge_links rkl
              JOIN resources r ON r.id=rkl.resource_id
              JOIN library_sources ls ON ls.id=r.library_source_id
              WHERE rkl.status IN ('Suggested','Accepted')
                AND ls.lifecycle_status='Active' AND ls.is_enabled=1
                AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include'
                AND r.catalogue_lane IN ('Learning','Both')
              GROUP BY rkl.resource_id HAVING COUNT(*)>1
            );
            """);
        return new KnowledgeWorkbenchOverview(areas, courses, subjects, topics, topicCount, multiLinked);
    }

    public async Task<IReadOnlyList<ResourceRoleSummaryItem>> GetResourceRoleSummaryAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rr.id,rr.role_key,rr.label,rr.description,
                   SUM(CASE WHEN rra.status='Accepted' AND ls.lifecycle_status='Active' AND ls.is_enabled=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN rra.status='Suggested' AND ls.lifecycle_status='Active' AND ls.is_enabled=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include' THEN 1 ELSE 0 END)
            FROM resource_roles rr
            LEFT JOIN resource_role_assignments rra ON rra.role_id=rr.id
            LEFT JOIN resources r ON r.id=rra.resource_id
            LEFT JOIN library_sources ls ON ls.id=r.library_source_id
            WHERE rr.is_active=1
            GROUP BY rr.id
            ORDER BY rr.sort_order,rr.label COLLATE NOCASE;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ResourceRoleSummaryItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            int I(int ordinal) => reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetInt64(ordinal));
            result.Add(new ResourceRoleSummaryItem(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), I(4), I(5)));
        }
        return result;
    }

    public async Task<SourceAnalysisWorkbenchSummary> GetSourceAnalysisWorkbenchSummaryAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM source_analysis_runs),
              (SELECT COUNT(*) FROM source_analysis_runs WHERE status='Completed'),
              (SELECT COUNT(*) FROM source_fragments),
              (SELECT COUNT(*) FROM source_assets),
              (SELECT COUNT(*) FROM source_fragments WHERE review_status='NeedsReview'),
              (SELECT COUNT(*) FROM source_analysis_runs WHERE status='Failed');
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        int I(int ordinal) => reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetInt64(ordinal));
        return new SourceAnalysisWorkbenchSummary(I(0), I(1), I(2), I(3), I(4), I(5));
    }

    public async Task<PrototypeMaterialSummary> GetPrototypeMaterialSummaryAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM learning_modules WHERE composition_state='PrototypeNeedsRecomposition' AND status<>'Archived'),
              (SELECT COUNT(*) FROM learning_blocks lb JOIN learning_modules lm ON lm.id=lb.module_id WHERE lm.composition_state='PrototypeNeedsRecomposition' AND lm.status<>'Archived'),
              (SELECT COUNT(*) FROM learning_candidates WHERE status<>'Rejected');
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        int I(int ordinal) => reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetInt64(ordinal));
        return new PrototypeMaterialSummary(I(0), I(1), I(2), "v0.5 study material is preserved as prototype source material. It is not treated as a finished v0.6 lesson and can be recomposed later.");
    }
}

public sealed partial class LibraryRepository
{
    public async Task<ResourceRoleAssignmentResult> AssignResourceRoleAsync(
        IReadOnlyList<long> resourceIds,
        string roleKey,
        string status = "Accepted",
        CancellationToken cancellationToken = default)
    {
        var ids = resourceIds.Distinct().Take(250).ToArray();
        if (ids.Length == 0) throw new InvalidOperationException("Select at least one resource.");
        if (resourceIds.Distinct().Count() > 250) throw new InvalidOperationException("Assign roles to up to 250 resources at a time.");
        if (status is not ("Accepted" or "Suggested" or "Rejected")) throw new InvalidOperationException("Unknown resource-role status.");

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var roleCommand = connection.CreateCommand();
        roleCommand.CommandText = "SELECT id,label FROM resource_roles WHERE role_key=$key AND is_active=1 LIMIT 1;";
        roleCommand.Parameters.AddWithValue("$key", roleKey.Trim());
        await using var reader = await roleCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Resource role not found.");
        var roleId = reader.GetInt64(0);
        var label = reader.GetString(1);
        await reader.DisposeAsync();

        using var tx = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow.ToString("O");
        var updated = 0;
        foreach (var id in ids)
        {
            var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = """
                INSERT INTO resource_role_assignments(resource_id,role_id,assignment_source,confidence,status,notes,created_utc,updated_utc)
                SELECT r.id,$role,'Manual',1.0,$status,'Assigned in v0.6 Workbench',$now,$now
                FROM resources r WHERE r.id=$resource
                ON CONFLICT(resource_id,role_id) DO UPDATE SET
                    assignment_source='Manual',confidence=1.0,status=excluded.status,notes=excluded.notes,updated_utc=excluded.updated_utc;
                """;
            command.Parameters.AddWithValue("$role", roleId);
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$now", now);
            command.Parameters.AddWithValue("$resource", id);
            updated += await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await tx.CommitAsync(cancellationToken);
        return new ResourceRoleAssignmentResult(ids.Length, updated, roleKey, label, status);
    }

    public async Task<long> SaveSourceAnalysisStagingAsync(ResourceAnalysis analysis, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);

        var fingerprint = connection.CreateCommand();
        fingerprint.CommandText = "SELECT size_bytes,last_write_utc FROM resources WHERE id=$resource;";
        fingerprint.Parameters.AddWithValue("$resource", analysis.ResourceId);
        await using var fpReader = await fingerprint.ExecuteReaderAsync(cancellationToken);
        if (!await fpReader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Resource not found for source-analysis staging.");
        var size = fpReader.GetInt64(0);
        var modified = fpReader.GetString(1);
        await fpReader.DisposeAsync();

        var existing = connection.CreateCommand();
        existing.CommandText = """
            SELECT id FROM source_analysis_runs
            WHERE resource_id=$resource AND analyser_kind=$kind AND analyser_version=$version
              AND source_size_bytes=$size AND source_last_write_utc=$modified
              AND analysis_profile='v0.6 selected scan'
            ORDER BY id DESC LIMIT 1;
            """;
        existing.Parameters.AddWithValue("$resource", analysis.ResourceId);
        existing.Parameters.AddWithValue("$kind", analysis.ScannerKind);
        existing.Parameters.AddWithValue("$version", analysis.ScannerVersion);
        existing.Parameters.AddWithValue("$size", size);
        existing.Parameters.AddWithValue("$modified", modified);
        var existingId = await existing.ExecuteScalarAsync(cancellationToken);
        if (existingId is not null) return Convert.ToInt64(existingId);

        using var tx = connection.BeginTransaction();
        var run = connection.CreateCommand();
        run.Transaction = tx;
        run.CommandText = """
            INSERT INTO source_analysis_runs(resource_id,analyser_kind,analyser_version,analysis_profile,source_size_bytes,source_last_write_utc,status,warning_count,error_message,started_utc,completed_utc)
            VALUES($resource,$kind,$version,'v0.6 selected scan',$size,$modified,$status,0,$error,$started,$completed)
            RETURNING id;
            """;
        run.Parameters.AddWithValue("$resource", analysis.ResourceId);
        run.Parameters.AddWithValue("$kind", analysis.ScannerKind);
        run.Parameters.AddWithValue("$version", analysis.ScannerVersion);
        run.Parameters.AddWithValue("$size", size);
        run.Parameters.AddWithValue("$modified", modified);
        run.Parameters.AddWithValue("$status", analysis.AnalysisStatus == "Failed" ? "Failed" : "Completed");
        run.Parameters.AddWithValue("$error", analysis.ErrorMessage ?? string.Empty);
        run.Parameters.AddWithValue("$started", analysis.ScannedUtc.ToString("O"));
        run.Parameters.AddWithValue("$completed", analysis.ScannedUtc.ToString("O"));
        var runId = Convert.ToInt64(await run.ExecuteScalarAsync(cancellationToken));

        var headingSet = new HashSet<string>(analysis.Headings, StringComparer.OrdinalIgnoreCase);
        var rawLines = (analysis.ExtractedText ?? string.Empty)
            .Replace("\r", string.Empty)
            .Split('\n')
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Take(5000)
            .ToArray();

        if (rawLines.Length == 0 && !string.IsNullOrWhiteSpace(analysis.TextPreview))
            rawLines = [analysis.TextPreview.Trim()];

        var sequence = 0;
        foreach (var raw in rawLines)
        {
            sequence++;
            var kind = headingSet.Contains(raw) ? "Heading" : GuessRawFragmentKind(raw);
            var fragment = connection.CreateCommand();
            fragment.Transaction = tx;
            fragment.CommandText = """
                INSERT INTO source_fragments(analysis_run_id,sequence_no,fragment_kind,raw_text,normalised_text,locator_kind,locator_value,confidence,review_status,reconstruction_group,created_utc)
                VALUES($run,$seq,$kind,$raw,$normalised,'','',$confidence,'Raw','',$now);
                """;
            fragment.Parameters.AddWithValue("$run", runId);
            fragment.Parameters.AddWithValue("$seq", sequence);
            fragment.Parameters.AddWithValue("$kind", kind);
            fragment.Parameters.AddWithValue("$raw", raw);
            fragment.Parameters.AddWithValue("$normalised", raw);
            fragment.Parameters.AddWithValue("$confidence", kind == "Unknown" ? 0.45 : 0.65);
            fragment.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await fragment.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return runId;
    }

    private static string GuessRawFragmentKind(string text)
    {
        var value = text.Trim();
        if (value.EndsWith('?')) return "Question";
        if (value.Contains('=') && value.Length <= 180) return "FormulaCandidate";
        if (value.StartsWith("describe ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("explain ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("calculate ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("identify ", StringComparison.OrdinalIgnoreCase)) return "Instruction";
        if (value.Length <= 90 && !value.EndsWith('.') && !value.EndsWith(',') && !value.EndsWith(';')) return "PossibleHeading";
        return "Paragraph";
    }
}

public sealed partial class LibraryRepository
{
    public async Task SyncPrimaryKnowledgeLinkAsync(
        long resourceId,
        string learningSubject,
        string topic,
        string subtopic,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();

        var context = connection.CreateCommand();
        context.Transaction = tx;
        context.CommandText = """
            SELECT c.id,c.knowledge_area_id
            FROM resources r
            JOIN source_courses sc ON sc.library_source_id=r.library_source_id
            JOIN courses c ON c.id=sc.course_id
            WHERE r.id=$resource LIMIT 1;
            """;
        context.Parameters.AddWithValue("$resource", resourceId);
        await using var reader = await context.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await reader.DisposeAsync();
            await tx.CommitAsync(cancellationToken);
            return;
        }
        var courseId = reader.GetInt64(0);
        var areaId = reader.GetInt64(1);
        await reader.DisposeAsync();

        long? subjectId = null;
        var subjectName = learningSubject.Trim();
        if (subjectName.Length > 0)
        {
            var subject = connection.CreateCommand();
            subject.Transaction = tx;
            subject.CommandText = "SELECT id FROM curriculum_subjects WHERE course_id=$course AND name=$name COLLATE NOCASE LIMIT 1;";
            subject.Parameters.AddWithValue("$course", courseId);
            subject.Parameters.AddWithValue("$name", subjectName);
            var value = await subject.ExecuteScalarAsync(cancellationToken);
            if (value is not null) subjectId = Convert.ToInt64(value);
        }

        long? topicId = null;
        if (subjectId is not null && !string.IsNullOrWhiteSpace(topic))
            topicId = await EnsureKnowledgeTopicAsync(connection, tx, subjectId.Value, null, topic.Trim(), cancellationToken);
        if (subjectId is not null && topicId is not null && !string.IsNullOrWhiteSpace(subtopic))
            topicId = await EnsureKnowledgeTopicAsync(connection, tx, subjectId.Value, topicId, subtopic.Trim(), cancellationToken);

        var clear = connection.CreateCommand();
        clear.Transaction = tx;
        clear.CommandText = "DELETE FROM resource_knowledge_links WHERE resource_id=$resource AND link_source='Primary organisation';";
        clear.Parameters.AddWithValue("$resource", resourceId);
        await clear.ExecuteNonQueryAsync(cancellationToken);

        var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO resource_knowledge_links(resource_id,knowledge_area_id,course_id,curriculum_subject_id,topic_id,confidence,status,link_source,notes,created_utc,updated_utc)
            VALUES($resource,$area,$course,$subject,$topic,1.0,'Accepted','Primary organisation','Updated from the manual Organise action. Original files are untouched.',$now,$now);
            """;
        insert.Parameters.AddWithValue("$resource", resourceId);
        insert.Parameters.AddWithValue("$area", areaId);
        insert.Parameters.AddWithValue("$course", courseId);
        insert.Parameters.AddWithValue("$subject", (object?)subjectId ?? DBNull.Value);
        insert.Parameters.AddWithValue("$topic", (object?)topicId ?? DBNull.Value);
        insert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await insert.ExecuteNonQueryAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }

    private static async Task<long> EnsureKnowledgeTopicAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction tx,
        long subjectId,
        long? parentId,
        string name,
        CancellationToken cancellationToken)
    {
        var find = connection.CreateCommand();
        find.Transaction = tx;
        find.CommandText = parentId is null
            ? "SELECT id FROM curriculum_topics WHERE curriculum_subject_id=$subject AND parent_topic_id IS NULL AND name=$name COLLATE NOCASE LIMIT 1;"
            : "SELECT id FROM curriculum_topics WHERE curriculum_subject_id=$subject AND parent_topic_id=$parent AND name=$name COLLATE NOCASE LIMIT 1;";
        find.Parameters.AddWithValue("$subject", subjectId);
        find.Parameters.AddWithValue("$name", name);
        if (parentId is not null) find.Parameters.AddWithValue("$parent", parentId.Value);
        var existing = await find.ExecuteScalarAsync(cancellationToken);
        if (existing is not null) return Convert.ToInt64(existing);

        var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO curriculum_topics(curriculum_subject_id,parent_topic_id,name,sort_order,is_active,created_utc,updated_utc)
            VALUES($subject,$parent,$name,100,1,$now,$now)
            RETURNING id;
            """;
        insert.Parameters.AddWithValue("$subject", subjectId);
        insert.Parameters.AddWithValue("$parent", (object?)parentId ?? DBNull.Value);
        insert.Parameters.AddWithValue("$name", name);
        insert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        return Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
    }
}
