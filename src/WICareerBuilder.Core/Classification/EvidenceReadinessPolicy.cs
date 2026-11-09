namespace WICareerBuilder.Core.Classification;

public sealed record EvidenceReadinessInput(
    string EvidenceKind,
    string EvidenceStage,
    bool PracticalEvidence,
    bool UnderstandingDemonstrated,
    string VerificationStatus);

public sealed record EvidenceReadinessDecision(bool CanRecommendForCv, bool CanSupportQualificationClaim, string Reason);

public static class EvidenceReadinessPolicy
{
    public static EvidenceReadinessDecision Assess(EvidenceReadinessInput input)
    {
        var kind = (input.EvidenceKind ?? "").Trim();
        var stage = (input.EvidenceStage ?? "").Trim();
        var verification = (input.VerificationStatus ?? "").Trim();

        if (kind.Equals("InternalQuiz", StringComparison.OrdinalIgnoreCase))
            return new(false, false, "Internal quizzes can demonstrate knowledge only; they cannot create qualification or certification claims.");

        var qualificationEvidence = stage.Equals("QualificationEvidence", StringComparison.OrdinalIgnoreCase)
            && (kind.Equals("ExternalQualification", StringComparison.OrdinalIgnoreCase) || kind.Equals("ExternalCertification", StringComparison.OrdinalIgnoreCase))
            && verification.Equals("UserConfirmed", StringComparison.OrdinalIgnoreCase);
        if (qualificationEvidence)
            return new(true, true, "User-confirmed external credential evidence may support a truthful credential and CV record.");

        var demonstrated = stage.Equals("DemonstratedProject", StringComparison.OrdinalIgnoreCase)
            || stage.Equals("DemonstratedEmployment", StringComparison.OrdinalIgnoreCase);
        if (demonstrated && input.PracticalEvidence && input.UnderstandingDemonstrated)
            return new(true, false, "Practical demonstrated evidence with understanding may be recommended for CV review.");

        return new(false, false, "Evidence remains useful but is not strong enough for a CV-ready recommendation.");
    }
}
