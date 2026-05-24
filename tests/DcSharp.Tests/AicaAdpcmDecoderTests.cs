using DcSharp.Core.Dreamcast.Audio;

namespace DcSharp.Tests;

public class AicaAdpcmDecoderTests
{
    [Fact]
    public void DecodeNibbleAdvancesPredictorAndStep()
    {
        var decoder = new AicaAdpcmDecoder();

        Assert.Equal(47, decoder.DecodeNibble(0x1));
        Assert.Equal(126, decoder.DecodeNibble(0x2));
        Assert.Equal(111, decoder.DecodeNibble(0x8));
    }

    [Fact]
    public void DecodeNibbleClampsToPcm16Range()
    {
        var decoder = new AicaAdpcmDecoder();

        short sample = 0;
        for (var index = 0; index < 1000; index++)
        {
            sample = decoder.DecodeNibble(0x7);
        }

        Assert.Equal(short.MaxValue, sample);

        for (var index = 0; index < 1000; index++)
        {
            sample = decoder.DecodeNibble(0xF);
        }

        Assert.Equal(short.MinValue, sample);
    }

    [Fact]
    public void ReadNibbleUsesLowNibbleFirst()
    {
        var ram = new byte[4];
        ram[1] = 0xA5;
        ram[2] = 0x3C;

        Assert.Equal(0x5, AicaAdpcmDecoder.ReadNibble(ram, sampleAddress: 1, sampleIndex: 0));
        Assert.Equal(0xA, AicaAdpcmDecoder.ReadNibble(ram, sampleAddress: 1, sampleIndex: 1));
        Assert.Equal(0xC, AicaAdpcmDecoder.ReadNibble(ram, sampleAddress: 1, sampleIndex: 2));
        Assert.Equal(0x3, AicaAdpcmDecoder.ReadNibble(ram, sampleAddress: 1, sampleIndex: 3));
    }

    [Fact]
    public void ReadNibbleReturnsZeroPastAudioRam()
    {
        var ram = new byte[1];

        Assert.Equal(0, AicaAdpcmDecoder.ReadNibble(ram, sampleAddress: 1, sampleIndex: 0));
    }
}
