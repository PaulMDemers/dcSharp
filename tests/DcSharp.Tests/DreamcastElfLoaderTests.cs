using DcSharp.Core.Dreamcast.Memory;
using DcSharp.Core.Loading;
using DcSharp.Core.Media;

namespace DcSharp.Tests;

public class DreamcastElfLoaderTests
{
    [Fact]
    public void LoadsProgramSegmentsIntoDreamcastRam()
    {
        var elf = ElfFile.Read(new MemoryStream(CreateElfWithLoadSegment()));
        var memory = new DreamcastMemory();

        var result = new DreamcastElfLoader().Load(elf, memory);

        Assert.Equal(0x8C01_0000u, result.EntryPoint);
        Assert.Equal(0x0C01_0000u, result.TranslatedEntryPoint);
        var segment = Assert.Single(result.LoadedSegments);
        Assert.Equal(0x8C01_0000u, segment.VirtualAddress);
        Assert.Equal(0x0C01_0000u, segment.PhysicalAddress);
        Assert.Equal(4u, result.LoadedBytes);
        Assert.Equal(8u, result.ReservedBytes);
        Assert.Equal(0x11, memory.ReadByte(0x8C01_0000));
        Assert.Equal(0x44, memory.ReadByte(0x8C01_0003));
        Assert.Equal(0x00, memory.ReadByte(0x8C01_0004));
    }

    [Fact]
    public void RawBinaryLoaderCanSeedIpBinAtBiosAddress()
    {
        var memory = new DreamcastMemory();
        var raw = new byte[] { 0x09, 0x00 };
        var ipBin = new byte[2048];
        ipBin[0] = (byte)'S';
        ipBin[1] = (byte)'E';
        ipBin[2] = (byte)'G';
        ipBin[3] = (byte)'A';

        var result = new DreamcastRawBinaryLoader().Load(raw, memory, ipBin: ipBin);

        Assert.Equal(0x8C01_0000u, result.EntryPoint);
        Assert.Equal(0x09, memory.ReadByte(0x8C01_0000));
        Assert.Equal((byte)'S', memory.ReadByte(0x8C00_8000));
        Assert.Equal((byte)'A', memory.ReadByte(0x8C00_8003));
    }

    private static byte[] CreateElfWithLoadSegment()
    {
        var bytes = new byte[88];
        bytes[0] = 0x7F;
        bytes[1] = (byte)'E';
        bytes[2] = (byte)'L';
        bytes[3] = (byte)'F';
        bytes[4] = 1;
        bytes[5] = 1;
        bytes[6] = 1;

        WriteUInt16(bytes, 16, 2);
        WriteUInt16(bytes, 18, 42);
        WriteUInt32(bytes, 20, 1);
        WriteUInt32(bytes, 24, 0x8C01_0000);
        WriteUInt32(bytes, 28, 52);
        WriteUInt16(bytes, 40, 52);
        WriteUInt16(bytes, 42, 32);
        WriteUInt16(bytes, 44, 1);
        WriteUInt16(bytes, 46, 40);
        WriteUInt16(bytes, 48, 3);

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
