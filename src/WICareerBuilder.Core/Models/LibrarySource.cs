namespace WICareerBuilder.Core.Models;

public sealed record LibrarySource(
    long Id,
    string Name,
    string RootPath,
    string Subject,
    string CourseLevel,
    IReadOnlyList<string> Exclusions,
    DateTimeOffset? LastDiscoveredUtc,
    bool IsEnabled,
    string LifecycleStatus = "Active",
    DateTimeOffset? RemovedUtc = null);
