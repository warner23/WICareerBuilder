using Microsoft.Data.Sqlite;
using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Data.Repositories;

public sealed partial class LearningRepository
{
    public async Task<LearningCandidateReviewSummary> GetCandidateReviewSummaryAsync(string courseLevel = "ONC", CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              SUM(CASE WHEN status='Draft' THEN 1 ELSE 0 END),
              SUM(CASE WHEN status='Compare' THEN 1 ELSE 0 END),
              SUM(CASE WHEN status='Approved' THEN 1 ELSE 0 END),
              SUM(CASE WHEN status='Rejected' THEN 1 ELSE 0 END),
              SUM(CASE WHEN status='Used' THEN 1 ELSE 0 END),
              SUM(CASE WHEN status='Draft' AND candidate_type='Question' THEN 1 ELSE 0 END),
              SUM(CASE WHEN status='Draft' AND candidate_type='Formula' THEN 1 ELSE 0 END),
              SUM(CASE WHEN status='Draft' AND candidate_type='Definition' THEN 1 ELSE 0 END),
              SUM(CASE WHEN status='Draft' AND candidate_type='LessonIdea' THEN 1 ELSE 0 END),
              SUM(CASE WHEN status='Draft' AND candidate_type='Practical' THEN 1 ELSE 0 END),
              SUM(CASE WHEN status='Draft' AND candidate_type='Safety' THEN 1 ELSE 0 END),
              SUM(CASE WHEN status='Draft' AND candidate_type NOT IN ('Question','Formula','Definition','LessonIdea','Practical','Safety') THEN 1 ELSE 0 END),
              (SELECT COUNT(*) FROM learning_question_bank qb JOIN learning_candidates qc ON qc.id=qb.candidate_id WHERE ($level='' OR qc.course_level=$level)),
              (SELECT COUNT(*) FROM learning_formula_library fb JOIN learning_candidates fc ON fc.id=fb.candidate_id WHERE ($level='' OR fc.course_level=$level))
            FROM learning_candidates
            WHERE ($level='' OR course_level=$level);
            """;
        command.Parameters.AddWithValue("$level", courseLevel.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return new LearningCandidateReviewSummary(0,0,0,0,0,0,0,0,0,0,0,0,0,0);
        int I(int index) => reader.IsDBNull(index) ? 0 : Convert.ToInt32(reader.GetInt64(index));
        return new LearningCandidateReviewSummary(I(0),I(1),I(2),I(3),I(4),I(5),I(6),I(7),I(8),I(9),I(10),I(11),I(12),I(13));
    }

    public async Task<IReadOnlyList<LearningCandidateListItem>> GetLearningCandidatesAsync(
        string courseLevel = "ONC", string status = "Draft", string candidateType = "", string learningSubject = "", string search = "", int limit = 250,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 500);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT lc.id,lc.candidate_type,lc.title,lc.body,lc.subject,lc.course_level,lc.learning_subject,lc.topic,lc.subtopic,
                   lc.status,lc.confidence,
                   (SELECT COUNT(*) FROM learning_candidate_sources lcs WHERE lcs.candidate_id=lc.id) AS source_count,
                   lc.target_module_id,lm.title,lds.existing_candidate_id,ec.title,COALESCE(lds.similarity,0),lc.updated_utc
            FROM learning_candidates lc
            LEFT JOIN learning_modules lm ON lm.id=lc.target_module_id
            LEFT JOIN learning_candidate_duplicate_suggestions lds ON lds.candidate_id=lc.id
            LEFT JOIN learning_candidates ec ON ec.id=lds.existing_candidate_id
            WHERE ($level='' OR lc.course_level=$level)
              AND ($status='' OR lc.status=$status)
              AND ($type='' OR lc.candidate_type=$type)
              AND ($learning='' OR lc.learning_subject=$learning COLLATE NOCASE)
              AND ($search='' OR lc.title LIKE $like OR lc.body LIKE $like OR lc.topic LIKE $like OR lc.subtopic LIKE $like)
            ORDER BY CASE lc.status WHEN 'Draft' THEN 0 WHEN 'Compare' THEN 1 WHEN 'Approved' THEN 2 WHEN 'Used' THEN 3 ELSE 4 END,
                     lc.confidence DESC, lc.updated_utc DESC, lc.id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$level", courseLevel.Trim());
        command.Parameters.AddWithValue("$status", status.Trim());
        command.Parameters.AddWithValue("$type", candidateType.Trim());
        command.Parameters.AddWithValue("$learning", learningSubject.Trim());
        command.Parameters.AddWithValue("$search", search.Trim());
        command.Parameters.AddWithValue("$like", "%" + search.Trim() + "%");
        command.Parameters.AddWithValue("$limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<LearningCandidateListItem>();
        while (await reader.ReadAsync(cancellationToken)) items.Add(ReadCandidate(reader));
        return items;
    }

    public async Task<LearningCandidateDetail?> GetLearningCandidateAsync(long candidateId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var candidate = await GetCandidateListItemAsync(connection, candidateId, cancellationToken);
        if (candidate is null) return null;

        var sourceCommand = connection.CreateCommand();
        sourceCommand.CommandText = """
            SELECT r.id,r.file_name,r.relative_path,ls.name,lcs.location_note
            FROM learning_candidate_sources lcs
            JOIN resources r ON r.id=lcs.resource_id
            JOIN library_sources ls ON ls.id=r.library_source_id
            WHERE lcs.candidate_id=$id
            ORDER BY ls.name COLLATE NOCASE,r.relative_path COLLATE NOCASE;
            """;
        sourceCommand.Parameters.AddWithValue("$id", candidateId);
        var sources = new List<LearningCandidateSource>();
        await using (var sourceReader = await sourceCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await sourceReader.ReadAsync(cancellationToken))
                sources.Add(new LearningCandidateSource(sourceReader.GetInt64(0), sourceReader.GetString(1), sourceReader.GetString(2), sourceReader.GetString(3), sourceReader.GetString(4)));
        }

        var bankCommand = connection.CreateCommand();
        bankCommand.CommandText = """
            SELECT EXISTS(SELECT 1 FROM learning_question_bank WHERE candidate_id=$id),
                   EXISTS(SELECT 1 FROM learning_formula_library WHERE candidate_id=$id);
            """;
        bankCommand.Parameters.AddWithValue("$id", candidateId);
        bool inQuestionBank;
        bool inFormulaLibrary;
        await using (var bankReader = await bankCommand.ExecuteReaderAsync(cancellationToken))
        {
            await bankReader.ReadAsync(cancellationToken);
            inQuestionBank = bankReader.GetInt64(0) != 0;
            inFormulaLibrary = bankReader.GetInt64(1) != 0;
        }

        var linksCommand = connection.CreateCommand();
        linksCommand.CommandText = "SELECT DISTINCT module_id FROM learning_candidate_module_links WHERE candidate_id=$id ORDER BY module_id;";
        linksCommand.Parameters.AddWithValue("$id", candidateId);
        var moduleIds = new List<long>();
        await using (var linksReader = await linksCommand.ExecuteReaderAsync(cancellationToken))
            while (await linksReader.ReadAsync(cancellationToken)) moduleIds.Add(linksReader.GetInt64(0));

        return new LearningCandidateDetail(candidate, sources, inQuestionBank, inFormulaLibrary, moduleIds);
    }

    public async Task<LearningCandidateListItem> UpdateLearningCandidateAsync(long candidateId, string title, string body,
        string learningSubject, string topic, string subtopic, CancellationToken cancellationToken = default)
    {
        title = title.Trim(); body = body.Trim();
        var canonical = CanonicalKey(body.Length > 0 ? body : title);
        if (canonical.Length < 3) throw new InvalidOperationException("Candidate content is too short to save.");

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var current = await GetCandidateListItemAsync(connection, candidateId, cancellationToken) ?? throw new InvalidOperationException("Learning candidate not found.");
        var duplicate = connection.CreateCommand();
        duplicate.CommandText = """
            SELECT id FROM learning_candidates
            WHERE id<>$id AND candidate_type=$type AND canonical_key=$key AND course_level=$level
              AND learning_subject=$learning COLLATE NOCASE LIMIT 1;
            """;
        duplicate.Parameters.AddWithValue("$id", candidateId);
        duplicate.Parameters.AddWithValue("$type", current.CandidateType);
        duplicate.Parameters.AddWithValue("$key", canonical);
        duplicate.Parameters.AddWithValue("$level", current.CourseLevel);
        duplicate.Parameters.AddWithValue("$learning", learningSubject.Trim());
        if (await duplicate.ExecuteScalarAsync(cancellationToken) is not null)
            throw new InvalidOperationException("An exact learning candidate already exists in this subject. Compare or reuse it instead of creating another copy.");

        var update = connection.CreateCommand();
        update.CommandText = """
            UPDATE learning_candidates
            SET title=$title,body=$body,canonical_key=$key,learning_subject=$learning,topic=$topic,subtopic=$subtopic,updated_utc=$now
            WHERE id=$id;
            """;
        update.Parameters.AddWithValue("$title", title);
        update.Parameters.AddWithValue("$body", body);
        update.Parameters.AddWithValue("$key", canonical);
        update.Parameters.AddWithValue("$learning", learningSubject.Trim());
        update.Parameters.AddWithValue("$topic", topic.Trim());
        update.Parameters.AddWithValue("$subtopic", subtopic.Trim());
        update.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        update.Parameters.AddWithValue("$id", candidateId);
        await update.ExecuteNonQueryAsync(cancellationToken);
        return await GetCandidateListItemAsync(connection, candidateId, cancellationToken) ?? throw new InvalidOperationException("Learning candidate could not be reloaded.");
    }

    public async Task<int> SetLearningCandidateStatusAsync(IEnumerable<long> candidateIds, string status, CancellationToken cancellationToken = default)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Draft", "Approved", "Rejected" };
        if (!allowed.Contains(status)) throw new InvalidOperationException("Unknown learning candidate status.");
        var ids = candidateIds.Where(x => x > 0).Distinct().Take(500).ToArray();
        if (ids.Length == 0) return 0;
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();
        var changed = 0;
        foreach (var id in ids)
        {
            var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = "UPDATE learning_candidates SET status=$status,updated_utc=$now WHERE id=$id;";
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$id", id);
            changed += await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await tx.CommitAsync(cancellationToken);
        return changed;
    }

    public async Task<LearningCandidateMutationResult> AddCandidateToQuestionBankAsync(long candidateId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var candidate = await GetCandidateListItemAsync(connection, candidateId, cancellationToken) ?? throw new InvalidOperationException("Learning candidate not found.");
        if (candidate.Status.Equals("Compare", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Resolve the possible-duplicate comparison before promoting this candidate.");
        if (!candidate.CandidateType.Equals("Question", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Only Question candidates can be added to the Question Bank.");
        var now = DateTimeOffset.UtcNow.ToString("O");
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO learning_question_bank(candidate_id,prompt,subject,course_level,learning_subject,topic,subtopic,status,created_utc,updated_utc)
            VALUES($id,$prompt,$subject,$level,$learning,$topic,$subtopic,'Draft',$now,$now)
            ON CONFLICT(candidate_id) DO UPDATE SET prompt=excluded.prompt,learning_subject=excluded.learning_subject,topic=excluded.topic,subtopic=excluded.subtopic,updated_utc=excluded.updated_utc
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$id", candidateId);
        command.Parameters.AddWithValue("$prompt", CandidateText(candidate));
        command.Parameters.AddWithValue("$subject", candidate.Subject);
        command.Parameters.AddWithValue("$level", candidate.CourseLevel);
        command.Parameters.AddWithValue("$learning", candidate.LearningSubject);
        command.Parameters.AddWithValue("$topic", candidate.Topic);
        command.Parameters.AddWithValue("$subtopic", candidate.Subtopic);
        command.Parameters.AddWithValue("$now", now);
        var bankId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        await MarkCandidateApprovedAsync(connection, candidateId, cancellationToken);
        return new LearningCandidateMutationResult(candidateId, "Approved", "Candidate added to the Question Bank as Draft learning material.", bankId);
    }

    public async Task<LearningCandidateMutationResult> AddCandidateToFormulaLibraryAsync(long candidateId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var candidate = await GetCandidateListItemAsync(connection, candidateId, cancellationToken) ?? throw new InvalidOperationException("Learning candidate not found.");
        if (candidate.Status.Equals("Compare", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Resolve the possible-duplicate comparison before promoting this candidate.");
        if (!candidate.CandidateType.Equals("Formula", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Only Formula candidates can be added to the Formula Library.");
        var now = DateTimeOffset.UtcNow.ToString("O");
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO learning_formula_library(candidate_id,title,formula,subject,course_level,learning_subject,topic,subtopic,status,created_utc,updated_utc)
            VALUES($id,$title,$formula,$subject,$level,$learning,$topic,$subtopic,'Draft',$now,$now)
            ON CONFLICT(candidate_id) DO UPDATE SET title=excluded.title,formula=excluded.formula,learning_subject=excluded.learning_subject,topic=excluded.topic,subtopic=excluded.subtopic,updated_utc=excluded.updated_utc
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$id", candidateId);
        command.Parameters.AddWithValue("$title", candidate.Title);
        command.Parameters.AddWithValue("$formula", CandidateText(candidate));
        command.Parameters.AddWithValue("$subject", candidate.Subject);
        command.Parameters.AddWithValue("$level", candidate.CourseLevel);
        command.Parameters.AddWithValue("$learning", candidate.LearningSubject);
        command.Parameters.AddWithValue("$topic", candidate.Topic);
        command.Parameters.AddWithValue("$subtopic", candidate.Subtopic);
        command.Parameters.AddWithValue("$now", now);
        var formulaId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        await MarkCandidateApprovedAsync(connection, candidateId, cancellationToken);
        return new LearningCandidateMutationResult(candidateId, "Approved", "Candidate added to the Formula Library as Draft learning material.", formulaId);
    }

    public async Task<LearningCandidateMutationResult> AddCandidateToModuleAsync(long candidateId, long moduleId, CancellationToken cancellationToken = default)
    {
        var detail = await GetLearningCandidateAsync(candidateId, cancellationToken) ?? throw new InvalidOperationException("Learning candidate not found.");
        if (await GetModuleAsync(moduleId, cancellationToken) is null) throw new InvalidOperationException("Learning module not found.");
        var candidate = detail.Candidate;
        if (candidate.Status.Equals("Compare", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Resolve the possible-duplicate comparison before adding this candidate to a lesson.");
        var primary = detail.Sources.FirstOrDefault();
        var blockType = candidate.CandidateType switch
        {
            "Question" => "PracticeQuestion",
            "Formula" => "Formula",
            "Practical" => "WorkedExample",
            _ => "Explanation"
        };
        var text = CandidateText(candidate);
        var draft = new LearningBlockDraft(moduleId, blockType,
            string.IsNullOrWhiteSpace(candidate.Title) ? candidate.CandidateType : candidate.Title,
            blockType is "Explanation" or "WorkedExample" ? text : string.Empty,
            blockType == "Formula" ? text : string.Empty,
            blockType == "PracticeQuestion" ? text : string.Empty,
            "None", string.Empty, null, string.Empty, string.Empty,
            primary?.ResourceId, null, null, null, primary is null ? "" : $"Extracted learning candidate; {primary.LocationNote}".Trim());

        var blockMatch = await FindSimilarBlockForCandidateAsync(moduleId, blockType, text, cancellationToken);
        if (blockMatch.BlockId is long similarBlockId && !blockMatch.Exact && blockMatch.Similarity >= 0.82)
        {
            await QueueBlockComparisonAsync(candidateId, moduleId, similarBlockId, blockMatch.Similarity, cancellationToken);
            return new LearningCandidateMutationResult(candidateId, "Compare",
                $"A substantially similar {blockType} already exists: {blockMatch.Title}. Review the comparison before deciding whether to reuse it or keep both.", similarBlockId);
        }

        var block = await AddBlockAsync(draft, cancellationToken);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        foreach (var source in detail.Sources.Skip(primary is null ? 0 : 1))
        {
            var sourceCommand = connection.CreateCommand();
            sourceCommand.CommandText = """
                INSERT INTO learning_source_refs(block_id,resource_id,note,created_utc)
                SELECT $block,$resource,$note,$now
                WHERE NOT EXISTS (SELECT 1 FROM learning_source_refs WHERE block_id=$block AND resource_id=$resource);
                """;
            sourceCommand.Parameters.AddWithValue("$block", block.Id);
            sourceCommand.Parameters.AddWithValue("$resource", source.ResourceId);
            sourceCommand.Parameters.AddWithValue("$note", $"Extracted learning candidate; {source.LocationNote}".Trim());
            sourceCommand.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await sourceCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        var link = connection.CreateCommand();
        link.CommandText = """
            INSERT OR IGNORE INTO learning_candidate_module_links(candidate_id,module_id,block_id,created_utc) VALUES($candidate,$module,$block,$now);
            UPDATE learning_candidates SET status='Used',target_module_id=$module,updated_utc=$now WHERE id=$candidate;
            """;
        link.Parameters.AddWithValue("$candidate", candidateId);
        link.Parameters.AddWithValue("$module", moduleId);
        link.Parameters.AddWithValue("$block", block.Id);
        link.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await link.ExecuteNonQueryAsync(cancellationToken);
        return new LearningCandidateMutationResult(candidateId, "Used", $"Candidate added to learning module #{moduleId}.", block.Id);
    }

    public async Task<LearningCandidateMutationResult> CreateDraftModuleFromCandidateAsync(long candidateId, CancellationToken cancellationToken = default)
    {
        var detail = await GetLearningCandidateAsync(candidateId, cancellationToken) ?? throw new InvalidOperationException("Learning candidate not found.");
        var candidate = detail.Candidate;
        if (candidate.Status.Equals("Compare", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Resolve the possible-duplicate comparison before creating a lesson from this candidate.");
        var source = detail.Sources.FirstOrDefault() ?? throw new InvalidOperationException("This learning candidate has no source resource to anchor a lesson.");
        var title = string.IsNullOrWhiteSpace(candidate.Title) ? CandidateText(candidate) : candidate.Title;
        var similar = await FindSimilarModuleAsync(title, candidate.CourseLevel, candidate.LearningSubject, candidate.Topic, cancellationToken);
        if (similar.ModuleId is long similarId && !similar.Exact && similar.Similarity >= 0.62)
            throw new InvalidOperationException($"A possible existing lesson already exists: {similar.Title} ({Math.Round(similar.Similarity * 100)}% similar, module #{similarId}). Compare it before creating another lesson.");

        long moduleId;
        if (similar.ModuleId is long exactId && similar.Exact)
        {
            moduleId = exactId;
            foreach (var candidateSource in detail.Sources) await LinkResourceToModuleAsync(moduleId, candidateSource.ResourceId, "Candidate source", cancellationToken);
        }
        else
        {
            moduleId = await CreateOrReuseModuleFromResourceAsync(source.ResourceId, title,
                "Draft lesson created from a reviewed learning candidate. Review and enrich before marking Ready.",
                candidate.Subject, candidate.CourseLevel, candidate.LearningSubject, candidate.Topic, candidate.Subtopic, false, cancellationToken);
            foreach (var candidateSource in detail.Sources.Skip(1)) await LinkResourceToModuleAsync(moduleId, candidateSource.ResourceId, "Candidate source", cancellationToken);
        }

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var update = connection.CreateCommand();
        update.CommandText = "UPDATE learning_candidates SET status='Used',target_module_id=$module,updated_utc=$now WHERE id=$candidate;";
        update.Parameters.AddWithValue("$module", moduleId);
        update.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        update.Parameters.AddWithValue("$candidate", candidateId);
        await update.ExecuteNonQueryAsync(cancellationToken);
        return new LearningCandidateMutationResult(candidateId, "Used", $"Draft lesson #{moduleId} created or reused from this candidate.", moduleId);
    }

    private static LearningCandidateListItem ReadCandidate(SqliteDataReader reader)
        => new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
            reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetDouble(10), Convert.ToInt32(reader.GetInt64(11)),
            reader.IsDBNull(12) ? null : reader.GetInt64(12), reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetInt64(14), reader.IsDBNull(15) ? null : reader.GetString(15), reader.GetDouble(16), DateTimeOffset.Parse(reader.GetString(17)));

    private static async Task<LearningCandidateListItem?> GetCandidateListItemAsync(SqliteConnection connection, long candidateId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT lc.id,lc.candidate_type,lc.title,lc.body,lc.subject,lc.course_level,lc.learning_subject,lc.topic,lc.subtopic,
                   lc.status,lc.confidence,
                   (SELECT COUNT(*) FROM learning_candidate_sources lcs WHERE lcs.candidate_id=lc.id),
                   lc.target_module_id,lm.title,lds.existing_candidate_id,ec.title,COALESCE(lds.similarity,0),lc.updated_utc
            FROM learning_candidates lc
            LEFT JOIN learning_modules lm ON lm.id=lc.target_module_id
            LEFT JOIN learning_candidate_duplicate_suggestions lds ON lds.candidate_id=lc.id
            LEFT JOIN learning_candidates ec ON ec.id=lds.existing_candidate_id
            WHERE lc.id=$id;
            """;
        command.Parameters.AddWithValue("$id", candidateId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCandidate(reader) : null;
    }

    private static async Task MarkCandidateApprovedAsync(SqliteConnection connection, long candidateId, CancellationToken cancellationToken)
    {
        var update = connection.CreateCommand();
        update.CommandText = "UPDATE learning_candidates SET status='Approved',updated_utc=$now WHERE id=$id;";
        update.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        update.Parameters.AddWithValue("$id", candidateId);
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string CandidateText(LearningCandidateListItem candidate)
        => string.IsNullOrWhiteSpace(candidate.Body) ? candidate.Title : candidate.Body;

}
