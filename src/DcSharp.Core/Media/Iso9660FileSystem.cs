using System.Buffers.Binary;
using System.Text;

namespace DcSharp.Core.Media;

public sealed class Iso9660FileSystem
{
    private const int SectorSize = 2048;
    private const int PrimaryVolumeDescriptorSector = 16;

    private readonly IDreamcastMediaImage image;
    private readonly Iso9660SectorMapping sectorMapping;
    private readonly Iso9660DirectoryRecord rootDirectory;

    private Iso9660FileSystem(IDreamcastMediaImage image, Iso9660SectorMapping sectorMapping, string volumeIdentifier, Iso9660DirectoryRecord rootDirectory)
    {
        this.image = image;
        this.sectorMapping = sectorMapping;
        this.rootDirectory = rootDirectory;
        VolumeIdentifier = volumeIdentifier;
    }

    public string VolumeIdentifier { get; }

    public IReadOnlyList<Iso9660DirectoryInfo> GetRootDirectories() =>
        ReadDirectory(rootDirectory)
            .Where(entry => entry.IsDirectory)
            .Select(entry => new Iso9660DirectoryInfo(entry.Name, entry.NormalizedName, entry.ExtentSector, entry.DataLength))
            .ToArray();

    public static bool TryOpen(IDreamcastMediaImage image, out Iso9660FileSystem? fileSystem, out string? error)
    {
        fileSystem = null;
        error = null;

        foreach (var mapping in CandidateSectorMappings(image))
        {
            var sector = new byte[SectorSize];
            if (!ReadSector(image, mapping.VolumeStartSector + PrimaryVolumeDescriptorSector, sector))
            {
                continue;
            }

            if (sector[0] != 1 || Encoding.ASCII.GetString(sector, 1, 5) != "CD001")
            {
                continue;
            }

            var volumeIdentifier = ReadAscii(sector, 40, 32);
            var root = Iso9660DirectoryRecord.Read(sector.AsSpan(156), mapping);
            if (root is null || !root.IsDirectory)
            {
                error = "Primary volume descriptor did not contain a valid root directory record.";
                return false;
            }

            fileSystem = new Iso9660FileSystem(image, mapping, volumeIdentifier, root);
            return true;
        }

        error = "Primary ISO9660 volume descriptor not found.";
        return false;
    }

    public bool TryGetFile(string path, out Iso9660FileInfo? file, out string? error)
    {
        file = null;
        error = null;

        var parts = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "ISO9660 file path is empty.";
            return false;
        }

        var directory = rootDirectory;
        for (var index = 0; index < parts.Length; index++)
        {
            var entries = ReadDirectory(directory);
            var record = entries.FirstOrDefault(entry => string.Equals(entry.NormalizedName, NormalizeFileName(parts[index]), StringComparison.OrdinalIgnoreCase));
            if (record is null)
            {
                error = $"ISO9660 file not found: {path}";
                return false;
            }

            if (index < parts.Length - 1)
            {
                if (!record.IsDirectory)
                {
                    error = $"ISO9660 path component is not a directory: {parts[index]}";
                    return false;
                }

                directory = record;
                continue;
            }

            if (record.IsDirectory)
            {
                error = $"ISO9660 path is a directory: {path}";
                return false;
            }

            file = new Iso9660FileInfo(record.Name, record.NormalizedName, record.ExtentSector, record.DataLength);
            return true;
        }

