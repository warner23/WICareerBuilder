namespace WICareerBuilder.Core.Models;

public sealed record DashboardSummary(
    int SourceCount,
    int ResourceCount,
    int DocumentCount,
    int VideoCount,
    int ImageCount,
    int ArchiveCount,
    int UnanalysedCount);
