namespace WICareerBuilder.Core.Models;

public sealed record CareerPortfolioEntry(
    long Id,
    long ProjectId,
    string ProjectName,
    string Title,
    string Problem,
    string DesignSummary,
    string BuildSummary,
    string TestSummary,
    string TroubleshootingSummary,
    string ResultSummary,
    string Status,
    string PrivacyMode,
    bool NoWebAccess,
    bool UserApprovedExport,
    int EvidenceCount,
    DateTimeOffset UpdatedUtc);

public sealed record CareerWorkspaceSummary(
    int ProjectCount,
    int SkillCount,
    int EvidenceCount,
    int CvRecommendedCount,
    int CvApprovedCount,
    int QualificationCount,
    int CertificationCount,
    int CvCount,
    int PortfolioCount);
