using DcSharp.Core.Dreamcast.Video;

namespace DcSharp.Tests;

public class DreamcastPvrTaStateTests
{
    [Fact]
    public void CompletesKnownOpaqueStrip()
    {
        var state = new DreamcastPvrTaState();

        Assert.Null(state.Accept(CreateWrite("PolygonHeader", 0x8084_0000)));
        Assert.Null(state.Accept(CreateWrite("Vertex", 0xE011_F800)));
        Assert.Null(state.Accept(CreateWrite("Vertex", 0xE021_F800)));
        var render = state.Accept(CreateWrite("VertexEndOfStrip", 0xF012_F800));

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
            },
            vertex =>
            {
                Assert.Equal(2, vertex.X);
                Assert.Equal(1, vertex.Y);
            },
            vertex =>
            {
                Assert.Equal(1, vertex.X);
                Assert.Equal(2, vertex.Y);
            });
    }

    [Fact]
    public void IgnoresIncompleteOrMismatchedStrips()
    {
        var state = new DreamcastPvrTaState();

        Assert.Null(state.Accept(CreateWrite("PolygonHeader", 0x8084_0000)));
        Assert.Null(state.Accept(CreateWrite("Vertex", 0xE000_F800)));
        Assert.Null(state.Accept(CreateWrite("VertexEndOfStrip", 0xF000_07E0)));

        Assert.Null(state.Accept(CreateWrite("PolygonHeader", 0x8084_0000)));
        Assert.Null(state.Accept(CreateWrite("VertexEndOfStrip", 0xF000_F800)));
    }

    private static DreamcastPvrTaCommandWrite CreateWrite(string kind, uint value) =>
        new(
            0x1000_0000,
            "0x10000000",
            "TA_INPUT",
            kind,
            0,
            "OpaquePolygon",
            string.Equals(kind, "VertexEndOfStrip", StringComparison.Ordinal),
            4,
            value,
            $"0x{value:X8}");
}
