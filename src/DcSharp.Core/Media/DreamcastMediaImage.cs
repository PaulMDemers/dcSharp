using System.Text;
using System.Text.RegularExpressions;

namespace DcSharp.Core.Media;

public interface IDreamcastMediaImage
{
    int SectorSize { get; }
    ulong SectorCount { get; }
    bool TryReadSector(uint sector, Span<byte> destination, out int bytesRead);
}

public static class DreamcastMediaImageLoader
{
    public const int DefaultSectorSize = 2048;
    private const int RawCdSectorSize = 2352;
    private const int RawCdSectorPayloadOffset = 16;

    public static IDreamcastMediaImage LoadFromFile(string path, int sectorSize = DefaultSectorSize)
    {
        if (TryLoadFromFile(path, sectorSize, out var image, out var error))
        {
            return image ?? throw new InvalidOperationException("Media loading did not return an image.");
        }

        throw new InvalidDataException(error ?? "Failed to load media image.");
    }

    public static bool TryLoadFromFile(string path, out IDreamcastMediaImage? image, out string? error) =>
        TryLoadFromFile(path, DefaultSectorSize, out image, out error);

    public static bool TryLoadFromFile(string path, int sectorSize, out IDreamcastMediaImage? image, out string? error)
    {
        image = null;
        error = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Media path is empty.";
            return false;
        }

        if (sectorSize <= 0)
        {
            error = "Sector size must be positive.";
            return false;
        }

        if (!File.Exists(path))
        {
            error = $"Media file not found: {path}";
            return false;
        }

        if (string.Equals(Path.GetExtension(path), ".cue", StringComparison.OrdinalIgnoreCase))
        {
            return TryLoadFromCue(path, sectorSize, out image, out error);
        }

