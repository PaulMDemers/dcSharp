using DcSharp.Core.Dreamcast.Video;

namespace DcSharp.Tests;

public class DreamcastPvrTaDiagnosticVertexPacketDecoderTests
{
    [Fact]
    public void DecodesControlAndPayloadWords()
    {
        var decoder = new DreamcastPvrTaDiagnosticVertexPacketDecoder();

        decoder.Begin(CreateWrite("VertexEndOfStrip", 0xF000_0000), endOfStrip: true);

        Assert.True(decoder.HasPending);
        Assert.False(decoder.AcceptPayload(0x0001_0000, out var vertex));
        Assert.Null(vertex);
        Assert.False(decoder.AcceptPayload(0x0002_0000, out vertex));
        Assert.Null(vertex);
        Assert.True(decoder.AcceptPayload(0x0000_F800, out vertex));

        Assert.NotNull(vertex);
        Assert.False(decoder.HasPending);
        Assert.Equal(1, vertex.X);
        Assert.Equal(2, vertex.Y);
        Assert.True(vertex.EndOfStrip);
        Assert.Equal(0xF800, vertex.Rgb565);
        Assert.Equal("0xF0000000", vertex.ControlValueHex);
        Assert.Equal("0x00010000", vertex.XValueHex);
        Assert.Equal("0x00020000", vertex.YValueHex);
        Assert.Equal("0x0000F800", vertex.ColorValueHex);
    }

    [Fact]
    public void DecodesSignedCoordinatePayloads()
    {
        var decoder = new DreamcastPvrTaDiagnosticVertexPacketDecoder();

        decoder.Begin(CreateWrite("Vertex", 0xE000_0000), endOfStrip: false);
        Assert.False(decoder.AcceptPayload(0xFFFF_0000, out _));
        Assert.False(decoder.AcceptPayload(0xFFFE_0000, out _));
        Assert.True(decoder.AcceptPayload(0x0000_07E0, out var vertex));

        Assert.NotNull(vertex);
        Assert.Equal(-1, vertex.X);
        Assert.Equal(-2, vertex.Y);
        Assert.False(vertex.EndOfStrip);
        Assert.Equal(0x07E0, vertex.Rgb565);
    }

    [Fact]
    public void ResetDropsIncompletePacket()
    {
        var decoder = new DreamcastPvrTaDiagnosticVertexPacketDecoder();

        decoder.Begin(CreateWrite("Vertex", 0xE000_0000), endOfStrip: false);
        Assert.False(decoder.AcceptPayload(0x0001_0000, out _));
        decoder.Reset();

        Assert.False(decoder.HasPending);
        Assert.False(decoder.AcceptPayload(0x0002_0000, out var vertex));
        Assert.Null(vertex);
    }

    private static DreamcastPvrTaCommandWrite CreateWrite(string kind, uint value) =>
        new(
            0x1000_0000,
            "0x10000000",
            "TA_INPUT",
            kind,
            0,
            "OpaquePolygon",
            (value & 0x1000_0000) != 0,
            4,
            value,
            $"0x{value:X8}");
}
