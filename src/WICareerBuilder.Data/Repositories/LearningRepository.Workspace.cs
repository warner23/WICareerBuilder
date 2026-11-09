using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Data.Repositories;

public sealed partial class LearningRepository
{
    public async Task<(long? ModuleId, string? Title, double Similarity, bool Exact)> FindSimilarModuleAsync(
        string title, string courseLevel, string learningSubject, string topic, CancellationToken cancellationToken = default)
    {
        var wantedKey = CanonicalKey(title);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,title,canonical_key,learning_subject,topic
            FROM learning_modules
            WHERE status<>'Archived' AND ($level='' OR course_level=$level)
            ORDER BY updated_utc DESC LIMIT 500;
            """;
        command.Parameters.AddWithValue("$level", courseLevel.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        long? bestId = null;
        string? bestTitle = null;
        var best = 0d;
        var exact = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0);
            var candidateTitle = reader.GetString(1);
            var key = reader.GetString(2);
            if (key.Length == 0) key = CanonicalKey(candidateTitle);
            var candidateSubject = reader.GetString(3);
            var candidateTopic = reader.GetString(4);
            var sameSubject = string.IsNullOrWhiteSpace(learningSubject) || candidateSubject.Equals(learningSubject, StringComparison.OrdinalIgnoreCase);
            var sameTopic = string.IsNullOrWhiteSpace(topic) || candidateTopic.Equals(topic, StringComparison.OrdinalIgnoreCase);
            if (key == wantedKey && wantedKey.Length > 0 && sameSubject && sameTopic)
                return (id, candidateTitle, 1d, true);

            var score = Similarity(title, candidateTitle);
            if (!string.IsNullOrWhiteSpace(learningSubject) && candidateSubject.Equals(learningSubject, StringComparison.OrdinalIgnoreCase)) score += 0.08;
            if (!string.IsNullOrWhiteSpace(topic) && candidateTopic.Equals(topic, StringComparison.OrdinalIgnoreCase)) score += 0.08;
            score = Math.Min(score, 0.99);
            if (score > best)
            {
                best = score;
                bestId = id;
                bestTitle = candidateTitle;
            }
        }
        return (bestId, bestTitle, best, exact);
    }

    public async Task<long> CreateOrReuseModuleFromResourceAsync(long resourceId, string title, string summary,
        string subject, string courseLevel, string learningSubject, string topic, string subtopic,
        bool allowSimilarMerge = false, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();
        var existingBySource = connection.CreateCommand();
        existingBySource.Transaction = tx;
        existingBySource.CommandText = """
            SELECT lm.id FROM learning_modules lm
            LEFT JOIN learning_module_sources lms ON lms.module_id=lm.id
            WHERE lm.primary_resource_id=$resource OR lms.resource_id=$resource
            ORDER BY lm.id LIMIT 1;
            """;
        existingBySource.Parameters.AddWithValue("$resource", resourceId);
        if (await existingBySource.ExecuteScalarAsync(cancellationToken) is object sourceExisting)
        {
            await tx.CommitAsync(cancellationToken);
            return Convert.ToInt64(sourceExisting);
        }

        var key = CanonicalKey(title);
        var exact = connection.CreateCommand();
        exact.Transaction = tx;
        exact.CommandText = """
            SELECT id FROM learning_modules
            WHERE canonical_key=$key AND course_level=$level
              AND ($learning='' OR learning_subject=$learning COLLATE NOCASE)
              AND status<>'Archived'
            ORDER BY id LIMIT 1;
            """;
        exact.Parameters.AddWithValue("$key", key);
        exact.Parameters.AddWithValue("$level", courseLevel.Trim());
        exact.Parameters.AddWithValue("$learning", learningSubject.Trim());
        var exactId = await exact.ExecuteScalarAsync(cancellationToken);
        if (exactId is not null)
        {
            var id = Convert.ToInt64(exactId);
            await LinkSourceAsync(connection, tx, id, resourceId, "Additional source", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return id;
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO learning_modules(title,summary,subject,course_level,learning_subject,topic,subtopic,status,primary_resource_id,created_utc,updated_utc,canonical_key)
            VALUES($title,$summary,$subject,$level,$learning,$topic,$subtopic,'Draft',$resource,$now,$now,$key)
            RETURNING id;
            """;
        insert.Parameters.AddWithValue("$title", title.Trim());
        insert.Parameters.AddWithValue("$summary", summary.Trim());
        insert.Parameters.AddWithValue("$subject", subject.Trim());
        insert.Parameters.AddWithValue("$level", courseLevel.Trim());
        insert.Parameters.AddWithValue("$learning", learningSubject.Trim());
        insert.Parameters.AddWithValue("$topic", topic.Trim());
        insert.Parameters.AddWithValue("$subtopic", subtopic.Trim());
        insert.Parameters.AddWithValue("$resource", resourceId);
        insert.Parameters.AddWithValue("$now", now);
        insert.Parameters.AddWithValue("$key", key);
        var moduleId = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
        await LinkSourceAsync(connection, tx, moduleId, resourceId, "Primary", cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return moduleId;
    }

    public async Task LinkResourceToModuleAsync(long moduleId, long resourceId, string role = "Additional source", CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();
        await LinkSourceAsync(connection, tx, moduleId, resourceId, role, cancellationToken);
        var touch = connection.CreateCommand();
        touch.Transaction = tx;
        touch.CommandText = "UPDATE learning_modules SET updated_utc=$now WHERE id=$id;";
        touch.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        touch.Parameters.AddWithValue("$id", moduleId);
        await touch.ExecuteNonQueryAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<LearningExtractionResult> ExtractLearningCandidatesAsync(
        ResourceItem resource, ResourceAnalysis analysis, ResourceOrganisation organisation, CancellationToken cancellationToken = default)
    {
        var raw = BuildCandidates(resource, analysis).ToArray();
        var newCount = 0;
        var linked = 0;
        var possibleDuplicates = 0;
        var questions = 0;
        var formulas = 0;
        var lessons = 0;
        var other = 0;
        var now = DateTimeOffset.UtcNow.ToString("O");

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();
        foreach (var item in raw)
        {
            var key = CanonicalKey(item.Body.Length > 0 ? item.Body : item.Title);
            if (key.Length < 3) continue;
            var find = connection.CreateCommand();
            find.Transaction = tx;
            find.CommandText = """
                SELECT id FROM learning_candidates
                WHERE candidate_type=$type AND canonical_key=$key
                  AND course_level=$level AND learning_subject=$learning COLLATE NOCASE
                LIMIT 1;
                """;
            find.Parameters.AddWithValue("$type", item.Type);
            find.Parameters.AddWithValue("$key", key);
            find.Parameters.AddWithValue("$level", organisation.CourseLevel);
            find.Parameters.AddWithValue("$learning", organisation.LearningSubject);
            var found = await find.ExecuteScalarAsync(cancellationToken);
            long candidateId;
            if (found is null)
            {
                var similar = connection.CreateCommand();
                similar.Transaction = tx;
                similar.CommandText = """
                    SELECT id,title,body FROM learning_candidates
                    WHERE candidate_type=$type AND course_level=$level
                      AND learning_subject=$learning COLLATE NOCASE
                    ORDER BY updated_utc DESC LIMIT 250;
                    """;
                similar.Parameters.AddWithValue("$type", item.Type);
                similar.Parameters.AddWithValue("$level", organisation.CourseLevel);
                similar.Parameters.AddWithValue("$learning", organisation.LearningSubject);
                long? possibleDuplicateOf = null;
                var bestSimilarity = 0d;
                await using (var similarReader = await similar.ExecuteReaderAsync(cancellationToken))
                {
                    while (await similarReader.ReadAsync(cancellationToken))
                    {
                        var existingText = string.Join(" ", similarReader.GetString(1), similarReader.GetString(2));
                        var score = Similarity(item.Body.Length > 0 ? item.Body : item.Title, existingText);
                        if (score > bestSimilarity)
                        {
                            bestSimilarity = score;
                            possibleDuplicateOf = similarReader.GetInt64(0);
                        }
                    }
                }

                var needsComparison = possibleDuplicateOf is not null && bestSimilarity >= 0.88;
                var insert = connection.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO learning_candidates(candidate_type,title,body,canonical_key,subject,course_level,learning_subject,topic,subtopic,status,confidence,created_utc,updated_utc)
                    VALUES($type,$title,$body,$key,$subject,$level,$learning,$topic,$subtopic,$status,$confidence,$now,$now)
                    RETURNING id;
                    """;
                insert.Parameters.AddWithValue("$type", item.Type);
                insert.Parameters.AddWithValue("$title", item.Title);
                insert.Parameters.AddWithValue("$body", item.Body);
                insert.Parameters.AddWithValue("$key", key);
                insert.Parameters.AddWithValue("$subject", organisation.Subject);
                insert.Parameters.AddWithValue("$level", organisation.CourseLevel);
                insert.Parameters.AddWithValue("$learning", organisation.LearningSubject);
                insert.Parameters.AddWithValue("$topic", organisation.Topic);
                insert.Parameters.AddWithValue("$subtopic", organisation.Subtopic);
                insert.Parameters.AddWithValue("$status", needsComparison ? "Compare" : "Draft");
                insert.Parameters.AddWithValue("$confidence", item.Confidence);
                insert.Parameters.AddWithValue("$now", now);
                candidateId = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
                if (needsComparison && possibleDuplicateOf is long existingCandidateId)
                {
                    var suggestion = connection.CreateCommand();
                    suggestion.Transaction = tx;
                    suggestion.CommandText = """
                        INSERT OR REPLACE INTO learning_candidate_duplicate_suggestions(candidate_id,existing_candidate_id,similarity,created_utc)
                        VALUES($candidate,$existing,$similarity,$now);
                        """;
                    suggestion.Parameters.AddWithValue("$candidate", candidateId);
                    suggestion.Parameters.AddWithValue("$existing", existingCandidateId);
                    suggestion.Parameters.AddWithValue("$similarity", bestSimilarity);
                    suggestion.Parameters.AddWithValue("$now", now);
                    await suggestion.ExecuteNonQueryAsync(cancellationToken);
                    possibleDuplicates++;
                }
                else
                {
                    newCount++;
                }
            }
            else
            {
                candidateId = Convert.ToInt64(found);
            }

            var source = connection.CreateCommand();
            source.Transaction = tx;
            source.CommandText = """
                INSERT OR IGNORE INTO learning_candidate_sources(candidate_id,resource_id,location_note,created_utc)
                VALUES($candidate,$resource,$note,$now);
                """;
            source.Parameters.AddWithValue("$candidate", candidateId);
            source.Parameters.AddWithValue("$resource", resource.Id);
            source.Parameters.AddWithValue("$note", analysis.ScannerKind);
            source.Parameters.AddWithValue("$now", now);
            var added = await source.ExecuteNonQueryAsync(cancellationToken);
            if (found is not null && added > 0) linked++;

            switch (item.Type)
            {
                case "Question": questions++; break;
                case "Formula": formulas++; break;
                case "LessonIdea": lessons++; break;
                default: other++; break;
            }
        }
        await tx.CommitAsync(cancellationToken);
        return new LearningExtractionResult(1, newCount, linked, possibleDuplicates, questions, formulas, lessons, other);
    }

    public async Task<int> GetDraftCandidateCountAsync(string courseLevel = "ONC", CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM learning_candidates WHERE status='Draft' AND ($level='' OR course_level=$level);";
        command.Parameters.AddWithValue("$level", courseLevel);
        return Convert.ToInt32((long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L));
    }

    private static IEnumerable<(string Type, string Title, string Body, double Confidence)> BuildCandidates(ResourceItem resource, ResourceAnalysis analysis)
    {
        foreach (var h in analysis.Headings.Where(x => x.Length >= 5).Take(8))
            yield return ("LessonIdea", h, h, 0.68);

        var sourceText = string.IsNullOrWhiteSpace(analysis.ExtractedText) ? analysis.TextPreview : analysis.ExtractedText;
        var lines = sourceText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Regex.Replace(x.Trim(), @"\s+", " "))
            .Where(x => x.Length is >= 8 and <= 320)
            .ToArray();
        foreach (var line in lines.Where(x => x.EndsWith('?')).Take(20))
            yield return ("Question", line, line, 0.82);
        foreach (var line in lines.Where(IsFormulaLike).Take(12))
            yield return ("Formula", "Formula from " + resource.FileName, line, 0.62);
        foreach (var line in lines.Where(x => Regex.IsMatch(x, @"\b(is|means|defined as|refers to)\b", RegexOptions.IgnoreCase) && x.Length <= 220).Take(10))
            yield return ("Definition", "Definition", line, 0.58);
        if (analysis.ContentIndicators.Any(x => x.Contains("Practical", StringComparison.OrdinalIgnoreCase)))
            yield return ("Practical", Path.GetFileNameWithoutExtension(resource.FileName), "Review this source for a practical/build activity.", 0.55);
        if (analysis.ContentIndicators.Any(x => x.Contains("Safety", StringComparison.OrdinalIgnoreCase)))
            yield return ("Safety", Path.GetFileNameWithoutExtension(resource.FileName), "Review this source for safety learning points.", 0.60);
    }

    private static bool IsFormulaLike(string line)
        => line.Contains('=') && Regex.IsMatch(line, @"[A-Za-z]", RegexOptions.CultureInvariant) && line.Length <= 180;

    private static async Task LinkSourceAsync(SqliteConnection connection, SqliteTransaction tx, long moduleId, long resourceId, string role, CancellationToken cancellationToken)
    {
        var link = connection.CreateCommand();
        link.Transaction = tx;
        link.CommandText = """
            INSERT OR IGNORE INTO learning_module_sources(module_id,resource_id,source_role,created_utc)
            VALUES($module,$resource,$role,$now);
            """;
        link.Parameters.AddWithValue("$module", moduleId);
        link.Parameters.AddWithValue("$resource", resourceId);
        link.Parameters.AddWithValue("$role", role);
        link.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await link.ExecuteNonQueryAsync(cancellationToken);
    }

    public static string CanonicalKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var words = Regex.Matches(value.ToLowerInvariant(), @"[a-z0-9]+")
            .Select(m => m.Value)
            .Where(x => x is not "the" and not "a" and not "an" and not "of" and not "and" and not "to")
            .ToArray();
        return string.Join(' ', words);
    }

    private static double Similarity(string a, string b)
    {
        var aa = CanonicalKey(a).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bb = CanonicalKey(b).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (aa.Count == 0 || bb.Count == 0) return 0;
        var intersection = aa.Count(bb.Contains);
        var union = aa.Union(bb, StringComparer.OrdinalIgnoreCase).Count();
        return union == 0 ? 0 : intersection / (double)union;
    }
}
