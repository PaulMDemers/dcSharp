using DcSharp.Core.Dreamcast.Video;

namespace DcSharp.Tests;

public class DreamcastPvrTaStreamDecoderTests
{
    [Fact]
    public void DecodesRealPolygonHeaderPayloadWords()
    {
        var writes = new[]
        {
            CreateWrite("TA_INPUT", 0x8084_0000),
            CreateWrite("TA_INPUT", 0x0600_0000),
            CreateWrite("TA_INPUT", 0x8010_0000),
            CreateWrite("TA_INPUT", 0x4800_1234),
            CreateWrite("TA_INPUT", 0x1111_1111),
            CreateWrite("TA_INPUT", 0x2222_2222),
            CreateWrite("TA_INPUT", 0x3333_3333),
            CreateWrite("TA_INPUT", 0x4444_4444)
        };

        var payload = Assert.Single(DreamcastPvrTaPolygonHeaderPayloadDecoder.Decode(writes));

        Assert.Equal("TA_INPUT", payload.Region);
        Assert.Equal("OpaquePolygon", payload.ListTypeName);
        Assert.Equal(0x8084_0000u, payload.HeaderValue);
        Assert.Equal(0x0600_0000u, payload.Mode1);
        Assert.True(payload.Mode1Fields.TextureEnabled);
        Assert.True(payload.Mode1Fields.DepthWriteDisabled);
        Assert.Equal("None", payload.Mode1Fields.CullingName);
        Assert.Equal("Never", payload.Mode1Fields.DepthCompareName);
        Assert.Equal(0x8010_0000u, payload.Mode2);
        Assert.True(payload.Mode2Fields.AlphaEnabled);
        Assert.Equal("Zero", payload.Mode2Fields.BlendDstName);
        Assert.Equal("SrcAlpha", payload.Mode2Fields.BlendSrcName);
        Assert.Equal(0x4800_1234u, payload.Mode3);
        Assert.Equal(0x0000_1234u, payload.Mode3Fields.TextureBase);
        Assert.Equal("Rgb565", payload.Mode3Fields.PixelFormatName);
        Assert.True(payload.Mode3Fields.VqEnabled);
        Assert.False(payload.Mode3Fields.MipMapEnabled);
        Assert.Equal(0x1111_1111u, payload.Parameter0);
        Assert.Equal(0x4444_4444u, payload.Parameter3);
    }

    [Fact]
    public void IgnoresDiagnosticVertexShortcutAfterPolygonHeader()
    {
        var writes = new[]
        {
            CreateWrite("TA_INPUT", 0x8084_0000),
            CreateWrite("TA_INPUT", 0xE000_0000),
            CreateWrite("TA_INPUT", 0x0001_0000),
            CreateWrite("TA_INPUT", 0x0001_0000),
            CreateWrite("TA_INPUT", 0x0000_F800)
        };

        Assert.Empty(DreamcastPvrTaPolygonHeaderPayloadDecoder.Decode(writes));
    }

    [Fact]
    public void DecodesRealVertexPayloadsAfterRealPolygonHeader()
    {
        var writes = new[]
        {
            CreateWrite("TA_INPUT", 0x8084_0000),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0xE000_0000),
            CreateWrite("TA_INPUT", 0x3F80_0000),
            CreateWrite("TA_INPUT", 0x4000_0000),
            CreateWrite("TA_INPUT", 0x4040_0000),
            CreateWrite("TA_INPUT", 0x3F00_0000),
            CreateWrite("TA_INPUT", 0x3E80_0000),
            CreateWrite("TA_INPUT", 0xFF00_00FF),
            CreateWrite("TA_INPUT", 0x0102_0304)
        };

        var vertex = Assert.Single(DreamcastPvrTaRealVertexPayloadDecoder.Decode(writes));

