using WICareerBuilder.Core.Classification;
namespace WICareerBuilder.Core.Tests;
[TestClass]
public sealed class CvItemEligibilityPolicyTests
{
 [TestMethod] public void RecommendedEvidenceWithoutUserApproval_IsRejected()=>Assert.IsFalse(CvItemEligibilityPolicy.Assess(new("Evidence",true,false,false,"")).CanAdd);
 [TestMethod] public void UserApprovedButNotRecommendedEvidence_IsRejected()=>Assert.IsFalse(CvItemEligibilityPolicy.Assess(new("Evidence",false,true,false,"")).CanAdd);
 [TestMethod] public void RecommendedAndApprovedEvidence_IsAccepted()=>Assert.IsTrue(CvItemEligibilityPolicy.Assess(new("Evidence",true,true,false,"")).CanAdd);
 [TestMethod] public void UnconfirmedCredential_IsRejected()=>Assert.IsFalse(CvItemEligibilityPolicy.Assess(new("Credential",false,false,false,"Held")).CanAdd);
 [TestMethod] public void ConfirmedHeldCredential_IsAccepted()=>Assert.IsTrue(CvItemEligibilityPolicy.Assess(new("Credential",false,false,true,"Held")).CanAdd);
}
