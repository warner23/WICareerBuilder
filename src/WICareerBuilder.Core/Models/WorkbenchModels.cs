namespace WICareerBuilder.Core.Models;

public sealed record WorkbenchSummary(
    int ResourceCount,
    int UnsortedCount,
    int NotAnalysedCount,
    int ImageCount,
    int CourseworkCount,
    int KnowledgeLinkCount,
    int SourceAnalysisRunCount,
    int RawFragmentCount,
    int PrototypeLessonCount,
    int OutputSuggestionCount,
    int EvidenceCandidateCount,
    int CareerPathCount);

public sealed record KnowledgeAreaOverviewItem(
    long Id,
    string Name,
    string Description,
    int CourseCount,
    int ResourceCount);

public sealed record KnowledgeCourseOverviewItem(
    long Id,
    long KnowledgeAreaId,
    string KnowledgeArea,
    string Name,
    string LevelCode,
    int SubjectCount,
    int ResourceCount);

public sealed record KnowledgeSubjectOverviewItem(
    long Id,
    long CourseId,
    string Name,
    int TopicCount,
    int ResourceCount,
    int PrototypeLessonCount);

public sealed record KnowledgeTopicOverviewItem(
    long Id,
    long CurriculumSubjectId,
    long? ParentTopicId,
    string Name,
    int ResourceCount);

public sealed record KnowledgeWorkbenchOverview(
    IReadOnlyList<KnowledgeAreaOverviewItem> Areas,
    IReadOnlyList<KnowledgeCourseOverviewItem> Courses,
    IReadOnlyList<KnowledgeSubjectOverviewItem> Subjects,
    IReadOnlyList<KnowledgeTopicOverviewItem> Topics,
    int TopicCount,
    int MultiLinkedResourceCount);

public sealed record ResourceRoleSummaryItem(
    long RoleId,
    string RoleKey,
    string Label,
    string Description,
    int AcceptedCount,
    int SuggestedCount);

public sealed record SourceAnalysisWorkbenchSummary(
    int RunCount,
    int CompletedRuns,
    int RawFragmentCount,
    int AssetCount,
    int NeedsReviewCount,
    int FailedRunCount);

public sealed record PrototypeMaterialSummary(
    int ModuleCount,
    int BlockCount,
    int CandidateCount,
    string Message);

public sealed record ResourceRoleAssignmentResult(
    int Requested,
    int Updated,
    string RoleKey,
    string RoleLabel,
    string Status);
