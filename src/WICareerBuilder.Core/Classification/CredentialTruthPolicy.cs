namespace WICareerBuilder.Core.Classification;

public sealed record CredentialTruthInput(string CredentialType,string Status,string Issuer,bool UserConfirmed,string VerificationStatus);
public sealed record CredentialTruthDecision(bool CanRecordAsHeld,bool CanUseAsCredentialClaim,string Reason);

public static class CredentialTruthPolicy
{
    public static CredentialTruthDecision Assess(CredentialTruthInput input)
    {
        var type=(input.CredentialType??"").Trim();
        var status=(input.Status??"").Trim();
        var issuer=(input.Issuer??"").Trim();
        var verification=(input.VerificationStatus??"").Trim();

        if (type.Equals("StudyHistory",StringComparison.OrdinalIgnoreCase))
            return new(true,false,"Study history may be recorded without presenting it as an awarded credential.");

        var external=type.Equals("Qualification",StringComparison.OrdinalIgnoreCase)||type.Equals("Certification",StringComparison.OrdinalIgnoreCase);
        if (!external) return new(false,false,"Unknown credential type.");
        if (!status.Equals("Held",StringComparison.OrdinalIgnoreCase)) return new(true,false,"Credential is not marked held.");
        if (!input.UserConfirmed) return new(false,false,"Held credentials require explicit user confirmation.");
        if (issuer.Length==0 || issuer.Equals("WICareerBuilder",StringComparison.OrdinalIgnoreCase)) return new(false,false,"External qualifications/certifications require a genuine external issuer.");
        var verified=verification.Equals("UserConfirmed",StringComparison.OrdinalIgnoreCase)||verification.Equals("Verified",StringComparison.OrdinalIgnoreCase);
        return new(true,verified,"Held external credential confirmed by the user; verification controls claim strength.");
    }
}