        error = $"ISO9660 file not found: {path}";
        return false;
    }

    public byte[] ReadFile(Iso9660FileInfo file)
    {
        if (file.Length > int.MaxValue)
        {
            throw new InvalidDataException($"ISO9660 file is too large to extract in-memory: {file.Length} bytes.");
        }

        var data = new byte[(int)file.Length];
        var sector = new byte[SectorSize];
        var remaining = data.Length;
        var destinationOffset = 0;
        var sectorIndex = 0u;
        while (remaining > 0)
        {
            if (!ReadSector(image, file.ExtentSector + sectorIndex, sector))
            {
                throw new InvalidDataException($"Failed to read ISO9660 sector 0x{file.ExtentSector + sectorIndex:X8}.");
            }

            var copyLength = Math.Min(remaining, SectorSize);
            sector.AsSpan(0, copyLength).CopyTo(data.AsSpan(destinationOffset, copyLength));
            destinationOffset += copyLength;
            remaining -= copyLength;
            sectorIndex++;
        }

        return data;
    }

    private IReadOnlyList<Iso9660DirectoryRecord> ReadDirectory(Iso9660DirectoryRecord directory)
    {
        var data = new byte[directory.DataLength];
        var sector = new byte[SectorSize];
        var remaining = data.Length;
        var destinationOffset = 0;
        var sectorIndex = 0u;
        while (remaining > 0)
        {
            if (!ReadSector(image, directory.ExtentSector + sectorIndex, sector))
            {
                throw new InvalidDataException($"Failed to read ISO9660 directory sector 0x{directory.ExtentSector + sectorIndex:X8}.");
            }

            var copyLength = Math.Min(remaining, SectorSize);
            sector.AsSpan(0, copyLength).CopyTo(data.AsSpan(destinationOffset, copyLength));
            destinationOffset += copyLength;
            remaining -= copyLength;
            sectorIndex++;
        }

        var entries = new List<Iso9660DirectoryRecord>();
        var offset = 0;
        while (offset < data.Length)
        {
            var length = data[offset];
            if (length == 0)
            {
                offset = ((offset / SectorSize) + 1) * SectorSize;
                continue;
            }

            if (offset + length > data.Length)
            {
                break;
            }

            var record = Iso9660DirectoryRecord.Read(data.AsSpan(offset, length), sectorMapping);
            if (record is not null && record.Name is not "\0" and not "\u0001")
            {
                entries.Add(record);
            }

            offset += length;
        }

        return entries;
    }

    private static IReadOnlyList<Iso9660SectorMapping> CandidateSectorMappings(IDreamcastMediaImage image)
    {
        var mappings = new List<Iso9660SectorMapping>();
        var firstTrackStart = image.Tracks.FirstOrDefault()?.StartFad ?? 0;
        mappings.Add(new Iso9660SectorMapping(0, firstTrackStart));
        foreach (var track in image.Tracks)
        {
            mappings.Add(new Iso9660SectorMapping(track.StartFad, track.StartFad));
        }

        return mappings
            .Distinct()
            .OrderByDescending(mapping => mapping.VolumeStartSector)
            .ThenByDescending(mapping => mapping.ExtentBias)
            .ToArray();
    }

    private static bool ReadSector(IDreamcastMediaImage image, uint sector, Span<byte> destination) =>
        destination.Length >= SectorSize && image.TryReadSector(sector, destination, out var bytesRead) && bytesRead >= SectorSize;

    private static string ReadAscii(ReadOnlySpan<byte> data, int offset, int length) =>
        Encoding.ASCII.GetString(data.Slice(offset, length)).TrimEnd(' ', '\0');

    private static string NormalizeFileName(string name)
    {
        var versionIndex = name.IndexOf(';', StringComparison.Ordinal);
        if (versionIndex >= 0)
        {
            name = name[..versionIndex];
        }

        return name.TrimEnd('.');
    }

    private sealed record Iso9660DirectoryRecord(
        string Name,
        string NormalizedName,
        uint ExtentSector,
        uint DataLength,
        bool IsDirectory)
    {
        public static Iso9660DirectoryRecord? Read(ReadOnlySpan<byte> data, Iso9660SectorMapping sectorMapping)
        {
            if (data.Length < 34)
            {
                return null;
            }

            var length = data[0];
            if (length == 0 || length > data.Length)
            {
                return null;
            }

            var extent = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(2, 4));
            var dataLength = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(10, 4));
            var flags = data[25];
            var nameLength = data[32];
            if (33 + nameLength > length)
            {
                return null;
            }

            var name = nameLength == 1 && data[33] == 0
                ? "\0"
                : nameLength == 1 && data[33] == 1
                    ? "\u0001"
                    : Encoding.ASCII.GetString(data.Slice(33, nameLength));
            return new Iso9660DirectoryRecord(
                name,
                NormalizeFileName(name),
                sectorMapping.MapExtent(extent),
                dataLength,
                (flags & 0x02) != 0);
        }
    }
}

internal sealed record Iso9660SectorMapping(uint VolumeStartSector, uint ExtentBias)
{
    public uint MapExtent(uint extent) =>
        extent >= ExtentBias
            ? VolumeStartSector + (extent - ExtentBias)
            : VolumeStartSector + extent;
}

public sealed record Iso9660FileInfo(
    string Name,
    string NormalizedName,
    uint ExtentSector,
    uint Length);

public sealed record Iso9660DirectoryInfo(
    string Name,
    string NormalizedName,
    uint ExtentSector,
    uint Length);
