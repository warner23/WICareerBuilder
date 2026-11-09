using Microsoft.Data.Sqlite;
using WICareerBuilder.Core.Models;
using WICareerBuilder.Data.Database;

namespace WICareerBuilder.Data.Repositories;

public sealed partial class LibraryRepository
{
    public async Task<OncWorkspaceOverview> GetWorkspaceOverviewAsync(string courseLevel = "ONC", CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var course = connection.CreateCommand();
        course.CommandText = """
            SELECT cp.name, c.id, c.name, c.level_code
            FROM courses c
            JOIN knowledge_areas cp ON cp.id=c.knowledge_area_id
            WHERE c.is_active=1 AND cp.is_active=1 AND c.level_code=$level
            ORDER BY c.sort_order, c.id LIMIT 1;
            """;
        course.Parameters.AddWithValue("$level", courseLevel.Trim());
        await using var cr = await course.ExecuteReaderAsync(cancellationToken);
        if (!await cr.ReadAsync(cancellationToken))
            return new OncWorkspaceOverview(0, "Engineering", "ONC Engineering", courseLevel, [], []);
        var knowledgeArea = cr.GetString(0);
        var courseId = cr.GetInt64(1);
        var courseName = cr.GetString(2);
        var level = cr.GetString(3);
        await cr.DisposeAsync();

        var subjects = new List<CurriculumSubjectSummary>();
        var subjectCommand = connection.CreateCommand();
        subjectCommand.CommandText = """
            SELECT cs.id, cs.course_id, cp.name, c.name, c.level_code, cs.name, cs.sort_order,
                   (SELECT COUNT(DISTINCT rkl.resource_id)
                    FROM resource_knowledge_links rkl
                    JOIN resources r ON r.id=rkl.resource_id
                    JOIN library_sources ls ON ls.id=r.library_source_id
                    WHERE ls.lifecycle_status='Active' AND ls.is_enabled=1
                      AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include'
                      AND r.catalogue_lane IN ('Learning','Both')
                      AND rkl.curriculum_subject_id=cs.id AND rkl.status IN ('Suggested','Accepted')),
                   (SELECT COUNT(*) FROM learning_modules lm
                    WHERE lm.course_level=c.level_code AND lm.learning_subject=cs.name COLLATE NOCASE
                      AND lm.status <> 'Archived')
            FROM curriculum_subjects cs
            JOIN courses c ON c.id=cs.course_id
            JOIN knowledge_areas cp ON cp.id=c.knowledge_area_id
            WHERE cs.course_id=$course AND cs.is_active=1
            ORDER BY cs.sort_order, cs.name COLLATE NOCASE;
            """;
        subjectCommand.Parameters.AddWithValue("$course", courseId);
        await using var sr = await subjectCommand.ExecuteReaderAsync(cancellationToken);
        while (await sr.ReadAsync(cancellationToken))
        {
            subjects.Add(new CurriculumSubjectSummary(
                sr.GetInt64(0), sr.GetInt64(1), sr.GetString(2), sr.GetString(3), sr.GetString(4), sr.GetString(5), sr.GetInt32(6),
                Convert.ToInt32(sr.GetInt64(7)), Convert.ToInt32(sr.GetInt64(8))));
        }
        await sr.DisposeAsync();

        async Task<int> CountAsync(string where, bool includeProjectArtifacts = false)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                SELECT COUNT(*) FROM resources r
                JOIN library_sources ls ON ls.id=r.library_source_id
                LEFT JOIN resource_organisation ro ON ro.resource_id=r.id
                WHERE ls.lifecycle_status='Active' AND ls.is_enabled=1
                  AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include'
                  AND {(includeProjectArtifacts ? "r.catalogue_lane IN ('Learning','Both','ProjectArtifact')" : "r.catalogue_lane IN ('Learning','Both')")}
                  AND {where};
                """;
            return Convert.ToInt32((long)(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0L));
        }

        var libraries = new List<WorkspaceLibrarySummary>
        {
            new("images", "Images & Diagrams", await CountAsync("r.media_kind='Image'"), "Schematics, diagrams, photos and visual references."),
            new("references", "Reference Material", await CountAsync("COALESCE(ro.study_status,'Unsorted')='Reference' OR r.resource_type IN ('Reference Document','Reference Material','Manual / Technical Reference')"), "Material kept for reading, lookup and deliberate reuse rather than automatic output creation."),
            new("datasheets", "Datasheets & Manuals", await CountAsync("r.resource_type IN ('Datasheet','Manual / Technical Reference') OR lower(r.relative_path) LIKE '%datasheet%' OR lower(r.relative_path) LIKE '%data sheet%'"), "Component, equipment and technical reference material."),
            new("assignments", "Old Assignments", await CountAsync("r.resource_type='Old Assignment' OR lower(r.relative_path) LIKE '%old assignments%' OR lower(r.relative_path) LIKE '%completed assignments%'"), "Historical coursework retained as evidence and a source of reusable knowledge, questions and revision material."),
            new("projects", "Projects", await CountAsync("r.resource_type='Project Material' OR lower(r.relative_path) LIKE 'projects/%' OR lower(r.relative_path) LIKE '%/projects/%'", includeProjectArtifacts: true), "Project reports, designs and practical evidence."),
            new("books", "Books & PDFs", await CountAsync("r.extension='.pdf' OR r.resource_type LIKE 'Book%'"), "Reading material stays reference-first and is scanned only when deliberately selected.")
        };
        return new OncWorkspaceOverview(courseId, knowledgeArea, courseName, level, subjects, libraries);
    }


    public async Task EnsureSourceCourseLinkAsync(long sourceId, string knowledgeAreaName, string courseLevel, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(knowledgeAreaName) || string.IsNullOrWhiteSpace(courseLevel)) return;
        var area = knowledgeAreaName.Trim();
        var level = courseLevel.Trim();
        var courseName = $"{level} {area}";
        var now = DateTimeOffset.UtcNow.ToString("O");
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();

        var path = connection.CreateCommand();
        path.Transaction = tx;
        path.CommandText = """
            INSERT INTO knowledge_areas(name,description,sort_order,is_active,created_utc,updated_utc)
            VALUES($name,$description,100,1,$now,$now)
            ON CONFLICT(name) DO UPDATE SET is_active=1, updated_utc=excluded.updated_utc
            RETURNING id;
            """;
        path.Parameters.AddWithValue("$name", area);
        path.Parameters.AddWithValue("$description", area + " knowledge area");
        path.Parameters.AddWithValue("$now", now);
        var pathId = Convert.ToInt64(await path.ExecuteScalarAsync(cancellationToken));

        var course = connection.CreateCommand();
        course.Transaction = tx;
        course.CommandText = """
            INSERT INTO courses(knowledge_area_id,name,level_code,description,sort_order,is_active,created_utc,updated_utc)
            VALUES($path,$name,$level,$description,100,1,$now,$now)
            ON CONFLICT(knowledge_area_id,name) DO UPDATE SET level_code=excluded.level_code,is_active=1,updated_utc=excluded.updated_utc
            RETURNING id;
            """;
        course.Parameters.AddWithValue("$path", pathId);
        course.Parameters.AddWithValue("$name", courseName);
        course.Parameters.AddWithValue("$level", level);
        course.Parameters.AddWithValue("$description", courseName + " knowledge collection");
        course.Parameters.AddWithValue("$now", now);
        var courseId = Convert.ToInt64(await course.ExecuteScalarAsync(cancellationToken));

        var link = connection.CreateCommand();
        link.Transaction = tx;
        link.CommandText = """
            INSERT INTO source_courses(library_source_id,course_id,linked_utc) VALUES($source,$course,$now)
            ON CONFLICT(library_source_id) DO UPDATE SET course_id=excluded.course_id, linked_utc=excluded.linked_utc;
            """;
        link.Parameters.AddWithValue("$source", sourceId);
        link.Parameters.AddWithValue("$course", courseId);
        link.Parameters.AddWithValue("$now", now);
        await link.ExecuteNonQueryAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<CurriculumSubjectSummary> AddCurriculumSubjectAsync(long courseId, string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Subject name is required.");
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow.ToString("O");
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO curriculum_subjects(course_id,name,sort_order,created_utc,updated_utc)
            VALUES($course,$name,(SELECT COALESCE(MAX(sort_order),0)+10 FROM curriculum_subjects WHERE course_id=$course),$now,$now)
            ON CONFLICT(course_id,name) DO UPDATE SET is_active=1, updated_utc=excluded.updated_utc
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$course", courseId);
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$now", now);
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        var levelCommand = connection.CreateCommand();
        levelCommand.CommandText = "SELECT level_code FROM courses WHERE id=$course;";
        levelCommand.Parameters.AddWithValue("$course", courseId);
        var level = Convert.ToString(await levelCommand.ExecuteScalarAsync(cancellationToken)) ?? "ONC";
        var overview = await GetWorkspaceOverviewAsync(level, cancellationToken);
        return overview.Subjects.FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("Subject was saved but could not be reloaded.");
    }

    public async Task<bool> IsResourceAnalysisFreshAsync(ResourceItem resource, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM resource_analysis
            WHERE resource_id=$resource
              AND source_size_bytes=$size
              AND source_last_write_utc=$modified
              AND analysis_status IN ('Analysed','Limited')
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$resource", resource.Id);
        command.Parameters.AddWithValue("$size", resource.SizeBytes);
        command.Parameters.AddWithValue("$modified", resource.LastWriteUtc.ToString("O"));
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task RefreshResourceRelationshipsAsync(long? sourceId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();
        var clear = connection.CreateCommand();
        clear.Transaction = tx;
        clear.CommandText = """
            DELETE FROM resource_relationships
            WHERE relationship_type IN ('ArchiveCompanion','ExactCopy')
              AND ($source=0 OR from_resource_id IN (SELECT id FROM resources WHERE library_source_id=$source));
            """;
        clear.Parameters.AddWithValue("$source", sourceId ?? 0);
        await clear.ExecuteNonQueryAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow.ToString("O");
        var companions = connection.CreateCommand();
        companions.Transaction = tx;
        companions.CommandText = """
            INSERT OR IGNORE INTO resource_relationships(from_resource_id,to_resource_id,relationship_type,confidence,reason,created_utc)
            SELECT a.id, r.id, 'ArchiveCompanion',
                   CASE WHEN lower(substr(a.relative_path,1,length(a.relative_path)-length(a.file_name))) = lower(substr(r.relative_path,1,length(r.relative_path)-length(r.file_name))) THEN 1.0 ELSE 0.97 END,
                   'Archive name matches an already included resource', $now
            FROM resources a
            JOIN resources r ON r.library_source_id=a.library_source_id AND r.id<>a.id
            WHERE ($source=0 OR a.library_source_id=$source)
              AND lower(a.extension) IN ('.zip','.rar','.7z')
              AND lower(r.file_name)=lower(substr(a.file_name,1,length(a.file_name)-length(a.extension)))
              AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include'
              AND a.is_policy_excluded=0 AND a.is_ignored=0
              AND r.media_kind<>'Archive';
            """;
        companions.Parameters.AddWithValue("$source", sourceId ?? 0);
        companions.Parameters.AddWithValue("$now", now);
        await companions.ExecuteNonQueryAsync(cancellationToken);

        var copies = connection.CreateCommand();
        copies.Transaction = tx;
        copies.CommandText = """
            INSERT OR IGNORE INTO resource_relationships(from_resource_id,to_resource_id,relationship_type,confidence,reason,created_utc)
            SELECT a.id, b.id, 'ExactCopy', 0.98, 'Same filename and byte size', $now
            FROM resources a
            JOIN resources b ON b.library_source_id=a.library_source_id AND b.id>a.id
              AND b.file_name=a.file_name COLLATE NOCASE AND b.size_bytes=a.size_bytes
            WHERE ($source=0 OR a.library_source_id=$source)
              AND a.is_policy_excluded=0 AND b.is_policy_excluded=0
              AND a.is_ignored=0 AND b.is_ignored=0
              AND a.discovery_decision='Include' AND b.discovery_decision='Include';
            """;
        copies.Parameters.AddWithValue("$source", sourceId ?? 0);
        copies.Parameters.AddWithValue("$now", now);
        await copies.ExecuteNonQueryAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<LibraryLifecycleResult> ReconnectLibraryAsync(long sourceId, string rootPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath)) throw new InvalidOperationException("Choose a library folder.");
        var fullPath = Path.GetFullPath(rootPath.Trim());
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException(fullPath);
        var root = Path.GetPathRoot(fullPath);
        if (!string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);

        // Reconnect means "the same library moved", not "replace this source with unrelated files".
        // Check a bounded sample of known relative paths before changing the root so an accidental
        // wrong-folder selection cannot immediately turn a healthy catalogue into thousands of Missing rows.
        var sample = connection.CreateCommand();
        sample.CommandText = """
            SELECT relative_path
            FROM resources
            WHERE library_source_id=$id AND is_policy_excluded=0
            ORDER BY CASE WHEN is_available=1 THEN 0 ELSE 1 END, id
            LIMIT 200;
            """;
        sample.Parameters.AddWithValue("$id", sourceId);
        var knownPaths = new List<string>();
        await using (var sampleReader = await sample.ExecuteReaderAsync(cancellationToken))
        {
            while (await sampleReader.ReadAsync(cancellationToken)) knownPaths.Add(sampleReader.GetString(0));
        }

        if (knownPaths.Count >= 10)
        {
            var matched = 0;
            foreach (var relative in knownPaths)
            {
                var candidate = Path.GetFullPath(Path.Combine(fullPath, relative.Replace('/', Path.DirectorySeparatorChar)));
                var prefix = fullPath.EndsWith(Path.DirectorySeparatorChar) ? fullPath : fullPath + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(candidate)) matched++;
            }

            var minimumMatches = Math.Max(2, (int)Math.Ceiling(knownPaths.Count * 0.05));
            if (matched < minimumMatches)
                throw new InvalidOperationException($"The selected folder does not look like the same library source ({matched} of {knownPaths.Count} sampled relative paths matched). No source path was changed. Add it as a new source instead if it is a different collection.");
        }

        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE library_sources
            SET root_path=$path, is_enabled=1, lifecycle_status='Active', removed_utc=NULL
            WHERE id=$id;
            """;
        command.Parameters.AddWithValue("$path", fullPath);
        command.Parameters.AddWithValue("$id", sourceId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0) throw new InvalidOperationException("Library source not found.");
        var state = await GetLibraryLifecycleAsync(sourceId, cancellationToken);
        return state with { Message = "Library path reconnected after a bounded same-source check. Run Rescan source to refresh availability. Original files were not changed." };
    }

