using DcSharp.Core.Media;

namespace DcSharp.Tests;

public class ElfFileTests
{
    [Fact]
    public void ReadsLittleEndianSuperHElfHeader()
    {
        var bytes = CreateElfHeader(machine: 42, entryPoint: 0x8C01_0000, programHeaderCount: 0);

        var elf = ElfFile.Read(new MemoryStream(bytes));

        Assert.Equal(ElfEndianness.LittleEndian, elf.Endianness);
        Assert.Equal(ElfMachine.SuperH, elf.Machine);
        Assert.Equal(0x8C01_0000u, elf.EntryPoint);
        Assert.Empty(elf.ProgramHeaders);
        Assert.True(elf.IsDreamcastCandidate);
    }

    [Fact]
    public void ReadsLoadableProgramHeaderAndSegmentBytes()
    {
        var bytes = CreateElfWithLoadSegment();

        var elf = ElfFile.Read(new MemoryStream(bytes));

        var segment = Assert.Single(elf.ProgramHeaders);
        Assert.True(segment.IsLoadable);
        Assert.Equal(0x8C01_0000u, segment.VirtualAddress);
        Assert.Equal(4u, segment.FileSize);
        Assert.Equal(8u, segment.MemorySize);
        Assert.Equal([0x11, 0x22, 0x33, 0x44], segment.Data);
    }

    [Fact]
    public void ReadsFunctionSymbolsFromSymbolTable()
    {
        var bytes = CreateElfWithLoadSegmentAndSymbol();

        var elf = ElfFile.Read(new MemoryStream(bytes));

        var symbol = Assert.Single(elf.Symbols);
        Assert.Equal("main", symbol.Name);
        Assert.Equal(0x8C01_0000u, symbol.Value);
        Assert.Equal(4u, symbol.Size);
        Assert.True(symbol.IsFunction);
        Assert.Contains(elf.SectionHeaders, section => section.Name == ".symtab");
    }

    [Fact]
    public void RejectsNonElfInput()
    {
        var ex = Assert.Throws<InvalidDataException>(() => ElfFile.Read(new MemoryStream(new byte[52])));

        Assert.Contains("not an ELF", ex.Message);
    }

    private static byte[] CreateElfHeader(ushort machine, uint entryPoint, ushort programHeaderCount)
    {
        var bytes = new byte[52];
        bytes[0] = 0x7F;
        bytes[1] = (byte)'E';
        bytes[2] = (byte)'L';
        bytes[3] = (byte)'F';
        bytes[4] = 1;
        bytes[5] = 1;
        bytes[6] = 1;

        WriteUInt16(bytes, 16, 2);
        WriteUInt16(bytes, 18, machine);
        WriteUInt32(bytes, 20, 1);
        WriteUInt32(bytes, 24, entryPoint);
        WriteUInt32(bytes, 28, 52);
        WriteUInt16(bytes, 40, 52);
        WriteUInt16(bytes, 42, 32);
        WriteUInt16(bytes, 44, programHeaderCount);
        WriteUInt16(bytes, 46, 40);
        WriteUInt16(bytes, 48, 3);

        return bytes;
    }

    private static byte[] CreateElfWithLoadSegment()
    {
        var bytes = new byte[88];
        CreateElfHeader(machine: 42, entryPoint: 0x8C01_0000, programHeaderCount: 1).CopyTo(bytes, 0);

        WriteUInt32(bytes, 52, 1);
        WriteUInt32(bytes, 56, 84);
        WriteUInt32(bytes, 60, 0x8C01_0000);
        WriteUInt32(bytes, 64, 0x0C01_0000);
        WriteUInt32(bytes, 68, 4);
        WriteUInt32(bytes, 72, 8);
        WriteUInt32(bytes, 76, 5);
        WriteUInt32(bytes, 80, 32);

        bytes[84] = 0x11;
        bytes[85] = 0x22;
        bytes[86] = 0x33;
        bytes[87] = 0x44;

        return bytes;
    }