        Assert.Equal("TA_INPUT", vertex.Region);
        Assert.Equal("OpaquePolygon", vertex.ListTypeName);
        Assert.False(vertex.EndOfStrip);
        Assert.Equal(0xE000_0000u, vertex.ControlValue);
        Assert.Equal(1, vertex.RoundedX);
        Assert.Equal(2, vertex.RoundedY);
        Assert.Equal(3.0f, vertex.Z);
        Assert.Equal(0.5f, vertex.U);
        Assert.Equal(0.25f, vertex.V);
        Assert.Equal(0xFF00_00FFu, vertex.Argb);
        Assert.Equal(0x001F, vertex.Rgb565);
        Assert.Equal(0x0102_0304u, vertex.OffsetArgb);
    }

    [Fact]
    public void IgnoresDiagnosticVertexShortcutsForRealVertexPayloads()
    {
        var writes = new[]
        {
            CreateWrite("TA_INPUT", 0x8084_0000),
            CreateWrite("TA_INPUT", 0xE000_0000),
            CreateWrite("TA_INPUT", 0x0001_0000),
            CreateWrite("TA_INPUT", 0x0001_0000),
            CreateWrite("TA_INPUT", 0x0000_F800)
        };

        Assert.Empty(DreamcastPvrTaRealVertexPayloadDecoder.Decode(writes));
    }

    [Fact]
    public void TracksKnownHeaderPayloadWords()
    {
        var writes = new[]
        {
            CreateWrite("TA_INPUT", 0x8084_0000),
            CreateWrite("TA_INPUT", 0xE000_0000),
            CreateWrite("TA_INPUT", 0x0001_0000),
            CreateWrite("TA_INPUT", 0x0001_0000)
        };

        var decoded = DreamcastPvrTaStreamDecoder.Decode(writes);

        Assert.Collection(
            decoded,
            write =>
            {
                Assert.Equal("Control", write.Role);
                Assert.Equal("PolygonHeader", write.ControlKind);
                Assert.Equal(7, write.PayloadWordsRemaining);
                Assert.Null(write.PayloadWordIndex);
                Assert.Null(write.PayloadWordName);
            },
            write =>
            {
                Assert.Equal("Payload", write.Role);
                Assert.Equal("PolygonHeader", write.ControlKind);
                Assert.Equal(0, write.PayloadWordIndex);
                Assert.Equal(6, write.PayloadWordsRemaining);
                Assert.Equal("Mode1", write.PayloadWordName);
            },
            write =>
            {
                Assert.Equal("Payload", write.Role);
                Assert.Equal(1, write.PayloadWordIndex);
                Assert.Equal(5, write.PayloadWordsRemaining);
                Assert.Equal("Mode2", write.PayloadWordName);
            },
            write =>
            {
                Assert.Equal("Payload", write.Role);
                Assert.Equal(2, write.PayloadWordIndex);
                Assert.Equal(4, write.PayloadWordsRemaining);
                Assert.Equal("Mode3", write.PayloadWordName);
            });
    }

    [Fact]
    public void TracksGenericVertexPayloadWords()
    {
        var writes = new[]
        {
            CreateWrite("TA_INPUT", 0xE000_0000),
            CreateWrite("TA_INPUT", 0x3F80_0000),
            CreateWrite("TA_INPUT", 0x3F80_0000)
        };

        var decoded = DreamcastPvrTaStreamDecoder.Decode(writes);

        Assert.Collection(
            decoded,
            write =>
            {
                Assert.Equal("Control", write.Role);
                Assert.Equal("Vertex", write.ControlKind);
                Assert.Null(write.PayloadWordIndex);
                Assert.Equal(7, write.PayloadWordsRemaining);
                Assert.Null(write.PayloadWordName);
            },
            write =>
            {
                Assert.Equal("Payload", write.Role);
                Assert.Equal("Vertex", write.ControlKind);
                Assert.Equal(0, write.PayloadWordIndex);
                Assert.Equal(6, write.PayloadWordsRemaining);
                Assert.Null(write.PayloadWordName);
            },
            write =>
            {
                Assert.Equal("Payload", write.Role);
                Assert.Equal("Vertex", write.ControlKind);
                Assert.Equal(1, write.PayloadWordIndex);
                Assert.Equal(5, write.PayloadWordsRemaining);
                Assert.Null(write.PayloadWordName);
            });
    }

    private static DreamcastPvrTaCommandWrite CreateWrite(string region, uint value)
    {
        var command = DreamcastPvrTaCommandDecoder.Decode(region, value);
        return new DreamcastPvrTaCommandWrite(
            string.Equals(region, "TA_YUV_CONV", StringComparison.Ordinal) ? 0x1080_0000u : 0x1000_0000u,
            string.Equals(region, "TA_YUV_CONV", StringComparison.Ordinal) ? "0x10800000" : "0x10000000",
            region,
            command.Kind,
            command.ListType,
            command.ListTypeName,
            command.EndOfStrip,
            4,
            value,
            $"0x{value:X8}");
    }
}
