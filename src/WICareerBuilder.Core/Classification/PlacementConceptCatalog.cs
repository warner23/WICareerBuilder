using System.Text.RegularExpressions;

namespace WICareerBuilder.Core.Classification;

public sealed record PlacementConceptMatch(
    string Concept,
    string Family,
    string Purpose,
    bool TitleMatched,
    bool ContentMatched,
    bool PathContextMatched,
    int FamilyScoreBoost,
    int PurposeScoreBoost);

/// <summary>
/// Small, explicit concept-anchor catalogue used by placement scoring.
/// Anchors express domain relationships (for example, a four-point starter is a DC-motor starter)
/// rather than hard-coding a complete subject taxonomy. The catalogue is deliberately additive and
/// can later move to database-backed concept packs without changing the placement pipeline.
/// </summary>
public static class PlacementConceptCatalog
{
    private sealed record Anchor(
        string Concept,
        string Family,
        string Purpose,
        string[] Aliases,
        bool RequiresDcContext = false,
        bool RequiresMachineContext = false);

    private static readonly Anchor[] MachineAnchors =
    [
        new("Three-point DC motor starter", "DC Machines", "Starters", ["3 point starter", "3-point starter", "three point starter", "three-point starter"]),
        new("Four-point DC motor starter", "DC Machines", "Starters", ["4 point starter", "4-point starter", "four point starter", "four-point starter"]),
        new("DC motor starting", "DC Machines", "Starters", ["starting of dc motor", "dc motor starting", "starting dc motor"]),
        new("DC series motor", "DC Machines", "Types & Applications", ["series motor", "dc series motor", "series"], RequiresDcContext: true),
        new("DC shunt motor", "DC Machines", "Types & Applications", ["shunt motor", "dc shunt motor", "shunt"], RequiresDcContext: true),
        new("DC compound motor", "DC Machines", "Types & Applications", ["compound motor", "dc compound motor", "compound"], RequiresDcContext: true),
        new("Permanent-magnet DC motor", "DC Machines", "Types & Applications", ["permanent magnet dc motor", "permanent-magnet dc motor", "pmdc"]),
        new("Brushed DC motor", "DC Machines", "Types & Applications", ["brushed dc motor", "brush dc motor"]),
        new("Brushless DC motor", "DC Machines", "Types & Applications", ["brushless dc motor", "bldc", "brushless"], RequiresDcContext: true),
        new("DC generator", "DC Machines", "Generators", ["dc generator", "dc generators"]),
        new("DC motor torque", "DC Machines", "Principles & Characteristics", ["torque equation of dc motor", "dc motor torque equation", "torque equation"] , RequiresDcContext: true),
        new("DC motor operation", "DC Machines", "Operation", ["dc motor operation", "operation of dc motor"]),
        new("Ward-Leonard DC speed control", "DC Machines", "Speed Control", ["ward leonard", "ward-leonard"], RequiresDcContext: true),
        new("DC armature-voltage speed control", "DC Machines", "Speed Control", ["armature voltage control", "armature control"], RequiresDcContext: true),
        new("DC field-flux speed control", "DC Machines", "Speed Control", ["field control", "field flux control", "flux control"], RequiresDcContext: true),
        new("DC machine construction", "DC Machines", "Construction", ["armature winding", "field winding", "commutator construction", "dc motor construction"], RequiresDcContext: true),
        new("DC back EMF", "DC Machines", "Principles & Characteristics", ["back emf", "back e.m.f", "counter emf"], RequiresDcContext: true),
        new("Hybrid stepper motor", "Stepper Motors", "Types & Applications", ["hybrid stepper motor", "hybrid stepper"]),
        new("Permanent-magnet stepper motor", "Stepper Motors", "Types & Applications", ["permanent magnet stepper motor", "permanent-magnet stepper motor"]),
        new("Induction motor", "AC Machines", "", ["induction motor", "squirrel cage motor"]),
        new("Squirrel-cage induction motor", "AC Machines", "Types & Applications", ["squirrel cage induction motor", "squirrel-cage induction motor", "squirrel cage motor"]),
        new("Slip-ring induction motor", "AC Machines", "Types & Applications", ["slip ring induction motor", "slip-ring induction motor", "wound rotor induction motor"]),
        new("Single-phase induction motor", "AC Machines", "Types & Applications", ["single phase induction motor", "single-phase induction motor"]),
        new("Synchronous motor", "AC Machines", "", ["synchronous motor"]),
        new("Brushless AC motor", "AC Machines", "Types & Applications", ["brushless ac motor"]),
        new("Step-up transformer", "Transformers", "Types & Applications", ["step up transformer", "step-up transformer"]),
        new("Step-down transformer", "Transformers", "Types & Applications", ["step down transformer", "step-down transformer"]),
        new("Autotransformer", "Transformers", "Types & Applications", ["autotransformer", "auto transformer"]),
        new("Transformer construction", "Transformers", "Construction", ["transformer construction", "core type transformer", "shell type transformer"])
    ];