        if (string.Equals(Path.GetExtension(path), ".gdi", StringComparison.OrdinalIgnoreCase))
        {
            return TryLoadFromGdi(path, out image, out error);
        }

        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            error = $"Failed to read media file '{path}': {ex.Message}";
            return false;
        }

        if (data.Length == 0)
        {
            error = "Media file is empty.";
            return false;
        }

        if (data.Length % sectorSize != 0)
        {
            if (sectorSize == DefaultSectorSize && data.Length % RawCdSectorSize == 0)
            {
                image = new RawSectorFromCdImage(data);
                return true;
            }

            error = $"Media file size {data.Length} is not aligned to {sectorSize}-byte sectors.";
            return false;
        }

        image = new RawSectorMediaImage(data, sectorSize);
        return true;
    }

    private static bool TryLoadFromCue(string cuePath, int sectorSize, out IDreamcastMediaImage? image, out string? error)
    {
        image = null;
        error = null;

        var directory = Path.GetDirectoryName(cuePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            error = $"Unable to resolve cue directory for {cuePath}";
            return false;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(cuePath);
        }
        catch (Exception ex)
        {
            error = $"Failed to read cue sheet '{cuePath}': {ex.Message}";
            return false;
        }

        string? activeFile = null;
        string? selectedMediaPath = null;
        var fileRegex = new Regex("^FILE\\s+\"(?<path>[^\"]+)\"\\s+(?<type>\\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var trackRegex = new Regex("^TRACK\\s+\\d+\\s+(?<type>\\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var cueDir = Path.GetFullPath(directory);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("REM", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileMatch = fileRegex.Match(trimmed);
            if (fileMatch.Success)
            {
                activeFile = Path.Combine(cueDir, fileMatch.Groups["path"].Value);
                continue;
            }

            var trackMatch = trackRegex.Match(trimmed);
            if (!trackMatch.Success || activeFile is null)
            {
                continue;
            }

            var trackType = trackMatch.Groups["type"].Value;
            if (trackType.StartsWith("MODE1", StringComparison.OrdinalIgnoreCase) ||
                trackType.StartsWith("MODE2", StringComparison.OrdinalIgnoreCase))
            {
                selectedMediaPath = activeFile;
            }
        }

        if (selectedMediaPath is null)
        {
            error = $"No mode1/mode2 data track found in cue sheet '{cuePath}'.";
            return false;
        }

        return TryLoadFromFile(selectedMediaPath, sectorSize, out image, out error);
    }

    private static bool TryLoadFromGdi(string gdiPath, out IDreamcastMediaImage? image, out string? error)
    {
        image = null;
        error = null;

        var directory = Path.GetDirectoryName(gdiPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            error = $"Unable to resolve GDI directory for {gdiPath}";
            return false;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(gdiPath);
        }
        catch (Exception ex)
        {
            error = $"Failed to read GDI descriptor '{gdiPath}': {ex.Message}";
            return false;
        }

        if (lines.Length == 0 || !int.TryParse(lines[0].Trim(), out var declaredTrackCount) || declaredTrackCount <= 0)
        {
            error = $"Invalid GDI descriptor '{gdiPath}': first line must be a positive track count.";
            return false;
        }

        var gdiDir = Path.GetFullPath(directory);
        var tracks = new List<GdiMediaTrack>();
        foreach (var line in lines.Skip(1))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var tokens = SplitDescriptorLine(trimmed);
            if (tokens.Count < 6)
            {
                error = $"Invalid GDI track line in '{gdiPath}': {trimmed}";
                return false;
            }

            if (!uint.TryParse(tokens[1], out var startLba)
                || !int.TryParse(tokens[2], out var flags)
                || !int.TryParse(tokens[3], out var sourceSectorSize)
                || !long.TryParse(tokens[5], out var fileOffset))
            {
                error = $"Invalid GDI numeric field in '{gdiPath}': {trimmed}";
                return false;
            }

            if ((flags & 0x4) == 0)
            {
                continue;
            }

            if (sourceSectorSize is not DefaultSectorSize and not RawCdSectorSize)
            {
                error = $"Unsupported GDI data track sector size {sourceSectorSize} in '{gdiPath}'.";
                return false;
            }

            var trackPath = Path.Combine(gdiDir, tokens[4]);
            if (!File.Exists(trackPath))
            {
                error = $"GDI track file not found: {trackPath}";
                return false;
            }

            byte[] data;
            try
            {
                data = File.ReadAllBytes(trackPath);
            }
            catch (Exception ex)
            {
                error = $"Failed to read GDI track file '{trackPath}': {ex.Message}";
                return false;
            }

            if (fileOffset < 0 || fileOffset > data.Length)
            {
                error = $"Invalid GDI file offset {fileOffset} for '{trackPath}'.";
                return false;
            }

            tracks.Add(new GdiMediaTrack(startLba, sourceSectorSize, fileOffset, data));
        }

        if (tracks.Count == 0)
        {
            error = $"No data tracks found in GDI descriptor '{gdiPath}'.";
            return false;
        }

        image = new GdiMediaImage(tracks);
        return true;
    }

    private static IReadOnlyList<string> SplitDescriptorLine(string line)
    {
        var matches = Regex.Matches(line, "\"(?<quoted>[^\"]+)\"|(?<bare>\\S+)");
        return matches
            .Select(match => match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["bare"].Value)
            .ToArray();
    }
}

public sealed class RawSectorMediaImage : IDreamcastMediaImage
{
    private readonly byte[] data;
    private readonly int sectorSize;

    public RawSectorMediaImage(byte[] data, int sectorSize)
    {
        this.data = data;
        this.sectorSize = sectorSize;
    }

    public int SectorSize => sectorSize;
    public ulong SectorCount => (ulong)data.Length / (uint)sectorSize;

    public bool TryReadSector(uint sector, Span<byte> destination, out int bytesRead)
    {
        bytesRead = 0;
        if (destination.Length < sectorSize)
        {
            return false;
        }

        var offset = (long)sector * sectorSize;
        if (offset < 0 || offset + sectorSize > data.Length)
        {
            return false;
        }

        data.AsSpan((int)offset, sectorSize).CopyTo(destination);
        bytesRead = sectorSize;
        return true;
    }
}

