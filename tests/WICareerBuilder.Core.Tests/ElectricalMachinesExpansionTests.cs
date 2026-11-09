using WICareerBuilder.Core.Classification;

namespace WICareerBuilder.Core.Tests;

[TestClass]
public sealed class ElectricalMachinesExpansionTests
{
    [TestMethod]
    [DataRow("Ward Leonard Method.txt", "Ward Leonard speed control for a dc motor.", "Electrical Machines/dc motors/Ward Leonard Method.txt", "DC Machines", "Speed Control")]
    [DataRow("Armature Voltage Control.txt", "Armature voltage control changes dc motor speed.", "Electrical Machines/dc motors/Armature Voltage Control.txt", "DC Machines", "Speed Control")]
    [DataRow("Back EMF.txt", "Back emf is fundamental to dc motor operation and characteristics.", "Electrical Machines/dc motors/Back EMF.txt", "DC Machines", "Principles & Characteristics")]
    [DataRow("Armature Winding.txt", "Armature winding and commutator construction in a dc motor.", "Electrical Machines/dc motors/Armature Winding.txt", "DC Machines", "Construction")]
    [DataRow("Slip Ring Induction Motor.txt", "A wound rotor induction motor uses slip rings.", "Electrical Machines/ac motors/Slip Ring Induction Motor.txt", "AC Machines", "Types & Applications")]
    [DataRow("Single Phase Induction Motor.txt", "A single phase induction motor is an ac machine.", "Electrical Machines/ac motors/Single Phase Induction Motor.txt", "AC Machines", "Types & Applications")]
    [DataRow("Step Up Transformer.txt", "A step up transformer has primary and secondary windings.", "Electrical Machines/transformers/Step Up Transformer.txt", "Transformers", "Types & Applications")]
    [DataRow("Transformer Construction.txt", "Transformer construction uses a magnetic core and windings.", "Electrical Machines/transformers/Transformer Construction.txt", "Transformers", "Construction")]
    public void ExpandedMachineConcepts_MapToExpectedFamilyAndPurpose(string file, string body, string path, string family, string purpose)
    {
        var matches = PlacementConceptCatalog.MatchMachineConcepts(file, body, path);
        Assert.IsTrue(matches.Any(x => x.Family == family && x.Purpose == purpose),
            $"Expected {family}/{purpose}. Matches: {string.Join(", ", matches.Select(x => x.Concept + "=>" + x.Family + "/" + x.Purpose))}");
    }
}
