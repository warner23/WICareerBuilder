namespace WICareerBuilder.Core.Models;

public sealed record DiscoveryReviewItem(
    long LibrarySourceId,
    string SourceName,
    string RelativePath,
    string FileName,
    string Extension,
    string DiscoveryReason,
    string OverrideAction,
    bool HasResource,
    bool IsLearningCollectionCandidate);


public sealed record DiscoveryReviewSummary(
    int PendingCount,
    int AutoExcludedCount,
    int KeptExcludedCount,
    int IncludedCount,
    int AllReviewCount,
    int CollectionCount,
    int CollectionManagedItemCount,
    int ArchiveReviewCount);

public sealed record DiscoveryBulkActionResult(
    int Requested,
    int Updated,
    int CreatedResources,
    int PurgedResources,
    int ProtectedResources);

public sealed record LearningCollectionCandidate(
    long Id,
    long LibrarySourceId,
    string SourceName,
    string RootRelativePath,
    string DisplayName,
    string Status,
    int ItemCount,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);
