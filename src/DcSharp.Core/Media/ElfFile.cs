using System.Buffers.Binary;

namespace DcSharp.Core.Media;

public sealed record ElfFile(
    ElfEndianness Endianness,
    ElfMachine Machine,
    uint EntryPoint,
    ushort ProgramHeaderCount,
    ushort SectionHeaderCount,
    IReadOnlyList<ElfProgramHeader> ProgramHeaders,
    IReadOnlyList<ElfSectionHeader> SectionHeaders,
    IReadOnlyList<ElfSymbol> Symbols)
{
    private const byte ElfClass32 = 1;
    private const byte ElfDataLittleEndian = 1;
    private const byte ElfDataBigEndian = 2;
    private const ushort MachineSuperH = 42;

    public bool IsDreamcastCandidate =>
        Endianness == ElfEndianness.LittleEndian && Machine == ElfMachine.SuperH;

    public static ElfFile Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        Span<byte> header = stackalloc byte[52];
        ReadExactly(stream, header);

        if (header[0] != 0x7F || header[1] != (byte)'E' || header[2] != (byte)'L' || header[3] != (byte)'F')
        {
            throw new InvalidDataException("Input is not an ELF file.");
        }

        if (header[4] != ElfClass32)
        {
            throw new InvalidDataException("Only ELF32 files are supported by the initial Dreamcast inspector.");
        }

        var endianness = header[5] switch
        {
            ElfDataLittleEndian => ElfEndianness.LittleEndian,
            ElfDataBigEndian => ElfEndianness.BigEndian,
            _ => throw new InvalidDataException($"Unknown ELF data encoding {header[5]}.")
        };

        var littleEndian = endianness == ElfEndianness.LittleEndian;

        var machine = ReadUInt16(header, 18, littleEndian) switch
        {
            MachineSuperH => ElfMachine.SuperH,
            var value => ElfMachine.Unknown(value)
        };

        var programHeaderOffset = ReadUInt32(header, 28, littleEndian);
        var sectionHeaderOffset = ReadUInt32(header, 32, littleEndian);
        var programHeaderEntrySize = ReadUInt16(header, 42, littleEndian);
        var programHeaderCount = ReadUInt16(header, 44, littleEndian);
        var sectionHeaderEntrySize = ReadUInt16(header, 46, littleEndian);
        var sectionHeaderCount = ReadUInt16(header, 48, littleEndian);
        var sectionNameStringTableIndex = ReadUInt16(header, 50, littleEndian);
        var programHeaders = ReadProgramHeaders(stream, programHeaderOffset, programHeaderEntrySize, programHeaderCount, littleEndian);
        var sectionHeaders = ReadSectionHeaders(stream, sectionHeaderOffset, sectionHeaderEntrySize, sectionHeaderCount, sectionNameStringTableIndex, littleEndian);
        var symbols = ReadSymbols(stream, sectionHeaders, littleEndian);

        return new ElfFile(
            endianness,
            machine,
            ReadUInt32(header, 24, littleEndian),
            programHeaderCount,
            sectionHeaderCount,
            programHeaders,
            sectionHeaders,
            symbols);
    }

    private static IReadOnlyList<ElfProgramHeader> ReadProgramHeaders(
        Stream stream,
        uint offset,
        ushort entrySize,
        ushort count,
        bool littleEndian)
    {
        if (count == 0)
        {
            return Array.Empty<ElfProgramHeader>();
        }

        if (!stream.CanSeek)
        {
            throw new InvalidDataException("ELF program headers require a seekable stream.");
        }

        if (entrySize < 32)
        {
            throw new InvalidDataException($"ELF program header entry size {entrySize} is smaller than ELF32 requires.");
        }

        var headers = new List<ElfProgramHeader>(count);
        var buffer = new byte[entrySize];

        for (var index = 0; index < count; index++)
        {
            stream.Position = checked(offset + (uint)(index * entrySize));
            ReadExactly(stream, buffer);

            var type = ReadUInt32(buffer, 0, littleEndian);
            var fileOffset = ReadUInt32(buffer, 4, littleEndian);
            var virtualAddress = ReadUInt32(buffer, 8, littleEndian);
            var physicalAddress = ReadUInt32(buffer, 12, littleEndian);
            var fileSize = ReadUInt32(buffer, 16, littleEndian);
            var memorySize = ReadUInt32(buffer, 20, littleEndian);
            var flags = ReadUInt32(buffer, 24, littleEndian);
            var alignment = ReadUInt32(buffer, 28, littleEndian);

            if (fileSize > memorySize)
            {
                throw new InvalidDataException($"ELF program header {index} has file size larger than memory size.");
            }

            var data = ReadSegmentData(stream, fileOffset, fileSize);

            headers.Add(new ElfProgramHeader(
                index,
                type,
                fileOffset,
                virtualAddress,
                physicalAddress,
                fileSize,
                memorySize,
                flags,
                alignment,
                data));
        }

        return headers;
    }

    private static byte[] ReadSegmentData(Stream stream, uint offset, uint size)
    {
        if (size == 0)
        {
            return [];
        }

        if (size > int.MaxValue)
        {
            throw new InvalidDataException($"ELF segment is too large to load into memory: {size} bytes.");
        }

        var data = new byte[size];
        stream.Position = offset;
        ReadExactly(stream, data);

        return data;
    }

    private static IReadOnlyList<ElfSectionHeader> ReadSectionHeaders(
        Stream stream,
        uint offset,
        ushort entrySize,
        ushort count,
        ushort sectionNameStringTableIndex,
        bool littleEndian)
    {
        if (count == 0 || offset == 0)
        {
            return Array.Empty<ElfSectionHeader>();
        }

        if (!stream.CanSeek)
        {
            throw new InvalidDataException("ELF section headers require a seekable stream.");
        }

        if (entrySize < 40)
        {
            throw new InvalidDataException($"ELF section header entry size {entrySize} is smaller than ELF32 requires.");
        }

        var rawHeaders = new List<RawSectionHeader>(count);
        var buffer = new byte[entrySize];
        for (var index = 0; index < count; index++)
        {
            stream.Position = checked(offset + (uint)(index * entrySize));
            ReadExactly(stream, buffer);

            rawHeaders.Add(new RawSectionHeader(
                index,
                ReadUInt32(buffer, 0, littleEndian),
                ReadUInt32(buffer, 4, littleEndian),
                ReadUInt32(buffer, 8, littleEndian),
                ReadUInt32(buffer, 12, littleEndian),
                ReadUInt32(buffer, 16, littleEndian),
                ReadUInt32(buffer, 20, littleEndian),
                ReadUInt32(buffer, 24, littleEndian),
                ReadUInt32(buffer, 28, littleEndian),
                ReadUInt32(buffer, 32, littleEndian),
                ReadUInt32(buffer, 36, littleEndian)));
        }

        var sectionNameBytes = sectionNameStringTableIndex < rawHeaders.Count
            ? ReadSectionData(stream, rawHeaders[sectionNameStringTableIndex])
            : [];

        return rawHeaders.Select(header => new ElfSectionHeader(
            header.Index,
            ReadString(sectionNameBytes, header.NameOffset),
            header.Type,
            header.Flags,
            header.Address,
            header.FileOffset,
            header.Size,
            header.Link,
            header.Info,
            header.AddressAlignment,
            header.EntrySize)).ToArray();
    }

    private static IReadOnlyList<ElfSymbol> ReadSymbols(Stream stream, IReadOnlyList<ElfSectionHeader> sections, bool littleEndian)
    {
        if (sections.Count == 0)
        {
            return Array.Empty<ElfSymbol>();
        }

        var symbols = new List<ElfSymbol>();
        foreach (var section in sections.Where(section => section.Type is 2 or 11))
        {
            if (section.EntrySize < 16 || section.Link >= sections.Count)
            {
                continue;
            }

            var symbolBytes = ReadSectionData(stream, section);
            var stringBytes = ReadSectionData(stream, sections[(int)section.Link]);
            for (var offset = 0; offset + 16 <= symbolBytes.Length; offset += (int)section.EntrySize)
            {
                var nameOffset = ReadUInt32(symbolBytes, offset, littleEndian);
                var value = ReadUInt32(symbolBytes, offset + 4, littleEndian);
                var size = ReadUInt32(symbolBytes, offset + 8, littleEndian);
                var info = symbolBytes[offset + 12];
                var other = symbolBytes[offset + 13];
                var sectionIndex = ReadUInt16(symbolBytes, offset + 14, littleEndian);
                var name = ReadString(stringBytes, nameOffset);
                if (string.IsNullOrWhiteSpace(name) || value == 0)
                {
                    continue;
                }

                symbols.Add(new ElfSymbol(name, value, size, info, other, sectionIndex));
            }
        }

        return symbols
            .OrderBy(symbol => symbol.Value)
            .ThenBy(symbol => symbol.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static byte[] ReadSectionData(Stream stream, ElfSectionHeader section) =>
        ReadSectionData(stream, section.FileOffset, section.Size);

    private static byte[] ReadSectionData(Stream stream, RawSectionHeader section) =>
        ReadSectionData(stream, section.FileOffset, section.Size);

    private static byte[] ReadSectionData(Stream stream, uint offset, uint size)
    {
        if (size == 0)
        {
            return [];
        }

        if (size > int.MaxValue)
        {
            throw new InvalidDataException($"ELF section is too large to load into memory: {size} bytes.");
        }

        var data = new byte[size];
        stream.Position = offset;
        ReadExactly(stream, data);
        return data;
    }

    private static string ReadString(byte[] bytes, uint offset)
    {
        if (offset >= bytes.Length)
        {
            return string.Empty;
        }

        var length = 0;
        while (offset + length < bytes.Length && bytes[offset + length] != 0)
        {
            length++;
        }

        return length == 0 ? string.Empty : System.Text.Encoding.UTF8.GetString(bytes, (int)offset, length);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset, bool littleEndian) =>
        littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..])
            : BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset, bool littleEndian) =>
        littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..])
            : BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            var read = stream.Read(buffer);
            if (read == 0)
            {
                throw new InvalidDataException("Input ended before the ELF header was complete.");
            }

            buffer = buffer[read..];
        }
    }
}

