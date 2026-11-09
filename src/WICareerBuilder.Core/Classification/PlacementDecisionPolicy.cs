using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Core.Classification;

public sealed record ContentPlacementConfidence(
    double Subject,
    double Topic,
    double Subtopic,
    double Overall,
    string Reason);

public sealed record ContentPlacementDecision(
    bool CanApplySubject,
    bool CanApplyTopic,
    bool CanApplySubtopic,
    bool IsPartial,
    string AppliedSubject,
    string AppliedTopic,
    string AppliedSubtopic);

/// <summary>
/// Pure placement policy extracted from the desktop bridge.
/// M02 intentionally preserves v0.9.2 confidence and threshold behaviour.
/// </summary>
public static class PlacementDecisionPolicy
{
    public const double AutoThreshold = 0.86;

    public static ContentPlacementDecision Decide(
        string learningSubject,
        string topic,
        string subtopic,
        ContentPlacementConfidence confidence)
    {
        var canApplySubject = !string.IsNullOrWhiteSpace(learningSubject) && confidence.Subject >= AutoThreshold;
        var canApplyTopic = canApplySubject && !string.IsNullOrWhiteSpace(topic) && confidence.Topic >= AutoThreshold;
        var canApplySubtopic = canApplyTopic && (string.IsNullOrWhiteSpace(subtopic) || confidence.Subtopic >= AutoThreshold);
        var isPartial = canApplyTopic && !string.IsNullOrWhiteSpace(subtopic) && confidence.Subtopic < AutoThreshold;

        return new ContentPlacementDecision(
            canApplySubject,
            canApplyTopic,
            canApplySubtopic,
            isPartial,
            canApplySubject ? learningSubject : string.Empty,
            canApplyTopic ? topic : string.Empty,
            canApplySubtopic ? subtopic : string.Empty);
    }

