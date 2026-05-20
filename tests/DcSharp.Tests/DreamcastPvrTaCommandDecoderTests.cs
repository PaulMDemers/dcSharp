using DcSharp.Core.Dreamcast.Video;

namespace DcSharp.Tests;

public class DreamcastPvrTaCommandDecoderTests
{
    [Theory]
    [InlineData(0x8084_0000u, "PolygonHeader", false)]
    [InlineData(0xE000_0000u, "Vertex", false)]
    [InlineData(0xF000_0000u, "VertexEndOfStrip", true)]
    [InlineData(0xA000_0000u, "SpriteHeader", false)]
    [InlineData(0x2000_0000u, "UserClip", false)]
    [InlineData(0x8000_0000u, "ModifierVolume", false)]
    public void DecodesTaInputCommandKind(uint value, string expectedKind, bool expectedEndOfStrip)
    {
        var command = DreamcastPvrTaCommandDecoder.Decode("TA_INPUT", value);

        Assert.Equal(expectedKind, command.Kind);
        Assert.Equal(expectedEndOfStrip, command.EndOfStrip);
    }

    [Fact]
    public void DecodesListType()
    {
        var command = DreamcastPvrTaCommandDecoder.Decode("TA_INPUT", 0x8284_0000);

        Assert.Equal(2, command.ListType);
        Assert.Equal("TranslucentPolygon", command.ListTypeName);
    }

    [Fact]
    public void ClassifiesYuvRegionAsConverterData()
    {
        var command = DreamcastPvrTaCommandDecoder.Decode("TA_YUV_CONV", 0x0000_0001);

        Assert.Equal("YuvConverterData", command.Kind);
        Assert.Null(command.ListType);
    }
}
