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
}
