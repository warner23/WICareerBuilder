namespace WICareerBuilder.Core.Classification;

public sealed record CvItemEligibilityInput(string SourceKind,bool EvidenceRecommended,bool EvidenceUserApproved,bool CredentialUserConfirmed,string CredentialStatus);
public sealed record CvItemEligibilityDecision(bool CanAdd,string Reason);

public static class CvItemEligibilityPolicy
{
    public static CvItemEligibilityDecision Assess(CvItemEligibilityInput input)
    {
        var kind=(input.SourceKind??"").Trim();
        if(kind.Equals("Evidence",StringComparison.OrdinalIgnoreCase))
            return input.EvidenceRecommended&&input.EvidenceUserApproved
                ? new(true,"Evidence was recommended and explicitly approved by the user.")
                : new(false,"Evidence must be both recommended and user-approved before entering a CV.");
        if(kind.Equals("Credential",StringComparison.OrdinalIgnoreCase))
            return input.CredentialUserConfirmed&&input.CredentialStatus.Equals("Held",StringComparison.OrdinalIgnoreCase)
                ? new(true,"Held credential was explicitly confirmed by the user.")
                : new(false,"Only user-confirmed held credentials may enter a CV.");
        return new(false,"CV items must trace to approved evidence or truthful held credentials.");
    }
}
