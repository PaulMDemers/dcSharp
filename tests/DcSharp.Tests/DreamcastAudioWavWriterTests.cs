using DcSharp.Core.Dreamcast.Audio;
using DcSharp.Core.Dreamcast.Memory;
using System.Buffers.Binary;

namespace DcSharp.Tests;

public class DreamcastAudioWavWriterTests
{
    [Fact]
    public void WritePcm16StereoWritesHeaderAndPannedPcm16Sample()
    {
        var memory = new DreamcastMemory();
        memory.Write(0xA080_0000, [0x00, 0x10]);
        memory.WriteUInt32(0xA070_000C, 0x0000_0001);
        memory.Write(0xA070_0024, [0x0F]);
        memory.Write(0xA070_0029, [0xFF]);
        memory.WriteUInt32(0xA070_0000, 0x0000_C000);
        memory.AdvanceHardware(10_000);

        using var stream = new MemoryStream();
        DreamcastAudioWavWriter.WritePcm16Stereo(stream, memory.CreateAudioSnapshot());

        var bytes = stream.ToArray();
        Assert.Equal("RIFF", ReadAscii(bytes, 0, 4));
        Assert.Equal("WAVE", ReadAscii(bytes, 8, 4));
        Assert.Equal("fmt ", ReadAscii(bytes, 12, 4));
        Assert.Equal("data", ReadAscii(bytes, 36, 4));
        Assert.Equal(44_100, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24, 4)));
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40, 4)));
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44, 2)));
        Assert.Equal(0x1000, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(46, 2)));
    }

    [Fact]
    public void WritePcm16StereoUsesPcm8StrideAndVolume()
    {
        var memory = new DreamcastMemory();
        memory.Write(0xA080_0000, [0xFF]);
        memory.WriteUInt32(0xA070_000C, 0x0000_0001);
        memory.Write(0xA070_0024, [0x00]);
        memory.Write(0xA070_0029, [0x80]);
        memory.WriteUInt32(0xA070_0000, 0x0000_C080);
        memory.AdvanceHardware(10_000);

        using var stream = new MemoryStream();
        DreamcastAudioWavWriter.WritePcm16Stereo(stream, memory.CreateAudioSnapshot());

        var bytes = stream.ToArray();
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40, 4)));
        Assert.Equal(16_319, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44, 2)));
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(46, 2)));
    }

    [Fact]
    public void WritePcm16StereoSkipsCompressedChannels()
    {
        var memory = new DreamcastMemory();
        memory.Write(0xA080_0000, [0x00, 0x10]);
        memory.WriteUInt32(0xA070_000C, 0x0000_0001);
        memory.Write(0xA070_0024, [0x0F]);
        memory.Write(0xA070_0029, [0xFF]);
        memory.WriteUInt32(0xA070_0000, 0x0000_C100);
        memory.AdvanceHardware(10_000);

        using var stream = new MemoryStream();
        DreamcastAudioWavWriter.WritePcm16Stereo(stream, memory.CreateAudioSnapshot());

        var bytes = stream.ToArray();
        Assert.Equal(44, bytes.Length);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40, 4)));
    }

    private static string ReadAscii(byte[] bytes, int offset, int length) =>
        System.Text.Encoding.ASCII.GetString(bytes, offset, length);
}
