using System.Text.RegularExpressions;

namespace DcSharp.Core.Media;

internal sealed class AdjacentTrackSetMediaImage(IReadOnlyList<AdjacentTrackSetTrack> tracks) : IDreamcastMediaImage
{
    private const uint DefaultDataTrackStartFad = 45_000;

    public int SectorSize => DreamcastMediaImageLoader.DefaultSectorSize;

    public ulong SectorCount => tracks.Count == 0
        ? 0
        : tracks.Max(track => (ulong)track.StartFad + track.SectorCount);

    public uint LeadoutFad => (uint)Math.Min(SectorCount, uint.MaxValue);

    public IReadOnlyList<DreamcastMediaTrackInfo> Tracks => tracks
        .Select(track => DreamcastMediaTrackInfo.Data(track.TrackNumber, track.StartFad, track.SectorCount))
        .ToArray();

    public static bool TryCreate(string sourceTrackPath, out IDreamcastMediaImage? image, out string? error)
    {
        image = null;
        error = null;

        var fullPath = Path.GetFullPath(sourceTrackPath);
        var directory = Path.GetDirectoryName(fullPath);
        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            error = $"Unable to resolve track directory for {sourceTrackPath}.";
            return false;
        }

        var match = Regex.Match(fileName, "^(?<prefix>.+)\\s+\\(Track (?<track>\\d+)\\)\\.bin$", RegexOptions.IgnoreCase);
        if (!match.Success || !int.TryParse(match.Groups["track"].Value, out var sourceTrackNumber) || sourceTrackNumber < 3)
        {
            return false;
        }

        var prefix = match.Groups["prefix"].Value;
        var trackFiles = Directory.EnumerateFiles(directory, "*.bin")
            .Select(path => (Path: path, Match: Regex.Match(Path.GetFileName(path), $"^{Regex.Escape(prefix)}\\s+\\(Track (?<track>\\d+)\\)\\.bin$", RegexOptions.IgnoreCase)))
            .Where(entry => entry.Match.Success && int.TryParse(entry.Match.Groups["track"].Value, out var trackNumber) && trackNumber >= sourceTrackNumber)
            .Select(entry => new
            {
                entry.Path,
                TrackNumber = int.Parse(entry.Match.Groups["track"].Value, System.Globalization.CultureInfo.InvariantCulture)
            })
            .OrderBy(entry => entry.TrackNumber)
            .ToArray();

        if (trackFiles.Length < 2)
        {
            error = "Need at least two adjacent track files.";
            return false;
        }

        var tracks = new List<AdjacentTrackSetTrack>();
        var startFad = DefaultDataTrackStartFad;
        foreach (var trackFile in trackFiles)
        {
            if (!AdjacentTrackSetTrack.TryCreate(trackFile.TrackNumber, startFad, trackFile.Path, out var track, out error) || track is null)
            {
                image = null;
                return false;
            }

            tracks.Add(track);
            startFad += (uint)Math.Min(track.SectorCount, uint.MaxValue - startFad);
        }

        image = new AdjacentTrackSetMediaImage(tracks);
        return true;
    }

    public bool TryReadSector(uint sector, Span<byte> destination, out int bytesRead)
    {
        bytesRead = 0;
        if (destination.Length < DreamcastMediaImageLoader.DefaultSectorSize)
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

internal sealed class AdjacentTrackSetTrack
{
    private const int RawCdSectorSize = 2352;
    private const int RawCdSectorPayloadOffset = 16;

    private readonly string path;
    private readonly int sourceSectorSize;
    private readonly int payloadOffset;

    private AdjacentTrackSetTrack(int trackNumber, uint startFad, string path, int sourceSectorSize, int payloadOffset, ulong sectorCount)
    {
        TrackNumber = trackNumber;
        StartFad = startFad;
        this.path = path;
        this.sourceSectorSize = sourceSectorSize;
        this.payloadOffset = payloadOffset;
        SectorCount = sectorCount;
    }

    public int TrackNumber { get; }

    public uint StartFad { get; }

    public ulong SectorCount { get; }

    public static bool TryCreate(int trackNumber, uint startFad, string path, out AdjacentTrackSetTrack? track, out string? error)
    {
        track = null;
        error = null;

        var length = new FileInfo(path).Length;
        if (length >= RawCdSectorSize && length % RawCdSectorSize == 0)
        {
            track = new AdjacentTrackSetTrack(trackNumber, startFad, path, RawCdSectorSize, RawCdSectorPayloadOffset, (ulong)length / RawCdSectorSize);
            return true;
        }

        if (length >= DreamcastMediaImageLoader.DefaultSectorSize && length % DreamcastMediaImageLoader.DefaultSectorSize == 0)
        {
            track = new AdjacentTrackSetTrack(trackNumber, startFad, path, DreamcastMediaImageLoader.DefaultSectorSize, 0, (ulong)length / DreamcastMediaImageLoader.DefaultSectorSize);
            return true;
        }

        error = $"Track file size is not aligned to 2048-byte or 2352-byte sectors: {path}";
        return false;
    }

    public bool Contains(uint sector) =>
        sector >= StartFad && (ulong)sector < (ulong)StartFad + SectorCount;

    public bool TryReadSector(uint sector, Span<byte> destination, out int bytesRead)
    {
        bytesRead = 0;
        if (!Contains(sector) || destination.Length < DreamcastMediaImageLoader.DefaultSectorSize)
        {
            return false;
        }

        var relativeSector = sector - StartFad;
        var offset = ((long)relativeSector * sourceSectorSize) + payloadOffset;
        using var stream = File.OpenRead(path);
        if (offset < 0 || offset + DreamcastMediaImageLoader.DefaultSectorSize > stream.Length)
        {
            return false;
        }

        stream.Position = offset;
        bytesRead = stream.Read(destination[..DreamcastMediaImageLoader.DefaultSectorSize]);
        return bytesRead >= DreamcastMediaImageLoader.DefaultSectorSize;
    }
}