    private static byte[] CreateElfWithLoadSegmentAndSymbol()
    {
        var segmentBytes = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        var strtab = new byte[] { 0, (byte)'m', (byte)'a', (byte)'i', (byte)'n', 0 };
        var shstrtab = new byte[]
        {
            0,
            (byte)'.', (byte)'t', (byte)'e', (byte)'x', (byte)'t', 0,
            (byte)'.', (byte)'s', (byte)'y', (byte)'m', (byte)'t', (byte)'a', (byte)'b', 0,
            (byte)'.', (byte)'s', (byte)'t', (byte)'r', (byte)'t', (byte)'a', (byte)'b', 0,
            (byte)'.', (byte)'s', (byte)'h', (byte)'s', (byte)'t', (byte)'r', (byte)'t', (byte)'a', (byte)'b', 0
        };

        const int textOffset = 84;
        var symtabOffset = textOffset + segmentBytes.Length;
        var strtabOffset = symtabOffset + 32;
        var shstrtabOffset = strtabOffset + 6;
        var sectionHeaderOffset = shstrtabOffset + 33;
        var bytes = new byte[sectionHeaderOffset + (5 * 40)];
        CreateElfHeader(machine: 42, entryPoint: 0x8C01_0000, programHeaderCount: 1).CopyTo(bytes, 0);

        WriteUInt32(bytes, 32, (uint)sectionHeaderOffset);
        WriteUInt16(bytes, 48, 5);
        WriteUInt16(bytes, 50, 4);

        WriteUInt32(bytes, 52, 1);
        WriteUInt32(bytes, 56, textOffset);
        WriteUInt32(bytes, 60, 0x8C01_0000);
        WriteUInt32(bytes, 64, 0x0C01_0000);
        WriteUInt32(bytes, 68, (uint)segmentBytes.Length);
        WriteUInt32(bytes, 72, (uint)segmentBytes.Length);
        WriteUInt32(bytes, 76, 5);
        WriteUInt32(bytes, 80, 32);
        segmentBytes.CopyTo(bytes, textOffset);

        WriteUInt32(bytes, symtabOffset + 16, 1);
        WriteUInt32(bytes, symtabOffset + 20, 0x8C01_0000);
        WriteUInt32(bytes, symtabOffset + 24, 4);
        bytes[symtabOffset + 28] = 0x12;
        WriteUInt16(bytes, symtabOffset + 30, 1);
        strtab.CopyTo(bytes, strtabOffset);
        shstrtab.CopyTo(bytes, shstrtabOffset);

        WriteSectionHeader(bytes, sectionHeaderOffset + 40, 1, 1, 6, 0x8C01_0000, textOffset, (uint)segmentBytes.Length, 0, 0, 4, 0);
        WriteSectionHeader(bytes, sectionHeaderOffset + 80, 7, 2, 0, 0, (uint)symtabOffset, 32, 3, 1, 4, 16);
        WriteSectionHeader(bytes, sectionHeaderOffset + 120, 15, 3, 0, 0, (uint)strtabOffset, (uint)strtab.Length, 0, 0, 1, 0);
        WriteSectionHeader(bytes, sectionHeaderOffset + 160, 23, 3, 0, 0, (uint)shstrtabOffset, (uint)shstrtab.Length, 0, 0, 1, 0);

        return bytes;
    }

    private static void WriteUInt16(byte[] bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteSectionHeader(
        byte[] bytes,
        int offset,
        uint name,
        uint type,
        uint flags,
        uint address,
        uint fileOffset,
        uint size,
        uint link,
        uint info,
        uint addressAlignment,
        uint entrySize)
    {
        WriteUInt32(bytes, offset, name);
        WriteUInt32(bytes, offset + 4, type);
        WriteUInt32(bytes, offset + 8, flags);
        WriteUInt32(bytes, offset + 12, address);
        WriteUInt32(bytes, offset + 16, fileOffset);
        WriteUInt32(bytes, offset + 20, size);
        WriteUInt32(bytes, offset + 24, link);
        WriteUInt32(bytes, offset + 28, info);
        WriteUInt32(bytes, offset + 32, addressAlignment);
        WriteUInt32(bytes, offset + 36, entrySize);
    }
}
