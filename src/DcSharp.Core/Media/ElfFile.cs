using System.Buffers.Binary;

namespace DcSharp.Core.Media;

public sealed record ElfFile(
    ElfEndianness Endianness,
    ElfMachine Machine,
    uint EntryPoint,
    ushort ProgramHeaderCount,
    ushort SectionHeaderCount,
    IReadOnlyList<ElfProgramHeader> ProgramHeaders)
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
        var programHeaderEntrySize = ReadUInt16(header, 42, littleEndian);
        var programHeaderCount = ReadUInt16(header, 44, littleEndian);
        var programHeaders = ReadProgramHeaders(stream, programHeaderOffset, programHeaderEntrySize, programHeaderCount, littleEndian);

        return new ElfFile(
            endianness,
            machine,
            ReadUInt32(header, 24, littleEndian),
            programHeaderCount,
            ReadUInt16(header, 48, littleEndian),
            programHeaders);
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
