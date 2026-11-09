using Microsoft.Data.Sqlite;
using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Data.Repositories;

public sealed partial class LearningRepository
{
    public async Task<IReadOnlyList<StudyClassificationIssue>> GetStudyClassificationIssuesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT lm.id,lm.title,lm.learning_subject,lm.topic,lm.subtopic,
                   COALESCE(r.file_name,''),COALESCE(r.relative_path,''),
                   (SELECT COUNT(DISTINCT resource_id) FROM (
                       SELECT lm2.primary_resource_id AS resource_id FROM learning_modules lm2 WHERE lm2.id=lm.id AND lm2.primary_resource_id IS NOT NULL
                       UNION
                       SELECT lms.resource_id FROM learning_module_sources lms WHERE lms.module_id=lm.id
                   )) AS source_count
            FROM learning_modules lm
            LEFT JOIN resources r ON r.id=lm.primary_resource_id
            WHERE lm.status<>'Archived'
            ORDER BY lm.updated_utc DESC,lm.id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var issues = new List<StudyClassificationIssue>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var moduleId = reader.GetInt64(0);
            var title = reader.GetString(1);
            var learning = reader.GetString(2);
            var topic = reader.GetString(3);
            var subtopic = reader.GetString(4);
            var fileName = reader.GetString(5);
            var relativePath = reader.GetString(6);
            var sourceCount = Convert.ToInt32(reader.GetInt64(7));
            var suggestion = SuggestStudyClassification(title, learning, topic, subtopic, fileName, relativePath);
            if (suggestion is null) continue;
            issues.Add(new StudyClassificationIssue(moduleId, title, fileName, learning, topic, subtopic,
                suggestion.Value.Title, suggestion.Value.LearningSubject, suggestion.Value.Topic, suggestion.Value.Subtopic,
                suggestion.Value.Reason, sourceCount));
        }
        return issues;
    }

    public async Task<StudyClassificationRepairResult> ApplyStudyClassificationRepairsAsync(IEnumerable<long> moduleIds, CancellationToken cancellationToken = default)
    {
        var wanted = moduleIds.Where(x => x > 0).Distinct().ToArray();
        if (wanted.Length == 0) return new StudyClassificationRepairResult(0, 0, 0, 0, 0);
        var issues = (await GetStudyClassificationIssuesAsync(cancellationToken)).Where(x => wanted.Contains(x.ModuleId)).ToArray();
        var updatedModules = 0;
        var updatedCandidates = 0;
        var mergedCandidates = 0;
        var updatedResources = 0;
        var now = DateTimeOffset.UtcNow.ToString("O");

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        foreach (var issue in issues)
        {
            using var tx = connection.BeginTransaction();

            var module = connection.CreateCommand();
            module.Transaction = tx;
            module.CommandText = """
                UPDATE learning_modules
                SET title=$title,canonical_key=$key,learning_subject=$learning,topic=$topic,subtopic=$subtopic,updated_utc=$now
                WHERE id=$id;
                """;
            module.Parameters.AddWithValue("$title", issue.SuggestedTitle);
            module.Parameters.AddWithValue("$key", CanonicalKey(issue.SuggestedTitle));
            module.Parameters.AddWithValue("$learning", issue.SuggestedLearningSubject);
            module.Parameters.AddWithValue("$topic", issue.SuggestedTopic);
            module.Parameters.AddWithValue("$subtopic", issue.SuggestedSubtopic);
            module.Parameters.AddWithValue("$now", now);
            module.Parameters.AddWithValue("$id", issue.ModuleId);
            updatedModules += await module.ExecuteNonQueryAsync(cancellationToken);

            // Candidate uniqueness deliberately includes learning_subject. Moving a lesson into the
            // correct subject can therefore reveal an exact candidate that already exists there.
            // Merge those exact candidates instead of letting SQLite abort the whole repair.
            var candidateLookup = connection.CreateCommand();
            candidateLookup.Transaction = tx;
            candidateLookup.CommandText = """
                SELECT DISTINCT lc.id,lc.candidate_type,lc.course_level,lc.canonical_key,lc.status,lc.target_module_id
                FROM learning_candidates lc
                WHERE lc.target_module_id=$module
                   OR lc.id IN (SELECT candidate_id FROM learning_candidate_module_links WHERE module_id=$module)
                ORDER BY lc.id;
                """;
            candidateLookup.Parameters.AddWithValue("$module", issue.ModuleId);
            var candidatesForRepair = new List<(long Id, string Type, string Level, string Key, string Status, long? TargetModuleId)>();
            await using (var candidateReader = await candidateLookup.ExecuteReaderAsync(cancellationToken))
            {
                while (await candidateReader.ReadAsync(cancellationToken))
                {
                    candidatesForRepair.Add((
                        candidateReader.GetInt64(0),
                        candidateReader.GetString(1),
                        candidateReader.GetString(2),
                        candidateReader.GetString(3),
                        candidateReader.GetString(4),
                        candidateReader.IsDBNull(5) ? null : candidateReader.GetInt64(5)));
                }
            }

            foreach (var candidate in candidatesForRepair)
            {
                var duplicate = connection.CreateCommand();
                duplicate.Transaction = tx;
                duplicate.CommandText = """
                    SELECT id FROM learning_candidates
                    WHERE id<>$id AND candidate_type=$type AND course_level=$level
                      AND learning_subject=$learning COLLATE NOCASE AND canonical_key=$key
                    ORDER BY id LIMIT 1;
                    """;
                duplicate.Parameters.AddWithValue("$id", candidate.Id);
                duplicate.Parameters.AddWithValue("$type", candidate.Type);
                duplicate.Parameters.AddWithValue("$level", candidate.Level);
                duplicate.Parameters.AddWithValue("$learning", issue.SuggestedLearningSubject);
                duplicate.Parameters.AddWithValue("$key", candidate.Key);
                var duplicateValue = await duplicate.ExecuteScalarAsync(cancellationToken);

                if (duplicateValue is not null && duplicateValue is not DBNull)
                {
                    var existingId = Convert.ToInt64(duplicateValue);
                    await MergeClassificationCandidateAsync(connection, tx,
                        candidate.Id, existingId, candidate.Status, candidate.TargetModuleId,
                        issue.SuggestedLearningSubject, issue.SuggestedTopic, issue.SuggestedSubtopic, now, cancellationToken);
                    mergedCandidates++;
                    continue;
                }

                var candidateUpdate = connection.CreateCommand();
                candidateUpdate.Transaction = tx;
                candidateUpdate.CommandText = """
                    UPDATE learning_candidates
                    SET learning_subject=$learning,topic=$topic,subtopic=$subtopic,updated_utc=$now
                    WHERE id=$id;
                    UPDATE learning_question_bank
                    SET learning_subject=$learning,topic=$topic,subtopic=$subtopic,updated_utc=$now
                    WHERE candidate_id=$id;
                    UPDATE learning_formula_library
                    SET learning_subject=$learning,topic=$topic,subtopic=$subtopic,updated_utc=$now
                    WHERE candidate_id=$id;
                    """;
                candidateUpdate.Parameters.AddWithValue("$learning", issue.SuggestedLearningSubject);
                candidateUpdate.Parameters.AddWithValue("$topic", issue.SuggestedTopic);
                candidateUpdate.Parameters.AddWithValue("$subtopic", issue.SuggestedSubtopic);
                candidateUpdate.Parameters.AddWithValue("$now", now);
                candidateUpdate.Parameters.AddWithValue("$id", candidate.Id);
                await candidateUpdate.ExecuteNonQueryAsync(cancellationToken);
                updatedCandidates++;
            }

            var resources = connection.CreateCommand();
            resources.Transaction = tx;
            resources.CommandText = """
                UPDATE resource_organisation
                SET learning_subject=$learning,topic=$topic,subtopic=$subtopic,updated_utc=$now
                WHERE resource_id IN (
                    SELECT primary_resource_id FROM learning_modules WHERE id=$module AND primary_resource_id IS NOT NULL
                    UNION
                    SELECT resource_id FROM learning_module_sources WHERE module_id=$module
                );
                """;
            resources.Parameters.AddWithValue("$learning", issue.SuggestedLearningSubject);
            resources.Parameters.AddWithValue("$topic", issue.SuggestedTopic);
            resources.Parameters.AddWithValue("$subtopic", issue.SuggestedSubtopic);
            resources.Parameters.AddWithValue("$now", now);
            resources.Parameters.AddWithValue("$module", issue.ModuleId);
            updatedResources += await resources.ExecuteNonQueryAsync(cancellationToken);
            tx.Commit();
        }
        return new StudyClassificationRepairResult(wanted.Length, updatedModules, updatedCandidates, updatedResources, mergedCandidates);
    }

    private static async Task MergeClassificationCandidateAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction tx,
        long fromCandidateId,
        long intoCandidateId,
        string incomingStatus,
        long? incomingTargetModuleId,
        string learningSubject,
        string topic,
        string subtopic,
        string now,
        CancellationToken cancellationToken)
    {
        var merge = connection.CreateCommand();
        merge.Transaction = tx;
        merge.CommandText = """
            INSERT OR IGNORE INTO learning_candidate_sources(candidate_id,resource_id,location_note,created_utc)
            SELECT $into,resource_id,location_note,created_utc
            FROM learning_candidate_sources WHERE candidate_id=$from;

            INSERT OR IGNORE INTO learning_candidate_module_links(candidate_id,module_id,block_id,created_utc)
            SELECT $into,module_id,block_id,created_utc
            FROM learning_candidate_module_links WHERE candidate_id=$from;

            UPDATE learning_candidates
            SET learning_subject=$learning,topic=$topic,subtopic=$subtopic,
                status=CASE
                    WHEN status='Used' OR $incomingStatus='Used' THEN 'Used'
                    WHEN status='Approved' OR $incomingStatus='Approved' THEN 'Approved'
                    WHEN status='Draft' OR $incomingStatus='Draft' THEN 'Draft'
                    WHEN status='Compare' OR $incomingStatus='Compare' THEN 'Compare'
                    ELSE status
                END,
                target_module_id=COALESCE(target_module_id,$incomingTargetModule),
                updated_utc=$now
            WHERE id=$into;

            UPDATE learning_question_bank
            SET learning_subject=$learning,topic=$topic,subtopic=$subtopic,updated_utc=$now
            WHERE candidate_id IN ($from,$into);
            DELETE FROM learning_question_bank
            WHERE candidate_id=$from AND EXISTS(SELECT 1 FROM learning_question_bank WHERE candidate_id=$into);
            UPDATE learning_question_bank SET candidate_id=$into WHERE candidate_id=$from;

            UPDATE learning_formula_library
            SET learning_subject=$learning,topic=$topic,subtopic=$subtopic,updated_utc=$now
            WHERE candidate_id IN ($from,$into);
            DELETE FROM learning_formula_library
            WHERE candidate_id=$from AND EXISTS(SELECT 1 FROM learning_formula_library WHERE candidate_id=$into);
            UPDATE learning_formula_library SET candidate_id=$into WHERE candidate_id=$from;

            UPDATE learning_candidate_duplicate_suggestions
            SET existing_candidate_id=$into WHERE existing_candidate_id=$from;
            DELETE FROM learning_candidate_duplicate_suggestions WHERE candidate_id=existing_candidate_id;

            UPDATE OR IGNORE learning_block_comparisons SET candidate_id=$into WHERE candidate_id=$from;
            DELETE FROM learning_block_comparisons WHERE candidate_id=$from;

            DELETE FROM learning_candidates WHERE id=$from;
            """;
        merge.Parameters.AddWithValue("$from", fromCandidateId);
        merge.Parameters.AddWithValue("$into", intoCandidateId);
        merge.Parameters.AddWithValue("$incomingStatus", incomingStatus);
        merge.Parameters.AddWithValue("$incomingTargetModule", (object?)incomingTargetModuleId ?? DBNull.Value);
        merge.Parameters.AddWithValue("$learning", learningSubject);
        merge.Parameters.AddWithValue("$topic", topic);
        merge.Parameters.AddWithValue("$subtopic", subtopic);
        merge.Parameters.AddWithValue("$now", now);
        await merge.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LearningBlockComparisonItem>> GetPendingBlockComparisonsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureBlockComparisonSuggestionsAsync(cancellationToken);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.id,c.candidate_id,lc.candidate_type,lc.title,lc.body,c.module_id,lm.title,
                   c.existing_block_id,lb.block_type,lb.title,lb.body,lb.formula,lb.prompt,c.similarity,c.status,
                   (SELECT COUNT(*) FROM learning_candidate_sources lcs WHERE lcs.candidate_id=lc.id)
            FROM learning_block_comparisons c
            JOIN learning_candidates lc ON lc.id=c.candidate_id
            JOIN learning_modules lm ON lm.id=c.module_id
            JOIN learning_blocks lb ON lb.id=c.existing_block_id
            WHERE c.status='Pending'
            ORDER BY c.similarity DESC,c.id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<LearningBlockComparisonItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var candidateText = string.IsNullOrWhiteSpace(reader.GetString(4)) ? reader.GetString(3) : reader.GetString(4);
            var existingText = string.Join(" ", new[] { reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetString(12) }.Where(x => !string.IsNullOrWhiteSpace(x)));
            items.Add(new LearningBlockComparisonItem(reader.GetInt64(0),reader.GetInt64(1),reader.GetString(2),reader.GetString(3),candidateText,
                reader.GetInt64(5),reader.GetString(6),reader.GetInt64(7),reader.GetString(8),reader.GetString(9),existingText,
                reader.GetDouble(13),reader.GetString(14),Convert.ToInt32(reader.GetInt64(15))));
        }
        return items;
    }

    public async Task<LearningBlockComparisonResolution> ResolveBlockComparisonAsync(long comparisonId, string action, CancellationToken cancellationToken = default)
    {
        if (comparisonId <= 0) throw new InvalidOperationException("Comparison not found.");
        action = action.Trim();
        if (!action.Equals("UseExisting", StringComparison.Ordinal)
            && !action.Equals("KeepBoth", StringComparison.Ordinal)
            && !action.Equals("RejectCandidate", StringComparison.Ordinal))
            throw new InvalidOperationException("Unknown comparison action.");

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var lookup = connection.CreateCommand();
        lookup.CommandText = """
            SELECT c.candidate_id,c.module_id,c.existing_block_id,c.status
            FROM learning_block_comparisons c WHERE c.id=$id;
            """;
        lookup.Parameters.AddWithValue("$id", comparisonId);
        await using var reader = await lookup.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Comparison not found.");
        var candidateId = reader.GetInt64(0);
        var moduleId = reader.GetInt64(1);
        var existingBlockId = reader.GetInt64(2);
        var status = reader.GetString(3);
        await reader.DisposeAsync();
        if (!status.Equals("Pending", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("This comparison has already been resolved.");

        var detail = await GetLearningCandidateAsync(candidateId, cancellationToken) ?? throw new InvalidOperationException("Learning candidate not found.");
        long blockId = existingBlockId;
        if (action == "KeepBoth")
        {
            blockId = await InsertCandidateBlockUncheckedAsync(detail, moduleId, cancellationToken);
        }
        else if (action == "UseExisting")
        {
            foreach (var source in detail.Sources)
            {
                var draft = new LearningBlockDraft(moduleId, "Explanation", "", "", "", "", "None", "", null, "", "",
                    source.ResourceId, null, null, null, $"Additional source from reviewed candidate; {source.LocationNote}".Trim());
                await AddBlockSourceIfMissingAsync(connection, existingBlockId, source.ResourceId, draft, cancellationToken);
            }
        }

        using var tx = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow.ToString("O");
        if (action == "RejectCandidate")
        {
            var reject = connection.CreateCommand();
            reject.Transaction = tx;
            reject.CommandText = "UPDATE learning_candidates SET status='Rejected',updated_utc=$now WHERE id=$id;";
            reject.Parameters.AddWithValue("$now", now);
            reject.Parameters.AddWithValue("$id", candidateId);
            await reject.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            var link = connection.CreateCommand();
            link.Transaction = tx;
            link.CommandText = """
                INSERT OR IGNORE INTO learning_candidate_module_links(candidate_id,module_id,block_id,created_utc)
                VALUES($candidate,$module,$block,$now);
                UPDATE learning_candidates SET status='Used',target_module_id=$module,updated_utc=$now WHERE id=$candidate;
                """;
            link.Parameters.AddWithValue("$candidate", candidateId);
            link.Parameters.AddWithValue("$module", moduleId);
            link.Parameters.AddWithValue("$block", blockId);
            link.Parameters.AddWithValue("$now", now);
            await link.ExecuteNonQueryAsync(cancellationToken);
        }
        var resolve = connection.CreateCommand();
        resolve.Transaction = tx;
        resolve.CommandText = "UPDATE learning_block_comparisons SET status='Resolved',resolution=$action,resolved_utc=$now WHERE id=$id;";
        resolve.Parameters.AddWithValue("$action", action);
        resolve.Parameters.AddWithValue("$now", now);
        resolve.Parameters.AddWithValue("$id", comparisonId);
        await resolve.ExecuteNonQueryAsync(cancellationToken);
        tx.Commit();

        var message = action switch
        {
            "UseExisting" => "Candidate linked to the existing learning block and its sources were preserved.",
            "KeepBoth" => "Candidate kept as a separate learning block by explicit review decision.",
            _ => "Candidate rejected; the existing learning block was left unchanged."
        };
        return new LearningBlockComparisonResolution(comparisonId, action, candidateId, moduleId, blockId, message);
    }

    internal async Task<(long? BlockId, string? Title, double Similarity, bool Exact)> FindSimilarBlockForCandidateAsync(
        long moduleId, string blockType, string candidateText, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,title,body,formula,prompt,canonical_key FROM learning_blocks
            WHERE module_id=$module AND block_type=$type ORDER BY id;
            """;
        command.Parameters.AddWithValue("$module", moduleId);
        command.Parameters.AddWithValue("$type", blockType);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var wanted = CanonicalKey(candidateText);
        long? bestId = null;
        string? bestTitle = null;
        var best = 0d;
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0);
            var title = reader.GetString(1);
            var existingText = string.Join(" ", new[] { title, reader.GetString(2), reader.GetString(3), reader.GetString(4) }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var key = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
            if (wanted.Length > 0 && (key.Equals(wanted, StringComparison.OrdinalIgnoreCase) || CanonicalKey(existingText).Equals(wanted, StringComparison.OrdinalIgnoreCase)))
                return (id, title, 1d, true);
            var similarity = Similarity(candidateText, existingText);
            if (similarity <= best) continue;
            best = similarity;
            bestId = id;
            bestTitle = title;
        }
        return (bestId, bestTitle, best, false);
    }

    internal async Task QueueBlockComparisonAsync(long candidateId, long moduleId, long existingBlockId, double similarity, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow.ToString("O");
        var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO learning_block_comparisons(candidate_id,module_id,existing_block_id,similarity,status,resolution,created_utc)
            VALUES($candidate,$module,$block,$similarity,'Pending','',$now)
            ON CONFLICT(candidate_id,module_id,existing_block_id)
            DO UPDATE SET similarity=excluded.similarity,status='Pending',resolution='',resolved_utc=NULL;
            """;
        insert.Parameters.AddWithValue("$candidate", candidateId);
        insert.Parameters.AddWithValue("$module", moduleId);
        insert.Parameters.AddWithValue("$block", existingBlockId);
        insert.Parameters.AddWithValue("$similarity", similarity);
        insert.Parameters.AddWithValue("$now", now);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        var update = connection.CreateCommand();
        update.Transaction = tx;
        update.CommandText = "UPDATE learning_candidates SET status='Compare',target_module_id=$module,updated_utc=$now WHERE id=$candidate;";
        update.Parameters.AddWithValue("$module", moduleId);
        update.Parameters.AddWithValue("$now", now);
        update.Parameters.AddWithValue("$candidate", candidateId);
        await update.ExecuteNonQueryAsync(cancellationToken);
        tx.Commit();
    }

    private async Task EnsureBlockComparisonSuggestionsAsync(CancellationToken cancellationToken)
    {
        var approved = await GetLearningCandidatesAsync("ONC", "Approved", "", "", "", 250, cancellationToken);
        foreach (var candidate in approved)
        {
            var blockType = CandidateBlockType(candidate.CandidateType);
            var text = CandidateText(candidate);
            if (string.IsNullOrWhiteSpace(text)) continue;
            await using var connection = await _database.OpenConnectionAsync(cancellationToken);
            var modules = connection.CreateCommand();
            modules.CommandText = """
                SELECT id FROM learning_modules
                WHERE status<>'Archived' AND course_level=$level
                  AND learning_subject=$learning COLLATE NOCASE
                  AND topic=$topic COLLATE NOCASE
                ORDER BY updated_utc DESC LIMIT 20;
                """;
            modules.Parameters.AddWithValue("$level", candidate.CourseLevel);
            modules.Parameters.AddWithValue("$learning", candidate.LearningSubject);
            modules.Parameters.AddWithValue("$topic", candidate.Topic);
            var moduleIds = new List<long>();
            await using (var reader = await modules.ExecuteReaderAsync(cancellationToken))
                while (await reader.ReadAsync(cancellationToken)) moduleIds.Add(reader.GetInt64(0));
            foreach (var moduleId in moduleIds)
            {
                var match = await FindSimilarBlockForCandidateAsync(moduleId, blockType, text, cancellationToken);
                if (match.BlockId is long blockId && !match.Exact && match.Similarity >= 0.82)
                {
                    await QueueBlockComparisonAsync(candidate.Id, moduleId, blockId, match.Similarity, cancellationToken);
                    break;
                }
            }
        }
    }

    private async Task<long> InsertCandidateBlockUncheckedAsync(LearningCandidateDetail detail, long moduleId, CancellationToken cancellationToken)
    {
        var candidate = detail.Candidate;
        var blockType = CandidateBlockType(candidate.CandidateType);
        var text = CandidateText(candidate);
        var primary = detail.Sources.FirstOrDefault();
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();
        var sort = connection.CreateCommand();
        sort.Transaction = tx;
        sort.CommandText = "SELECT COALESCE(MAX(sort_order),0)+10 FROM learning_blocks WHERE module_id=$module;";
        sort.Parameters.AddWithValue("$module", moduleId);
        var sortOrder = Convert.ToInt32(await sort.ExecuteScalarAsync(cancellationToken));
        var now = DateTimeOffset.UtcNow.ToString("O");
        var title = string.IsNullOrWhiteSpace(candidate.Title) ? candidate.CandidateType : candidate.Title;
        var body = blockType is "Explanation" or "WorkedExample" ? text : string.Empty;
        var formula = blockType == "Formula" ? text : string.Empty;
        var prompt = blockType == "PracticeQuestion" ? text : string.Empty;
        var canonical = CanonicalKey(string.Join(" ", new[] { title, body, formula, prompt }.Where(x => !string.IsNullOrWhiteSpace(x))));
        var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO learning_blocks(module_id,block_type,sort_order,title,body,formula,prompt,answer_kind,expected_answer,numeric_tolerance,hint,explanation,created_utc,updated_utc,canonical_key)
            VALUES($module,$type,$sort,$title,$body,$formula,$prompt,'None','',NULL,'','',$now,$now,$key)
            RETURNING id;
            """;
        insert.Parameters.AddWithValue("$module", moduleId);
        insert.Parameters.AddWithValue("$type", blockType);
        insert.Parameters.AddWithValue("$sort", sortOrder);
        insert.Parameters.AddWithValue("$title", title);
        insert.Parameters.AddWithValue("$body", body);
        insert.Parameters.AddWithValue("$formula", formula);
        insert.Parameters.AddWithValue("$prompt", prompt);
        insert.Parameters.AddWithValue("$now", now);
        insert.Parameters.AddWithValue("$key", canonical);
        var blockId = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
        foreach (var source in detail.Sources)
        {
            var sourceInsert = connection.CreateCommand();
            sourceInsert.Transaction = tx;
            sourceInsert.CommandText = """
                INSERT INTO learning_source_refs(block_id,resource_id,note,created_utc)
                SELECT $block,$resource,$note,$now
                WHERE NOT EXISTS(SELECT 1 FROM learning_source_refs WHERE block_id=$block AND resource_id=$resource);
                """;
            sourceInsert.Parameters.AddWithValue("$block", blockId);
            sourceInsert.Parameters.AddWithValue("$resource", source.ResourceId);
            sourceInsert.Parameters.AddWithValue("$note", $"Reviewed candidate source; {source.LocationNote}".Trim());
            sourceInsert.Parameters.AddWithValue("$now", now);
            await sourceInsert.ExecuteNonQueryAsync(cancellationToken);
        }
        tx.Commit();
        return blockId;
    }

    private static string CandidateBlockType(string candidateType) => candidateType switch
    {
        "Question" => "PracticeQuestion",
        "Formula" => "Formula",
        "Practical" => "WorkedExample",
        _ => "Explanation"
    };

    private static (string Title,string LearningSubject,string Topic,string Subtopic,string Reason)? SuggestStudyClassification(
        string title, string learningSubject, string topic, string subtopic, string fileName, string relativePath)
    {
        var context = string.Join(" ", title, fileName, relativePath, topic, subtopic).ToLowerInvariant();
        var isDc = ContainsAnyStudy(context, "dc motor", "dc machine", "dc generator", "direct current motor", "direct current machine")
            || topic.Equals("DC Machines", StringComparison.OrdinalIgnoreCase) || subtopic.Equals("DC Machines", StringComparison.OrdinalIgnoreCase);
        var isAc = ContainsAnyStudy(context, "ac motor", "induction motor", "synchronous motor", "alternator")
            || topic.Equals("AC Machines", StringComparison.OrdinalIgnoreCase) || subtopic.Equals("AC Machines", StringComparison.OrdinalIgnoreCase);
        if (!isDc && !isAc) return null;

        // Filename/title evidence decides ties; this prevents a DC source from inheriting a stray AC heading in its body.
        var fileContext = string.Join(" ", title, fileName, relativePath).ToLowerInvariant();
        if (ContainsAnyStudy(fileContext, "dc motor", "dc machine", "dc generator", "direct current")) isAc = false;
        if (ContainsAnyStudy(fileContext, "ac motor", "induction motor", "synchronous motor", "alternator")) isDc = false;
        var branch = isDc && !isAc ? "DC Machines" : isAc && !isDc ? "AC Machines" : topic.Equals("DC Machines", StringComparison.OrdinalIgnoreCase) ? "DC Machines" : "AC Machines";
        var specific = SuggestMachineSubtopic(fileContext);
        var suggestedTitle = SuggestMachineLessonTitle(fileContext, branch, specific, title);
        var changed = !learningSubject.Equals("Electrical Machines", StringComparison.OrdinalIgnoreCase)
            || !topic.Equals(branch, StringComparison.OrdinalIgnoreCase)
            || subtopic.Equals(branch == "DC Machines" ? "AC Machines" : "DC Machines", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(specific) && !subtopic.Equals(specific, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(suggestedTitle) && !title.Equals(suggestedTitle, StringComparison.OrdinalIgnoreCase));
        if (!changed) return null;
        var reason = learningSubject.Equals("Electrical Machines", StringComparison.OrdinalIgnoreCase)
            ? "Machine hierarchy contained a contradictory or overly broad branch; filename/source context provides a coherent machine topic."
            : "Motor/generator source is currently outside Electrical Machines; strong filename/topic evidence indicates an Electrical Machines lesson.";
        return (suggestedTitle, "Electrical Machines", branch, specific, reason);
    }

    private static string SuggestMachineSubtopic(string context)
    {
        if (ContainsAnyStudy(context, "speed control", "speed regulation")) return "Speed Control";
        if (ContainsAnyStudy(context, "construction", "construction of")) return "Construction";
        if (ContainsAnyStudy(context, "principles and characteristics", "principle and characteristic", "characteristics")) return "Principles & Characteristics";
        if (ContainsAnyStudy(context, "starter", "starters")) return "Starters";
        if (ContainsAnyStudy(context, "generator", "generators")) return "Generators";
        if (ContainsAnyStudy(context, "transformer", "transformers")) return "Transformers";
        return string.Empty;
    }

    private static string SuggestMachineLessonTitle(string context, string branch, string subtopic, string fallback)
    {
        var prefix = branch == "DC Machines" ? "DC" : "AC";
        if (subtopic == "Speed Control") return $"{prefix} Motor Speed Control";
        if (subtopic == "Construction") return $"{prefix} Machine Construction";
        if (subtopic == "Principles & Characteristics") return $"{prefix} Motor Principles & Characteristics";
        if (subtopic == "Starters") return $"{prefix} Motor Starters";
        if (subtopic == "Generators") return $"{prefix} Generator Principles";
        if (subtopic == "Transformers") return "Transformer Principles";
        return fallback.Equals(branch, StringComparison.OrdinalIgnoreCase) ? branch : fallback;
    }

    private static bool ContainsAnyStudy(string text, params string[] signals)
        => signals.Any(s => text.Contains(s, StringComparison.OrdinalIgnoreCase));
}
