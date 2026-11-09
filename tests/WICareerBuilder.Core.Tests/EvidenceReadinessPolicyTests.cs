using WICareerBuilder.Core.Classification;

namespace WICareerBuilder.Core.Tests;

[TestClass]
public sealed class EvidenceReadinessPolicyTests
{
    [TestMethod]
    public void InternalQuiz_NeverCreatesQualificationOrCvRecommendation()
    {
        var r = EvidenceReadinessPolicy.Assess(new("InternalQuiz","QualificationEvidence",true,true,"UserConfirmed"));
        Assert.IsFalse(r.CanRecommendForCv);
        Assert.IsFalse(r.CanSupportQualificationClaim);
    }

    [TestMethod]
    public void DemonstratedProject_WithPracticalEvidenceAndUnderstanding_CanBeRecommended()
        => Assert.IsTrue(EvidenceReadinessPolicy.Assess(new("ProjectTest","DemonstratedProject",true,true,"Reviewed")).CanRecommendForCv);

    [TestMethod]
    public void DemonstratedProject_WithoutUnderstanding_IsNotCvReady()
        => Assert.IsFalse(EvidenceReadinessPolicy.Assess(new("ProjectTest","DemonstratedProject",true,false,"Reviewed")).CanRecommendForCv);

    [TestMethod]
    public void StudiedMaterial_IsNotCvReadyEvenWhenReviewed()
        => Assert.IsFalse(EvidenceReadinessPolicy.Assess(new("Coursework","Studied",false,true,"Reviewed")).CanRecommendForCv);

    [TestMethod]
    public void UserConfirmedExternalQualification_CanSupportQualificationClaim()
    {
        var r = EvidenceReadinessPolicy.Assess(new("ExternalQualification","QualificationEvidence",false,true,"UserConfirmed"));
        Assert.IsTrue(r.CanRecommendForCv);
        Assert.IsTrue(r.CanSupportQualificationClaim);
    }

    [TestMethod]
    public void UnconfirmedExternalQualification_CannotSupportQualificationClaim()
        => Assert.IsFalse(EvidenceReadinessPolicy.Assess(new("ExternalQualification","QualificationEvidence",false,true,"Unverified")).CanSupportQualificationClaim);
}
