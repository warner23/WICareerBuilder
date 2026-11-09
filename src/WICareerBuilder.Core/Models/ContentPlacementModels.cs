namespace WICareerBuilder.Core.Models;

public sealed record ContentPlacementCandidate(
    long ResourceId,
    long LibrarySourceId,
    string RootPath,
    string RelativePath,
    string FileName,
    string Extension,
    string MediaKind,
    string FingerprintStatus);

public sealed record ContentPlacementItem(
    long ResourceId,
    string FileName,
    string RelativePath,
    string MediaKind,
    string LearningSubject,
    string Topic,
    string Subtopic,
    string ResourceType,
    double Confidence,
    double SubjectConfidence,
    double TopicConfidence,
    double SubtopicConfidence,
    string Disposition,
    string Reason,
    bool FingerprintedThisRun,
    string AnalysisStatus);

public sealed record ContentPlacementBatchResult(
    long RunId,
    string Focus,
    int Requested,
    int Examined,
    int Fingerprinted,
    int Analysed,
    int Cached,
    int AutoOrganised,
    int PartiallyOrganised,
    int NeedsReview,
    int ProtectedExisting,
    int Limited,
    int Failed,
    int RemainingMatching,
    IReadOnlyList<ContentPlacementItem> Items,
    DateTimeOffset CompletedUtc);

public sealed record ContentPlacementRunSummary(
    long RunId,
    long? LibrarySourceId,
    string Focus,
    int Requested,
    int Examined,
    int Fingerprinted,
    int Analysed,
    int Cached,
    int AutoOrganised,
    int PartiallyOrganised,
    int NeedsReview,
    int ProtectedExisting,
    int Limited,
    int Failed,
    DateTimeOffset StartedUtc,
    DateTimeOffset? CompletedUtc);
