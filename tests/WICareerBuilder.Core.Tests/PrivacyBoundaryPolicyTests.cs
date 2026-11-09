using WICareerBuilder.Core.Classification;

namespace WICareerBuilder.Core.Tests;

[TestClass]
public sealed class PrivacyBoundaryPolicyTests
{
    [TestMethod]
    public void NoWebAccess_BlocksExternalAiEvenWhenModeSaysWebAllowed()
    {
        var d=PrivacyBoundaryPolicy.Assess(new("WebAllowed",true,"ExternalAI"));
        Assert.IsFalse(d.Allowed);
    }

    [TestMethod]
    public void PrivateMode_BlocksWebResearchWithoutNoWebFlag()
    {
        var d=PrivacyBoundaryPolicy.Assess(new("Private",false,"WebResearch"));
        Assert.IsFalse(d.Allowed);
    }

    [TestMethod]
    public void PortfolioEligibility_IsNotWebConsent()
    {
        var d=PrivacyBoundaryPolicy.Assess(new("Portfolio eligible",false,"ExternalUpload"));
        Assert.IsFalse(d.Allowed);
    }

    [TestMethod]
    public void ExplicitWebAllowed_WithNoWebDisabled_AllowsExternalOperation()
    {
        var d=PrivacyBoundaryPolicy.Assess(new("WebAllowed",false,"WebResearch"));
        Assert.IsTrue(d.Allowed);
    }

    [TestMethod]
    public void LocalOperation_DoesNotRequireWebConsent()
    {
        var d=PrivacyBoundaryPolicy.Assess(new("Private",true,"LocalAnalysis"));
        Assert.IsTrue(d.Allowed);
    }
}
