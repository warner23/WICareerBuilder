using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using WICareerBuilder.Core.Models;
using WICareerBuilder.Core.Classification;

namespace WICareerBuilder.Documents.Analysis;

public sealed partial class StructuredResourceAnalyser
{
    public const string ScannerVersion = "0.9.2";
    private const int MaxCharacters = 160_000;
    private const int PreviewCharacters = 4_000;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about","after","again","against","also","among","and","are","because","been","before","being","between","both","but","can","could","does","each","from","have","into","more","most","not","other","our","out","over","same","should","some","such","than","that","the","their","then","there","these","they","this","through","under","using","very","was","were","what","when","where","which","while","will","with","would","you","your","page","figure","chapter","section","example","examples","question","questions"
    };

    public bool CanAnalyse(string extension)
    {
        extension = extension.ToLowerInvariant();
        return extension is ".docx" or ".pptx" or ".xlsx" or ".xlsm" or ".txt" or ".md" or ".csv" or ".html" or ".htm" or ".xml" or ".json" or ".doc" or ".ppt" or ".xls";
    }

    public Task<ResourceAnalysis> AnalyseAsync(ResourceItem resource, string fullPath, string profile = "Selected action", CancellationToken cancellationToken = default)
        => Task.Run(() => Analyse(resource, fullPath, profile, cancellationToken), cancellationToken);

    public ResourceAnalysis AnalyseMetadata(ResourceItem resource, string scannerKind = "Selected-resource metadata classification")
    {
        var suggestedType = SuggestResourceType(resource, string.Empty);
        var (topic, subtopics) = SuggestContentFocus(resource, string.Empty);
        return new ResourceAnalysis(resource.Id, "Limited", scannerKind, ScannerVersion, DateTimeOffset.UtcNow,
            suggestedType, topic, subtopics, InferTitle(resource.FileName), null, null, 0, 0, false, [], [], [], string.Empty);
    }

    private static ResourceAnalysis Analyse(ResourceItem resource, string fullPath, string profile, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            var ext = resource.Extension.ToLowerInvariant();
            string text;
            string scanner;
            int? pageCount = null;
            int sampled = 0;
            string status = "Analysed";

            switch (ext)
            {
                case ".docx":
                    text = ReadOpenXmlParts(fullPath, p => p.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase) || p.StartsWith("word/header", StringComparison.OrdinalIgnoreCase) || p.StartsWith("word/footer", StringComparison.OrdinalIgnoreCase), cancellationToken, out sampled);
                    scanner = "DOCX selected-resource scan";
                    break;
                case ".pptx":
                    text = ReadOpenXmlParts(fullPath, p => p.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase), cancellationToken, out sampled);
                    pageCount = sampled;
                    scanner = "PowerPoint selected-resource scan";
                    break;
                case ".xlsx":
                case ".xlsm":
                    text = ReadOpenXmlParts(fullPath, p => p.Equals("xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase) || p.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase), cancellationToken, out sampled);
                    scanner = "Spreadsheet selected-resource scan";
                    break;
                case ".txt":
                case ".md":
                case ".csv":
                case ".xml":
                case ".json":
                    text = ReadText(fullPath);
                    sampled = 1;
                    scanner = "Text selected-resource scan";
                    break;
                case ".html":
                case ".htm":
                    text = StripMarkup(ReadText(fullPath));
                    sampled = 1;
                    scanner = "HTML selected-resource scan";
                    break;
                case ".doc":
                case ".ppt":
                case ".xls":
                    text = ReadLegacyOfficeStrings(fullPath, cancellationToken);
                    status = "Limited";
                    scanner = text.Length >= 40 ? "Legacy Office binary text salvage" : "Legacy Office metadata scan";
                    sampled = text.Length >= 40 ? 1 : 0;
                    break;
                default:
                    text = string.Empty;
                    status = "Limited";
                    scanner = "Selected-resource metadata scan";
                    break;
            }

            text = NormalizeWhitespace(text);
            var suggestedType = SuggestResourceType(resource, text);
            var (topic, subtopics) = SuggestContentFocus(resource, text);
            var headings = ExtractHeadingCandidates(text);
            var keywords = ExtractKeywords(text, 16);
            var indicators = DetectIndicators(text, resource.FileName);
            var title = InferTitle(resource.FileName);
            return new ResourceAnalysis(
                resource.Id,
                status,
                scanner,
                ScannerVersion,
                now,
                suggestedType,
                topic,
                subtopics,
                title,
                null,
                pageCount,
                sampled,
                text.Length,
                text.Length >= 40,
                headings,
                keywords,
                indicators,
                text[..Math.Min(text.Length, PreviewCharacters)],
                null,
                text);
        }
        catch (Exception ex)
        {
            return new ResourceAnalysis(resource.Id, "Failed", "Selected-resource scan", ScannerVersion, now, "Unknown", "Unclassified", [], null, null, null, 0, 0, false, [], [], [], string.Empty, ex.Message, string.Empty);
        }
    }

    private static string ReadText(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var buffer = new char[MaxCharacters];
        var read = reader.ReadBlock(buffer, 0, buffer.Length);
        return new string(buffer, 0, read);
    }

    private static string ReadLegacyOfficeStrings(string path, CancellationToken cancellationToken)
    {
        // Legacy .doc/.ppt/.xls are OLE compound binaries. Without requiring Microsoft Office,
        // recover conservative ASCII and UTF-16LE text runs as a classification aid.
        // The result is deliberately marked Limited and must not be treated as authoritative extraction.
        const int maxBytes = 16 * 1024 * 1024;
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = (int)Math.Min(stream.Length, maxBytes);
        var data = new byte[length];
        var readTotal = 0;
        while (readTotal < length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(data, readTotal, length - readTotal);
            if (read <= 0) break;
            readTotal += read;
        }

        var runs = new List<string>();
        var collected = 0;

        static bool IsPrintable(int code) => (code >= 32 && code <= 126) || code is 9 or 10 or 13;

        void AddRun(StringBuilder builder)
        {
            if (builder.Length < 5)
            {
                builder.Clear();
                return;
            }

            var value = MultiSpaceRegex().Replace(builder.ToString(), " ").Trim();
            builder.Clear();
            if (value.Length < 5 || runs.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
            runs.Add(value);
            collected += value.Length + 1;
        }

        var ascii = new StringBuilder();
        for (var i = 0; i < readTotal && collected < MaxCharacters; i++)
        {
            if ((i & 0x3fff) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (IsPrintable(data[i]))
            {
                ascii.Append((char)data[i]);
                if (ascii.Length >= 512) AddRun(ascii);
            }
            else AddRun(ascii);
        }
        AddRun(ascii);

        var unicode = new StringBuilder();
        for (var i = 0; i + 1 < readTotal && collected < MaxCharacters; i += 2)
        {
            if ((i & 0x7fff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var code = data[i] | (data[i + 1] << 8);
            if (IsPrintable(code))
            {
                unicode.Append((char)code);
                if (unicode.Length >= 512) AddRun(unicode);
            }
            else AddRun(unicode);
        }
        AddRun(unicode);

        var combined = string.Join('\n', runs);
        return combined.Length <= MaxCharacters ? combined : combined[..MaxCharacters];
    }

    private static string ReadOpenXmlParts(string path, Func<string, bool> include, CancellationToken cancellationToken, out int partsRead)
    {
        partsRead = 0;
        var sb = new StringBuilder();
        using var archive = ZipFile.OpenRead(path);
        foreach (var entry in archive.Entries.Where(e => include(e.FullName)).OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sb.Length >= MaxCharacters) break;
            using var stream = entry.Open();
            var doc = XDocument.Load(stream, LoadOptions.None);
            var values = doc.Descendants()
                .Where(x => x.Name.LocalName is "t" or "v")
                .Select(x => x.Value?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x));
            foreach (var value in values)
            {
                if (sb.Length >= MaxCharacters) break;
                sb.AppendLine(value);
            }
            partsRead++;
        }
        return sb.ToString();
    }

    private static string SuggestResourceType(ResourceItem resource, string text)
    {
        var name = Path.GetFileNameWithoutExtension(resource.FileName).ToLowerInvariant();
        var path = resource.RelativePath.ToLowerInvariant();
        var front = text[..Math.Min(text.Length, 16_000)].ToLowerInvariant();

        if (ContainsAny(name, "datasheet", "data sheet") || ContainsAny(path, "/data sheets/", "/datasheets/")) return "Datasheet";
        if (ContainsAny(name, "worksheet", "work sheet", "questions") || ContainsAny(front, "worksheet")) return "Worksheet / Exercises";
        if (ContainsAny(path, "/old assignments/", "/completed assignments/", "/original/", "/assignments/") || ContainsAny(name, "assignment", "assessment")) return "Old Assignment";
        if (ContainsAny(name, "revision", "revision notes", "revision guide")) return "Revision Material";
        if (ContainsAny(name, "manual", "handbook") || ContainsAny(path, "/manuals/")) return "Manual / Technical Reference";
        if (ContainsAny(name, "project report", "final report") || ContainsAny(path, "/projects/")) return "Project Material";
        if (resource.MediaKind.Equals("Presentation", StringComparison.OrdinalIgnoreCase)) return "Teaching Presentation";
        if (resource.MediaKind.Equals("Spreadsheet", StringComparison.OrdinalIgnoreCase)) return ContainsAny(front, "question", "calculate", "exercise") ? "Interactive / Calculation Sheet" : "Spreadsheet Reference";
        if (resource.MediaKind.Equals("Document", StringComparison.OrdinalIgnoreCase)) return ContainsAny(front, "question", "exercise", "learning outcome") ? "Course Notes / Exercises" : "Reference Document";
        return resource.ResourceType is not "Unknown" ? resource.ResourceType : "Reference Material";
    }

    private static (string Topic, IReadOnlyList<string> Subs) SuggestContentFocus(ResourceItem resource, string text)
    {
        var fileNameFocus = resource.FileName.ToLowerInvariant();
        var pathFocus = resource.RelativePath.ToLowerInvariant();
        var contentFocus = text[..Math.Min(text.Length, 80_000)].ToLowerInvariant();
        var frontFocus = contentFocus[..Math.Min(contentFocus.Length, 8_000)];
        var hay = pathFocus + "\n" + fileNameFocus + "\n" + contentFocus;
        var machineConcepts = PlacementConceptCatalog.MatchMachineConcepts(resource.FileName, contentFocus, resource.RelativePath);

        // v0.9.2: filename/title evidence is strong but not authoritative; folder evidence is weaker;
        // extracted content is used to corroborate or contradict the proposed machine family and purpose.
        var machineScore = WeightedScore(hay,
            ("motor", 3), ("generator", 3), ("transformer", 3), ("stator", 3), ("rotor", 3),
            ("synchronous", 2), ("induction motor", 4), ("dc motor", 5), ("dc machine", 5),
            ("dc generator", 5), ("ac motor", 5), ("starter", 2), ("stepper motor", 5));

        var scores = new Dictionary<string, int>
        {
            ["Engineering Mathematics"] = WeightedScore(hay, ("math",2),("algebra",3),("calculus",3),("trigon",3),("logarithm",3),("indices",4),("formula",1),("equation",1),("geometry",2)),
            ["Electrical Principles"] = WeightedScore(hay, ("voltage",1),("current",1),("resistance",2),("impedance",2),("ohm",2),("kirchhoff",3),("three phase",3),("power factor",3),("ac circuit",3),("dc circuit",3)),
            ["Electronics"] = WeightedScore(hay, ("transistor",3),("diode",3),("op amp",4),("amplifier",3),("oscillator",3),("logic gate",3),("semiconductor",2),("rectifier",2)),
            ["Components"] = WeightedScore(hay, ("resistor",3),("capacitor",3),("inductor",3),("diode",2),("transistor",2),("component",1),("datasheet",2)),
            ["Electrical Machines"] = machineScore,
            ["PLC & Control"] = WeightedScore(hay, ("plc",5),("ladder logic",5),("control system",3),("pid",3),("programmable logic",5)),
            ["RF & Communications"] = WeightedScore(hay, ("rf",3),("radio",3),("antenna",3),("modulation",4),("demodulation",4),("receiver",3),("transmitter",3),("rfid",4),("wireless",2)),
            ["Test & Measurement"] = WeightedScore(hay, ("oscilloscope",4),("multimeter",4),("measurement",2),("calibration",3),("fault finding",4),("spectrum analyser",5),("spectrum analyzer",5)),
            ["Health & Safety"] = WeightedScore(hay, ("health safety",4),("health and safety",4),("risk assessment",5),("hazard",3),("riddor",5),("ppe",3)),
            ["Projects"] = WeightedScore(hay, ("/projects/",1),("project report",4),("design report",3),("prototype",3))
        };

        if (ContainsAny(fileNameFocus, "dc motor", "dc machine", "dc generator", "ac motor", "induction motor", "synchronous motor", "stepper motor", "transformer"))
            scores["Electrical Machines"] += 10;
        else if (ContainsAny(pathFocus, "/motors/", "/generators/", "electrical mahines", "electrical machines"))
            scores["Electrical Machines"] += 3;

        var best = scores.OrderByDescending(x => x.Value).First();
        if (best.Value <= 0) best = new KeyValuePair<string, int>("General Engineering", 0);

        var subs = new List<string>();
        switch (best.Key)
        {
            case "Engineering Mathematics":
                AddSub(subs, hay, "Indices", "indices", "laws of indices");
                AddSub(subs, hay, "Transposition of Formulae", "transpos", "rearrang");
                AddSub(subs, hay, "Algebra", "algebra");
                AddSub(subs, hay, "Trigonometry", "trigon", "sine rule", "cosine rule");
                AddSub(subs, hay, "Calculus", "calculus", "differentiat", "integrat");
                break;

            case "Electrical Machines":
            {
                var comparisonTitle = ContainsAny(fileNameFocus,
                    "ac and dc", "dc and ac", "ac & dc", "dc & ac", "ac versus dc", "dc versus ac", "comparison of the characteristics of ac and dc");

                var dcTitle = WeightedScore(fileNameFocus,
                    ("dc motor",14),("dc motors",14),("dc machine",14),("dc generator",14),("direct current motor",14),("direct current machine",14),("brushless dc",14));
                var acTitle = WeightedScore(fileNameFocus,
                    ("ac motor",14),("ac motors",14),("induction motor",14),("synchronous motor",14),("alternator",12),("alternating current motor",14));
                var stepperTitle = WeightedScore(fileNameFocus, ("stepper motor",16),("hybrid stepper",16));
                var transformerTitle = WeightedScore(fileNameFocus, ("transformer",14),("transformers",14));

                var dcScore = dcTitle + WeightedScore(contentFocus,
                    ("dc motor",5),("dc machine",5),("dc generator",5),("direct current motor",5),("direct current machine",5),("commutator",2),("brushes",2));
                var acScore = acTitle + WeightedScore(contentFocus,
                    ("ac motor",5),("induction motor",5),("synchronous motor",5),("alternator",4),("alternating current motor",5),("squirrel cage",2),("slip",2));
                var stepperScore = stepperTitle + WeightedScore(contentFocus, ("stepper motor",6),("step angle",3),("stepping",2));
                var transformerScore = transformerTitle + WeightedScore(contentFocus, ("transformer",5),("primary winding",2),("secondary winding",2));

                foreach (var concept in machineConcepts)
                {
                    switch (concept.Family)
                    {
                        case "DC Machines": dcScore += concept.FamilyScoreBoost; break;
                        case "AC Machines": acScore += concept.FamilyScoreBoost; break;
                        case "Stepper Motors": stepperScore += concept.FamilyScoreBoost; break;
                        case "Transformers": transformerScore += concept.FamilyScoreBoost; break;
                    }
                }

                // Folder context is evidence, not truth. It contributes only a small tie-breaker.
                if (ContainsAny(pathFocus, "/dc motors/", "/dc machines/")) dcScore += 2;
                if (ContainsAny(pathFocus, "/ac motors/", "/ac machines/")) acScore += 2;
                if (ContainsAny(pathFocus, "/stepper/", "/stepper motors/")) stepperScore += 2;
                if (ContainsAny(pathFocus, "/transformers/", "/transformer/")) transformerScore += 2;

                string machineTopic;
                if (comparisonTitle)
                {
                    machineTopic = "AC & DC Comparison";
                }
                else if (dcTitle > 0 && acTitle == 0 && stepperTitle == 0)
                {
                    machineTopic = "DC Machines";
                }
                else if (acTitle > 0 && dcTitle == 0 && stepperTitle == 0)
                {
                    machineTopic = "AC Machines";
                }
                else if (stepperTitle > 0 && dcTitle == 0 && acTitle == 0)
                {
                    machineTopic = "Stepper Motors";
                }
                else if (transformerTitle > 0 && dcTitle == 0 && acTitle == 0 && stepperTitle == 0)
                {
                    machineTopic = "Transformers";
                }
                else
                {
                    var families = new[]
                    {
                        (Label: "DC Machines", Score: dcScore),
                        (Label: "AC Machines", Score: acScore),
                        (Label: "Stepper Motors", Score: stepperScore),
                        (Label: "Transformers", Score: transformerScore)
                    }.OrderByDescending(x => x.Score).ToArray();
                    machineTopic = families[0].Score > 0 && families[0].Score - families[1].Score >= 3
                        ? families[0].Label
                        : "General Machines";
                }
                subs.Add(machineTopic);

                if (machineTopic is not "AC & DC Comparison" and not "General Machines")
                {
                    var purposeText = frontFocus + "\n" + contentFocus;
                    var branches = new[]
                    {
                        (Label: "Starters", Score: ScorePlacementBranch(fileNameFocus, purposeText,
                            new[]{"starter", "starting of", "starting ", "3 point starter", "4 point starter", "three point starter", "four point starter"},
                            new[]{"starter", "starting current", "3 point starter", "4 point starter", "three point starter", "four point starter"})),
                        (Label: "Speed Control", Score: ScorePlacementBranch(fileNameFocus, purposeText,
                            new[]{"speed control", "speed controls", "speed regulation", "drive control"},
                            new[]{"speed control", "speed regulation", "armature voltage control", "field control", "ward leonard"})),
                        (Label: "Generators", Score: ScorePlacementBranch(fileNameFocus, purposeText,
                            new[]{"generator", "generators"},
                            new[]{"generator action", "dc generator", "ac generator", "generating action", "generated emf"})),
                        (Label: "Construction", Score: ScorePlacementBranch(fileNameFocus, purposeText,
                            new[]{"construction", "winding", "stator and rotor", "parts of"},
                            new[]{"construction", "armature winding", "field winding", "commutator", "stator", "rotor"})),
                        (Label: "Principles & Characteristics", Score: ScorePlacementBranch(fileNameFocus, purposeText,
                            new[]{"principle", "principles", "characteristic", "characteristics", "torque equation", "back emf", "back e.m.f"},
                            new[]{"principle of operation", "torque equation", "torque characteristic", "speed torque", "back emf", "back e.m.f", "characteristics"})),
                        (Label: "Operation", Score: ScorePlacementBranch(fileNameFocus, purposeText,
                            new[]{"operation", "working principle", "how it works"},
                            new[]{"operation", "working principle", "operating principle"})),
                        (Label: "Types & Applications", Score: ScorePlacementBranch(fileNameFocus, purposeText,
                            new[]{"types of", "all types", "classes", "applications", "application of", "shunt motor", "series motor", "compound motor", "permanent magnet", "brushed dc", "brush dc", "brushless dc", "pmdc"},
                            new[]{"shunt motor", "series motor", "compound motor", "permanent magnet motor", "permanent magnet dc", "brushless dc", "brushed dc", "applications"}))
                    }.ToDictionary(x => x.Label, x => x.Score, StringComparer.OrdinalIgnoreCase);

                    // Concept anchors add relationship knowledge without turning the whole taxonomy into filename rules.
                    // Clear title-level concept evidence dominates generic construction vocabulary in the body.
                    foreach (var concept in machineConcepts.Where(x => x.Family.Equals(machineTopic, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.Purpose)))
                        branches[concept.Purpose] = branches.GetValueOrDefault(concept.Purpose) + concept.PurposeScoreBoost;

                    var purpose = branches.OrderByDescending(x => x.Value).First();
                    if (purpose.Value > 0) subs.Add(purpose.Key);
                }
                break;
            }

            case "Electrical Principles":
                AddSub(subs, hay, "Three-phase Systems", "three phase", "3 phase");
                AddSub(subs, hay, "Circuit Theory", "kirchhoff", "ohm", "resistance", "impedance");
                AddSub(subs, hay, "Power & Power Factor", "power factor", "real power", "reactive power");
                break;

            case "Electronics":
            case "Components":
                AddSub(subs, hay, "Op Amps", "op amp", "operational amplifier");
                AddSub(subs, hay, "Transistors", "transistor");
                AddSub(subs, hay, "Diodes", "diode");
                AddSub(subs, hay, "Amplifiers", "amplifier");
                break;

            case "RF & Communications":
                AddSub(subs, hay, "RFID", "rfid");
                AddSub(subs, hay, "Antennas", "antenna");
                AddSub(subs, hay, "Modulation", "modulation", "demodulation");
                AddSub(subs, hay, "Receivers & Transmitters", "receiver", "transmitter", "transceiver");
                break;

            case "Test & Measurement":
                AddSub(subs, hay, "Fault Finding", "fault finding");
                AddSub(subs, hay, "Oscilloscopes", "oscilloscope");
                AddSub(subs, hay, "Calibration", "calibration");
                break;

            case "Health & Safety":
                AddSub(subs, hay, "Risk Assessment", "risk assessment");
                AddSub(subs, hay, "RIDDOR", "riddor");
                break;
        }

        return (best.Key, subs.Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToArray());
    }

    private static int ScorePlacementBranch(string fileName, string content, string[] titleSignals, string[] contentSignals)
    {
        var score = 0;
        foreach (var signal in titleSignals)
            if (fileName.Contains(signal, StringComparison.OrdinalIgnoreCase)) score += 30;
        foreach (var signal in contentSignals)
            if (content.Contains(signal, StringComparison.OrdinalIgnoreCase)) score += 3;
        return score;
    }

    private static int WeightedScore(string text, params (string Signal, int Weight)[] signals)
        => signals.Sum(s => text.Contains(s.Signal, StringComparison.OrdinalIgnoreCase) ? s.Weight : 0);

    private static void AddFirstSpecificSubtopic(List<string> list, string text, params (string Label, string[] Signals)[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!candidate.Signals.Any(s => text.Contains(s, StringComparison.OrdinalIgnoreCase))) continue;
            if (!list.Contains(candidate.Label, StringComparer.OrdinalIgnoreCase)) list.Add(candidate.Label);
            return;
        }
    }

    private static IReadOnlyList<string> ExtractHeadingCandidates(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return text.Split('\n')
            .Select(x => MultiSpaceRegex().Replace(x.Trim(), " "))
            .Where(x => x.Length is >= 4 and <= 90 && x.Count(char.IsLetter) >= 4 && x.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 12)
            .Where(x => !x.EndsWith('.') && !x.EndsWith(',') && (char.IsUpper(x[0]) || x.Count(char.IsUpper) > x.Count(char.IsLower) / 2))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(18)
            .ToArray();
    }

    private static IReadOnlyList<string> ExtractKeywords(string text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in WordRegex().Matches(text))
        {
            var w = m.Value.Trim('\'', '-').ToLowerInvariant();
            if (w.Length < 4 || StopWords.Contains(w) || w.All(char.IsDigit)) continue;
            counts[w] = counts.GetValueOrDefault(w) + 1;
        }
        return counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Take(max).Select(x => x.Key).ToArray();
    }

    private static IReadOnlyList<string> DetectIndicators(string text, string fileName)
    {
        var lower = (fileName + "\n" + text).ToLowerInvariant();
        var list = new List<string>();
        if (lower.Contains('?') || ContainsAny(lower, "question", "exercise", "worksheet")) list.Add("Questions / exercises");
        if (ContainsAny(lower, "formula", "equation", "calculate", "calculation", "= v", "= i")) list.Add("Formulas / calculations");
        if (ContainsAny(lower, "worked example", "example:")) list.Add("Worked examples");
        if (ContainsAny(lower, "practical", "experiment", "laboratory", "build", "construct")) list.Add("Practical / build material");
        if (ContainsAny(lower, "health and safety", "risk assessment", "hazard", "ppe")) list.Add("Safety material");
        if (ContainsAny(lower, "assignment", "assessment")) list.Add("Assignment / assessment material");
        return list;
    }

    private static string StripMarkup(string value) => Regex.Replace(value, "<[^>]+>", " ");
    private static string NormalizeWhitespace(string value) => MultiBlankLineRegex().Replace(value.Replace("\r", ""), "\n\n").Trim();
    private static string? InferTitle(string fileName)
    {
        var stem = Regex.Replace(Path.GetFileNameWithoutExtension(fileName).Replace('_', ' '), @"\s+", " ").Trim();
        return stem.Length >= 3 ? stem : null;
    }
    private static int Score(string text, params string[] signals) => signals.Count(s => text.Contains(s, StringComparison.OrdinalIgnoreCase));
    private static bool ContainsAny(string text, params string[] values) => values.Any(v => text.Contains(v, StringComparison.OrdinalIgnoreCase));
    private static void AddSub(List<string> list, string text, string label, params string[] signals) { if (signals.Any(s => text.Contains(s, StringComparison.OrdinalIgnoreCase))) list.Add(label); }

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'-]*", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex MultiSpaceRegex();
    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultiBlankLineRegex();
}
