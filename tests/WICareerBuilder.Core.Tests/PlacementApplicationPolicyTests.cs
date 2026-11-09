using WICareerBuilder.Core.Classification;
using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Core.Tests;

[TestClass]
public sealed class PlacementApplicationPolicyTests
{
    [TestMethod]
    public void ManualDifferentPlacement_IsProtectedAndNeverSelectedForAutomaticWrite()
    {
        var proposed = FullDecision("Electrical Machines", "DC Machines", "Starters");
        var current = Organisation("Electrical Machines", "AC Machines", "", "Studied");

        var decision = PlacementApplicationPolicy.Decide(proposed, current, "Manual");

        Assert.AreEqual(PlacementApplicationAction.ProtectedExisting, decision.Action);
        Assert.AreEqual("ProtectedExisting", decision.Disposition);
        Assert.AreEqual("ProtectedExisting", decision.ProposalStatus);
        Assert.IsTrue(decision.HasProtectedPlacement);
        Assert.IsFalse(decision.PriorContentPlacement);
        Assert.IsFalse(decision.WithdrawPriorAutomatic);
        Assert.IsFalse(decision.ShouldWriteOrganisation);
    }

    [TestMethod]
    public void ManualWeakProposal_RemainsReviewOnlyWithoutWithdrawOrWrite()
    {
        var proposed = PlacementDecisionPolicy.Decide(
            "Electrical Machines",
            "DC Machines",
            "Starters",
            new ContentPlacementConfidence(0.85, 0.92, 0.92, 0.85, "weak subject"));
        var current = Organisation("Electrical Machines", "AC Machines", "", "Studied");

        var decision = PlacementApplicationPolicy.Decide(proposed, current, "Manual");

        Assert.AreEqual(PlacementApplicationAction.NeedsReview, decision.Action);
        Assert.AreEqual("NeedsReview", decision.Disposition);
        Assert.IsTrue(decision.HasProtectedPlacement);
        Assert.IsFalse(decision.PriorContentPlacement);
        Assert.IsFalse(decision.WithdrawPriorAutomatic);
        Assert.IsFalse(decision.ShouldWriteOrganisation);
    }

    [TestMethod]
    public void ManualSamePlacement_IsRecognisedWithoutAutomaticRewrite()
    {
        var proposed = FullDecision("Electrical Machines", "DC Machines", "Starters");
        var current = Organisation("Electrical Machines", "DC Machines", "Starters", "Studied");

        var decision = PlacementApplicationPolicy.Decide(proposed, current, "Manual");

        Assert.AreEqual(PlacementApplicationAction.AlreadyPlaced, decision.Action);
        Assert.AreEqual("AlreadyPlaced", decision.Disposition);
        Assert.AreEqual("Applied", decision.ProposalStatus);
        Assert.IsTrue(decision.HasProtectedPlacement);
        Assert.IsTrue(decision.SameAppliedPlacement);
        Assert.IsFalse(decision.ShouldWriteOrganisation);
    }

    [TestMethod]
    public void PriorContentAnalysis_WithWeakHierarchy_IsWithdrawnForReview()
    {
        var proposed = PlacementDecisionPolicy.Decide(
            "Electrical Machines",
            "DC Machines",
            "Starters",
            new ContentPlacementConfidence(0.85, 0.92, 0.92, 0.85, "weak subject"));
        var current = Organisation("Electrical Machines", "DC Machines", "Starters", "Studied");

        var decision = PlacementApplicationPolicy.Decide(proposed, current, "ContentAnalysis:0.9.1");

        Assert.AreEqual(PlacementApplicationAction.NeedsReview, decision.Action);
        Assert.AreEqual("NeedsReview", decision.Disposition);
        Assert.IsTrue(decision.PriorContentPlacement);
        Assert.IsFalse(decision.HasProtectedPlacement);
        Assert.IsTrue(decision.WithdrawPriorAutomatic);
        Assert.IsFalse(decision.ShouldWriteOrganisation);
    }

    [TestMethod]
    public void PriorContentAnalysis_WithChangedConfidentHierarchy_IsReevaluatedAndApplied()
    {
        var proposed = FullDecision("Electrical Machines", "DC Machines", "Speed Control");
        var current = Organisation("Electrical Machines", "DC Machines", "Starters", "Studied");

        var decision = PlacementApplicationPolicy.Decide(proposed, current, "ContentAnalysis:0.9.1");

        Assert.AreEqual(PlacementApplicationAction.ApplyFull, decision.Action);
        Assert.AreEqual("AutoOrganised", decision.Disposition);
        Assert.AreEqual("Applied", decision.ProposalStatus);
        Assert.IsTrue(decision.PriorContentPlacement);
        Assert.IsFalse(decision.SameAppliedPlacement);
        Assert.IsFalse(decision.WithdrawPriorAutomatic);
        Assert.IsTrue(decision.ShouldWriteOrganisation);
    }

    [TestMethod]
    public void PriorContentAnalysis_WithWeakChild_IsReevaluatedToParentOnlyReview()
    {
        var proposed = PlacementDecisionPolicy.Decide(
            "Electrical Machines",
            "DC Machines",
            "Starters",
            new ContentPlacementConfidence(0.95, 0.94, 0.85, 0.85, "weak child"));
        var current = Organisation("Electrical Machines", "DC Machines", "Starters", "Studied");

        var decision = PlacementApplicationPolicy.Decide(proposed, current, "ContentAnalysis:0.9.2");

        Assert.AreEqual(PlacementApplicationAction.ApplyPartial, decision.Action);
        Assert.AreEqual("PartiallyOrganised", decision.Disposition);
        Assert.AreEqual("NeedsReview", decision.ProposalStatus);
        Assert.IsTrue(decision.PriorContentPlacement);
        Assert.IsFalse(decision.WithdrawPriorAutomatic);
        Assert.IsTrue(decision.ShouldWriteOrganisation);
    }

    private static ContentPlacementDecision FullDecision(string subject, string topic, string subtopic)
        => PlacementDecisionPolicy.Decide(
            subject,
            topic,
            subtopic,
            new ContentPlacementConfidence(0.95, 0.94, 0.93, 0.93, "confident"));

    private static ResourceOrganisation Organisation(string subject, string topic, string subtopic, string studyStatus)
        => new(
            1,
            "",
            "",
            subject,
            topic,
            subtopic,
            "Course Notes",
            studyStatus,
            "",
            DateTimeOffset.UtcNow);
}