public sealed class RawSectorFromCdImage(byte[] data) : IDreamcastMediaImage
{
    private const int UserDataBytes = 2048;
    private const int CdSectorSize = 2352;
    private const int CdSectorPayloadOffset = 16;

    public int SectorSize => UserDataBytes;
    public ulong SectorCount => (ulong)data.Length / CdSectorSize;

    public bool TryReadSector(uint sector, Span<byte> destination, out int bytesRead)
    {
        bytesRead = 0;
        if (destination.Length < UserDataBytes)
        {
            return false;
        }

        var sourceOffset = ((long)sector * CdSectorSize) + CdSectorPayloadOffset;
        if (sourceOffset < 0 || sourceOffset + UserDataBytes > data.Length)
        {
            return false;
        }

        data.AsSpan((int)sourceOffset, UserDataBytes).CopyTo(destination);
        bytesRead = UserDataBytes;
        return true;
    }
}

public sealed class GdiMediaImage : IDreamcastMediaImage
{
    private readonly IReadOnlyList<GdiMediaTrack> tracks;

    public GdiMediaImage(IReadOnlyList<GdiMediaTrack> tracks)
    {
        if (tracks.Count == 0)
        {
            throw new ArgumentException("GDI media requires at least one data track.", nameof(tracks));
        }

        this.tracks = tracks.OrderBy(track => track.StartLba).ToArray();
    }

    public int SectorSize => DreamcastMediaImageLoader.DefaultSectorSize;

    public ulong SectorCount => tracks.Max(track => (ulong)track.StartLba + track.SectorCount);

    public bool TryReadSector(uint sector, Span<byte> destination, out int bytesRead)
    {
        bytesRead = 0;
        if (destination.Length < SectorSize)
        {
            return false;
        }

        var track = tracks.FirstOrDefault(candidate => candidate.Contains(sector));
        if (track is null)
        {
            return false;
        }

        return track.TryReadSector(sector, destination, out bytesRead);
    }
}

public sealed class GdiMediaTrack
{
    private readonly byte[] data;
    private readonly long fileOffset;
    private readonly int payloadOffset;

    public GdiMediaTrack(uint startLba, int sourceSectorSize, long fileOffset, byte[] data)
    {
        if (sourceSectorSize is not DreamcastMediaImageLoader.DefaultSectorSize and not 2352)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceSectorSize), sourceSectorSize, "Unsupported GDI sector size.");
        }

        if (fileOffset < 0 || fileOffset > data.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(fileOffset), fileOffset, "File offset must point inside the track data.");
        }

        StartLba = startLba;
        SourceSectorSize = sourceSectorSize;
        this.fileOffset = fileOffset;
        this.data = data;
        payloadOffset = sourceSectorSize == 2352 ? 16 : 0;
    }

    public uint StartLba { get; }

    public int SourceSectorSize { get; }

    public ulong SectorCount => (ulong)Math.Max(0, data.Length - fileOffset) / (uint)SourceSectorSize;

    public bool Contains(uint sector) =>
        sector >= StartLba && (ulong)sector < (ulong)StartLba + SectorCount;

    public bool TryReadSector(uint sector, Span<byte> destination, out int bytesRead)
    {
        bytesRead = 0;
        if (!Contains(sector) || destination.Length < DreamcastMediaImageLoader.DefaultSectorSize)
        {
            return false;
        }

        var relativeSector = sector - StartLba;
        var sourceOffset = fileOffset + ((long)relativeSector * SourceSectorSize) + payloadOffset;
        if (sourceOffset < 0 || sourceOffset + DreamcastMediaImageLoader.DefaultSectorSize > data.Length)
        {
            return false;
        }

        data.AsSpan((int)sourceOffset, DreamcastMediaImageLoader.DefaultSectorSize).CopyTo(destination);
        bytesRead = DreamcastMediaImageLoader.DefaultSectorSize;
        return true;
    }
}
