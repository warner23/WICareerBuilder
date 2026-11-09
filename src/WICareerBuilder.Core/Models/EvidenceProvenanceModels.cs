namespace WICareerBuilder.Core.Models;

public sealed record CareerEvidenceProvenance(
    long Id,
    long EvidenceId,
    string SourceKind,
    string SourceReference,
    string LearningSubject,
    string Topic,
    string Subtopic,
    string Notes,
    DateTimeOffset CreatedUtc);
