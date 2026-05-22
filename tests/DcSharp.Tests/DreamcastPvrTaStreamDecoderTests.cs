using DcSharp.Core.Dreamcast.Video;

namespace DcSharp.Tests;

public class DreamcastPvrTaStreamDecoderTests
{
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
