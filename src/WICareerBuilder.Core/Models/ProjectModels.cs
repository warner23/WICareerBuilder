namespace WICareerBuilder.Core.Models;

public sealed record CareerProjectSummary(
    long Id,
    string Name,
    string Category,
    string Summary,
    string Status,
    string PrivacyMode,
    bool NoWebAccess,
    bool CvEligible,
    bool PortfolioEligible,
    int ResourceCount,
    int JournalCount,
    DateTimeOffset UpdatedUtc);

public sealed record CareerProjectDetail(
    CareerProjectSummary Project,
    IReadOnlyList<ProjectResourceLink> Resources,
    IReadOnlyList<ProjectJournalEntry> Journal);

public sealed record ProjectResourceLink(
    long ResourceId,
    string FileName,
    string RelativePath,
    string RelationshipKind,
    string Notes);

public sealed record ProjectJournalEntry(
    long Id,
    long ProjectId,
    string EntryType,
    string Title,
    string Body,
    bool IsPrivate,
    DateTimeOffset CreatedUtc);
