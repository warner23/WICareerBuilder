namespace WICareerBuilder.Core.Models;

public sealed record ScannerSummary(
    long? SourceId,
    int TotalResources,
    int NewResources,
    int ModifiedResources,
    int UnchangedResources,
    int MissingResources,
    int AnalysisNeeded,
    int FingerprintPending,
    int Fingerprinted,
    int DuplicateFingerprintGroups,
    int DuplicateFingerprintResources,
    DateTimeOffset? LastScanUtc);

public sealed record FingerprintCandidate(
    long ResourceId,
    long LibrarySourceId,
    string RootPath,
    string RelativePath,
    long SizeBytes,
    DateTimeOffset LastWriteUtc);

public sealed record FingerprintBatchResult(
    int Requested,
    int Examined,
    int Fingerprinted,
    int SkippedUnavailable,
    int Failed,
    int DuplicateFingerprintGroups,
    int DuplicateFingerprintResources,
    IReadOnlyList<string> Errors);

public sealed record StagedAnalysisBatchResult(
    int Requested,
    int Analysed,
    int Cached,
    int Limited,
    int Failed,
    int Remaining,
    IReadOnlyList<long> ProcessedResourceIds);
