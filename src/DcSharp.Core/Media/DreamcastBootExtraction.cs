namespace DcSharp.Core.Media;

public static class DreamcastBootExtractor
{
    public static DreamcastBootExtractionResult ExtractBootFile(string path, int bootScanSectors = 1024)
    {
        var fullPath = Path.GetFullPath(path);
        var report = DreamcastMediaInspector.Inspect(fullPath, bootScanSectors);
        var attempts = new List<string>();

        foreach (var source in BootSources(fullPath, report))
        {
            if (!DreamcastMediaImageLoader.TryLoadFromFile(source.Path, out var image, out var loadError) || image is null)
            {
                attempts.Add($"{source.Path}: {loadError}");
                continue;
            }

            if (!Iso9660FileSystem.TryOpen(image, out var fileSystem, out var isoError) || fileSystem is null)
            {
                attempts.Add($"{source.Path}: {isoError}");
                continue;
            }

            if (!fileSystem.TryGetFile(source.BootSector.BootFile, out var file, out var fileError) || file is null)
            {
                attempts.Add($"{source.Path}: {fileError}");
                continue;
            }

            byte[] data;
            try
            {
                data = fileSystem.ReadFile(file);
            }
            catch (InvalidDataException ex)
            {
                attempts.Add($"{source.Path}: {ex.Message}");
                if (TryExtractFromAdjacentTrackSet(fullPath, source.Path, source.BootSector, out var adjacentResult, out var adjacentAttempt))
                {
                    return adjacentResult;
                }

                if (!string.IsNullOrWhiteSpace(adjacentAttempt))
                {
                    attempts.Add(adjacentAttempt);
                }

                continue;
            }

            return new DreamcastBootExtractionResult(
                fullPath,
                source.Path,
                source.BootSector,
                fileSystem.VolumeIdentifier,
                file,
                data,
                attempts);
        }

        var detail = attempts.Count == 0
            ? "No Dreamcast boot sector was found."
            : string.Join(Environment.NewLine, attempts.Select(attempt => $"  {attempt}"));
        throw new InvalidDataException($"Unable to extract Dreamcast boot file from '{fullPath}'.{Environment.NewLine}{detail}");
    }

    private static bool TryExtractFromAdjacentTrackSet(
        string mediaPath,
        string sourcePath,
        DreamcastBootSectorInfo bootSector,
        out DreamcastBootExtractionResult result,
        out string? attempt)
    {
        result = default!;
        attempt = null;
        if (!AdjacentTrackSetMediaImage.TryCreate(sourcePath, out var image, out var error) || image is null)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                attempt = $"{sourcePath}: adjacent track set unavailable: {error}";
            }

            return false;
        }

        if (!Iso9660FileSystem.TryOpen(image, out var fileSystem, out var isoError) || fileSystem is null)
        {
            attempt = $"{sourcePath}: adjacent track set: {isoError}";
            return false;
        }

        if (!fileSystem.TryGetFile(bootSector.BootFile, out var file, out var fileError) || file is null)
        {
            attempt = $"{sourcePath}: adjacent track set: {fileError}";
            return false;
        }

        try
        {
            result = new DreamcastBootExtractionResult(
                mediaPath,
                sourcePath,
                bootSector,
                fileSystem.VolumeIdentifier,
                file,
                fileSystem.ReadFile(file),
                []);
            return true;
        }
        catch (InvalidDataException ex)
        {
            attempt = $"{sourcePath}: adjacent track set: {ex.Message}";
            return false;
        }
    }

    private static IReadOnlyList<DreamcastBootSource> BootSources(string path, DreamcastMediaInspectionReport report)
    {
        var sources = new List<DreamcastBootSource>();
        if (report.BootSector is not null)
        {
            sources.Add(new DreamcastBootSource(path, report.BootSector));
        }
        else if (report.BootSectorCandidates.Count > 0)
        {
            sources.Add(new DreamcastBootSource(path, report.BootSectorCandidates[0].BootSector));
        }

        foreach (var candidate in report.BootSectorCandidates)
        {
            sources.Add(new DreamcastBootSource(candidate.FilePath, candidate.BootSector));
        }

        return sources
            .GroupBy(source => (source.Path, source.BootSector.BootFile), StringTupleComparer.Instance)
            .Select(group => group.First())
            .ToArray();
    }

    private sealed record DreamcastBootSource(string Path, DreamcastBootSectorInfo BootSector);

    private sealed class StringTupleComparer : IEqualityComparer<(string Path, string BootFile)>
    {
        public static StringTupleComparer Instance { get; } = new();

        public bool Equals((string Path, string BootFile) x, (string Path, string BootFile) y) =>
            string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.BootFile, y.BootFile, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Path, string BootFile) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Path),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.BootFile));
    }
}

public sealed record DreamcastBootExtractionResult(
    string MediaPath,
    string SourcePath,
    DreamcastBootSectorInfo BootSector,
    string VolumeIdentifier,
    Iso9660FileInfo File,
    byte[] Data,
    IReadOnlyList<string> PriorAttempts);
