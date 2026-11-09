using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Core.Discovery;

public sealed class ResourceDiscoveryService
{
    public Task<DiscoveryScan> ScanAsync(
        LibrarySource source,
        IReadOnlyList<string>? protectedRelativePaths = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Scan(source, protectedRelativePaths ?? Array.Empty<string>(), cancellationToken), cancellationToken);

    private static DiscoveryScan Scan(
        LibrarySource source,
        IReadOnlyList<string> protectedRelativePaths,
        CancellationToken cancellationToken)
    {
        var files = new List<DiscoveredFile>();
        var excluded = new List<ExcludedDiscoveryFile>();
        var prunedDirectories = new List<PrunedDiscoveryDirectory>();
        var errors = new List<string>();
        var directoriesVisited = 0;
        var directoriesSkipped = 0;
        var infrastructureDirectoriesSkipped = 0;
        var contextDirectoriesSkipped = 0;
        var unsupportedFilesSkipped = 0;

        if (!Directory.Exists(source.RootPath))
        {
            errors.Add($"Library source is unavailable: {source.RootPath}");
            return new DiscoveryScan(files, excluded, prunedDirectories, 0, 0, 0, 0, 0, errors);
        }

        var root = Path.GetFullPath(source.RootPath);
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            directoriesVisited++;

            var relativeDirectory = DiscoveryPolicy.Normalize(Path.GetRelativePath(root, directory));
            if (relativeDirectory == ".") relativeDirectory = string.Empty;

            if (relativeDirectory.Length > 0)
            {
                var dirName = Path.GetFileName(directory);

                // Infrastructure remains its own deterministic category for reporting.
                if (DiscoveryPolicy.IsInfrastructureDirectory(dirName))
                {
                    infrastructureDirectoriesSkipped++;
                    continue;
                }

                var directoryAssessment = DiscoveryPolicy.EvaluateDirectory(
                    relativeDirectory,
                    source.Exclusions,
                    protectedRelativePaths);
                if (directoryAssessment.ShouldPrune)
                {
                    directoriesSkipped++;
                    contextDirectoriesSkipped++;
                    prunedDirectories.Add(new PrunedDiscoveryDirectory(relativeDirectory, directoryAssessment.Reason));
                    continue;
                }
            }

            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory))
                    pending.Push(child);

                foreach (var filePath in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relativePath = DiscoveryPolicy.Normalize(Path.GetRelativePath(root, filePath));
                    var assessment = DiscoveryPolicy.Evaluate(relativePath, source.Exclusions);

                    if (assessment.Decision.Equals("Exclude", StringComparison.OrdinalIgnoreCase))
                    {
                        if (assessment.Reason.Equals("Unsupported file type", StringComparison.OrdinalIgnoreCase))
                            unsupportedFilesSkipped++;
                        else
                            excluded.Add(new ExcludedDiscoveryFile(relativePath, Path.GetExtension(filePath).ToLowerInvariant(), assessment.Reason));
                        continue;
                    }

                    try
                    {
                        var info = new FileInfo(filePath);
                        files.Add(new DiscoveredFile(
                            relativePath,
                            info.Name,
                            info.Extension.ToLowerInvariant(),
                            info.Length,
                            info.LastWriteTimeUtc,
                            assessment.MediaKind ?? "Other",
                            assessment.Decision,
                            assessment.Reason,
                            assessment.SuggestedRoleKey,
                            assessment.CatalogueLane,
                            assessment.LaneReason));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        errors.Add($"{filePath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                directoriesSkipped++;
                errors.Add($"{directory}: {ex.Message}");
            }
        }

        return new DiscoveryScan(
            files,
            excluded,
            prunedDirectories,
            directoriesVisited,
            directoriesSkipped,
            infrastructureDirectoriesSkipped,
            contextDirectoriesSkipped,
            unsupportedFilesSkipped,
            errors);
    }
}
