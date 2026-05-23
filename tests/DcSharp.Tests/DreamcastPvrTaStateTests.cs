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
        Assert.Null(strip.HeaderPayload);
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
    public void CompletesRealPvrVertexStrip()
    {
        var state = new DreamcastPvrTaState();

        Assert.Null(state.Accept(CreateWrite("PolygonHeader", 0x8084_0000)));
        for (var index = 0; index < 7; index++)
        {
            Assert.Null(state.Accept(CreateWrite("Unknown", 0)));
        }

        Assert.Null(AcceptRealVertexPacket(state, "Vertex", 0x3F80_0000, 0x3F80_0000, 0xFFFF_0000));
        Assert.Null(AcceptRealVertexPacket(state, "Vertex", 0x4000_0000, 0x3F80_0000, 0xFFFF_0000));
        var render = AcceptRealVertexPacket(state, "VertexEndOfStrip", 0x3F80_0000, 0x4000_0000, 0xFFFF_0000);

        Assert.NotNull(render);
        Assert.Equal(0xF800, render.Rgb565);
        var strip = Assert.Single(state.CompletedStrips);
        Assert.Equal("OpaquePolygon", strip.ListTypeName);
        Assert.NotNull(strip.HeaderPayload);
        Assert.Equal("0x00000000", strip.HeaderPayload.Mode1Hex);
        Assert.Equal("Never", strip.HeaderPayload.Mode1Fields.DepthCompareName);
        Assert.Equal("Argb1555", strip.HeaderPayload.Mode3Fields.PixelFormatName);
        Assert.Equal(3, strip.Vertices.Count);
        Assert.Collection(
            strip.Vertices,
            vertex =>
            {
                Assert.Equal(1, vertex.X);
                Assert.Equal(1, vertex.Y);
                Assert.Equal("0x3F800000", vertex.XValueHex);
                Assert.Equal("0xFFFF0000", vertex.ColorValueHex);
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
    public void CarriesRealPolygonHeaderPayloadIntoCompletedStrip()
    {
        var state = new DreamcastPvrTaState();

        Assert.Null(state.Accept(CreateWrite("PolygonHeader", 0x8084_0008)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x9600_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x8490_2064)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0xCE00_1234)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x1111_1111)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x2222_2222)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x3333_3333)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x4444_4444)));

        Assert.Null(AcceptRealVertexPacket(state, "Vertex", 0x3F80_0000, 0x3F80_0000, 0xFF00_00FF));
        Assert.Null(AcceptRealVertexPacket(state, "Vertex", 0x4000_0000, 0x3F80_0000, 0xFF00_00FF));
        var render = AcceptRealVertexPacket(state, "VertexEndOfStrip", 0x3F80_0000, 0x4000_0000, 0xFF00_00FF);

        Assert.NotNull(render);
        var strip = Assert.Single(state.CompletedStrips);
        var payload = Assert.IsType<DreamcastPvrTaPolygonHeaderPayload>(strip.HeaderPayload);
        Assert.Equal("0x96000000", payload.Mode1Hex);
        Assert.True(payload.Mode1Fields.TextureEnabled);
        Assert.True(payload.Mode1Fields.DepthWriteDisabled);
        Assert.Equal("Ccw", payload.Mode1Fields.CullingName);
        Assert.Equal("Greater", payload.Mode1Fields.DepthCompareName);
        Assert.Equal("0x84902064", payload.Mode2Hex);
        Assert.Equal("SrcAlpha", payload.Mode2Fields.BlendSrcName);
        Assert.Equal("One", payload.Mode2Fields.BlendDstName);
        Assert.True(payload.Mode2Fields.AlphaEnabled);
        Assert.Equal("Disabled", payload.Mode2Fields.FogTypeName);
        Assert.Equal("0xCE001234", payload.Mode3Hex);
        Assert.Equal("0x00001234", payload.Mode3Fields.TextureBaseHex);
        Assert.Equal("Rgb565", payload.Mode3Fields.PixelFormatName);
        Assert.True(payload.Mode3Fields.VqEnabled);
        Assert.True(payload.Mode3Fields.MipMapEnabled);
    }

    [Fact]
    public void CompletesRealPvrVertexStripWhenArgbPayloadLooksLikeHeader()
    {
        var state = new DreamcastPvrTaState();

        Assert.Null(state.Accept(CreateWrite("PolygonHeader", 0x8084_0000)));
        for (var index = 0; index < 7; index++)
        {
            Assert.Null(state.Accept(CreateWrite("Unknown", 0)));
        }

        Assert.Null(AcceptRealVertexPacket(state, "Vertex", 0x3F80_0000, 0x3F80_0000, 0x80FF_0000));
        Assert.Null(AcceptRealVertexPacket(state, "Vertex", 0x4000_0000, 0x3F80_0000, 0x80FF_0000));
        var render = AcceptRealVertexPacket(state, "VertexEndOfStrip", 0x3F80_0000, 0x4000_0000, 0x80FF_0000);

        Assert.NotNull(render);
        var strip = Assert.Single(state.CompletedStrips);
        Assert.Equal(0xF800, strip.Rgb565);
        Assert.All(strip.Vertices, vertex => Assert.Equal("0x80FF0000", vertex.ColorValueHex));
    }

    [Fact]
    public void CompletesGouraudRealPvrVertexStripWithMixedColors()
    {
        var state = new DreamcastPvrTaState();

        Assert.Null(state.Accept(CreateWrite("PolygonHeader", 0x8084_0002)));
        for (var index = 0; index < 7; index++)
        {
            Assert.Null(state.Accept(CreateWrite("Unknown", 0)));
        }

        Assert.Null(AcceptRealVertexPacket(state, "Vertex", 0x3F80_0000, 0x3F80_0000, 0xFFFF_0000));
        Assert.Null(AcceptRealVertexPacket(state, "Vertex", 0x4000_0000, 0x3F80_0000, 0xFF00_FF00));
        var render = AcceptRealVertexPacket(state, "VertexEndOfStrip", 0x3F80_0000, 0x4000_0000, 0xFF00_00FF);

        Assert.NotNull(render);
        Assert.Equal(0xF800, render.Rgb565);
        var strip = Assert.Single(state.CompletedStrips);
        Assert.True(strip.Gouraud);
        Assert.Equal("0x80840002", strip.HeaderValueHex);
        Assert.Equal(0xF800, strip.Rgb565);
        Assert.Collection(
            strip.Vertices,
            vertex => Assert.Equal(0xF800, vertex.Rgb565),
            vertex => Assert.Equal(0x07E0, vertex.Rgb565),
            vertex => Assert.Equal(0x001F, vertex.Rgb565));
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

    private static DreamcastPvrTaRenderCommand? AcceptRealVertexPacket(
        DreamcastPvrTaState state,
        string kind,
        uint x,
        uint y,
        uint argb)
    {
        Assert.Null(state.Accept(CreateWrite(kind, string.Equals(kind, "VertexEndOfStrip", StringComparison.Ordinal) ? 0xF000_0000 : 0xE000_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", x)));
        Assert.Null(state.Accept(CreateWrite("Unknown", y)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x3F80_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0)));
        Assert.Null(state.Accept(CreateWrite("Unknown", argb)));
        return state.Accept(CreateWrite("Unknown", 0));
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
