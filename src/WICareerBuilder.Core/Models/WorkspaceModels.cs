namespace WICareerBuilder.Core.Models;

public sealed record CurriculumSubjectSummary(
    long Id,
    long CourseId,
    string KnowledgeArea,
    string CourseName,
    string CourseLevel,
    string Name,
    int SortOrder,
    int ResourceCount,
    int ModuleCount);

public sealed record WorkspaceLibrarySummary(
    string Key,
    string Label,
    int ResourceCount,
    string Description);

public sealed record OncWorkspaceOverview(
    long CourseId,
    string KnowledgeArea,
    string CourseName,
    string CourseLevel,
    IReadOnlyList<CurriculumSubjectSummary> Subjects,
    IReadOnlyList<WorkspaceLibrarySummary> Libraries);

public sealed record BulkPreparationItem(
    long ResourceId,
    string FileName,
    string MediaKind,
    string Extension,
    string ScanStatus,
    string SuggestedResourceType,
    string SuggestedLearningSubject,
    string SuggestedTopic,
    string SuggestedSubtopic,
    string SuggestedStudyStatus,
    string Recommendation,
    long? ExistingModuleId,
    string? ExistingModuleTitle,
    double ExistingSimilarity,
    bool EligibleForBulkStudy,
    bool IsArchive,
    int RelatedCopyCount);

public sealed record BulkPreparationResult(
    string Purpose,
    int Requested,
    int Analysed,
    int ReusedAnalysis,
    int Limited,
    int Failed,
    IReadOnlyList<BulkPreparationItem> Items);

public sealed record BulkOrganisationResult(int Requested, int Updated);

public sealed record BulkStudyResult(
    int Requested,
    int Created,
    int ReusedExact,
    int LinkedToExisting,
    int SkippedPossibleDuplicate,
    int SkippedIneligible,
    IReadOnlyList<long> ModuleIds);

public sealed record LearningExtractionResult(
    int ResourcesProcessed,
    int NewCandidates,
    int ExistingCandidatesLinked,
    int PossibleDuplicatesForComparison,
    int Questions,
    int Formulas,
    int LessonIdeas,
    int OtherCandidates);

public sealed record LibraryLifecycleResult(
    long SourceId,
    string Status,
    int ResourceCount,
    int LearningReferenceCount,
    bool CanPermanentlyPurge,
    string Message);

public sealed record UpdatePackageInfo(
    bool IsInstalledBuild,
    string CurrentVersion,
    string InstallDirectory,
    string DataDirectory,
    string UpdateFeedUrl,
    string Message);

public sealed record LearningCandidateReviewSummary(
    int DraftCount,
    int CompareCount,
    int ApprovedCount,
    int RejectedCount,
    int UsedCount,
    int QuestionCount,
    int FormulaCount,
    int DefinitionCount,
    int LessonIdeaCount,
    int PracticalCount,
    int SafetyCount,
    int OtherCount,
    int QuestionBankCount,
    int FormulaLibraryCount);

public sealed record LearningCandidateListItem(
    long Id,
    string CandidateType,
    string Title,
    string Body,
    string Subject,
    string CourseLevel,
    string LearningSubject,
    string Topic,
    string Subtopic,
    string Status,
    double Confidence,
    int SourceCount,
    long? TargetModuleId,
    string? TargetModuleTitle,
    long? PossibleDuplicateOfId,
    string? PossibleDuplicateTitle,
    double PossibleDuplicateSimilarity,
    DateTimeOffset UpdatedUtc);

public sealed record LearningCandidateSource(
    long ResourceId,
    string FileName,
    string RelativePath,
    string SourceName,
    string LocationNote);

public sealed record LearningCandidateDetail(
    LearningCandidateListItem Candidate,
    IReadOnlyList<LearningCandidateSource> Sources,
    bool IsInQuestionBank,
    bool IsInFormulaLibrary,
    IReadOnlyList<long> LinkedModuleIds);

public sealed record LearningCandidateMutationResult(
    long CandidateId,
    string Status,
    string Message,
    long? CreatedId = null);

public sealed record ApprovedStudyProposalGroup(
    string GroupKey,
    string Subject,
    string CourseLevel,
    string LearningSubject,
    string Topic,
    string Subtopic,
    string ProposedTitle,
    int CandidateCount,
    int QuestionCount,
    int FormulaCount,
    int DefinitionCount,
    int LessonIdeaCount,
    int PracticalCount,
    int SafetyCount,
    int OtherCount,
    int SourceCount,
    bool IsBuildable,
    long? ExistingModuleId,
    string? ExistingModuleTitle,
    double ExistingSimilarity,
    bool ExactExisting,
    bool NeedsLessonComparison,
    string Recommendation);

public sealed record ApprovedStudyProposal(
    string CourseLevel,
    int ApprovedCandidateCount,
    int BuildableCandidateCount,
    int GroupCount,
    int BuildableGroupCount,
    int ComparisonGroupCount,
    IReadOnlyList<ApprovedStudyProposalGroup> Groups);

public sealed record ApprovedStudyBuildResult(
    int GroupsRequested,
    int ModulesCreated,
    int ExistingModulesEnriched,
    int GroupsSkippedForComparison,
    int CandidatesUsed,
    int CandidatesSkippedForSimilarity,
    int QuestionsAddedToBank,
    int FormulasAddedToLibrary,
    int QuestionsUsed,
    int FormulasUsed,
    int DefinitionsUsed,
    int LessonIdeasUsed,
    int PracticalUsed,
    int SafetyUsed,
    int OtherUsed,
    IReadOnlyList<long> ModuleIds);


public sealed record StudyClassificationIssue(
    long ModuleId,
    string ModuleTitle,
    string PrimaryResourceName,
    string CurrentLearningSubject,
    string CurrentTopic,
    string CurrentSubtopic,
    string SuggestedTitle,
    string SuggestedLearningSubject,
    string SuggestedTopic,
    string SuggestedSubtopic,
    string Reason,
    int LinkedResourceCount);

public sealed record StudyClassificationRepairResult(
    int Requested,
    int UpdatedModules,
    int UpdatedCandidates,
    int UpdatedResources,
    int MergedCandidates);

public sealed record LearningBlockComparisonItem(
    long Id,
    long CandidateId,
    string CandidateType,
    string CandidateTitle,
    string CandidateText,
    long ModuleId,
    string ModuleTitle,
    long ExistingBlockId,
    string ExistingBlockType,
    string ExistingBlockTitle,
    string ExistingBlockText,
    double Similarity,
    string Status,
    int CandidateSourceCount);

public sealed record LearningBlockComparisonResolution(
    long ComparisonId,
    string Action,
    long CandidateId,
    long ModuleId,
    long BlockId,
    string Message);