    public static ContentPlacementConfidence Assess(
        ResourceItem resource,
        ResourceAnalysis analysis,
        string learningSubject,
        string topic,
        string subtopic)
    {

        var fileName = resource.FileName.ToLowerInvariant();
        var path = resource.RelativePath.ToLowerInvariant();
        var content = analysis.ExtractedText[..Math.Min(analysis.ExtractedText.Length, 80_000)].ToLowerInvariant();

        var subjectConfidence = analysis.AnalysisStatus.Equals("Analysed", StringComparison.OrdinalIgnoreCase) ? 0.72 : 0.62;
        var topicConfidence = analysis.AnalysisStatus.Equals("Analysed", StringComparison.OrdinalIgnoreCase) ? 0.58 : 0.50;
        var subtopicConfidence = string.IsNullOrWhiteSpace(subtopic)
            ? 0.0
            : (analysis.AnalysisStatus.Equals("Analysed", StringComparison.OrdinalIgnoreCase) ? 0.46 : 0.40);

        if (analysis.ExtractedCharacters >= 120) subjectConfidence += 0.04;
        if (analysis.ExtractedCharacters >= 1200) subjectConfidence += 0.03;
        if (!string.IsNullOrWhiteSpace(learningSubject)) subjectConfidence += 0.05;

        var signals = new List<string>();
        var subjectCap = 0.98;
        var topicCap = 0.98;
        var subtopicCap = 0.98;

        bool Any(string value, params string[] terms) => terms.Any(t => value.Contains(t, StringComparison.OrdinalIgnoreCase));
        void AddSubject(string label, double amount) { signals.Add(label); subjectConfidence += amount; }
        void AddTopic(string label, double amount) { signals.Add(label); topicConfidence += amount; }
        void AddSubtopic(string label, double amount) { signals.Add(label); subtopicConfidence += amount; }
        void TopicConflict(string label, double cap = 0.82) { signals.Add(label); topicCap = Math.Min(topicCap, cap); }
        void SubtopicConflict(string label, double cap = 0.82) { signals.Add(label); subtopicCap = Math.Min(subtopicCap, cap); }

        if (learningSubject.Equals("Electrical Machines", StringComparison.OrdinalIgnoreCase))
        {
            var titleDc = Any(fileName, "dc motor", "dc motors", "dc machine", "dc generator", "direct current motor", "brushless dc");
            var titleAc = Any(fileName, "ac motor", "ac motors", "induction motor", "synchronous motor", "alternator");
            var titleStepper = Any(fileName, "stepper motor", "hybrid stepper");
            var titleTransformer = Any(fileName, "transformer", "transformers");
            var bodyDc = Any(content, "dc motor", "dc machine", "dc generator", "direct current motor", "commutator", "back emf", "back e.m.f");
            var bodyAc = Any(content, "ac motor", "induction motor", "synchronous motor", "alternator", "squirrel cage", "slip", "rotating magnetic field");
            var bodyStepper = Any(content, "stepper motor", "step angle", "stepping motor");
            var bodyTransformer = Any(content, "transformer", "primary winding", "secondary winding");
            var machineText = fileName + "\n" + content;

            if (Any(machineText, "motor", "generator", "transformer", "stator", "rotor", "armature", "commutator", "stepper"))
                AddSubject("content confirms the Electrical Machines domain", 0.08);

            var conceptMatches = PlacementConceptCatalog.MatchMachineConcepts(resource.FileName, content, resource.RelativePath);

            if (topic.Equals("DC Machines", StringComparison.OrdinalIgnoreCase))
            {
                if (titleDc) AddTopic("filename/title confirms DC machine family", 0.20);
                if (bodyDc) AddTopic("content confirms DC machine family", 0.10);
                if (!titleDc && Any(path, "/dc motors/", "/dc machines/")) AddTopic("folder context supports DC machine family", 0.04);
                var concept = conceptMatches.FirstOrDefault(x => x.Family.Equals("DC Machines", StringComparison.OrdinalIgnoreCase));
                if (concept is not null)
                {
                    if (concept.TitleMatched) AddTopic($"concept anchor '{concept.Concept}' supports DC machine family", 0.25);
                    else if (concept.ContentMatched) AddTopic($"content concept '{concept.Concept}' supports DC machine family", 0.10);
                }
                if (titleAc || titleStepper) TopicConflict("filename/title conflicts with proposed DC machine family", 0.80);
            }
            else if (topic.Equals("AC Machines", StringComparison.OrdinalIgnoreCase))
            {
                if (titleAc) AddTopic("filename/title confirms AC machine family", 0.20);
                if (bodyAc) AddTopic("content confirms AC machine family", 0.10);
                if (!titleAc && Any(path, "/ac motors/", "/ac machines/")) AddTopic("folder context supports AC machine family", 0.04);
                var concept = conceptMatches.FirstOrDefault(x => x.Family.Equals("AC Machines", StringComparison.OrdinalIgnoreCase));
                if (concept is not null)
                {
                    if (concept.TitleMatched) AddTopic($"concept anchor '{concept.Concept}' supports AC machine family", 0.25);
                    else if (concept.ContentMatched) AddTopic($"content concept '{concept.Concept}' supports AC machine family", 0.10);
                }
                if (titleDc || titleStepper) TopicConflict("filename/title conflicts with proposed AC machine family", 0.80);
                if (!titleAc && Any(path, "/dc motors/", "/dc machines/")) TopicConflict("AC content conflicts with DC-folder context; family review required", 0.85);
            }
            else if (topic.Equals("Stepper Motors", StringComparison.OrdinalIgnoreCase))
            {
                if (titleStepper) AddTopic("filename/title confirms stepper-motor family", 0.20);
                if (bodyStepper) AddTopic("content confirms stepper-motor family", 0.10);
                var concept = conceptMatches.FirstOrDefault(x => x.Family.Equals("Stepper Motors", StringComparison.OrdinalIgnoreCase));
                if (concept is not null && concept.TitleMatched) AddTopic($"concept anchor '{concept.Concept}' supports stepper-motor family", 0.20);
                if (titleDc || titleAc) TopicConflict("filename/title conflicts with proposed stepper-motor family", 0.80);
            }
            else if (topic.Equals("Transformers", StringComparison.OrdinalIgnoreCase))
            {
                if (titleTransformer) AddTopic("filename/title confirms transformer family", 0.20);
                if (bodyTransformer) AddTopic("content confirms transformer family", 0.10);
            }
            else if (topic.Equals("AC & DC Comparison", StringComparison.OrdinalIgnoreCase))
            {
                if (Any(fileName, "ac and dc", "dc and ac", "ac & dc", "dc & ac", "ac versus dc", "dc versus ac", "comparison"))
                    AddTopic("filename/title explicitly indicates a multi-family comparison", 0.25);
                if (bodyDc && bodyAc) AddTopic("content contains both AC and DC machine evidence", 0.12);
                topicCap = Math.Min(topicCap, 0.95);
            }
            else if (topic.Equals("General Machines", StringComparison.OrdinalIgnoreCase))
            {
                if (Any(content, "motor", "generator", "transformer", "stator", "rotor")) AddTopic("content confirms electrical-machine material", 0.08);
                topicCap = Math.Min(topicCap, 0.84);
            }

            if (!string.IsNullOrWhiteSpace(subtopic))
            {
                bool titlePurpose = subtopic switch
                {
                    "Starters" => Any(fileName, "starter", "starting of", "3 point starter", "4 point starter", "three point starter", "four point starter"),
                    "Speed Control" => Any(fileName, "speed control", "speed controls", "speed regulation", "drive control"),
                    "Generators" => Any(fileName, "generator", "generators"),
                    "Construction" => Any(fileName, "construction", "winding", "parts of"),
                    "Principles & Characteristics" => Any(fileName, "principle", "principles", "characteristic", "characteristics", "torque equation", "back emf", "back e.m.f"),
                    "Operation" => Any(fileName, "operation", "working principle", "how it works"),
                    "Types & Applications" => Any(fileName, "types of", "all types", "classes", "applications", "application of", "shunt motor", "series motor", "compound motor", "permanent magnet", "brushed dc", "brush dc", "brushless dc", "pmdc"),
                    _ => false
                };
                bool bodyPurpose = subtopic switch
                {
                    "Starters" => Any(content, "starting current", "3 point starter", "4 point starter", "three point starter", "four point starter", "motor starter"),
                    "Speed Control" => Any(content, "speed control", "speed regulation", "armature voltage control", "field control", "ward leonard"),
                    "Generators" => Any(content, "generator action", "dc generator", "ac generator", "generated emf"),
                    "Construction" => Any(content, "construction", "armature winding", "field winding", "commutator", "stator", "rotor"),
                    "Principles & Characteristics" => Any(content, "torque equation", "torque characteristic", "speed torque", "back emf", "back e.m.f", "characteristics", "principle of operation"),
                    "Operation" => Any(content, "operation", "working principle", "operating principle"),
                    "Types & Applications" => Any(content, "shunt motor", "series motor", "compound motor", "permanent magnet motor", "permanent magnet dc", "brushless dc", "brushed dc", "applications"),
                    _ => false
                };

                if (titlePurpose) AddSubtopic("filename/title confirms the proposed subtopic", 0.34);
                if (bodyPurpose) AddSubtopic("content corroborates the proposed subtopic", 0.10);

                var concept = conceptMatches.FirstOrDefault(x =>
                    x.Family.Equals(topic, StringComparison.OrdinalIgnoreCase)
                    && x.Purpose.Equals(subtopic, StringComparison.OrdinalIgnoreCase));
                if (concept is not null)
                {
                    if (concept.TitleMatched) AddSubtopic($"concept anchor '{concept.Concept}' confirms the proposed subtopic", 0.40);
                    else if (concept.ContentMatched) AddSubtopic($"content concept '{concept.Concept}' supports the proposed subtopic", 0.12);
                }

                // Strong title intent is a contradiction guard, not just another additive keyword.
                if (Any(fileName, "starter", "starting of") && subtopic != "Starters") SubtopicConflict("filename indicates starting/starters but another subtopic was proposed", 0.80);
                if (Any(fileName, "speed control", "speed controls", "speed regulation") && subtopic != "Speed Control") SubtopicConflict("filename indicates speed control but another subtopic was proposed", 0.80);
                if (Any(fileName, "operation") && subtopic != "Operation") SubtopicConflict("filename indicates operation but another subtopic was proposed", 0.80);
                if (Any(fileName, "torque equation") && subtopic != "Principles & Characteristics") SubtopicConflict("filename indicates a torque/principles resource but another subtopic was proposed", 0.80);
                if (Any(fileName, "types of", "all types") && subtopic != "Types & Applications") SubtopicConflict("filename indicates a types/applications resource but another subtopic was proposed", 0.80);
            }
        }
        else
        {
            var domainText = fileName + "\n" + content;
            var domainConfirmed = false;
            if (learningSubject.Equals("Electrical Principles", StringComparison.OrdinalIgnoreCase) && Any(domainText, "kirchhoff", "ohm's law", "ohms law", "impedance", "power factor")) domainConfirmed = true;
            if (learningSubject.Equals("Electronics", StringComparison.OrdinalIgnoreCase) && Any(domainText, "transistor", "diode", "op amp", "operational amplifier", "amplifier", "oscillator")) domainConfirmed = true;
            if (learningSubject.Equals("Components", StringComparison.OrdinalIgnoreCase) && Any(domainText, "resistor", "capacitor", "inductor", "datasheet")) domainConfirmed = true;
            if (learningSubject.Equals("PLC & Control", StringComparison.OrdinalIgnoreCase) && Any(domainText, "plc", "ladder logic", "programmable logic", "pid", "control system")) domainConfirmed = true;
            if (learningSubject.Equals("RF & Communications", StringComparison.OrdinalIgnoreCase) && Any(domainText, "rfid", "antenna", "modulation", "demodulation", "transmitter", "receiver", "transceiver")) domainConfirmed = true;
            if (learningSubject.Equals("Test & Measurement", StringComparison.OrdinalIgnoreCase) && Any(domainText, "oscilloscope", "multimeter", "calibration", "fault finding", "spectrum analyser", "spectrum analyzer")) domainConfirmed = true;
            if (learningSubject.Equals("Health & Safety", StringComparison.OrdinalIgnoreCase) && Any(domainText, "risk assessment", "hazard", "riddor", "ppe", "health and safety")) domainConfirmed = true;
            if (learningSubject.Equals("Engineering Mathematics", StringComparison.OrdinalIgnoreCase) && Any(domainText, "algebra", "calculus", "trigonometry", "logarithm", "equation")) domainConfirmed = true;
            if (domainConfirmed)
            {
                AddSubject("content corroborates the proposed learning subject", 0.08);
                AddTopic("content corroborates the proposed topic", 0.18);
            }
        }

        if (analysis.AnalysisStatus.Equals("Limited", StringComparison.OrdinalIgnoreCase))
        {
            subjectCap = Math.Min(subjectCap, 0.82);
            topicCap = Math.Min(topicCap, 0.82);
            subtopicCap = Math.Min(subtopicCap, 0.82);
        }

        subjectConfidence = Math.Clamp(Math.Min(subjectConfidence, subjectCap), 0.0, 0.98);
        topicConfidence = Math.Clamp(Math.Min(topicConfidence, topicCap), 0.0, 0.98);
        subtopicConfidence = string.IsNullOrWhiteSpace(subtopic)
            ? 0.0
            : Math.Clamp(Math.Min(subtopicConfidence, subtopicCap), 0.0, 0.98);

        var overall = string.IsNullOrWhiteSpace(subtopic)
            ? Math.Min(subjectConfidence, topicConfidence)
            : Math.Min(subjectConfidence, Math.Min(topicConfidence, subtopicConfidence));

        var reason = signals.Count > 0
            ? $"Hierarchy confidence — subject {subjectConfidence:P0}, topic {topicConfidence:P0}, subtopic {(string.IsNullOrWhiteSpace(subtopic) ? "n/a" : subtopicConfidence.ToString("P0"))}. Evidence: {string.Join(", ", signals)}."
            : $"Hierarchy confidence — subject {subjectConfidence:P0}, topic {topicConfidence:P0}, subtopic {(string.IsNullOrWhiteSpace(subtopic) ? "n/a" : subtopicConfidence.ToString("P0"))}. No strong corroborating concept signal was found.";
        return new ContentPlacementConfidence(subjectConfidence, topicConfidence, subtopicConfidence, overall, reason);
    }
}
