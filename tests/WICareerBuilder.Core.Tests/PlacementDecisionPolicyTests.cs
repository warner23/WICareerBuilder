using WICareerBuilder.Core.Classification;
using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Core.Tests;

[TestClass]
public sealed class PlacementDecisionPolicyTests
{
    [TestMethod]
    public void AutoThreshold_RemainsPointEightSix()
    {
        var field = typeof(PlacementDecisionPolicy).GetField(nameof(PlacementDecisionPolicy.AutoThreshold));
        Assert.IsNotNull(field);
        Assert.AreEqual(0.86, (double)field!.GetRawConstantValue()!, 0.000001);
    }

    [TestMethod]
    public void Decide_AllLevelsAtThreshold_AppliesFullHierarchy()
    {
        var confidence = new ContentPlacementConfidence(0.86, 0.86, 0.86, 0.86, "boundary");
        var decision = PlacementDecisionPolicy.Decide("Electrical Machines", "DC Machines", "Starters", confidence);

        Assert.IsTrue(decision.CanApplySubject);
        Assert.IsTrue(decision.CanApplyTopic);
        Assert.IsTrue(decision.CanApplySubtopic);
        Assert.IsFalse(decision.IsPartial);
        Assert.AreEqual("Electrical Machines", decision.AppliedSubject);
        Assert.AreEqual("DC Machines", decision.AppliedTopic);
        Assert.AreEqual("Starters", decision.AppliedSubtopic);
    }

    [TestMethod]
    public void Decide_LowSubtopic_AppliesParentAndMarksPartial()
    {
        var confidence = new ContentPlacementConfidence(0.95, 0.91, 0.85, 0.85, "boundary");
        var decision = PlacementDecisionPolicy.Decide("Electrical Machines", "DC Machines", "Starters", confidence);

        Assert.IsTrue(decision.CanApplySubject);
        Assert.IsTrue(decision.CanApplyTopic);
        Assert.IsFalse(decision.CanApplySubtopic);
        Assert.IsTrue(decision.IsPartial);
        Assert.AreEqual("Electrical Machines", decision.AppliedSubject);
        Assert.AreEqual("DC Machines", decision.AppliedTopic);
        Assert.AreEqual(string.Empty, decision.AppliedSubtopic);
    }

    [TestMethod]
    public void Decide_LowTopic_DoesNotApplyChildHierarchy()
    {
        var confidence = new ContentPlacementConfidence(0.95, 0.85, 0.98, 0.85, "boundary");
        var decision = PlacementDecisionPolicy.Decide("Electrical Machines", "DC Machines", "Starters", confidence);

        Assert.IsTrue(decision.CanApplySubject);
        Assert.IsFalse(decision.CanApplyTopic);
        Assert.IsFalse(decision.CanApplySubtopic);
        Assert.IsFalse(decision.IsPartial);
        Assert.AreEqual("Electrical Machines", decision.AppliedSubject);
        Assert.AreEqual(string.Empty, decision.AppliedTopic);
        Assert.AreEqual(string.Empty, decision.AppliedSubtopic);
    }

    [TestMethod]
    public void Assess_FourPointStarter_ProducesAutomaticDcStarterPlacement()
    {
        var resource = Resource("Four Point Starter.docx", "Electrical Machines/dc motors/Four Point Starter.docx");
        var analysis = Analysis("Analysed", "DC Machines", ["DC Machines", "Starters"],
            "A four point starter limits the high starting current of a dc motor and provides no-volt and overload protection. " +
            "The starter uses armature, field and holding-coil connections so the motor can start safely while preserving normal field control.");

        var confidence = PlacementDecisionPolicy.Assess(resource, analysis, "Electrical Machines", "DC Machines", "Starters");
        var decision = PlacementDecisionPolicy.Decide("Electrical Machines", "DC Machines", "Starters", confidence);

        Assert.IsTrue(confidence.Subject >= PlacementDecisionPolicy.AutoThreshold);
        Assert.IsTrue(confidence.Topic >= PlacementDecisionPolicy.AutoThreshold);
        Assert.IsTrue(confidence.Subtopic >= PlacementDecisionPolicy.AutoThreshold);
        Assert.AreEqual("Starters", decision.AppliedSubtopic);
    }


    [TestMethod]
    public void Assess_FourPointStarter_ShortEvidence_RemainsBelowAutomaticSubjectThreshold()
    {
        var resource = Resource("Four Point Starter.docx", "Electrical Machines/dc motors/Four Point Starter.docx");
        var analysis = Analysis("Analysed", "DC Machines", ["DC Machines", "Starters"],
            "A four point starter limits starting current in a dc motor.");

        var confidence = PlacementDecisionPolicy.Assess(resource, analysis, "Electrical Machines", "DC Machines", "Starters");
        var decision = PlacementDecisionPolicy.Decide("Electrical Machines", "DC Machines", "Starters", confidence);

        Assert.AreEqual(0.85, confidence.Subject, 0.000001);
        Assert.IsTrue(confidence.Subject < PlacementDecisionPolicy.AutoThreshold);
        Assert.IsFalse(decision.CanApplySubject);
        Assert.AreEqual(string.Empty, decision.AppliedSubject);
    }

    [TestMethod]
    public void Assess_LimitedAnalysis_IsCappedBelowAutomaticThreshold()
    {
        var resource = Resource("Four Point Starter.docx", "Electrical Machines/dc motors/Four Point Starter.docx");
        var analysis = Analysis("Limited", "DC Machines", ["DC Machines", "Starters"],
            "A four point starter limits starting current in a dc motor.");

        var confidence = PlacementDecisionPolicy.Assess(resource, analysis, "Electrical Machines", "DC Machines", "Starters");
        var decision = PlacementDecisionPolicy.Decide("Electrical Machines", "DC Machines", "Starters", confidence);

        Assert.IsTrue(confidence.Subject <= 0.82);
        Assert.IsTrue(confidence.Topic <= 0.82);
        Assert.IsTrue(confidence.Subtopic <= 0.82);
        Assert.AreEqual(string.Empty, decision.AppliedSubject);
    }

    [TestMethod]
    public void Assess_AcTitleInDcFolder_CapsDcFamilyForReview()
    {
        var resource = Resource("Induction Motor Fundamentals.docx", "Electrical Machines/dc motors/Induction Motor Fundamentals.docx");
        var analysis = Analysis("Analysed", "DC Machines", ["DC Machines"],
            "Induction motor squirrel cage slip rotating magnetic field.");

        var confidence = PlacementDecisionPolicy.Assess(resource, analysis, "Electrical Machines", "DC Machines", string.Empty);

        Assert.IsTrue(confidence.Topic < PlacementDecisionPolicy.AutoThreshold);
    }

    private static ResourceItem Resource(string fileName, string relativePath)
        => new(1, 1, "Test", "", "", relativePath, fileName, ".docx", 1000,
            DateTimeOffset.UtcNow, "Document", "", "", true, false, "", "", "", "Unsorted");

    private static ResourceAnalysis Analysis(string status, string suggestedTopic, IReadOnlyList<string> subtopics, string text)
        => new(1, status, "Test", "test", DateTimeOffset.UtcNow, "Course Notes", suggestedTopic, subtopics,
            null, null, null, 1, text.Length, true, [], [], [], text[..Math.Min(text.Length, 200)], null, text);
}
