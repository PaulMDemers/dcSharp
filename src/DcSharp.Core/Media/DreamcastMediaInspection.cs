using System.Text;
using System.Text.RegularExpressions;

namespace DcSharp.Core.Media;

public static class DreamcastMediaInspector
{
    private const string DreamcastHardwareId = "SEGA SEGAKATANA";
    private const int DefaultBootScanSectors = 1024;

    public static DreamcastMediaInspectionReport Inspect(string path, int bootScanSectors = DefaultBootScanSectors)
    {
        if (bootScanSectors < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bootScanSectors), bootScanSectors, "Boot scan sector count must be zero or greater.");
        }

        var fullPath = Path.GetFullPath(path);
        var image = DreamcastMediaImageLoader.LoadFromFile(fullPath);
        var cueTracks = string.Equals(Path.GetExtension(fullPath), ".cue", StringComparison.OrdinalIgnoreCase)
            ? ParseCueTracks(fullPath)
            : [];
        return new DreamcastMediaInspectionReport(
            fullPath,
            Path.GetExtension(fullPath).TrimStart('.').ToUpperInvariant(),
            image.SectorSize,
            image.SectorCount,
            image.LeadoutFad,
            $"0x{image.LeadoutFad:X8}",
            image.Tracks,
            cueTracks,
            FindBootSector(image, bootScanSectors));
    }

    private static DreamcastBootSectorInfo? FindBootSector(IDreamcastMediaImage image, int bootScanSectors)
    {
        var candidates = CandidateSectors(image, bootScanSectors);
        var sector = new byte[Math.Max(image.SectorSize, 256)];
        foreach (var candidate in candidates)
        {
            Array.Clear(sector);
            if (!image.TryReadSector(candidate, sector, out var bytesRead) || bytesRead < 256)
            {
                continue;
            }

            if (ReadAscii(sector, 0x00, 0x10) == DreamcastHardwareId)
            {
                return DreamcastBootSectorInfo.FromSector(candidate, sector);
            }
        }

        return null;
    }

    private static IReadOnlyList<uint> CandidateSectors(IDreamcastMediaImage image, int bootScanSectors)
    {
        var sectors = new SortedSet<uint>();
        var sequentialLimit = (uint)Math.Min((ulong)bootScanSectors, Math.Min(image.SectorCount, uint.MaxValue));
        for (uint sector = 0; sector < sequentialLimit; sector++)
        {
            sectors.Add(sector);
        }

        foreach (var track in image.Tracks)
        {
            var trackLimit = (uint)Math.Min((ulong)bootScanSectors, track.SectorCount);
            for (uint offset = 0; offset < trackLimit; offset++)
            {
                sectors.Add(track.StartFad + offset);
            }
        }

        return sectors.ToArray();
    }

    private static IReadOnlyList<DreamcastCueTrackInspection> ParseCueTracks(string cuePath)
    {
        var cueDirectory = Path.GetDirectoryName(cuePath);
        if (string.IsNullOrWhiteSpace(cueDirectory))
        {
            return [];
        }

        var tracks = new List<DreamcastCueTrackInspection>();
        string? activeFile = null;
        var fileRegex = new Regex("^FILE\\s+\"(?<path>[^\"]+)\"\\s+(?<type>\\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var trackRegex = new Regex("^TRACK\\s+(?<number>\\d+)\\s+(?<type>\\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        foreach (var line in File.ReadLines(cuePath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("REM", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileMatch = fileRegex.Match(trimmed);
            if (fileMatch.Success)
            {
                activeFile = Path.GetFullPath(Path.Combine(cueDirectory, fileMatch.Groups["path"].Value));
                continue;
            }

            var trackMatch = trackRegex.Match(trimmed);
            if (!trackMatch.Success || activeFile is null)
            {
                continue;
            }

            var type = trackMatch.Groups["type"].Value;
            tracks.Add(new DreamcastCueTrackInspection(
                int.Parse(trackMatch.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture),
                type,
                activeFile,
                type.StartsWith("MODE1", StringComparison.OrdinalIgnoreCase) || type.StartsWith("MODE2", StringComparison.OrdinalIgnoreCase)));
        }

        return tracks;
    }

    internal static string ReadAscii(ReadOnlySpan<byte> bytes, int offset, int length)
    {
        var text = Encoding.ASCII.GetString(bytes.Slice(offset, length));
        var nul = text.IndexOf('\0');
        if (nul >= 0)
        {
            text = text[..nul];
        }

        return text.Trim();
    }
}

public sealed record DreamcastMediaInspectionReport(
    string Path,
    string Format,
    int SectorSize,
    ulong SectorCount,
    uint LeadoutFad,
    string LeadoutFadHex,
    IReadOnlyList<DreamcastMediaTrackInfo> Tracks,
    IReadOnlyList<DreamcastCueTrackInspection> CueTracks,
    DreamcastBootSectorInfo? BootSector);

public sealed record DreamcastCueTrackInspection(
    int TrackNumber,
    string Type,
    string FilePath,
    bool IsData);

public sealed record DreamcastBootSectorInfo(
    uint Sector,
    string SectorHex,
    string HardwareId,
    string MakerId,
    string DeviceInfo,
    string AreaSymbols,
    string Peripherals,
    string ProductNumber,
    string Version,
    string ReleaseDate,
    string BootFile,
    string SoftwareMaker,
    string Title)
{
    public static DreamcastBootSectorInfo FromSector(uint sector, ReadOnlySpan<byte> bytes) =>
        new(
            sector,
            $"0x{sector:X8}",
            DreamcastMediaInspector.ReadAscii(bytes, 0x00, 0x10),
            DreamcastMediaInspector.ReadAscii(bytes, 0x10, 0x10),
            DreamcastMediaInspector.ReadAscii(bytes, 0x20, 0x10),
            DreamcastMediaInspector.ReadAscii(bytes, 0x30, 0x08),
            DreamcastMediaInspector.ReadAscii(bytes, 0x38, 0x08),
            DreamcastMediaInspector.ReadAscii(bytes, 0x40, 0x0A),
            DreamcastMediaInspector.ReadAscii(bytes, 0x4A, 0x06),
            DreamcastMediaInspector.ReadAscii(bytes, 0x50, 0x10),
            DreamcastMediaInspector.ReadAscii(bytes, 0x60, 0x10),
            DreamcastMediaInspector.ReadAscii(bytes, 0x70, 0x10),
            DreamcastMediaInspector.ReadAscii(bytes, 0x80, 0x80));
}
