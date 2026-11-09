using Microsoft.Data.Sqlite;
using WICareerBuilder.Core.Models;
using WICareerBuilder.Data.Database;

namespace WICareerBuilder.Data.Repositories;

public sealed partial class LearningRepository
{
    private readonly DatabaseService _database;

    public LearningRepository(DatabaseService database) => _database = database;

    public async Task<IReadOnlyList<LearningModuleSummary>> GetModulesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT lm.id, lm.title, lm.subject, lm.course_level, lm.learning_subject, lm.topic, lm.subtopic,
                   lm.status, lm.primary_resource_id, r.file_name,
                   (SELECT COUNT(*) FROM learning_blocks lb WHERE lb.module_id = lm.id) AS block_count,
                   lm.updated_utc
            FROM learning_modules lm
            LEFT JOIN resources r ON r.id = lm.primary_resource_id
            ORDER BY lm.updated_utc DESC, lm.title COLLATE NOCASE;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<LearningModuleSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new LearningModuleSummary(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetInt64(8), reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetInt32(10), DateTimeOffset.Parse(reader.GetString(11))));
        }
        return items;
    }

    public async Task<long> CreateModuleFromResourceAsync(long resourceId, string? title = null, string? summary = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var lookup = connection.CreateCommand();
        lookup.CommandText = """
            SELECT r.file_name,
                   COALESCE(GROUP_CONCAT(DISTINCT s.name), '') AS subject,
                   ls.course_level,
                   COALESCE(ro.learning_subject, ''), COALESCE(ro.topic, ''), COALESCE(ro.subtopic, '')
            FROM resources r
            JOIN library_sources ls ON ls.id = r.library_source_id
            LEFT JOIN resource_subjects rs ON rs.resource_id = r.id
            LEFT JOIN subjects s ON s.id = rs.subject_id
            LEFT JOIN resource_organisation ro ON ro.resource_id = r.id
            WHERE r.id = $resource
            GROUP BY r.id;
            """;
        lookup.Parameters.AddWithValue("$resource", resourceId);
        await using var reader = await lookup.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Resource not found.");
        var resourceName = reader.GetString(0);
        var subject = reader.GetString(1);
        var courseLevel = reader.GetString(2);
        var learningSubject = reader.GetString(3);
        var topic = reader.GetString(4);
        var subtopic = reader.GetString(5);
        await reader.DisposeAsync();

        var moduleTitle = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(resourceName) : title.Trim();
        return await CreateOrReuseModuleFromResourceAsync(resourceId, moduleTitle, summary?.Trim() ?? string.Empty,
            subject, courseLevel, learningSubject, topic, subtopic, false, cancellationToken);
    }

    public async Task<LearningModule?> GetModuleAsync(long moduleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var moduleCommand = connection.CreateCommand();
        moduleCommand.CommandText = """
            SELECT lm.id, lm.title, lm.summary, lm.subject, lm.course_level, lm.learning_subject, lm.topic, lm.subtopic,
                   lm.status, lm.primary_resource_id, r.file_name, lm.created_utc, lm.updated_utc
            FROM learning_modules lm
            LEFT JOIN resources r ON r.id = lm.primary_resource_id
            WHERE lm.id = $id;
            """;
        moduleCommand.Parameters.AddWithValue("$id", moduleId);
        await using var moduleReader = await moduleCommand.ExecuteReaderAsync(cancellationToken);
        if (!await moduleReader.ReadAsync(cancellationToken)) return null;

        var id = moduleReader.GetInt64(0);
        var title = moduleReader.GetString(1);
        var summary = moduleReader.GetString(2);
        var subject = moduleReader.GetString(3);
        var courseLevel = moduleReader.GetString(4);
        var learningSubject = moduleReader.GetString(5);
        var topic = moduleReader.GetString(6);
        var subtopic = moduleReader.GetString(7);
        var status = moduleReader.GetString(8);
        long? primaryResourceId = moduleReader.IsDBNull(9) ? null : moduleReader.GetInt64(9);
        var primaryResourceName = moduleReader.IsDBNull(10) ? null : moduleReader.GetString(10);
        var created = DateTimeOffset.Parse(moduleReader.GetString(11));
        var updated = DateTimeOffset.Parse(moduleReader.GetString(12));
        await moduleReader.DisposeAsync();

        var blocks = await GetBlocksAsync(connection, moduleId, cancellationToken);
        return new LearningModule(id, title, summary, subject, courseLevel, learningSubject, topic, subtopic, status,
            primaryResourceId, primaryResourceName, blocks, created, updated);
    }

    public async Task<LearningModule> UpdateModuleAsync(long moduleId, string title, string summary, string learningSubject, string topic, string subtopic, string status, CancellationToken cancellationToken = default)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Draft", "Ready", "Archived" };
        if (!allowed.Contains(status)) throw new InvalidOperationException("Unknown module status.");
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("A learning module needs a title.");

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE learning_modules
            SET title=$title, summary=$summary, learning_subject=$learningSubject, topic=$topic, subtopic=$subtopic,
                canonical_key=$canonical, status=$status, updated_utc=$updated
            WHERE id=$id;
            """;
        command.Parameters.AddWithValue("$title", title.Trim());
        command.Parameters.AddWithValue("$summary", summary.Trim());
        command.Parameters.AddWithValue("$learningSubject", learningSubject.Trim());
        command.Parameters.AddWithValue("$topic", topic.Trim());
        command.Parameters.AddWithValue("$subtopic", subtopic.Trim());
        command.Parameters.AddWithValue("$status", status.Trim());
        command.Parameters.AddWithValue("$canonical", LearningRepository.CanonicalKey(title.Trim()));
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", moduleId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0) throw new InvalidOperationException("Learning module not found.");
        return await GetModuleAsync(moduleId, cancellationToken) ?? throw new InvalidOperationException("Learning module could not be reloaded.");
    }

    public async Task<LearningBlock> AddBlockAsync(LearningBlockDraft draft, CancellationToken cancellationToken = default)
    {
        var allowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Explanation", "Formula", "WorkedExample", "PracticeQuestion", "Checkpoint"
        };
        var allowedAnswers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "None", "Text", "Numeric" };
        if (!allowedTypes.Contains(draft.BlockType)) throw new InvalidOperationException("Unknown learning block type.");
        if (!allowedAnswers.Contains(draft.AnswerKind)) throw new InvalidOperationException("Unknown answer type.");

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);

        var candidateText = string.Join(" ", new[] { draft.Title, draft.Body, draft.Formula, draft.Prompt }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var canonical = CanonicalKey(candidateText);
        var existingCommand = connection.CreateCommand();
        existingCommand.CommandText = """
            SELECT id, title, body, formula, prompt, canonical_key
            FROM learning_blocks
            WHERE module_id=$module AND block_type=$type
            ORDER BY id;
            """;
        existingCommand.Parameters.AddWithValue("$module", draft.ModuleId);
        existingCommand.Parameters.AddWithValue("$type", draft.BlockType.Trim());
        await using (var existingReader = await existingCommand.ExecuteReaderAsync(cancellationToken))
        {
            long? exactId = null;
            string? similarTitle = null;
            double bestSimilarity = 0;
            while (await existingReader.ReadAsync(cancellationToken))
            {
                var existingText = string.Join(" ", new[] { existingReader.GetString(1), existingReader.GetString(2), existingReader.GetString(3), existingReader.GetString(4) }.Where(x => !string.IsNullOrWhiteSpace(x)));
                var existingCanonical = existingReader.IsDBNull(5) ? string.Empty : existingReader.GetString(5);
                if (canonical.Length > 0 && (string.Equals(existingCanonical, canonical, StringComparison.OrdinalIgnoreCase) || string.Equals(CanonicalKey(existingText), canonical, StringComparison.OrdinalIgnoreCase)))
                {
                    exactId = existingReader.GetInt64(0);
                    break;
                }
                var similarity = Similarity(candidateText, existingText);
                if (similarity > bestSimilarity)
                {
                    bestSimilarity = similarity;
                    similarTitle = existingReader.GetString(1);
                }
            }
            if (exactId is long duplicateId)
            {
                await existingReader.DisposeAsync();
                if (draft.SourceResourceId is long duplicateSourceResourceId)
                    await AddBlockSourceIfMissingAsync(connection, duplicateId, duplicateSourceResourceId, draft, cancellationToken);
                return await GetBlockAsync(duplicateId, cancellationToken) ?? throw new InvalidOperationException("Existing learning block could not be reloaded.");
            }
            if (bestSimilarity >= 0.82)
                throw new InvalidOperationException($"A substantially similar {draft.BlockType} already exists{(string.IsNullOrWhiteSpace(similarTitle) ? "." : $": {similarTitle}.")} Compare or edit the existing learning before creating another copy.");
        }

        using var tx = connection.BeginTransaction();
        var sort = connection.CreateCommand();
        sort.Transaction = tx;
        sort.CommandText = "SELECT COALESCE(MAX(sort_order), 0) + 10 FROM learning_blocks WHERE module_id=$module;";
        sort.Parameters.AddWithValue("$module", draft.ModuleId);
        var sortOrder = Convert.ToInt32(await sort.ExecuteScalarAsync(cancellationToken));
        var now = DateTimeOffset.UtcNow.ToString("O");

        var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO learning_blocks(module_id, block_type, sort_order, title, body, formula, prompt,
                                        answer_kind, expected_answer, numeric_tolerance, hint, explanation,
                                        created_utc, updated_utc, canonical_key)
            VALUES ($module, $type, $sort, $title, $body, $formula, $prompt,
                    $answerKind, $answer, $tolerance, $hint, $explanation, $now, $now, $canonical)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$module", draft.ModuleId);
        command.Parameters.AddWithValue("$type", draft.BlockType.Trim());
        command.Parameters.AddWithValue("$sort", sortOrder);
        command.Parameters.AddWithValue("$title", draft.Title.Trim());
        command.Parameters.AddWithValue("$body", draft.Body.Trim());
        command.Parameters.AddWithValue("$formula", draft.Formula.Trim());
        command.Parameters.AddWithValue("$prompt", draft.Prompt.Trim());
        command.Parameters.AddWithValue("$answerKind", draft.AnswerKind.Trim());
        command.Parameters.AddWithValue("$answer", draft.ExpectedAnswer.Trim());
        command.Parameters.AddWithValue("$tolerance", draft.NumericTolerance is null ? DBNull.Value : draft.NumericTolerance.Value);
        command.Parameters.AddWithValue("$hint", draft.Hint.Trim());
        command.Parameters.AddWithValue("$explanation", draft.Explanation.Trim());
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$canonical", canonical);
        var blockId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));

        if (draft.SourceResourceId is long sourceResourceId)
        {
            var source = connection.CreateCommand();
            source.Transaction = tx;
            source.CommandText = """
                INSERT INTO learning_source_refs(block_id, resource_id, page_number, end_page_number, timestamp_seconds, note, created_utc)
                VALUES ($block, $resource, $page, $endPage, $timestamp, $note, $now);
                """;
            source.Parameters.AddWithValue("$block", blockId);
            source.Parameters.AddWithValue("$resource", sourceResourceId);
            source.Parameters.AddWithValue("$page", draft.SourcePageNumber is null ? DBNull.Value : draft.SourcePageNumber.Value);
            source.Parameters.AddWithValue("$endPage", draft.SourceEndPageNumber is null ? DBNull.Value : draft.SourceEndPageNumber.Value);
            source.Parameters.AddWithValue("$timestamp", draft.SourceTimestampSeconds is null ? DBNull.Value : draft.SourceTimestampSeconds.Value);
            source.Parameters.AddWithValue("$note", draft.SourceNote.Trim());
            source.Parameters.AddWithValue("$now", now);
            await source.ExecuteNonQueryAsync(cancellationToken);
        }

        var touch = connection.CreateCommand();
        touch.Transaction = tx;
        touch.CommandText = "UPDATE learning_modules SET updated_utc=$now WHERE id=$module;";
        touch.Parameters.AddWithValue("$now", now);
        touch.Parameters.AddWithValue("$module", draft.ModuleId);
        await touch.ExecuteNonQueryAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
        return await GetBlockAsync(blockId, cancellationToken) ?? throw new InvalidOperationException("Learning block could not be reloaded.");
    }


    private static async Task AddBlockSourceIfMissingAsync(SqliteConnection connection, long blockId, long resourceId, LearningBlockDraft draft, CancellationToken cancellationToken)
    {
        var source = connection.CreateCommand();
        source.CommandText = """
            INSERT INTO learning_source_refs(block_id, resource_id, page_number, end_page_number, timestamp_seconds, note, created_utc)
            SELECT $block,$resource,$page,$endPage,$timestamp,$note,$now
            WHERE NOT EXISTS (
                SELECT 1 FROM learning_source_refs
                WHERE block_id=$block AND resource_id=$resource
                  AND COALESCE(page_number,-1)=COALESCE($page,-1)
                  AND COALESCE(end_page_number,-1)=COALESCE($endPage,-1)
                  AND COALESCE(timestamp_seconds,-1)=COALESCE($timestamp,-1)
            );
            """;
        source.Parameters.AddWithValue("$block", blockId);
        source.Parameters.AddWithValue("$resource", resourceId);
        source.Parameters.AddWithValue("$page", draft.SourcePageNumber is null ? DBNull.Value : draft.SourcePageNumber.Value);
        source.Parameters.AddWithValue("$endPage", draft.SourceEndPageNumber is null ? DBNull.Value : draft.SourceEndPageNumber.Value);
        source.Parameters.AddWithValue("$timestamp", draft.SourceTimestampSeconds is null ? DBNull.Value : draft.SourceTimestampSeconds.Value);
        source.Parameters.AddWithValue("$note", draft.SourceNote.Trim());
        source.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await source.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<LearningBlock> UpdateBlockAsync(long blockId, LearningBlockDraft draft, CancellationToken cancellationToken = default)
    {
        var allowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Explanation", "Formula", "WorkedExample", "PracticeQuestion", "Checkpoint"
        };
        var allowedAnswers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "None", "Text", "Numeric" };
        if (!allowedTypes.Contains(draft.BlockType)) throw new InvalidOperationException("Unknown learning block type.");
        if (!allowedAnswers.Contains(draft.AnswerKind)) throw new InvalidOperationException("Unknown answer type.");

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var current = await GetBlockAsync(blockId, cancellationToken) ?? throw new InvalidOperationException("Learning block not found.");
        var candidateText = string.Join(" ", new[] { draft.Title, draft.Body, draft.Formula, draft.Prompt }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var canonical = CanonicalKey(candidateText);
        var duplicate = connection.CreateCommand();
        duplicate.CommandText = """
            SELECT id,title,body,formula,prompt,canonical_key
            FROM learning_blocks
            WHERE module_id=$module AND block_type=$type AND id<>$id
            ORDER BY id;
            """;
        duplicate.Parameters.AddWithValue("$module", current.ModuleId);
        duplicate.Parameters.AddWithValue("$type", draft.BlockType.Trim());
        duplicate.Parameters.AddWithValue("$id", blockId);
        await using (var reader = await duplicate.ExecuteReaderAsync(cancellationToken))
        {
            string? similarTitle = null;
            var bestSimilarity = 0d;
            while (await reader.ReadAsync(cancellationToken))
            {
                var existingText = string.Join(" ", new[] { reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4) }.Where(x => !string.IsNullOrWhiteSpace(x)));
                var existingCanonical = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
                if (canonical.Length > 0 && (existingCanonical.Equals(canonical, StringComparison.OrdinalIgnoreCase) || CanonicalKey(existingText).Equals(canonical, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("An exact equivalent learning block already exists in this lesson. Keep one block and link sources rather than duplicating it.");
                var similarity = Similarity(candidateText, existingText);
                if (similarity > bestSimilarity)
                {
                    bestSimilarity = similarity;
                    similarTitle = reader.GetString(1);
                }
            }
            if (bestSimilarity >= 0.82)
                throw new InvalidOperationException($"A substantially similar {draft.BlockType} already exists{(string.IsNullOrWhiteSpace(similarTitle) ? "." : $": {similarTitle}.")} Compare the two blocks before saving duplicate learning.");
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        var update = connection.CreateCommand();
        update.CommandText = """
            UPDATE learning_blocks
            SET block_type=$type,title=$title,body=$body,formula=$formula,prompt=$prompt,answer_kind=$answerKind,
                expected_answer=$answer,numeric_tolerance=$tolerance,hint=$hint,explanation=$explanation,canonical_key=$canonical,updated_utc=$now
            WHERE id=$id;
            UPDATE learning_modules SET updated_utc=$now WHERE id=$module;
            """;
        update.Parameters.AddWithValue("$type", draft.BlockType.Trim());
        update.Parameters.AddWithValue("$title", draft.Title.Trim());
        update.Parameters.AddWithValue("$body", draft.Body.Trim());
        update.Parameters.AddWithValue("$formula", draft.Formula.Trim());
        update.Parameters.AddWithValue("$prompt", draft.Prompt.Trim());
        update.Parameters.AddWithValue("$answerKind", draft.AnswerKind.Trim());
        update.Parameters.AddWithValue("$answer", draft.ExpectedAnswer.Trim());
        update.Parameters.AddWithValue("$tolerance", draft.NumericTolerance is null ? DBNull.Value : draft.NumericTolerance.Value);
        update.Parameters.AddWithValue("$hint", draft.Hint.Trim());
        update.Parameters.AddWithValue("$explanation", draft.Explanation.Trim());
        update.Parameters.AddWithValue("$canonical", canonical);
        update.Parameters.AddWithValue("$now", now);
        update.Parameters.AddWithValue("$id", blockId);
        update.Parameters.AddWithValue("$module", current.ModuleId);
        await update.ExecuteNonQueryAsync(cancellationToken);
        return await GetBlockAsync(blockId, cancellationToken) ?? throw new InvalidOperationException("Learning block could not be reloaded.");
    }

    public async Task DeleteBlockAsync(long blockId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM learning_blocks WHERE id=$id;";
        command.Parameters.AddWithValue("$id", blockId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<LearningBlock?> GetBlockAsync(long blockId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var blocks = await GetBlocksAsync(connection, null, cancellationToken, blockId);
        return blocks.FirstOrDefault();
    }

    private static async Task<IReadOnlyList<LearningBlock>> GetBlocksAsync(SqliteConnection connection, long? moduleId, CancellationToken cancellationToken, long? blockId = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, module_id, block_type, sort_order, title, body, formula, prompt, answer_kind,
                   expected_answer, numeric_tolerance, hint, explanation
            FROM learning_blocks
            WHERE ($module IS NULL OR module_id=$module) AND ($block IS NULL OR id=$block)
            ORDER BY sort_order, id;
            """;
        command.Parameters.AddWithValue("$module", moduleId is null ? DBNull.Value : moduleId.Value);
        command.Parameters.AddWithValue("$block", blockId is null ? DBNull.Value : blockId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var raw = new List<(long Id,long ModuleId,string Type,int Sort,string Title,string Body,string Formula,string Prompt,string AnswerKind,string Expected,double? Tolerance,string Hint,string Explanation)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            raw.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetDouble(10), reader.GetString(11), reader.GetString(12)));
        }
        await reader.DisposeAsync();

        var result = new List<LearningBlock>();
        foreach (var row in raw)
        {
            var sources = await GetSourcesAsync(connection, row.Id, cancellationToken);
            result.Add(new LearningBlock(row.Id, row.ModuleId, row.Type, row.Sort, row.Title, row.Body, row.Formula, row.Prompt,
                row.AnswerKind, row.Expected, row.Tolerance, row.Hint, row.Explanation, sources));
        }
        return result;
    }

    private static async Task<IReadOnlyList<LearningSourceReference>> GetSourcesAsync(SqliteConnection connection, long blockId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT lsr.id, lsr.block_id, lsr.resource_id, r.file_name, lsr.page_number, lsr.end_page_number,
                   lsr.timestamp_seconds, lsr.note
            FROM learning_source_refs lsr
            JOIN resources r ON r.id = lsr.resource_id
            WHERE lsr.block_id=$block
            ORDER BY lsr.id;
            """;
        command.Parameters.AddWithValue("$block", blockId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<LearningSourceReference>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new LearningSourceReference(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6), reader.GetString(7)));
        }
        return items;
    }
}
