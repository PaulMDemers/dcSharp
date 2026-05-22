using DcSharp.Core.Dreamcast.Video;

namespace DcSharp.Tests;

public class DreamcastPvrTaParameterDecoderTests
{
    [Fact]
    public void DecodesPolygonHeaderControlFields()
    {
        var header = DreamcastPvrTaParameterDecoder.Decode("TA_INPUT", 0x8084_0000);

        Assert.Equal("TA_INPUT", header.Region);
        Assert.Equal("PolygonHeader", header.Kind);
        Assert.Equal(4, header.ParameterType);
        Assert.Equal(0, header.ListType);
        Assert.Equal("OpaquePolygon", header.ListTypeName);
        Assert.False(header.EndOfStrip);
        Assert.Equal(7, header.ExpectedPayloadWords);
        Assert.True(header.HasKnownPayloadLength);
        var command = Assert.IsType<DreamcastPvrTaPolygonHeaderCommand>(header.PolygonHeaderCommand);
        Assert.False(command.Uv16Bit);
        Assert.False(command.Gouraud);
        Assert.False(command.OffsetColorEnabled);
        Assert.False(command.TextureEnabled);
        Assert.Equal(0, command.ColorFormat);
        Assert.Equal("ArgbPacked", command.ColorFormatName);
        Assert.False(command.ModifierNormal);
        Assert.False(command.ModifierEnabled);
        Assert.Equal(0, command.ClipMode);
        Assert.Equal("Disabled", command.ClipModeName);
        Assert.Equal(1, command.StripLength);
        Assert.Equal("Strip2", command.StripLengthName);
        Assert.True(command.AutoStripLength);
    }

    [Theory]
    [InlineData(0xA000_0000u, "SpriteHeader")]
    [InlineData(0x8000_0000u, "ModifierVolume")]
    [InlineData(0x2000_0000u, "UserClip")]
    public void DecodesKnownHeaderPayloadLengths(uint value, string expectedKind)
    {
        var header = DreamcastPvrTaParameterDecoder.Decode("TA_INPUT", value);

        Assert.Equal(expectedKind, header.Kind);
        Assert.Equal(7, header.ExpectedPayloadWords);
        Assert.True(header.HasKnownPayloadLength);
    }

    [Fact]
    public void DecodesVertexControlFieldsWithoutClaimingPayloadLengthYet()
    {
        var header = DreamcastPvrTaParameterDecoder.Decode("TA_INPUT", 0xF000_0000);

        Assert.Equal("VertexEndOfStrip", header.Kind);
        Assert.True(header.EndOfStrip);
        Assert.False(header.HasKnownPayloadLength);
    }

    [Fact]
    public void DecodesYuvConverterApertureAsZeroPayloadDiagnostic()
    {
        var header = DreamcastPvrTaParameterDecoder.Decode("TA_YUV_CONV", 0x0000_0001);

        Assert.Equal("YuvConverterData", header.Kind);
        Assert.Null(header.ParameterType);
        Assert.Null(header.ListType);
        Assert.Null(header.ListTypeName);
        Assert.Equal(0, header.ExpectedPayloadWords);
        Assert.True(header.HasKnownPayloadLength);
    }
}
