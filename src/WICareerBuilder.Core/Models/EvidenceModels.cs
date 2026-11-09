namespace WICareerBuilder.Core.Models;

public sealed record CareerSkill(
    long Id,
    string Name,
    string Category,
    string Description,
    string Status,
    int EvidenceCount,
    DateTimeOffset UpdatedUtc);

public sealed record CareerEvidenceItem(
    long Id,
    string Title,
    string EvidenceKind,
    string EvidenceStage,
    string Description,
    long? ProjectId,
    long? ResourceId,
    bool PracticalEvidence,
    bool UnderstandingDemonstrated,
    string VerificationStatus,
    bool CvReadyRecommended,
    bool CvReadyUserApproved,
    string PrivacyMode,
    bool NoWebAccess,
    DateTimeOffset UpdatedUtc);

public sealed record EvidenceSkillLink(
    long EvidenceId,
    long SkillId,
    string SkillName,
    string Strength,
    string Notes);
