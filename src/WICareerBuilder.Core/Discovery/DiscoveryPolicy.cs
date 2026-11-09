using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Core.Discovery;

public static class DiscoveryPolicy
{
    // Generated/dependency/runtime infrastructure. These folders are skipped entirely.
    public static readonly HashSet<string> ExcludedInfrastructureDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", ".vs", ".idea",
        "node_modules", "vendor", "packages", "bin", "obj",
        "debug", "release", "dist", "build", ".cache", "cache", "tmp", "temp",
        "__pycache__", ".pytest_cache", ".nuget"
    };

    // Formats that are software/configuration rather than normal learning documents. HTML remains
    // excluded from the normal file catalogue but can still be represented by a Learning Collection.
    public static readonly HashSet<string> HardExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".msi", ".msix", ".sys", ".com", ".scr", ".ocx", ".pdb",
        ".cs", ".csx", ".cpp", ".cxx", ".cc", ".c", ".h", ".hpp", ".java", ".py",
        ".php", ".rb", ".go", ".rs", ".fs", ".fsx", ".vb", ".swift", ".kt", ".kts",
        ".bat", ".cmd", ".ps1", ".sh", ".bash", ".zsh", ".fish", ".vbs",
        ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx", ".css", ".scss", ".sass", ".less",
        ".html", ".htm", ".xhtml", ".aspx", ".ascx", ".cshtml", ".razor", ".jsp", ".vue", ".svelte",
        ".ini", ".config", ".yaml", ".yml", ".toml", ".env", ".properties", ".lock",
        ".sql", ".sln", ".suo", ".csproj", ".fsproj", ".vbproj", ".vcxproj", ".vcproj",
        ".gradle", ".map"
    };

    private static readonly HashSet<string> WebDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".xhtml"
    };

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff"
    };

    private static readonly HashSet<string> TextLikeAmbiguousExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md"
    };

    private static readonly HashSet<string> ProjectSourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csx", ".cpp", ".cxx", ".cc", ".c", ".h", ".hpp", ".java", ".py",
        ".php", ".rb", ".go", ".rs", ".fs", ".fsx", ".vb", ".swift", ".kt", ".kts",
        ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx", ".html", ".htm", ".xhtml",
        ".css", ".scss", ".sass", ".less"
    };

    private static readonly HashSet<string> StrongApplicationContext = new(StringComparer.OrdinalIgnoreCase)
    {
        "program files", "program_files", "application data", "application_data", "appdata",
        "wwwroot", "htdocs", "public_html", "site-packages"
    };

    private static readonly HashSet<string> ApplicationStackContext = new(StringComparer.OrdinalIgnoreCase)
    {
        "ampps", "xampp", "wamp", "wamp64", "mamp", "iis", "apache", "nginx", "runtime", "runtimes"
    };

    // Generic development context is not automatically a reason to throw away a supported document.
    // v0.8.5 distinguishes application/runtime archives from project/development areas that can hold
    // valuable notes, datasheets, measurements, demonstrations and evidence.
    private static readonly HashSet<string> DevelopmentContext = new(StringComparer.OrdinalIgnoreCase)
    {
        "software", "source", "src", "code", "source code", "reader code", "software code",
        "c++", "cpp", "csharp", "python", "php", "javascript", "web", "web app", "web application",
        "engineering_programs", "programs", "applications", "application",
        "websites", "website", "webpages", "webpage", "configs", "config", "configuration", "settings",
        "firmware"
    };

    private static readonly HashSet<string> EngineeringProgramContext = new(StringComparer.OrdinalIgnoreCase)
    {
        "engineering_programs", "engineering programs", "programs", "applications"
    };

    private static readonly HashSet<string> ProjectContext = new(StringComparer.OrdinalIgnoreCase)
    {
        "project", "projects"
    };

    // Installer/licence/support metadata remains noise even when its extension is PDF/RTF/DOCX.
    private static readonly HashSet<string> AdministrativeSoftwareContext = new(StringComparer.OrdinalIgnoreCase)
    {
        "license", "licenses", "licence", "licences", "eula", "eulas",
        "setup", "installer", "installers", "installation", "redist", "redistributable", "redistributables",
        "prerequisite", "prerequisites", "open source licenses", "opensource licenses"
    };

    private static readonly HashSet<string> ProjectEvidenceContext = new(StringComparer.OrdinalIgnoreCase)
    {
        "evidence", "result", "results", "measurement", "measurements", "test results", "tests",
        "data", "screenshots", "screenshots and video", "screen shots", "video", "videos", "photos",
        "demonstration", "demonstrations", "demo", "demos", "reports"
    };

    // High-confidence learning/reference branch names. These are policy hints rather than a fixed
    // subject tree; semantic classification comes later.
    private static readonly HashSet<string> LearningContext = new(StringComparer.OrdinalIgnoreCase)
    {
        "schematic", "schematics", "diagram", "diagrams", "datasheet", "datasheets",
        "worksheet", "worksheets", "assignment", "assignments", "revision", "study",
        "report", "reports", "documentation", "docs", "manual", "manuals",
        "tutorial", "tutorials", "notes", "research", "reference", "references",
        "evidence", "results", "formula", "formula's", "formulas", "cheatsheet", "cheatsheets",
        "lesson", "lessons", "lecture", "lectures", "book", "books", "textbook", "textbooks",
        "components", "capacitors", "resistors", "diodes", "transistors", "op amps", "op-amps",
        "circuits", "motors", "generators", "machines", "electrical machines", "electrical mahines",
        "health and safety", "health & safety", "safety"
    };

    private static readonly HashSet<string> TextLearningContext = new(StringComparer.OrdinalIgnoreCase)
    {
        "datasheet", "datasheets", "worksheet", "worksheets", "assignment", "assignments",
        "revision", "study", "report", "reports", "documentation", "docs", "manual", "manuals",
        "tutorial", "tutorials", "notes", "research", "reference", "references", "evidence", "results",
        "formula", "formula's", "formulas", "cheatsheet", "cheatsheets",
        "lesson", "lessons", "lecture", "lectures", "book", "books", "textbook", "textbooks"
    };

    private static readonly HashSet<string> EducationalSoftwareContext = new(StringComparer.OrdinalIgnoreCase)
    {
        "glossary", "courseware", "training", "training cd", "tutorial cd", "learning",
        "education", "educational", "e-learning", "elearning", "electronics cd"
    };

    private static readonly string[] StrongReferenceNameSignals =
    {
        "tutorial", "guide", "manual", "handbook", "bible", "textbook", "mastering", "master class",
        "datasheet", "data sheet", "product specification", "product spec", "specification",
        "pinout", "command list", "code explanation", "circuit analysis", "reference",
        "schematic", "diagram", "lecture", "lesson", "worksheet", "workbook", "revision",
        "training manual", "technical manual", "application note"
    };

    private static readonly string[] EvidenceNameSignals =
    {
        "signal_data", "signal data", "measurement", "measurements", "test result", "results",
        "demonstration", "demo", "screenshot", "screen shot", "prototype", "build log", "test log"
    };

    public static DirectoryDiscoveryAssessment EvaluateDirectory(
        string relativeDirectory,
        IReadOnlyList<string>? sourceExclusions = null,
        IReadOnlyList<string>? protectedRelativePaths = null)
    {
        var normalized = Normalize(relativeDirectory).Trim('/');
        if (normalized.Length == 0) return new DirectoryDiscoveryAssessment(false, string.Empty);

        if (protectedRelativePaths is not null && IsProtectedTraversalPath(normalized, protectedRelativePaths))
            return new DirectoryDiscoveryAssessment(false, "Protected by saved Learning Collection/manual include");

        if (sourceExclusions is not null && IsExplicitlyExcluded(normalized, sourceExclusions))
            return new DirectoryDiscoveryAssessment(true, "Explicit source exclusion");

        var segments = GetPathSegments(normalized).ToArray();
        if (IsContextPrunedDirectory(normalized, segments))
            return new DirectoryDiscoveryAssessment(true, ContextPruneReason(normalized, segments));

        if (IsEducationalSoftware(normalized + "/", segments))
            return new DirectoryDiscoveryAssessment(false, string.Empty);

        return new DirectoryDiscoveryAssessment(false, string.Empty);
    }

    public static DiscoveryAssessment Evaluate(string relativePath, IReadOnlyList<string>? sourceExclusions = null)
    {
        var normalized = Normalize(relativePath).Trim('/');
        var extension = Path.GetExtension(normalized).ToLowerInvariant();
        var segments = GetDirectorySegments(normalized).ToArray();
        var fileName = Path.GetFileName(normalized);

        if (sourceExclusions is not null && IsExplicitlyExcluded(normalized, sourceExclusions))
            return Excluded("Explicit source exclusion");

        if (IsTemporaryOfficeFile(fileName))
            return Excluded("Temporary/editor lock file");

        // Hard application/archive branches beat both the learning lane and project-artifact lane.
        if (IsContextPrunedFilePath(normalized, segments))
            return Excluded("Application/web archive branch; directory context takes precedence");

        var hasLearningContext = segments.Any(IsLearningContextSegment);
        var hasTextLearningContext = segments.Any(IsTextLearningContextSegment);
        var hasDevelopmentContext = segments.Any(IsDevelopmentContextSegment);
        var hasProjectContext = segments.Any(IsProjectContextSegment);
        var hasEngineeringProgramContext = segments.Any(IsEngineeringProgramContextSegment);
        var hasAdministrativeSoftwareContext = segments.Any(IsAdministrativeSoftwareContextSegment) || LooksLikeLicenceFile(fileName);
        var hasProjectEvidenceContext = segments.Any(IsProjectEvidenceContextSegment) || LooksLikeEvidenceFile(fileName);
        var hasStrongReferenceSignal = hasLearningContext || LooksLikeReferenceFile(normalized);

        if (WebDocumentExtensions.Contains(extension) && IsEducationalSoftware(normalized, segments))
            return new DiscoveryAssessment("Exclude", "Educational software / learning collection candidate", "Document", null, "None", "Represented by Learning Collection rather than as a standalone resource");

        // v0.8.5: project-owned source code is no longer confused with a learning document. It can be
        // tracked as a project artifact, but obvious third-party/dependency source trees stay out.
        if (HardExcludedExtensions.Contains(extension))
        {
            if (hasProjectContext && ProjectSourceExtensions.Contains(extension) && !LooksLikeThirdPartyDependencyPath(normalized))
                return new DiscoveryAssessment("Include", "Project source artifact; kept out of the learning catalogue", "Code", "ProjectMaterial", "ProjectArtifact", "Project source/evidence lane only");
            return Excluded(HardExclusionReason(extension));
        }

        if (!ResourceClassifier.TryClassify(normalized, out var mediaKind))
            return Excluded("Unsupported file type");

        if (segments.Any(s => StrongApplicationContext.Contains(s)))
            return Excluded("Application/program/web/configuration tree", mediaKind);

        // Licences/EULAs/install metadata are not educational or useful project evidence merely because
        // their extension is PDF/RTF/DOCX.
        if (hasAdministrativeSoftwareContext && hasDevelopmentContext)
            return Excluded("Software licence/install/support metadata", mediaKind);

        if (hasProjectContext)
            return EvaluateProjectResource(normalized, extension, mediaKind, hasDevelopmentContext, hasStrongReferenceSignal, hasProjectEvidenceContext);

        if (hasEngineeringProgramContext)
            return EvaluateEngineeringProgramResource(normalized, extension, mediaKind, hasLearningContext, hasStrongReferenceSignal);

        if (ArchiveExtensions.Contains(extension))
        {
            if (hasDevelopmentContext && !hasLearningContext)
                return Excluded("Archive in software/development branch", mediaKind);

            var reason = hasLearningContext
                ? "Archive in learning/reference branch; contents not inspected"
                : hasDevelopmentContext
                    ? "Archive may contain learning material inside development branch; contents not inspected"
                    : "Archive contents not inspected";
            return new DiscoveryAssessment("Review", reason, mediaKind, hasLearningContext ? "Reference" : null);
        }

        if (ImageExtensions.Contains(extension))
        {
            if (hasLearningContext && hasDevelopmentContext)
                return new DiscoveryAssessment("Review", "Potential learning/reference image inside development branch", mediaKind, "ImageDiagram");

            if (hasDevelopmentContext)
                return Excluded("Development/application asset", mediaKind);

            if (hasLearningContext)
                return new DiscoveryAssessment("Include", "High-confidence learning/reference image", mediaKind, "ImageDiagram");

            return new DiscoveryAssessment("Review", "Ambiguous resource format; included for manual review", mediaKind, "ImageDiagram");
        }

        if (TextLikeAmbiguousExtensions.Contains(extension))
        {
            if (hasDevelopmentContext)
            {
                if (hasTextLearningContext || LooksLikeReferenceFile(normalized))
                    return new DiscoveryAssessment("Review", "Potential project/development documentation; retained for learning review", mediaKind, "Reference");
                return Excluded("Development text/metadata", mediaKind);
            }

            if (hasTextLearningContext)
                return new DiscoveryAssessment("Include", "High-confidence learning/reference text", mediaKind, "Reference");

            return new DiscoveryAssessment("Review", "Ambiguous resource format; included for manual review", mediaKind);
        }

        if (hasDevelopmentContext)
        {
            if (hasStrongReferenceSignal)
                return new DiscoveryAssessment("Review", "Technical/reference resource inside development branch; retained for learning review", mediaKind, SuggestedReferenceRole(normalized));

            if (mediaKind is "Document" or "Presentation" or "Spreadsheet")
                return new DiscoveryAssessment("Review", "Supported document/data inside development branch; retained for learning review", mediaKind, "Reference");

            return Excluded("Development/application asset", mediaKind);
        }

        return new DiscoveryAssessment("Include", "Supported learning-resource format", mediaKind, SuggestedReferenceRole(normalized));
    }

    private static DiscoveryAssessment EvaluateProjectResource(
        string normalized,
        string extension,
        string mediaKind,
        bool hasDevelopmentContext,
        bool hasStrongReferenceSignal,
        bool hasProjectEvidenceContext)
    {
        // Strong technical references can legitimately serve both the project and the learning system.
        // Do this before implementation-noise filtering so a datasheet/tutorial inside libraries remains useful.
        if (hasStrongReferenceSignal && IsStrongReferenceMedia(mediaKind, extension))
            return new DiscoveryAssessment("Include", "Project technical reference retained in both learning and project lanes", mediaKind, SuggestedReferenceRole(normalized) ?? "Reference", "Both", "Technical reference supports both learning and the project");

        // Explicit project data/result branches are artifacts even when they live inside a third-party
        // source tree. This keeps useful ECG/RF measurements without promoting README/build noise.
        if (IsExplicitProjectDataPath(normalized) && IsProjectDataArtifactMedia(mediaKind, extension))
            return new DiscoveryAssessment("Include", "Project data/result artifact; kept out of the learning catalogue", mediaKind, "ProjectEvidence", "ProjectArtifact", "Explicit project data/results branch");

        if (LooksLikeLowValueProjectImplementation(normalized, extension))
            return Excluded("Low-value project implementation/dependency asset", mediaKind);

        if (ArchiveExtensions.Contains(extension))
        {
            if (hasDevelopmentContext)
                return Excluded("Archive in project software/source branch", mediaKind);
            return new DiscoveryAssessment("Include", "Project archive artifact; contents are not unpacked or treated as learning material", mediaKind, "ProjectMaterial", "ProjectArtifact", "Project artifact lane only; archive contents remain opaque");
        }

        // Data/results/demonstrations are project artifacts. They are valuable, but must not inflate the
        // learning-library review queue or enter learning deep analysis merely because they are supported.
        if (hasProjectEvidenceContext && IsProjectEvidenceMedia(mediaKind, extension))
            return new DiscoveryAssessment("Include", "Project evidence/data artifact; kept out of the learning catalogue", mediaKind, "ProjectEvidence", "ProjectArtifact", "Evidence/data produced by or supporting the project");

        if (mediaKind == "Presentation")
            return new DiscoveryAssessment("Include", "Project presentation artifact", mediaKind, "ProjectMaterial", "ProjectArtifact", "Project presentation; not assumed to be teaching material");

        if (mediaKind is "Document" or "Spreadsheet" or "Video" or "Audio" or "Image")
            return new DiscoveryAssessment("Include", "Project artifact tracked separately from the learning library", mediaKind, (mediaKind is "Video" or "Image") ? "ProjectEvidence" : "ProjectMaterial", "ProjectArtifact", "Project/development artifact; content analysis may later promote it to Both");

        return new DiscoveryAssessment("Include", "Project artifact tracked separately from the learning library", mediaKind, "ProjectMaterial", "ProjectArtifact", "Project/development artifact lane");
    }

    private static DiscoveryAssessment EvaluateEngineeringProgramResource(
        string normalized,
        string extension,
        string mediaKind,
        bool hasLearningContext,
        bool hasStrongReferenceSignal)
    {
        if (ArchiveExtensions.Contains(extension))
            return Excluded("Engineering software/application archive", mediaKind);

        if (LooksLikeEngineeringSoftwareMetadata(normalized))
            return Excluded("Engineering software installer/runtime/support metadata", mediaKind);

        if (mediaKind == "Image")
        {
            if (hasLearningContext || LooksLikeVisualReferenceFile(normalized))
                return new DiscoveryAssessment("Include", "Technical diagram/reference retained alongside engineering software", mediaKind, "ImageDiagram");
            return Excluded("Engineering software/application image asset", mediaKind);
        }

        if (hasStrongReferenceSignal && IsStrongReferenceMedia(mediaKind, extension))
            return new DiscoveryAssessment("Include", "Technical book/reference retained alongside engineering software", mediaKind, SuggestedReferenceRole(normalized) ?? "Reference");

        // Plain text beside installers is overwhelmingly metadata. Keep richer documents conservative.
        if (TextLikeAmbiguousExtensions.Contains(extension))
            return Excluded("Engineering software text/support metadata", mediaKind);

        if (mediaKind is "Document" or "Presentation" or "Spreadsheet")
            return new DiscoveryAssessment("Review", "Potential technical reference beside engineering software; retained for learning review", mediaKind, "Reference");

        return Excluded("Engineering software/application asset", mediaKind);
    }

    public static bool IsLearningCollectionCandidate(string relativePath, string reason)
        => reason.Contains("learning collection candidate", StringComparison.OrdinalIgnoreCase)
           || (WebDocumentExtensions.Contains(Path.GetExtension(relativePath)) && IsEducationalSoftware(Normalize(relativePath), GetDirectorySegments(Normalize(relativePath)).ToArray()));

    public static string? GetLearningCollectionGroupingPrefix(string relativePath)
    {
        var normalized = Normalize(relativePath).Trim('/');
        var allSegments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (allSegments.Length < 2) return null;

        var directories = allSegments.Take(allSegments.Length - 1).ToArray();
        for (var i = 0; i < directories.Length; i++)
        {
            if (EducationalSoftwareContext.Contains(directories[i].ToLowerInvariant()))
                return string.Join('/', directories.Take(i + 1));
        }

        for (var i = 0; i < directories.Length; i++)
        {
            if (directories[i].Equals("glossary", StringComparison.OrdinalIgnoreCase))
                return string.Join('/', directories.Take(i + 1));
        }

        return null;
    }

    public static string ManualIncludeMediaKind(string relativePath)
    {
        if (ResourceClassifier.TryClassify(relativePath, out var mediaKind)) return mediaKind;
        var extension = Path.GetExtension(relativePath);
        if (WebDocumentExtensions.Contains(extension) || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)) return "Document";
        return "Other";
    }

    public static bool IsInfrastructureDirectory(string directoryName)
        => ExcludedInfrastructureDirectoryNames.Contains(directoryName);

    public static bool IsExplicitlyExcluded(string relativePath, IReadOnlyList<string> exclusions)
    {
        var normalized = Normalize(relativePath).Trim('/');
        if (normalized.Length == 0) return false;

        foreach (var raw in exclusions)
        {
            var pattern = Normalize(raw).Trim('/');
            if (pattern.Length == 0) continue;
            if (normalized.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(pattern + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static string Normalize(string value) => value.Replace('\\', '/');

    private static bool IsContextPrunedFilePath(string normalizedPath, IReadOnlyCollection<string> directorySegments)
    {
        var directory = Normalize(Path.GetDirectoryName(normalizedPath.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty).Trim('/');
        if (directory.Length == 0) return false;
        return IsContextPrunedDirectory(directory, directorySegments);
    }

    private static bool IsContextPrunedDirectory(string normalizedDirectory, IReadOnlyCollection<string> segments)
    {
        var lower = normalizedDirectory.ToLowerInvariant();

        if (lower.Equals("software/web", StringComparison.OrdinalIgnoreCase) ||
            lower.StartsWith("software/web/", StringComparison.OrdinalIgnoreCase) ||
            lower.Equals("software/websites", StringComparison.OrdinalIgnoreCase) ||
            lower.StartsWith("software/websites/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (segments.Any(s => StrongApplicationContext.Contains(s))) return true;

        if (segments.Any(s => ApplicationStackContext.Contains(s)) && segments.Any(IsDevelopmentContextSegment))
            return true;

        return false;
    }

    private static string ContextPruneReason(string normalizedDirectory, IReadOnlyCollection<string> segments)
    {
        var lower = normalizedDirectory.ToLowerInvariant();
        if (lower.Equals("software/web", StringComparison.OrdinalIgnoreCase) || lower.StartsWith("software/web/", StringComparison.OrdinalIgnoreCase) ||
            lower.Equals("software/websites", StringComparison.OrdinalIgnoreCase) || lower.StartsWith("software/websites/", StringComparison.OrdinalIgnoreCase))
            return "Application/web archive branch";
        if (segments.Any(s => StrongApplicationContext.Contains(s))) return "Application/runtime directory";
        if (segments.Any(s => ApplicationStackContext.Contains(s))) return "Local application/web stack directory";
        return "Context-pruned application branch";
    }

    private static bool IsProtectedTraversalPath(string directory, IReadOnlyList<string> protectedRelativePaths)
    {
        foreach (var raw in protectedRelativePaths)
        {
            var protectedPath = Normalize(raw).Trim('/');
            if (protectedPath.Length == 0) continue;

            if (directory.Equals(protectedPath, StringComparison.OrdinalIgnoreCase) ||
                protectedPath.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase) ||
                directory.StartsWith(protectedPath + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsEducationalSoftware(string normalizedPath, IReadOnlyCollection<string> segments)
    {
        if (segments.Any(s => EducationalSoftwareContext.Contains(s))) return true;

        var lower = normalizedPath.ToLowerInvariant();
        return lower.Contains("electronics cd/")
               || lower.Contains("courseware/")
               || lower.Contains("training cd/")
               || lower.Contains("/glossary/");
    }

    private static bool IsLearningContextSegment(string segment)
    {
        if (LearningContext.Contains(segment)) return true;
        return LearningContext.Contains(NormalizeSegment(segment));
    }

    private static bool IsTextLearningContextSegment(string segment)
    {
        if (TextLearningContext.Contains(segment)) return true;
        return TextLearningContext.Contains(NormalizeSegment(segment));
    }

    private static bool IsDevelopmentContextSegment(string segment)
    {
        if (DevelopmentContext.Contains(segment)) return true;
        var normalized = NormalizeSegment(segment);
        return DevelopmentContext.Contains(normalized)
               || normalized.Equals("source code", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("reader code", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("software code", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("web application", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("web app", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProjectContextSegment(string segment)
        => ProjectContext.Contains(segment) || ProjectContext.Contains(NormalizeSegment(segment));

    private static bool IsEngineeringProgramContextSegment(string segment)
        => EngineeringProgramContext.Contains(segment) || EngineeringProgramContext.Contains(NormalizeSegment(segment));

    private static bool IsAdministrativeSoftwareContextSegment(string segment)
        => AdministrativeSoftwareContext.Contains(segment) || AdministrativeSoftwareContext.Contains(NormalizeSegment(segment));

    private static bool IsProjectEvidenceContextSegment(string segment)
        => ProjectEvidenceContext.Contains(segment) || ProjectEvidenceContext.Contains(NormalizeSegment(segment));

    private static bool LooksLikeReferenceFile(string path)
    {
        var lower = NormalizeSegment(Path.GetFileNameWithoutExtension(path));
        return StrongReferenceNameSignals.Any(signal => lower.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeEvidenceFile(string fileName)
    {
        var lower = NormalizeSegment(Path.GetFileNameWithoutExtension(fileName));
        return EvidenceNameSignals.Any(signal => lower.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeLicenceFile(string fileName)
    {
        var lower = NormalizeSegment(Path.GetFileNameWithoutExtension(fileName));
        return lower.Contains("license", StringComparison.OrdinalIgnoreCase)
               || lower.Contains("licence", StringComparison.OrdinalIgnoreCase)
               || lower.Contains("eula", StringComparison.OrdinalIgnoreCase)
               || lower.Contains("foss notice", StringComparison.OrdinalIgnoreCase)
               || lower.Contains("classpath exception", StringComparison.OrdinalIgnoreCase)
               || lower.Contains("lgpl", StringComparison.OrdinalIgnoreCase)
               || lower.Contains("lpgl", StringComparison.OrdinalIgnoreCase);
    }

    private static DiscoveryAssessment Excluded(string reason, string? mediaKind = null)
        => new("Exclude", reason, mediaKind, null, "None", "Not catalogued for learning or project artifacts");

    private static bool IsStrongReferenceMedia(string mediaKind, string extension)
        => mediaKind is "Document" or "Presentation" or "Spreadsheet"
           && !TextLikeAmbiguousExtensions.Contains(extension);

    private static bool IsProjectEvidenceMedia(string mediaKind, string extension)
        => mediaKind is "Spreadsheet" or "Video" or "Audio" or "Image" or "Presentation"
           || (mediaKind == "Document" && !TextLikeAmbiguousExtensions.Contains(extension));

    private static bool LooksLikeThirdPartyDependencyPath(string path)
    {
        var lower = Normalize(path).ToLowerInvariant();
        return lower.Contains("/node_modules/") || lower.Contains("/vendor/") || lower.Contains("/packages/")
               || lower.Contains("/libs/") || lower.Contains("/libraries/") || lower.Contains("/third_party/")
               || lower.Contains("/third-party/") || lower.Contains("/back up original/") || lower.Contains("/backup original/")
               || lower.Contains("-master/") || lower.Contains("/bower_components/");
    }


    private static bool IsExplicitProjectDataPath(string path)
    {
        var lower = Normalize(path).ToLowerInvariant();
        return lower.Contains("/data/") || lower.Contains("/results/") || lower.Contains("/result/")
               || lower.Contains("/measurements/") || lower.Contains("/measurement/")
               || lower.Contains("/output_samples/") || lower.Contains("/test results/");
    }

    private static bool IsProjectDataArtifactMedia(string mediaKind, string extension)
        => mediaKind is "Spreadsheet" or "Image" or "Video" or "Audio" or "Presentation"
           || TextLikeAmbiguousExtensions.Contains(extension);

    private static bool LooksLikeLowValueProjectImplementation(string path, string extension)
    {
        var lower = Normalize(path).ToLowerInvariant();
        var name = Path.GetFileName(lower);

        if (LooksLikeThirdPartyDependencyPath(lower) && (name.StartsWith("readme") || name == "keywords.txt" || TextLikeAmbiguousExtensions.Contains(extension)))
            return true;

        // Generic files copied in with third-party libraries/repositories are supporting dependency assets,
        // not user project evidence. Strong technical references and explicit data/results were already
        // handled before this function is reached and therefore remain available.
        if (LooksLikeThirdPartyDependencyPath(lower))
            return true;

        if (name is "robots.txt" or "cmakelists.txt" or "output-build.txt" || name.Contains("assemblyinfo")
            || name.Contains("designer_cs") || name.Contains("designer.cs") || name.Contains("resources.designer")
            || name.Contains("settings.designer") || name is "program_cs.txt" or "form1_cs.txt")
            return true;

        if (name.Contains("client_api_log") || name.Contains("client_post_api_log") || name == "exceptions.txt")
            return true;

        if (lower.Contains("/btn_cache/") || lower.Contains("/btn_template/icons/") || lower.Contains("/btn_template/small_version/")
            || lower.Contains("/resources/maintable/") || lower.Contains("/webmail/tests/") || lower.Contains("/tests/src/"))
            return true;

        if (TextLikeAmbiguousExtensions.Contains(extension) && lower.Contains("/website/")
            && (lower.Contains("/includes/") || lower.Contains("/resources/"))
            && !LooksLikeEvidenceFile(name) && !LooksLikeReferenceFile(name))
            return true;

        // Generic static UI assets inside a website-code tree are implementation detail, not evidence.
        if ((mediaKindForExtension(extension) == "Image") && lower.Contains("/website/")
            && (lower.Contains("/includes/") || lower.Contains("/resources/") || lower.Contains("/icons/") || lower.Contains("/cache/"))
            && !LooksLikeEvidenceFile(name) && !LooksLikeVisualReferenceFile(name))
            return true;

        return false;
    }

    private static string mediaKindForExtension(string extension)
    {
        if (ImageExtensions.Contains(extension)) return "Image";
        if (TextLikeAmbiguousExtensions.Contains(extension)) return "Document";
        return string.Empty;
    }

    private static bool LooksLikeEngineeringSoftwareMetadata(string path)
    {
        var lower = Normalize(path).ToLowerInvariant();
        var name = Path.GetFileName(lower);
        return lower.Contains("/crack/") || lower.Contains("/keygen/") || lower.Contains("/eula/") || lower.Contains("/licenses/")
               || lower.Contains("/setup/") || lower.Contains("/installer/") || lower.Contains("/supportfiles/")
               || name.StartsWith("torrent_") || name.StartsWith("provided_by_") || name.StartsWith("tracked_by_")
               || name is "install.txt" or "installation.txt" or "silent_install.txt" or "patents.txt" or "mid.txt" or "mid01.txt"
               || name.StartsWith("lang_") || name == "accresource.txt" || name == "_errorlog.txt"
               || name.StartsWith("sms_sccm_readme") || name == "copying.txt" || name.StartsWith("mpl-");
    }

    private static bool LooksLikeVisualReferenceFile(string path)
    {
        var lower = NormalizeSegment(Path.GetFileNameWithoutExtension(path));
        return lower.Contains("diagram") || lower.Contains("schematic") || lower.Contains("pinout")
               || lower.Contains("class diagram") || lower.Contains("wiring") || lower.Contains("connection");
    }

    private static bool IsTemporaryOfficeFile(string fileName)
        => fileName.StartsWith("~$", StringComparison.OrdinalIgnoreCase)
           || fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
           || fileName.EndsWith(".temp", StringComparison.OrdinalIgnoreCase);

    private static string? SuggestedReferenceRole(string path)
    {
        var lower = NormalizeSegment(Path.GetFileNameWithoutExtension(path));
        if (lower.Contains("datasheet") || lower.Contains("data sheet") || lower.Contains("product specification") || lower.Contains("product spec")) return "Datasheet";
        if (lower.Contains("manual") || lower.Contains("handbook")) return "Manual";
        if (lower.Contains("guide") || lower.Contains("bible") || lower.Contains("mastering") || lower.Contains("textbook")) return "Book";
        if (lower.Contains("worksheet")) return "Worksheet";
        if (lower.Contains("revision")) return "RevisionMaterial";
        return null;
    }

    private static string NormalizeSegment(string segment)
        => segment.Replace('_', ' ').Replace('-', ' ').Trim().ToLowerInvariant();

    private static IEnumerable<string> GetDirectorySegments(string relativePath)
    {
        var directory = Path.GetDirectoryName(relativePath.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
        return GetPathSegments(Normalize(directory));
    }

    private static IEnumerable<string> GetPathSegments(string path)
        => Normalize(path)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.Trim().ToLowerInvariant());

    private static string HardExclusionReason(string extension)
    {
        if (extension is ".html" or ".htm" or ".xhtml" or ".aspx" or ".ascx" or ".cshtml" or ".razor" or ".jsp" or ".vue" or ".svelte" or ".js" or ".mjs" or ".cjs" or ".ts" or ".tsx" or ".jsx" or ".css" or ".scss" or ".sass" or ".less")
            return "Website/web-page source";
        if (extension is ".ini" or ".config" or ".yaml" or ".yml" or ".toml" or ".env" or ".properties" or ".lock")
            return "Configuration/settings file";
        if (extension is ".exe" or ".dll" or ".msi" or ".msix" or ".sys" or ".com" or ".scr" or ".ocx" or ".pdb")
            return "Application/binary file";
        if (extension is ".sln" or ".suo" or ".csproj" or ".fsproj" or ".vbproj" or ".vcxproj" or ".vcproj" or ".gradle" or ".sql")
            return "Software/project infrastructure";
        return "Programming/script source";
    }
}