internal sealed record RawSectionHeader(
    int Index,
    uint NameOffset,
    uint Type,
    uint Flags,
    uint Address,
    uint FileOffset,
    uint Size,
    uint Link,
    uint Info,
    uint AddressAlignment,
    uint EntrySize);

public sealed record ElfProgramHeader(
    int Index,
    uint Type,
    uint FileOffset,
    uint VirtualAddress,
    uint PhysicalAddress,
    uint FileSize,
    uint MemorySize,
    uint Flags,
    uint Alignment,
    byte[] Data)
{
    public bool IsLoadable => Type == 1;
}

public sealed record ElfSectionHeader(
    int Index,
    string Name,
    uint Type,
    uint Flags,
    uint Address,
    uint FileOffset,
    uint Size,
    uint Link,
    uint Info,
    uint AddressAlignment,
    uint EntrySize);

public sealed record ElfSymbol(
    string Name,
    uint Value,
    uint Size,
    byte Info,
    byte Other,
    ushort SectionIndex)
{
    public byte Binding => (byte)(Info >> 4);
    public byte Type => (byte)(Info & 0x0F);
    public bool IsFunction => Type == 2;

    public bool Contains(uint address) =>
        Size == 0 ? address == Value : address >= Value && (ulong)address < (ulong)Value + Size;
}

public enum ElfEndianness
{
    LittleEndian,
    BigEndian
}

public readonly record struct ElfMachine(ushort Value, string Name)
{
    public static ElfMachine SuperH { get; } = new(42, "Renesas SuperH");

    public static ElfMachine Unknown(ushort value) => new(value, $"Unknown ({value})");

    public override string ToString() => Name;
}
