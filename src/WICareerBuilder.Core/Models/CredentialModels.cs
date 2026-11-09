namespace WICareerBuilder.Core.Models;

public sealed record CareerCredential(
    long Id,
    string CredentialType,
    string Title,
    string Issuer,
    string Status,
    string AwardDate,
    string ExpiryDate,
    string VerificationStatus,
    bool UserConfirmed,
    long? EvidenceId,
    string Notes,
    DateTimeOffset UpdatedUtc);