    public static IReadOnlyList<PlacementConceptMatch> MatchMachineConcepts(string fileName, string content, string relativePath)
    {
        var title = Normalize(fileName);
        var body = Normalize(content);
        var path = Normalize(relativePath);
        var dcContext = ContainsAny(title + "\n" + body + "\n" + path,
            "dc motor", "dc motors", "dc machine", "dc machines", "dc generator", "/dc motors/", "/dc machines/");
        var machineContext = dcContext || ContainsAny(title + "\n" + body + "\n" + path,
            "motor", "machine", "generator", "starter", "stator", "rotor", "/motors/", "/generators/");

        var matches = new List<PlacementConceptMatch>();
        foreach (var anchor in MachineAnchors)
        {
            if (anchor.RequiresDcContext && !dcContext) continue;
            if (anchor.RequiresMachineContext && !machineContext) continue;

            var titleMatched = anchor.Aliases.Any(a => ContainsPhrase(title, a));
            var contentMatched = anchor.Aliases.Any(a => ContainsPhrase(body, a));
            var pathMatched = anchor.Aliases.Any(a => ContainsPhrase(path, a));
            if (!titleMatched && !contentMatched && !pathMatched) continue;

            // A title match is deliberately strongest. Content corroborates it; path is only context.
            var familyBoost = (titleMatched ? 18 : 0) + (contentMatched ? 6 : 0) + (pathMatched ? 2 : 0);
            var purposeBoost = string.IsNullOrWhiteSpace(anchor.Purpose)
                ? 0
                : (titleMatched ? 24 : 0) + (contentMatched ? 8 : 0) + (pathMatched ? 2 : 0);
            matches.Add(new PlacementConceptMatch(anchor.Concept, anchor.Family, anchor.Purpose,
                titleMatched, contentMatched, pathMatched, familyBoost, purposeBoost));
        }

        return matches
            .OrderByDescending(x => x.TitleMatched)
            .ThenByDescending(x => x.ContentMatched)
            .ThenByDescending(x => x.FamilyScoreBoost + x.PurposeScoreBoost)
            .ToArray();
    }

    private static string Normalize(string value)
    {
        value = (value ?? string.Empty).ToLowerInvariant().Replace('_', ' ').Replace('\\', '/');
        value = Regex.Replace(value, "[-–—]+", " ");
        return Regex.Replace(value, "\\s+", " ").Trim();
    }

    private static bool ContainsAny(string value, params string[] terms)
        => terms.Any(t => value.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsPhrase(string value, string phrase)
    {
        phrase = Normalize(phrase);
        if (phrase.Length == 0) return false;
        if (phrase.All(char.IsLetterOrDigit) && phrase.Length <= 5)
            return Regex.IsMatch(value, $@"(?<![a-z0-9]){Regex.Escape(phrase)}(?![a-z0-9])", RegexOptions.IgnoreCase);
        return value.Contains(phrase, StringComparison.OrdinalIgnoreCase);
    }
}
