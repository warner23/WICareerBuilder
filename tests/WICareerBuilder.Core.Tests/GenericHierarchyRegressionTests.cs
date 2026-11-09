using WICareerBuilder.Core.Classification;
using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Core.Tests;

[TestClass]
public sealed class GenericHierarchyRegressionTests
{
    [TestMethod]
    [DataRow("Electrical Principles", "Circuit Theory", "Kirchhoff and Ohm's law describe voltage current resistance and impedance in circuits.")]
    [DataRow("Electronics", "Op Amps", "Operational amplifier and op amp circuits use feedback, gain and semiconductor devices.")]
    [DataRow("Components", "Capacitors", "A capacitor, resistor and inductor datasheet describes passive component behaviour and values.")]
    [DataRow("PLC & Control", "Ladder Logic", "PLC programmable logic and ladder logic are used in an industrial control system.")]
    [DataRow("RF & Communications", "Antennas", "RF antenna modulation receiver transmitter and transceiver concepts are used in wireless communications.")]
    [DataRow("Test & Measurement", "Oscilloscopes", "Oscilloscope multimeter calibration and fault finding are core measurement activities.")]
    [DataRow("Health & Safety", "Risk Assessment", "A health and safety risk assessment identifies hazard controls and PPE requirements.")]
    [DataRow("Engineering Mathematics", "Algebra", "Algebra calculus trigonometry logarithm and equation manipulation support engineering mathematics.")]
    public void ExistingNonMachineDomains_ConfirmSubjectButDoNotOverstateTopic(string subject, string topic, string text)
    {
        text = text + " " + text; // representative analysed extract >=120 chars
        var resource = new ResourceItem(1,1,"Test","","","Learning/notes.txt","notes.txt",".txt",text.Length,DateTimeOffset.UtcNow,"Document","","",true,false,"","","","Unsorted");
        var analysis = new ResourceAnalysis(1,"Analysed","Test","test",DateTimeOffset.UtcNow,"Course Notes",subject,[topic],null,null,null,1,text.Length,true,[],[],[],text[..Math.Min(200,text.Length)],null,text);
        var confidence = PlacementDecisionPolicy.Assess(resource, analysis, subject, topic, string.Empty);
        var decision = PlacementDecisionPolicy.Decide(subject, topic, string.Empty, confidence);

        Assert.IsTrue(confidence.Subject >= PlacementDecisionPolicy.AutoThreshold, $"{subject} subject confidence {confidence.Subject:0.00}");
        Assert.IsTrue(confidence.Topic < PlacementDecisionPolicy.AutoThreshold, $"{subject}/{topic} topic confidence unexpectedly auto-qualified at {confidence.Topic:0.00}");
        Assert.IsTrue(decision.CanApplySubject);
        Assert.IsFalse(decision.CanApplyTopic);
    }

    [TestMethod]
    public void LimitedGenericAnalysis_RemainsBelowAutomaticThreshold()
    {
        var text = string.Concat(Enumerable.Repeat("PLC ladder logic programmable logic control system. ", 5));
        var resource = new ResourceItem(1,1,"Test","","","Learning/plc.txt","plc.txt",".txt",text.Length,DateTimeOffset.UtcNow,"Document","","",true,false,"","","","Unsorted");
        var analysis = new ResourceAnalysis(1,"Limited","Test","test",DateTimeOffset.UtcNow,"Course Notes","PLC & Control",["Ladder Logic"],null,null,null,1,text.Length,true,[],[],[],text[..Math.Min(200,text.Length)],null,text);
        var confidence = PlacementDecisionPolicy.Assess(resource, analysis, "PLC & Control", "Ladder Logic", string.Empty);
        Assert.IsTrue(confidence.Subject <= 0.82);
        Assert.IsTrue(confidence.Topic <= 0.82);
    }
}
