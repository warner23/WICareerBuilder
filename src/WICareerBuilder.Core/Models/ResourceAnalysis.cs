namespace WICareerBuilder.Core.Models;

public sealed record ResourceAnalysis(
    long ResourceId,
    string AnalysisStatus,
    string ScannerKind,
    string ScannerVersion,
    DateTimeOffset ScannedUtc,
    string SuggestedResourceType,
    string SuggestedTopic,
    IReadOnlyList<string> SuggestedSubtopics,
    string? DocumentTitle,
    string? Author,
    int? PageCount,
    int PagesSampled,
    long ExtractedCharacters,
    bool HasSearchableText,
    IReadOnlyList<string> Headings,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> ContentIndicators,
    string TextPreview,
    string? ErrorMessage = null,
    string ExtractedText = "");
