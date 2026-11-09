using WICareerBuilder.Core.Classification;
using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Core.Tests;

[TestClass]
public sealed class PlacementAuditRegressionTests
{
    [TestMethod]
    public void ThreePointStarter_MapsToDcMachinesStarters()
    {
        AssertAutomatic(
            "Three Point Starter.docx",
            "Electrical Machines/dc motors/Three Point Starter.docx",
            "DC Machines",
            "Starters",
            "A three point starter limits the high starting current of a dc motor and provides no-volt and overload protection. " +
            "Its armature, field and holding-coil connections allow controlled starting while retaining normal motor protection.",
            "Starters");
    }

    [TestMethod]
    public void TypesOfDcMotor_PrefersTypesAndApplications_WhenBodyContainsConstructionVocabulary()
    {
        AssertAutomatic(
            "Types of DC Motor.docx",
            "Electrical Machines/dc motors/Types of DC Motor.docx",
            "DC Machines",
            "Types & Applications",
            "Types of dc motor include shunt motor, series motor, compound motor and permanent magnet dc arrangements. " +
            "Construction vocabulary such as armature winding, field winding, commutator, stator and rotor is included only to compare how the motor types are built and applied.",
            "Types & Applications");
    }

    [TestMethod]
    public void DcMotorSpeedControl_MapsToSpeedControl()
    {
        AssertAutomatic(
            "DC MOTOR speed control.docx",
            "Electrical Machines/dc motors/DC MOTOR speed control.docx",
            "DC Machines",
            "Speed Control",
            "DC motor speed control can use armature voltage control, field control and Ward Leonard methods. " +
            "The notes compare speed regulation, operating range and practical control of a direct current motor under load.",
            "Speed Control");
    }

    [TestMethod]
    public void PrinciplesOfDcGenerators_MapsToGenerators()
    {
        AssertAutomatic(
            "Principles of DC Generators.docx",
            "Electrical Machines/dc motors/Principles of DC Generators.docx",
            "DC Machines",
            "Generators",
            "A dc generator converts mechanical energy to electrical energy through generator action and generated emf. " +
            "These principles describe armature conductors, field flux, commutation and the behaviour of a direct current generator.",
            "Generators");
    }

    [TestMethod]
    public void TorqueEquationOfDcMotor_MapsToPrinciplesAndCharacteristics()
    {
        AssertAutomatic(
            "Torque equation of DC motor.docx",
            "Electrical Machines/dc motors/Torque equation of DC motor.docx",
            "DC Machines",
            "Principles & Characteristics",
            "The torque equation of a dc motor relates armature current and magnetic flux to developed torque. " +
            "The material also discusses back emf, torque characteristic, speed torque behaviour and the operating principles of the dc machine.",
            "Principles & Characteristics");
    }

    private static void AssertAutomatic(
        string fileName,
        string relativePath,
        string topic,
        string subtopic,
        string text,
        string expectedSubtopic)
    {
        var resource = Resource(fileName, relativePath);
        var analysis = Analysis("Analysed", topic, [topic, subtopic], text);
        var confidence = PlacementDecisionPolicy.Assess(resource, analysis, "Electrical Machines", topic, subtopic);
        var decision = PlacementDecisionPolicy.Decide("Electrical Machines", topic, subtopic, confidence);

        Assert.IsTrue(confidence.Subject >= PlacementDecisionPolicy.AutoThreshold,
            $"Subject confidence was {confidence.Subject:0.00}. {confidence.Reason}");
        Assert.IsTrue(confidence.Topic >= PlacementDecisionPolicy.AutoThreshold,
            $"Topic confidence was {confidence.Topic:0.00}. {confidence.Reason}");
        Assert.IsTrue(confidence.Subtopic >= PlacementDecisionPolicy.AutoThreshold,
            $"Subtopic confidence was {confidence.Subtopic:0.00}. {confidence.Reason}");
        Assert.IsTrue(decision.CanApplySubtopic);
        Assert.AreEqual(expectedSubtopic, decision.AppliedSubtopic);
    }

    private static ResourceItem Resource(string fileName, string relativePath)
        => new(1, 1, "Test", "", "", relativePath, fileName, ".docx", 1000,
            DateTimeOffset.UtcNow, "Document", "", "", true, false, "", "", "", "Unsorted");

    private static ResourceAnalysis Analysis(string status, string suggestedTopic, IReadOnlyList<string> subtopics, string text)
        => new(1, status, "Test", "test", DateTimeOffset.UtcNow, "Course Notes", suggestedTopic, subtopics,
            null, null, null, 1, text.Length, true, [], [], [], text[..Math.Min(text.Length, 200)], null, text);
}
