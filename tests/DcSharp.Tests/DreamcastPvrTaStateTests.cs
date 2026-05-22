using DcSharp.Core.Dreamcast.Video;

namespace DcSharp.Tests;

public class DreamcastPvrTaStateTests
{
    [Fact]
    public void CompletesKnownOpaqueStrip()
    {
        var state = new DreamcastPvrTaState();

        Assert.Null(state.Accept(CreateWrite("PolygonHeader", 0x8084_0000)));
        Assert.Null(AcceptVertexPacket(state, "Vertex", 1, 1, 0xF800));
        Assert.Null(AcceptVertexPacket(state, "Vertex", 2, 1, 0xF800));
        var render = AcceptVertexPacket(state, "VertexEndOfStrip", 1, 2, 0xF800);

        Assert.NotNull(render);
        Assert.Equal(0xF800, render.Rgb565);
        var strip = Assert.Single(state.CompletedStrips);
        Assert.Equal("OpaquePolygon", strip.ListTypeName);
        Assert.Equal("0x80840000", strip.HeaderValueHex);
        Assert.Equal("0xF800", strip.Rgb565Hex);
        Assert.Collection(
            strip.Vertices,
            vertex =>
            {
                Assert.Equal(1, vertex.X);
                Assert.Equal(1, vertex.Y);
                Assert.False(vertex.EndOfStrip);
                Assert.Equal("0x00010000", vertex.XValueHex);
                Assert.Equal("0x00010000", vertex.YValueHex);
                Assert.Equal("0x0000F800", vertex.ColorValueHex);
            },
            vertex =>
            {
                Assert.Equal(2, vertex.X);
                Assert.Equal(1, vertex.Y);
                Assert.False(vertex.EndOfStrip);
            },
            vertex =>
            {
                Assert.Equal(1, vertex.X);
                Assert.Equal(2, vertex.Y);
                Assert.True(vertex.EndOfStrip);
            });
    }

    [Fact]
    public void IgnoresIncompleteOrMismatchedStrips()
    {
        var state = new DreamcastPvrTaState();

        Assert.Null(state.Accept(CreateWrite("PolygonHeader", 0x8084_0000)));
        Assert.Null(AcceptVertexPacket(state, "Vertex", 1, 1, 0xF800));
        Assert.Null(AcceptVertexPacket(state, "VertexEndOfStrip", 1, 2, 0x07E0));

        Assert.Null(state.Accept(CreateWrite("PolygonHeader", 0x8084_0000)));
        Assert.Null(AcceptVertexPacket(state, "VertexEndOfStrip", 1, 1, 0xF800));
    }

    private static DreamcastPvrTaRenderCommand? AcceptVertexPacket(
        DreamcastPvrTaState state,
        string kind,
        int x,
        int y,
        ushort color)
    {
        Assert.Null(state.Accept(CreateWrite(kind, string.Equals(kind, "VertexEndOfStrip", StringComparison.Ordinal) ? 0xF000_0000 : 0xE000_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", (uint)x << 16)));
        Assert.Null(state.Accept(CreateWrite("Unknown", (uint)y << 16)));
        return state.Accept(CreateWrite("Unknown", color));
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
