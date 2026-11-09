using WICareerBuilder.Core.Classification;
namespace WICareerBuilder.Core.Tests;

[TestClass]
public sealed class CredentialTruthPolicyTests
{
    [TestMethod] public void HeldQualification_RequiresUserConfirmation()=>Assert.IsFalse(CredentialTruthPolicy.Assess(new("Qualification","Held","College",false,"Unverified")).CanRecordAsHeld);
    [TestMethod] public void HeldCertification_RequiresExternalIssuer()=>Assert.IsFalse(CredentialTruthPolicy.Assess(new("Certification","Held","WICareerBuilder",true,"UserConfirmed")).CanRecordAsHeld);
    [TestMethod] public void IncompleteStudyHistory_IsRecordableButNotCredentialClaim(){var r=CredentialTruthPolicy.Assess(new("StudyHistory","Incomplete","College",false,"Unverified"));Assert.IsTrue(r.CanRecordAsHeld);Assert.IsFalse(r.CanUseAsCredentialClaim);}
    [TestMethod] public void UserConfirmedHeldQualification_CanBeRecorded()=>Assert.IsTrue(CredentialTruthPolicy.Assess(new("Qualification","Held","College",true,"UserConfirmed")).CanRecordAsHeld);
    [TestMethod] public void VerifiedHeldCertification_CanBeUsedAsCredentialClaim()=>Assert.IsTrue(CredentialTruthPolicy.Assess(new("Certification","Held","CompTIA",true,"Verified")).CanUseAsCredentialClaim);
}
