using System.Text.Json;
using WICareerBuilder.Core.Classification;
using WICareerBuilder.Core.Models;
using WICareerBuilder.Documents.Analysis;

static string? Arg(string[] args, string name)
{
    var i = Array.FindIndex(args, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var root = Arg(args, "--root");
var expectationsPath = Arg(args, "--expectations");
var reportPath = Arg(args, "--report");
if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/WICareerBuilder.ContentValidation -- --root <folder> [--expectations <tsv>] [--report <json>]");
    return 2;
}

var expectations = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
if (!string.IsNullOrWhiteSpace(expectationsPath) && File.Exists(expectationsPath))
{
    foreach (var line in File.ReadLines(expectationsPath).Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith('#')))
    {
        var parts = line.Split('\t');
        if (parts.Length >= 5) expectations[parts[0].Replace('\\','/')] = parts;
    }
}

var analyser = new StructuredResourceAnalyser();
var results = new List<object>();
var failures = 0;
var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".docx", ".pptx", ".xlsx", ".xlsm", ".txt", ".md", ".csv", ".html", ".htm", ".xml", ".json", ".doc", ".ppt", ".xls" };
foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
{
    var ext = Path.GetExtension(file);
    if (!supported.Contains(ext) || Path.GetFileName(file).Equals("expectations.tsv", StringComparison.OrdinalIgnoreCase)) continue;
    var rel = Path.GetRelativePath(root, file).Replace('\\','/');
    var kind = ext.Equals(".pptx", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ppt", StringComparison.OrdinalIgnoreCase) ? "Presentation"
        : ext is ".xlsx" or ".xlsm" or ".xls" ? "Spreadsheet" : "Document";
    var info = new FileInfo(file);
    var resource = new ResourceItem(1,1,"Corpus","","",rel,info.Name,ext,info.Length,info.LastWriteTimeUtc,kind,"Unknown","",true,false,"","","","Unsorted");
    var analysis = await analyser.AnalyseAsync(resource, file, "Headless content placement corpus validation");
    var subject = LearningSubjectMapper.Map(analysis.SuggestedTopic);
    var topic = analysis.SuggestedSubtopics.FirstOrDefault() ?? string.Empty;
    var subtopic = analysis.SuggestedSubtopics.Skip(1).FirstOrDefault() ?? string.Empty;
    var confidence = PlacementDecisionPolicy.Assess(resource, analysis, subject, topic, subtopic);
    var decision = PlacementDecisionPolicy.Decide(subject, topic, subtopic, confidence);
    var disposition = !decision.CanApplySubject || !decision.CanApplyTopic ? "NeedsReview" : decision.IsPartial ? "PartiallyOrganised" : "AutoOrganised";

    var ok = true;
    if (expectations.TryGetValue(rel, out var exp))
    {
        bool Match(string expected, string actual) => expected == "*" || expected.Equals(actual, StringComparison.OrdinalIgnoreCase);
        ok = Match(exp[1], subject) && Match(exp[2], topic) && Match(exp[3], subtopic) && Match(exp[4], disposition);
        if (!ok) failures++;
    }
    results.Add(new { relativePath=rel, analysis.AnalysisStatus, subject, topic, subtopic, disposition, confidence.Subject, confidence.Topic, confidence.Subtopic, ok });
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}\t{rel}\t{subject}\t{topic}\t{subtopic}\t{disposition}\tS={confidence.Subject:0.00}\tT={confidence.Topic:0.00}\tST={confidence.Subtopic:0.00}");
}

if (!string.IsNullOrWhiteSpace(reportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
}
Console.WriteLine($"Corpus validation complete. Files={results.Count}; Failures={failures}");
return failures == 0 ? 0 : 1;
