namespace WICareerBuilder.Core.Discovery;

public static class ResourceClassifier
{
    private static readonly Dictionary<string, string> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "Document", [".doc"] = "Document", [".docx"] = "Document",
        [".rtf"] = "Document", [".txt"] = "Document", [".md"] = "Document", [".odt"] = "Document",

        [".ppt"] = "Presentation", [".pptx"] = "Presentation", [".pps"] = "Presentation",
        [".ppsx"] = "Presentation", [".odp"] = "Presentation",

        [".xls"] = "Spreadsheet", [".xlsx"] = "Spreadsheet", [".xlsm"] = "Spreadsheet",
        [".csv"] = "Spreadsheet", [".ods"] = "Spreadsheet",

        [".mp4"] = "Video", [".webm"] = "Video", [".mkv"] = "Video",
        [".mov"] = "Video", [".avi"] = "Video", [".wmv"] = "Video", [".m4v"] = "Video",

        [".mp3"] = "Audio", [".wav"] = "Audio", [".m4a"] = "Audio", [".flac"] = "Audio",

        [".png"] = "Image", [".jpg"] = "Image", [".jpeg"] = "Image", [".gif"] = "Image",
        [".webp"] = "Image", [".bmp"] = "Image", [".tif"] = "Image", [".tiff"] = "Image",

        [".zip"] = "Archive", [".7z"] = "Archive", [".rar"] = "Archive"
    };

    public static bool TryClassify(string path, out string mediaKind)
    {
        var extension = Path.GetExtension(path);
        return Kinds.TryGetValue(extension, out mediaKind!);
    }

    public static IReadOnlyCollection<string> SupportedExtensions => Kinds.Keys;
}