    public async Task<LibraryLifecycleResult> SetLibraryEnabledAsync(long sourceId, bool enabled, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE library_sources
            SET is_enabled=$enabled,
                lifecycle_status=CASE WHEN $enabled=1 THEN 'Active' ELSE 'Disabled' END,
                removed_utc=NULL
            WHERE id=$id AND lifecycle_status<>'Removed';
            """;
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$id", sourceId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0) throw new InvalidOperationException("Library source not found or already removed.");
        return await GetLibraryLifecycleAsync(sourceId, cancellationToken);
    }

    public async Task<LibraryLifecycleResult> RemoveLibraryAsync(long sourceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE library_sources SET is_enabled=0,lifecycle_status='Removed',removed_utc=$now WHERE id=$id;";
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", sourceId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0) throw new InvalidOperationException("Library source not found.");
        return await GetLibraryLifecycleAsync(sourceId, cancellationToken);
    }

    public async Task<LibraryLifecycleResult> RestoreLibraryAsync(long sourceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE library_sources SET is_enabled=1,lifecycle_status='Active',removed_utc=NULL WHERE id=$id;";
        command.Parameters.AddWithValue("$id", sourceId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0) throw new InvalidOperationException("Removed library source not found.");
        return await GetLibraryLifecycleAsync(sourceId, cancellationToken);
    }

    public async Task<LibraryLifecycleResult> PurgeLibraryAsync(long sourceId, CancellationToken cancellationToken = default)
    {
        var state = await GetLibraryLifecycleAsync(sourceId, cancellationToken);
        if (!state.CanPermanentlyPurge)
            throw new InvalidOperationException($"This library still supports {state.LearningReferenceCount} learning reference(s). Remove or re-link those learning references before permanent purge.");
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM library_sources WHERE id=$id AND lifecycle_status='Removed';";
        command.Parameters.AddWithValue("$id", sourceId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new InvalidOperationException("Only a library already in Recently Removed can be permanently purged.");
        return new LibraryLifecycleResult(sourceId, "Purged", 0, 0, true, "WICareerBuilder catalogue data was purged. Files on the drive were not touched.");
    }

    public async Task<LibraryLifecycleResult> GetLibraryLifecycleAsync(long sourceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ls.lifecycle_status,
                   (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=ls.id),
                   (SELECT COUNT(*) FROM learning_module_sources lms JOIN resources r ON r.id=lms.resource_id WHERE r.library_source_id=ls.id)
                   + (SELECT COUNT(*) FROM learning_source_refs lsr JOIN resources r ON r.id=lsr.resource_id WHERE r.library_source_id=ls.id)
                   + (SELECT COUNT(*) FROM learning_candidate_sources lcs JOIN resources r ON r.id=lcs.resource_id WHERE r.library_source_id=ls.id)
            FROM library_sources ls WHERE ls.id=$id;
            """;
        command.Parameters.AddWithValue("$id", sourceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Library source not found.");
        var status = reader.GetString(0);
        var resources = Convert.ToInt32(reader.GetInt64(1));
        var refs = Convert.ToInt32(reader.GetInt64(2));
        return new LibraryLifecycleResult(sourceId, status, resources, refs, refs == 0,
            status == "Removed" ? "Library is removed from active WICareerBuilder views. Original files remain untouched." : "Library lifecycle updated. Original files remain untouched.");
    }
}
