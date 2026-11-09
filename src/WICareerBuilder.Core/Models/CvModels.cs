namespace WICareerBuilder.Core.Models;

public sealed record CareerCvSummary(
    long Id,
    string Name,
    string TargetRole,
    string FocusKey,
    string Status,
    int ItemCount,
    DateTimeOffset UpdatedUtc);

public sealed record CareerCvItem(
    long Id,
    long CvId,
    string SourceKind,
    long SourceId,
    string Heading,
    string Statement,
    int SortOrder,
    bool UserApproved);

public sealed record MasterEvidenceSummary(
    int ProjectCount,
    int SkillCount,
    int EvidenceCount,
    int ApprovedCvEvidenceCount,
    int CredentialCount,
    int CvCount);
