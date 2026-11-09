using WICareerBuilder.Core.Classification;

namespace WICareerBuilder.Core.Tests;

[TestClass]
public sealed class PlacementConceptCatalogTests
{
    [TestMethod]
    public void FourPointStarter_TitleMapsToDcMachinesStarters()
    {
        var matches = PlacementConceptCatalog.MatchMachineConcepts(
            "Four Point Starter.docx",
            "A starter limits starting current for a motor.",
            "Electrical Machines/dc motors/Four Point Starter.docx");

        var match = matches.Single(x => x.Concept == "Four-point DC motor starter");
        Assert.AreEqual("DC Machines", match.Family);
        Assert.AreEqual("Starters", match.Purpose);
        Assert.IsTrue(match.TitleMatched);
    }

    [TestMethod]
    public void SeriesMotor_WithDcContextMapsToTypesAndApplications()
    {
        var matches = PlacementConceptCatalog.MatchMachineConcepts(
            "Series Motor Notes.docx",
            "Series motor characteristics and applications.",
            "Electrical Machines/dc motors/Series Motor Notes.docx");

        var match = matches.Single(x => x.Concept == "DC series motor");
        Assert.AreEqual("DC Machines", match.Family);
        Assert.AreEqual("Types & Applications", match.Purpose);
    }

    [TestMethod]
    public void SeriesMotor_WithoutDcContextDoesNotBecomeDcSeriesMotor()
    {
        var matches = PlacementConceptCatalog.MatchMachineConcepts(
            "Series Motor Notes.docx",
            "Series motor characteristics and applications.",
            "General Engineering/Series Motor Notes.docx");

        Assert.IsFalse(matches.Any(x => x.Concept == "DC series motor"));
    }

    [TestMethod]
    public void TorqueEquation_WithDcContextMapsToPrinciplesAndCharacteristics()
    {
        var matches = PlacementConceptCatalog.MatchMachineConcepts(
            "Torque Equation.docx",
            "Derivation of the torque equation.",
            "Electrical Machines/dc motors/Torque Equation.docx");

        var match = matches.Single(x => x.Concept == "DC motor torque");
        Assert.AreEqual("DC Machines", match.Family);
        Assert.AreEqual("Principles & Characteristics", match.Purpose);
    }

    [TestMethod]
    public void InductionMotor_TitleMapsToAcMachines()
    {
        var matches = PlacementConceptCatalog.MatchMachineConcepts(
            "Induction Motor Fundamentals.docx",
            "Squirrel cage induction motor construction.",
            "Electrical Machines/ac motors/Induction Motor Fundamentals.docx");

        var match = matches.Single(x => x.Concept == "Induction motor");
        Assert.AreEqual("AC Machines", match.Family);
        Assert.IsTrue(match.TitleMatched);
    }
}
