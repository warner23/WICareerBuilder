namespace WICareerBuilder.Core.Models;

public sealed record CareerPrivacyAccessAudit(
    long Id,
    string SourceKind,
    long SourceId,
    string Operation,
    string PrivacyMode,
    bool NoWebAccess,
    bool Allowed,
    string Reason,
    DateTimeOffset CreatedUtc);
