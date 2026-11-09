namespace WICareerBuilder.Core.Models;

public sealed record DiscoveryAssessment(
    string Decision,
    string Reason,
    string? MediaKind = null,
    string? SuggestedRoleKey = null,
    string CatalogueLane = "Learning",
    string LaneReason = "Learning/reference catalogue");

public sealed record DirectoryDiscoveryAssessment(
    bool ShouldPrune,
    string Reason);

public sealed record DiscoveredFile(
    string RelativePath,
    string FileName,
    string Extension,
    long SizeBytes,
    DateTimeOffset LastWriteUtc,
    string MediaKind,
    string DiscoveryDecision,
    string DiscoveryReason,
    string? SuggestedRoleKey = null,
    string CatalogueLane = "Learning",
    string LaneReason = "Learning/reference catalogue");

public sealed record ExcludedDiscoveryFile(
    string RelativePath,
    string Extension,
    string Reason);

public sealed record PrunedDiscoveryDirectory(
    string RelativePath,
    string Reason);

public sealed record DiscoveryScan(
    IReadOnlyList<DiscoveredFile> Files,
    IReadOnlyList<ExcludedDiscoveryFile> ExcludedFiles,
    IReadOnlyList<PrunedDiscoveryDirectory> PrunedDirectories,
    int DirectoriesVisited,
    int DirectoriesSkipped,
    int InfrastructureDirectoriesSkipped,
    int ContextDirectoriesSkipped,
    int UnsupportedFilesSkipped,
    IReadOnlyList<string> Errors);

public sealed record DiscoveryResult(
    int ResourcesFound,
    int PolicyExcludedFiles,
    int DirectoriesVisited,
    int DirectoriesSkipped,
    int InfrastructureDirectoriesSkipped,
    int ContextDirectoriesSkipped,
    int UnsupportedFilesSkipped,
    int Errors,
    int NewFiles = 0,
    int ModifiedFiles = 0,
    int UnchangedFiles = 0,
    int MissingFiles = 0,
    int AnalysisNeeded = 0,
    int HardNoiseSkipped = 0,
    int PersistentExclusions = 0,
    int PrunedBranches = 0,
    int PrunedCatalogueRows = 0,
    int ReconciledLeakageRemoved = 0,
    int ProtectedLeakageRows = 0,
    int RestoredByRefinedContext = 0,
    int ContextRoleSuggestions = 0,
    int LearningCatalogueResources = 0,
    int ProjectArtifactResources = 0,
    int DualLaneResources = 0,
    int LowValueProjectNoiseSkipped = 0,
    long ScanRunId = 0);

public sealed record LibraryCleanupResult(
    long? SourceId,
    int Examined,
    int Excluded,
    int Restored,
    int Review,
    int Included);

public sealed record ExcludedResourceItem(
    long Id,
    long LibrarySourceId,
    string SourceName,
    string RelativePath,
    string FileName,
    string Extension,
    string DiscoveryReason);
