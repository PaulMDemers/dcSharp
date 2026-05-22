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
        Assert.False(header.HasKnownPayloadLength);
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
