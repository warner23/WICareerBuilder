using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Data.Repositories;

public sealed partial class LearningRepository
{
    public async Task<ApprovedStudyProposal> GetApprovedStudyProposalAsync(string courseLevel = "ONC", CancellationToken cancellationToken = default)
    {
        var candidates = await GetApprovedCandidatesForBuildAsync(courseLevel, cancellationToken);
        var groups = candidates
            .GroupBy(x => BuildGroupingIdentity(x), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.First().LearningSubject, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.First().Topic, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.First().Subtopic, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var proposals = new List<ApprovedStudyProposalGroup>(groups.Length);
        foreach (var group in groups)
        {
            var first = group.First();
            var proposedTitle = ProposedLessonTitle(first.LearningSubject, first.Topic, first.Subtopic);
            var isBuildable = !string.IsNullOrWhiteSpace(first.LearningSubject) && group.Any(x => x.SourceCount > 0);
            long? existingModuleId = null;
            string? existingModuleTitle = null;
            var existingSimilarity = 0d;
            var exactExisting = false;
            if (isBuildable)
            {
                var match = await FindSimilarModuleAsync(proposedTitle, first.CourseLevel, first.LearningSubject, first.Topic, cancellationToken);
                existingModuleId = match.ModuleId;
                existingModuleTitle = match.Title;
                existingSimilarity = match.Similarity;
                exactExisting = match.Exact;
            }
            var needsComparison = isBuildable && existingModuleId is not null && !exactExisting && existingSimilarity >= 0.62;
            var sourceCount = await CountDistinctCandidateSourcesAsync(group.Select(x => x.Id), cancellationToken);
            var recommendation = !isBuildable
                ? "Organise these approved candidates into an ONC learning subject before building study."
                : needsComparison
                    ? $"Possible existing lesson: {existingModuleTitle}. Compare it before creating another lesson."
                    : exactExisting
                        ? $"Enrich existing Draft/Study Builder lesson: {existingModuleTitle}."
                        : "Create a new Draft Study Builder lesson and add the approved learning blocks.";

            proposals.Add(new ApprovedStudyProposalGroup(
                MakeStudyGroupKey(first.CourseLevel, first.LearningSubject, first.Topic, first.Subtopic),
                first.Subject, first.CourseLevel, first.LearningSubject, first.Topic, first.Subtopic, proposedTitle,
                group.Count(),
                group.Count(x => x.CandidateType.Equals("Question", StringComparison.OrdinalIgnoreCase)),
                group.Count(x => x.CandidateType.Equals("Formula", StringComparison.OrdinalIgnoreCase)),
                group.Count(x => x.CandidateType.Equals("Definition", StringComparison.OrdinalIgnoreCase)),
                group.Count(x => x.CandidateType.Equals("LessonIdea", StringComparison.OrdinalIgnoreCase)),
                group.Count(x => x.CandidateType.Equals("Practical", StringComparison.OrdinalIgnoreCase)),
                group.Count(x => x.CandidateType.Equals("Safety", StringComparison.OrdinalIgnoreCase)),
                group.Count(x => !x.CandidateType.Equals("Question", StringComparison.OrdinalIgnoreCase)
                    && !x.CandidateType.Equals("Formula", StringComparison.OrdinalIgnoreCase)
                    && !x.CandidateType.Equals("Definition", StringComparison.OrdinalIgnoreCase)
                    && !x.CandidateType.Equals("LessonIdea", StringComparison.OrdinalIgnoreCase)
                    && !x.CandidateType.Equals("Practical", StringComparison.OrdinalIgnoreCase)
                    && !x.CandidateType.Equals("Safety", StringComparison.OrdinalIgnoreCase)),
                sourceCount, isBuildable, existingModuleId, existingModuleTitle, existingSimilarity, exactExisting, needsComparison, recommendation));
        }

        return new ApprovedStudyProposal(
            courseLevel,
            candidates.Count,
            candidates.Count(x => !string.IsNullOrWhiteSpace(x.LearningSubject) && x.SourceCount > 0),
            proposals.Count,
            proposals.Count(x => x.IsBuildable && !x.NeedsLessonComparison),
            proposals.Count(x => x.NeedsLessonComparison),
            proposals);
    }

    public async Task<ApprovedStudyBuildResult> BuildApprovedStudyAsync(
        IEnumerable<string> selectedGroupKeys, string courseLevel = "ONC", CancellationToken cancellationToken = default)
    {
        var wanted = selectedGroupKeys.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) return new ApprovedStudyBuildResult(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []);

        var proposal = await GetApprovedStudyProposalAsync(courseLevel, cancellationToken);
        var selected = proposal.Groups.Where(x => wanted.Contains(x.GroupKey)).ToArray();
        var modulesCreated = 0;
        var existingEnriched = 0;
        var groupsSkippedForComparison = 0;
        var candidatesUsed = 0;
        var candidatesSkippedForSimilarity = 0;
        var questionsAdded = 0;
        var formulasAdded = 0;
        var questionsUsed = 0;
        var formulasUsed = 0;
        var definitionsUsed = 0;
        var lessonIdeasUsed = 0;
        var practicalUsed = 0;
        var safetyUsed = 0;
        var otherUsed = 0;
        var moduleIds = new HashSet<long>();

        foreach (var group in selected)
        {
            if (!group.IsBuildable) continue;
            if (group.NeedsLessonComparison)
            {
                groupsSkippedForComparison++;
                continue;
            }

            var candidates = await GetApprovedCandidatesForGroupAsync(group, cancellationToken);
            if (candidates.Count == 0) continue;
            var details = new List<LearningCandidateDetail>(candidates.Count);
            foreach (var candidate in candidates)
            {
                var detail = await GetLearningCandidateAsync(candidate.Id, cancellationToken);
                if (detail is not null) details.Add(detail);
            }
            var allSources = details.SelectMany(x => x.Sources).GroupBy(x => x.ResourceId).Select(x => x.First()).ToArray();
            var primary = allSources.FirstOrDefault();
            if (primary is null) continue;

            long moduleId;
            var createdModule = false;
            if (group.ExactExisting && group.ExistingModuleId is long exactId)
            {
                moduleId = exactId;
                existingEnriched++;
            }
            else
            {
                var created = await CreateOrReuseApprovedDraftModuleAsync(group, primary.ResourceId, cancellationToken);
                moduleId = created.ModuleId;
                createdModule = created.Created;
                if (createdModule) modulesCreated++; else existingEnriched++;
            }
            moduleIds.Add(moduleId);

            foreach (var source in allSources)
                await LinkResourceToModuleAsync(moduleId, source.ResourceId, "Approved learning source", cancellationToken);

            foreach (var detail in details)
            {
                var candidate = detail.Candidate;
                try
                {
                    if (candidate.CandidateType.Equals("Question", StringComparison.OrdinalIgnoreCase))
                    {
                        await AddCandidateToQuestionBankAsync(candidate.Id, cancellationToken);
                        if (!detail.IsInQuestionBank) questionsAdded++;
                    }
                    else if (candidate.CandidateType.Equals("Formula", StringComparison.OrdinalIgnoreCase))
                    {
                        await AddCandidateToFormulaLibraryAsync(candidate.Id, cancellationToken);
                        if (!detail.IsInFormulaLibrary) formulasAdded++;
                    }

                    var mutation = await AddCandidateToModuleAsync(candidate.Id, moduleId, cancellationToken);
                    if (mutation.Status.Equals("Compare", StringComparison.OrdinalIgnoreCase))
                    {
                        candidatesSkippedForSimilarity++;
                        continue;
                    }
                    candidatesUsed++;
                    switch (candidate.CandidateType)
                    {
                        case "Question": questionsUsed++; break;
                        case "Formula": formulasUsed++; break;
                        case "Definition": definitionsUsed++; break;
                        case "LessonIdea": lessonIdeasUsed++; break;
                        case "Practical": practicalUsed++; break;
                        case "Safety": safetyUsed++; break;
                        default: otherUsed++; break;
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("substantially similar", StringComparison.OrdinalIgnoreCase))
                {
                    candidatesSkippedForSimilarity++;
                }
            }
        }

        return new ApprovedStudyBuildResult(
            selected.Length, modulesCreated, existingEnriched, groupsSkippedForComparison, candidatesUsed,
            candidatesSkippedForSimilarity, questionsAdded, formulasAdded, questionsUsed, formulasUsed, definitionsUsed,
            lessonIdeasUsed, practicalUsed, safetyUsed, otherUsed, moduleIds.OrderBy(x => x).ToArray());
    }

    private async Task<IReadOnlyList<LearningCandidateListItem>> GetApprovedCandidatesForBuildAsync(string courseLevel, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
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
            WHERE lc.status='Approved' AND ($level='' OR lc.course_level=$level)
            ORDER BY lc.learning_subject COLLATE NOCASE,lc.topic COLLATE NOCASE,lc.subtopic COLLATE NOCASE,lc.id;
            """;
        command.Parameters.AddWithValue("$level", courseLevel.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<LearningCandidateListItem>();
        while (await reader.ReadAsync(cancellationToken)) items.Add(ReadCandidate(reader));
        return items;
    }

    private async Task<IReadOnlyList<LearningCandidateListItem>> GetApprovedCandidatesForGroupAsync(ApprovedStudyProposalGroup group, CancellationToken cancellationToken)
    {
        var all = await GetApprovedCandidatesForBuildAsync(group.CourseLevel, cancellationToken);
        return all.Where(x => MakeStudyGroupKey(x.CourseLevel, x.LearningSubject, x.Topic, x.Subtopic).Equals(group.GroupKey, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private async Task<int> CountDistinctCandidateSourcesAsync(IEnumerable<long> candidateIds, CancellationToken cancellationToken)
    {
        var ids = candidateIds.Where(x => x > 0).Distinct().ToArray();
        if (ids.Length == 0) return 0;
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        var names = new List<string>();
        for (var i = 0; i < ids.Length; i++)
        {
            var name = "$id" + i;
            names.Add(name);
            command.Parameters.AddWithValue(name, ids[i]);
        }
        command.CommandText = $"SELECT COUNT(DISTINCT resource_id) FROM learning_candidate_sources WHERE candidate_id IN ({string.Join(",", names)});";
        return Convert.ToInt32((long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L));
    }

    private async Task<(long ModuleId, bool Created)> CreateOrReuseApprovedDraftModuleAsync(
        ApprovedStudyProposalGroup group, long primaryResourceId, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();
        var key = CanonicalKey(group.ProposedTitle);
        var exact = connection.CreateCommand();
        exact.Transaction = tx;
        exact.CommandText = """
            SELECT id FROM learning_modules
            WHERE canonical_key=$key AND course_level=$level
              AND learning_subject=$learning COLLATE NOCASE
              AND topic=$topic COLLATE NOCASE
              AND status<>'Archived'
            ORDER BY id LIMIT 1;
            """;
        exact.Parameters.AddWithValue("$key", key);
        exact.Parameters.AddWithValue("$level", group.CourseLevel);
        exact.Parameters.AddWithValue("$learning", group.LearningSubject);
        exact.Parameters.AddWithValue("$topic", group.Topic);
        var existing = await exact.ExecuteScalarAsync(cancellationToken);
        if (existing is not null)
        {
            var id = Convert.ToInt64(existing);
            await LinkSourceAsync(connection, tx, id, primaryResourceId, "Approved learning source", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return (id, false);
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO learning_modules(title,summary,subject,course_level,learning_subject,topic,subtopic,status,primary_resource_id,created_utc,updated_utc,canonical_key)
            VALUES($title,$summary,$subject,$level,$learning,$topic,$subtopic,'Draft',$resource,$now,$now,$key)
            RETURNING id;
            """;
        insert.Parameters.AddWithValue("$title", group.ProposedTitle);
        insert.Parameters.AddWithValue("$summary", "Draft lesson assembled from reviewed and approved source-linked learning candidates. Review the teaching flow, answers, hints and interactive activities before marking Ready.");
        insert.Parameters.AddWithValue("$subject", group.Subject);
        insert.Parameters.AddWithValue("$level", group.CourseLevel);
        insert.Parameters.AddWithValue("$learning", group.LearningSubject);
        insert.Parameters.AddWithValue("$topic", group.Topic);
        insert.Parameters.AddWithValue("$subtopic", group.Subtopic);
        insert.Parameters.AddWithValue("$resource", primaryResourceId);
        insert.Parameters.AddWithValue("$now", now);
        insert.Parameters.AddWithValue("$key", key);
        var moduleId = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
        await LinkSourceAsync(connection, tx, moduleId, primaryResourceId, "Primary approved learning source", cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return (moduleId, true);
    }

    private static string BuildGroupingIdentity(LearningCandidateListItem x)
        => string.Join("\u001f", x.CourseLevel.Trim(), x.LearningSubject.Trim(), x.Topic.Trim(), x.Subtopic.Trim());

    private static string ProposedLessonTitle(string learningSubject, string topic, string subtopic)
    {
        var learning = learningSubject.Trim();
        var t = topic.Trim();
        var sub = subtopic.Trim();
        if (learning.Equals("Electrical Machines", StringComparison.OrdinalIgnoreCase))
        {
            var prefix = t.Equals("DC Machines", StringComparison.OrdinalIgnoreCase) ? "DC" : t.Equals("AC Machines", StringComparison.OrdinalIgnoreCase) ? "AC" : "";
            if (sub.Equals("Speed Control", StringComparison.OrdinalIgnoreCase) && prefix.Length > 0) return $"{prefix} Motor Speed Control";
            if (sub.Equals("Construction", StringComparison.OrdinalIgnoreCase) && prefix.Length > 0) return $"{prefix} Machine Construction";
            if (sub.Equals("Principles & Characteristics", StringComparison.OrdinalIgnoreCase) && prefix.Length > 0) return $"{prefix} Motor Principles & Characteristics";
            if (sub.Equals("Starters", StringComparison.OrdinalIgnoreCase) && prefix.Length > 0) return $"{prefix} Motor Starters";
            if (sub.Equals("Generators", StringComparison.OrdinalIgnoreCase) && prefix.Length > 0) return $"{prefix} Generator Principles";
        }
        if (!string.IsNullOrWhiteSpace(sub) && !sub.Equals(t, StringComparison.OrdinalIgnoreCase)) return $"{t} — {sub}";
        if (!string.IsNullOrWhiteSpace(t)) return t;
        if (!string.IsNullOrWhiteSpace(learning)) return learning + " — Reviewed Learning";
        return "Unassigned ONC Learning";
    }

    private static string MakeStudyGroupKey(string courseLevel, string learningSubject, string topic, string subtopic)
    {
        var raw = string.Join("\u001f", courseLevel.Trim().ToLowerInvariant(), learningSubject.Trim().ToLowerInvariant(), topic.Trim().ToLowerInvariant(), subtopic.Trim().ToLowerInvariant());
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash.AsSpan(0, 10));
    }
}
