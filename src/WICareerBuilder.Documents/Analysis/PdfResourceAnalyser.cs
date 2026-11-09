using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Documents.Analysis;

public sealed partial class PdfResourceAnalyser
{
    public const string ScannerVersion = "0.2.2";
    private const int MaxSamplePages = 24;
    private const int MaxExtractedCharacters = 160_000;
    private const int PreviewCharacters = 4_000;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about","after","again","against","also","among","and","are","because","been","before","being","between",
        "both","but","can","could","does","each","from","have","into","more","most","not","other","our","out","over",
        "same","should","some","such","than","that","the","their","then","there","these","they","this","through","under",
        "using","very","was","were","what","when","where","which","while","will","with","would","you","your","page",
        "figure","chapter","section","copyright","www","http","https","since","case","point","show","onto","proof","lemma",
        "theorem","therefore","thus","given","following","example","examples","problem","problems","result","results","note"
    };

    public Task<ResourceAnalysis> AnalyseAsync(ResourceItem resource, string fullPath, CancellationToken cancellationToken = default)
        => Task.Run(() => Analyse(resource, fullPath, cancellationToken), cancellationToken);

    private static ResourceAnalysis Analyse(ResourceItem resource, string fullPath, CancellationToken cancellationToken)
    {
        var scannedUtc = DateTimeOffset.UtcNow;
        try
        {
            using var document = PdfDocument.Open(fullPath);
            var pageCount = document.NumberOfPages;
            var samplePages = BuildSamplePages(pageCount);
            var text = new StringBuilder(Math.Min(MaxExtractedCharacters, 32_000));
            var headings = new List<string>();
            var extractedPages = 0;

            foreach (var pageNumber in samplePages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (text.Length >= MaxExtractedCharacters) break;

                var page = document.GetPage(pageNumber);
                var pageText = ContentOrderTextExtractor.GetText(page) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(pageText)) continue;

                extractedPages++;
                CollectHeadingCandidates(pageText, headings);

                var remaining = MaxExtractedCharacters - text.Length;
                if (remaining <= 0) break;
                if (pageText.Length > remaining) pageText = pageText[..remaining];
                text.AppendLine(pageText);
            }

            var extracted = NormalizeWhitespace(text.ToString());
            var hasSearchableText = extracted.Length >= 80;
            var metadataTitle = CleanMetadata(document.Information?.Title);
            var title = metadataTitle ?? InferTitleFromFileName(resource.FileName);
            var author = CleanMetadata(document.Information?.Author);
            var suggestedType = SuggestResourceType(resource.FileName, extracted, pageCount);
            var (suggestedTopic, suggestedSubtopics) = SuggestContentFocus(extracted);
            var keywords = ExtractKeywords(extracted, 16);
            var indicators = DetectContentIndicators(extracted);
            var preview = hasSearchableText ? extracted[..Math.Min(extracted.Length, PreviewCharacters)] : string.Empty;

            return new ResourceAnalysis(
                resource.Id,
                "Analysed",
                "PDF sampled text scan",
                ScannerVersion,
                scannedUtc,
                suggestedType,
                suggestedTopic,
                suggestedSubtopics,
                title,
                author,
                pageCount,
                extractedPages,
                extracted.Length,
                hasSearchableText,
                headings.Distinct(StringComparer.OrdinalIgnoreCase).Take(18).ToArray(),
                keywords,
                indicators,
                preview,
                null,
                extracted);
        }
        catch (Exception ex)
        {
            return new ResourceAnalysis(
                resource.Id,
                "Failed",
                "PDF sampled text scan",
                ScannerVersion,
                scannedUtc,
                "Unknown",
                "Unclassified",
                [],
                null,
                null,
                null,
                0,
                0,
                false,
                [],
                [],
                [],
                string.Empty,
                ex.Message,
                string.Empty);
        }
    }

    private static IReadOnlyList<int> BuildSamplePages(int pageCount)
    {
        if (pageCount <= 0) return [];
        if (pageCount <= MaxSamplePages) return Enumerable.Range(1, pageCount).ToArray();

        var pages = new SortedSet<int>();
        for (var i = 1; i <= Math.Min(12, pageCount); i++) pages.Add(i);

        var remainingSlots = MaxSamplePages - pages.Count;
        if (remainingSlots > 0)
        {
            var step = (pageCount - 1d) / remainingSlots;
            for (var i = 1; i <= remainingSlots; i++)
                pages.Add(Math.Clamp((int)Math.Round(1 + (step * i)), 1, pageCount));
        }

        pages.Add(pageCount);
        return pages.Take(MaxSamplePages).ToArray();
    }

    private static void CollectHeadingCandidates(string pageText, List<string> headings)
    {
        if (headings.Count >= 60) return;
        foreach (var raw in pageText.Split('\n'))
        {
            var line = MultiSpaceRegex().Replace(raw.Trim(), " ");
            if (line.Length < 5 || line.Length > 100) continue;
            if (line.Count(char.IsLetter) < 4) continue;
            if (line.EndsWith('.') || line.EndsWith(',') || line.EndsWith(';')) continue;
            if (line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 12) continue;

            var letters = line.Where(char.IsLetter).ToArray();
            if (letters.Length == 0) continue;
            var upperRatio = letters.Count(char.IsUpper) / (double)letters.Length;
            var titleLike = char.IsUpper(line[0]) && line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Count(word => word.Length > 2 && char.IsUpper(word[0])) >= 2;

            if (upperRatio >= 0.65 || titleLike)
                headings.Add(line);
            if (headings.Count >= 60) break;
        }
    }

    private static IReadOnlyList<string> ExtractKeywords(string text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in WordRegex().Matches(text))
        {
            var word = match.Value.Trim('\'', '-').ToLowerInvariant();
            if (word.Length < 4 || StopWords.Contains(word) || word.All(char.IsDigit)) continue;
            counts[word] = counts.GetValueOrDefault(word) + 1;
        }

        return counts
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(x => x.Key)
            .ToArray();
    }

    private static IReadOnlyList<string> DetectContentIndicators(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var lower = text.ToLowerInvariant();
        var result = new List<string>();
        AddIf(result, lower, "Worked examples", "worked example", "example 1", "example:");
        AddIf(result, lower, "Exercises / questions", "exercise", "questions", "problems", "worksheet");
        AddIf(result, lower, "Formulas / equations", "equation", "formula", "calculate", "calculation");
        AddIf(result, lower, "Contents / chapter structure", "contents", "table of contents", "chapter 1", "chapter one");
        AddIf(result, lower, "References / bibliography", "references", "bibliography");
        AddIf(result, lower, "Safety material", "health and safety", "risk assessment", "hazard", "safety");
        AddIf(result, lower, "Practical / laboratory material", "laboratory", "lab exercise", "practical", "experiment");
        return result;
    }

    private static void AddIf(List<string> result, string text, string label, params string[] needles)
    {
        if (needles.Any(text.Contains)) result.Add(label);
    }

    private static string SuggestResourceType(string fileName, string text, int pageCount)
    {
        var name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        var front = text[..Math.Min(text.Length, 24_000)].ToLowerInvariant();

        // Strong filename/front-matter signals come first. Do not classify a long book as
        // "revision material" merely because the word revision occurs somewhere in the text.
        if (ContainsAny(name, "worksheet", "work sheet")) return "Worksheet";
        if (ContainsAny(name, "assignment", "assessment brief", "assign ")) return "Coursework / Assignment";
        if (ContainsAny(name, "datasheet", "data sheet") || ContainsAny(front[..Math.Min(front.Length, 4_000)], "datasheet", "data sheet")) return "Datasheet";
        if (ContainsAny(name, "project report", "final report")) return "Project Report";
        if (ContainsAny(name, "service manual", "user manual", "instruction manual")) return "Manual";
        if (ContainsAny(name, "revision", "revision notes", "revision guide")) return "Revision Material";

        var hasContents = ContainsAny(front, "table of contents", "contents");
        var hasChapterStructure = ContainsAny(front, "chapter 1", "chapter one", "chapter 0", "chapter 2");
        var hasBookSignals = ContainsAny(front, "isbn", "edition", "bibliography", "preface") || (hasContents && hasChapterStructure);
        if (pageCount >= 80 && hasBookSignals) return "Book / Textbook";
        if (ContainsAny(name, "textbook", "book") || ContainsAny(front[..Math.Min(front.Length, 8_000)], "textbook")) return "Book / Textbook";
        if (ContainsAny(name, "handbook") || ContainsAny(front[..Math.Min(front.Length, 4_000)], "handbook")) return "Handbook / Reference";
        if (ContainsAny(name, "lecture notes", "course notes") || ContainsAny(front[..Math.Min(front.Length, 4_000)], "lecture notes", "course notes")) return "Course Notes";
        if (ContainsAny(name, "guide", "tutorial")) return "Guide / Reference";
        if (pageCount >= 150 && hasContents) return "Book / Reference";
        return "Reference Document";
    }

    private static (string Topic, IReadOnlyList<string> Subtopics) SuggestContentFocus(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ("Unclassified", []);
        var lower = text.ToLowerInvariant();
        var domains = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Mathematics"] = Score(lower, "algebra", "calculus", "trigonometry", "logarithm", "geometry", "matrix", "vector", "homotopy", "homology", "cohomology", "simplicial", "topology", "fundamental group"),
            ["Electrical"] = Score(lower, "voltage", "current", "resistance", "impedance", "three phase", "power factor", "transformer", "electrical machine", "dc motor", "ac motor", "kirchhoff"),
            ["Electronics"] = Score(lower, "transistor", "diode", "op amp", "operational amplifier", "logic gate", "semiconductor", "amplifier", "oscillator", "rectifier", "integrated circuit"),
            ["Communications / RF"] = Score(lower, "radio frequency", "rf ", "antenna", "modulation", "demodulation", "receiver", "transmitter", "wireless", "rfid", "bluetooth", "zigbee"),
            ["Control / PLC"] = Score(lower, "programmable logic controller", "programmable controller", "plc", "ladder logic", "control system", "pid", "sequential logic"),
            ["Measurement & Testing"] = Score(lower, "oscilloscope", "multimeter", "measurement", "test equipment", "fault finding", "calibration", "instrumentation"),
            ["Health & Safety"] = Score(lower, "health and safety", "risk assessment", "hazard", "ppe", "legislation", "reporting accidents"),
            ["Mechanical"] = Score(lower, "torque", "stress", "strain", "thermodynamic", "hydraulic", "pneumatic", "mechanical", "fluid mechanics")
        };

        var best = domains.OrderByDescending(x => x.Value).First();
        if (best.Value < 2) return ("General Engineering / Reference", []);

        var subs = new List<string>();
        switch (best.Key)
        {
            case "Mathematics":
                AddSubtopic(subs, lower, "Algebraic Topology", "algebraic topology", "homotopy", "homology", "cohomology", "simplicial");
                AddSubtopic(subs, lower, "Homotopy", "homotopy");
                AddSubtopic(subs, lower, "Homology", "homology");
                AddSubtopic(subs, lower, "Cohomology", "cohomology");
                AddSubtopic(subs, lower, "Algebra", "algebra", "quadratic", "simultaneous equation");
                AddSubtopic(subs, lower, "Calculus", "calculus", "differentiation", "integration", "derivative");
                AddSubtopic(subs, lower, "Trigonometry", "trigonometry", "sine rule", "cosine rule");
                break;
            case "Electrical":
                AddSubtopic(subs, lower, "AC circuits", "ac circuit", "impedance", "reactance", "phasor");
                AddSubtopic(subs, lower, "DC circuits", "dc circuit", "kirchhoff", "resistance");
                AddSubtopic(subs, lower, "Three-phase systems", "three phase", "star delta", "power factor");
                AddSubtopic(subs, lower, "Electrical machines", "motor", "transformer", "generator", "electrical machine");
                break;
            case "Electronics":
                AddSubtopic(subs, lower, "Semiconductors", "diode", "transistor", "semiconductor");
                AddSubtopic(subs, lower, "Analogue electronics", "op amp", "operational amplifier", "amplifier");
                AddSubtopic(subs, lower, "Digital logic", "logic gate", "boolean", "flip flop", "counter");
                AddSubtopic(subs, lower, "Power electronics", "power electronics", "rectifier", "thyristor");
                break;
            case "Communications / RF":
                AddSubtopic(subs, lower, "RF fundamentals", "radio frequency", "rf ");
                AddSubtopic(subs, lower, "Antennas", "antenna");
                AddSubtopic(subs, lower, "Modulation", "modulation", "demodulation");
                AddSubtopic(subs, lower, "Receivers / transmitters", "receiver", "transmitter", "transceiver");
                AddSubtopic(subs, lower, "Wireless / RFID", "wireless", "rfid", "bluetooth", "zigbee");
                break;
            case "Control / PLC":
                AddSubtopic(subs, lower, "PLCs", "plc", "programmable logic controller", "programmable controller");
                AddSubtopic(subs, lower, "Ladder logic", "ladder logic");
                AddSubtopic(subs, lower, "Control systems", "control system", "pid");
                break;
            case "Measurement & Testing":
                AddSubtopic(subs, lower, "Oscilloscopes", "oscilloscope");
                AddSubtopic(subs, lower, "Fault finding", "fault finding");
                AddSubtopic(subs, lower, "Instrumentation", "instrumentation", "measurement", "calibration");
                break;
            case "Health & Safety":
                AddSubtopic(subs, lower, "Risk assessment", "risk assessment");
                AddSubtopic(subs, lower, "Electrical safety", "electrical hazard", "electrical safety");
                AddSubtopic(subs, lower, "Legislation", "legislation", "regulation");
                break;
        }

        return (best.Key, subs.Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToArray());
    }

    private static int Score(string text, params string[] signals)
        => signals.Count(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase));

    private static void AddSubtopic(List<string> result, string text, string label, params string[] signals)
    {
        if (signals.Any(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase))) result.Add(label);
    }

    private static bool ContainsAny(string text, params string[] values)
        => values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static string? InferTitleFromFileName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName).Trim();
        if (stem.Length < 5) return null;
        var cleaned = Regex.Replace(stem.Replace('_', ' '), @"\s+", " ").Trim();
        if (cleaned.All(char.IsDigit)) return null;
        return cleaned;
    }

    private static string NormalizeWhitespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        value = value.Replace("\r", "");
        value = MultiBlankLineRegex().Replace(value, "\n\n");
        return value.Trim();
    }

    private static string? CleanMetadata(string? value)
    {
        value = value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'-]*", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultiBlankLineRegex();
}
