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
