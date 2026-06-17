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
        Assert.False(payload.Mode3Fields.NonTwiddled);
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

    [Theory]
    [InlineData(0x8284_0000u, "TranslucentPolygon")]
    [InlineData(0x8484_0000u, "PunchThroughPolygon")]
    public void DecodesRealVertexPayloadsAfterRenderableListHeaders(uint headerValue, string listTypeName)
    {
        var writes = new[]
        {
            CreateWrite("TA_INPUT", headerValue),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0xE000_0000),
            CreateWrite("TA_INPUT", 0x3F80_0000),
            CreateWrite("TA_INPUT", 0x3F80_0000),
            CreateWrite("TA_INPUT", 0x3F80_0000),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0xFFFF_0000),
            CreateWrite("TA_INPUT", 0x0000_0000)
        };

        var vertex = Assert.Single(DreamcastPvrTaRealVertexPayloadDecoder.Decode(writes));

        Assert.Equal(listTypeName, vertex.ListTypeName);
        Assert.Equal(0xFFFF_0000u, vertex.Argb);
        Assert.Equal(0xF800, vertex.Rgb565);
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

    [Theory]
    [InlineData(0xA084_0000u, "SpriteHeader", "Mode1", "Mode2", "Mode3", "Argb", "Dummy1")]
    [InlineData(0x8000_0000u, "ModifierVolume", "Mode1", "Dummy0", "Dummy1", "Dummy2", "Dummy5")]
    [InlineData(0x2000_0000u, "UserClip", "Clip0", "Clip1", "Clip2", "Clip3", "Clip6")]
    public void TracksKnownNonPolygonHeaderPayloadWords(
        uint controlValue,
        string controlKind,
        string payload0Name,
        string payload1Name,
        string payload2Name,
        string payload3Name,
        string payload6Name)
    {
        var writes = new[]
        {
            CreateWrite("TA_INPUT", controlValue),
            CreateWrite("TA_INPUT", 0x1111_1111),
            CreateWrite("TA_INPUT", 0x2222_2222),
            CreateWrite("TA_INPUT", 0x3333_3333),
            CreateWrite("TA_INPUT", 0x4444_4444),
            CreateWrite("TA_INPUT", 0x5555_5555),
            CreateWrite("TA_INPUT", 0x6666_6666),
            CreateWrite("TA_INPUT", 0x7777_7777)
        };

        var decoded = DreamcastPvrTaStreamDecoder.Decode(writes);

        Assert.Equal(8, decoded.Count);
        Assert.Equal("Control", decoded[0].Role);
        Assert.Equal(controlKind, decoded[0].ControlKind);
        Assert.Equal(7, decoded[0].PayloadWordsRemaining);
        Assert.Null(decoded[0].PayloadWordName);
        Assert.Equal("Payload", decoded[1].Role);
        Assert.Equal(controlKind, decoded[1].ControlKind);
        Assert.Equal(0, decoded[1].PayloadWordIndex);
        Assert.Equal(6, decoded[1].PayloadWordsRemaining);
        Assert.Equal(payload0Name, decoded[1].PayloadWordName);
        Assert.Equal(payload1Name, decoded[2].PayloadWordName);
        Assert.Equal(payload2Name, decoded[3].PayloadWordName);
        Assert.Equal(payload3Name, decoded[4].PayloadWordName);
        Assert.Equal(payload6Name, decoded[7].PayloadWordName);
        Assert.Equal(0, decoded[7].PayloadWordsRemaining);
    }

    [Theory]
    [InlineData(0xA084_0000u, 0x0200_0000u, "Dummy", "Auv", "Buv", "Cuv")]
    [InlineData(0xA084_0008u, 0x0000_0000u, "Dummy", "Auv", "Buv", "Cuv")]
    public void TracksSpriteVertexPayloadWordsAfterSpriteHeader(
        uint headerValue,
        uint mode1,
        string payload11Name,
        string payload12Name,
        string payload13Name,
        string payload14Name)
    {
        var writes = new[]
        {
            CreateWrite("TA_INPUT", headerValue),
            CreateWrite("TA_INPUT", mode1),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0xFF00_FF00),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0xF000_0000),
            CreateWrite("TA_INPUT", 0x3F80_0000),
            CreateWrite("TA_INPUT", 0x3F80_0000),
            CreateWrite("TA_INPUT", 0x3F80_0000),
            CreateWrite("TA_INPUT", 0x4040_0000),
            CreateWrite("TA_INPUT", 0x3F80_0000),
            CreateWrite("TA_INPUT", 0x3F80_0000),
            CreateWrite("TA_INPUT", 0x3F80_0000),
            CreateWrite("TA_INPUT", 0x4040_0000),
            CreateWrite("TA_INPUT", 0x3F80_0000),
            CreateWrite("TA_INPUT", 0x4040_0000),
            CreateWrite("TA_INPUT", 0x4040_0000),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0x0000_0000),
            CreateWrite("TA_INPUT", 0x0000_0000)
        };

        var decoded = DreamcastPvrTaStreamDecoder.Decode(writes);

        Assert.Equal(24, decoded.Count);
        Assert.Equal("Control", decoded[8].Role);
        Assert.Equal("VertexEndOfStrip", decoded[8].Write.Kind);
        Assert.Equal("SpriteVertexEndOfStrip", decoded[8].ControlKind);
        Assert.Equal(15, decoded[8].PayloadWordsRemaining);
        Assert.Equal("Payload", decoded[9].Role);
        Assert.Equal("SpriteVertexEndOfStrip", decoded[9].ControlKind);
        Assert.Equal(0, decoded[9].PayloadWordIndex);
        Assert.Equal(14, decoded[9].PayloadWordsRemaining);
        Assert.Equal("Ax", decoded[9].PayloadWordName);
        Assert.Equal("Dy", decoded[19].PayloadWordName);
        Assert.Equal(payload11Name, decoded[20].PayloadWordName);
        Assert.Equal(payload12Name, decoded[21].PayloadWordName);
        Assert.Equal(payload13Name, decoded[22].PayloadWordName);
        Assert.Equal(payload14Name, decoded[23].PayloadWordName);
        Assert.Equal(0, decoded[23].PayloadWordsRemaining);
        Assert.DoesNotContain(decoded, write => string.Equals(write.Role, "Control", StringComparison.Ordinal) && string.Equals(write.ControlKind, "UserClip", StringComparison.Ordinal));
    }

    [Fact]
    public void DoesNotTrackStandaloneVertexPayloadWordsWithoutActivePolygon()
    {
        var writes = new[]
        {
            CreateWrite("TA_INPUT", 0xE000_0000),
            CreateWrite("TA_INPUT", 0x0102_0304),
            CreateWrite("TA_INPUT", 0x1122_3344)
        };

        var decoded = DreamcastPvrTaStreamDecoder.Decode(writes);

        Assert.Collection(
            decoded,
            write =>
            {
                Assert.Equal("Control", write.Role);
                Assert.Equal("Vertex", write.ControlKind);
                Assert.Null(write.PayloadWordIndex);
                Assert.Null(write.PayloadWordsRemaining);
                Assert.Null(write.PayloadWordName);
            },
            write =>
            {
                Assert.Equal("Control", write.Role);
                Assert.Equal("Unknown", write.ControlKind);
                Assert.Null(write.PayloadWordIndex);
                Assert.Null(write.PayloadWordsRemaining);
                Assert.Null(write.PayloadWordName);
            },
            write =>
            {
                Assert.Equal("Control", write.Role);
                Assert.Equal("Unknown", write.ControlKind);
                Assert.Null(write.PayloadWordIndex);
                Assert.Null(write.PayloadWordsRemaining);
                Assert.Null(write.PayloadWordName);
            });
    }

    [Fact]
    public void TracksPolygonVertexPayloadWordsAfterCompleteHeader()
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
            CreateWrite("TA_INPUT", 0x4000_0000)
        };

        var decoded = DreamcastPvrTaStreamDecoder.Decode(writes);

        Assert.Equal("Control", decoded[8].Role);
        Assert.Equal("Vertex", decoded[8].ControlKind);
        Assert.Equal(7, decoded[8].PayloadWordsRemaining);
        Assert.Equal("Payload", decoded[9].Role);
        Assert.Equal("Vertex", decoded[9].ControlKind);
        Assert.Equal(0, decoded[9].PayloadWordIndex);
        Assert.Equal(6, decoded[9].PayloadWordsRemaining);
        Assert.Equal("Payload", decoded[10].Role);
        Assert.Equal("Vertex", decoded[10].ControlKind);
        Assert.Equal(1, decoded[10].PayloadWordIndex);
        Assert.Equal(5, decoded[10].PayloadWordsRemaining);
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
