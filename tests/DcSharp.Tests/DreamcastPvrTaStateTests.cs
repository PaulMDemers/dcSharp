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
    public void CompletesTranslucentRealPvrVertexStripWithHeaderListType()
    {
        var state = new DreamcastPvrTaState();

        Assert.Null(state.Accept(CreateWrite("PolygonHeader", 0x8284_0000, 2, "TranslucentPolygon")));
        for (var index = 0; index < 7; index++)
        {
            Assert.Null(state.Accept(CreateWrite("Unknown", 0)));
        }

        Assert.Null(AcceptRealVertexPacket(state, "Vertex", 0x3F80_0000, 0x3F80_0000, 0x80FF_0000));
        Assert.Null(AcceptRealVertexPacket(state, "Vertex", 0x4000_0000, 0x3F80_0000, 0x80FF_0000));
        var render = AcceptRealVertexPacket(state, "VertexEndOfStrip", 0x3F80_0000, 0x4000_0000, 0x80FF_0000);

        Assert.NotNull(render);
        var strip = Assert.Single(state.CompletedStrips);
        Assert.Equal(2, strip.ListType);
        Assert.Equal("TranslucentPolygon", strip.ListTypeName);
        Assert.Equal("0x82840000", strip.HeaderValueHex);
    }

    [Fact]
    public void CompletesPunchThroughRealPvrVertexStripWithHeaderListType()
    {
        var state = new DreamcastPvrTaState();

        Assert.Null(state.Accept(CreateWrite("PolygonHeader", 0x8484_0000, 4, "PunchThroughPolygon")));
        for (var index = 0; index < 7; index++)
        {
            Assert.Null(state.Accept(CreateWrite("Unknown", 0)));
        }

        Assert.Null(AcceptRealVertexPacket(state, "Vertex", 0x3F80_0000, 0x3F80_0000, 0xFFFF_0000));
        Assert.Null(AcceptRealVertexPacket(state, "Vertex", 0x4000_0000, 0x3F80_0000, 0xFFFF_0000));
        var render = AcceptRealVertexPacket(state, "VertexEndOfStrip", 0x3F80_0000, 0x4000_0000, 0xFFFF_0000);

        Assert.NotNull(render);
        var strip = Assert.Single(state.CompletedStrips);
        Assert.Equal(4, strip.ListType);
        Assert.Equal("PunchThroughPolygon", strip.ListTypeName);
        Assert.Equal("0x84840000", strip.HeaderValueHex);
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

    [Fact]
    public void CompletesSpritePacketWithHeaderFaceColor()
    {
        var state = new DreamcastPvrTaState();

        Assert.Null(state.Accept(CreateWrite("SpriteHeader", 0xA084_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x0000_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x0000_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x0000_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0xFFFF_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x0000_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x0000_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x0000_0000)));
        var render = AcceptSpritePacket(state);

        Assert.NotNull(render);
        Assert.Equal(0xF800, render.Rgb565);
        var sprite = Assert.Single(state.CompletedSprites);
        Assert.Equal("0xA0840000", sprite.HeaderValueHex);
        Assert.Equal("0xFFFF0000", sprite.HeaderPayload.ArgbHex);
        Assert.Equal("0xF0000000", sprite.ControlValueHex);
        Assert.True(sprite.EndOfStrip);
        Assert.Collection(
            sprite.PayloadWords,
            word =>
            {
                Assert.Equal("Ax", word.Name);
                Assert.Equal("0x3F800000", word.ValueHex);
            },
            word =>
            {
                Assert.Equal("Ay", word.Name);
                Assert.Equal("0x3F800000", word.ValueHex);
            },
            word => Assert.Equal("Az", word.Name),
            word => Assert.Equal("Bx", word.Name),
            word => Assert.Equal("By", word.Name),
            word => Assert.Equal("Bz", word.Name),
            word => Assert.Equal("Cx", word.Name),
            word => Assert.Equal("Cy", word.Name),
            word => Assert.Equal("Cz", word.Name),
            word => Assert.Equal("Dx", word.Name),
            word => Assert.Equal("Dy", word.Name),
            word => Assert.Equal("Dummy0", word.Name),
            word => Assert.Equal("Dummy1", word.Name),
            word => Assert.Equal("Dummy2", word.Name),
            word => Assert.Equal("Dummy3", word.Name));
        Assert.Collection(
            sprite.Vertices,
            vertex =>
            {
                Assert.Equal("A", vertex.Name);
                Assert.Equal(1, vertex.X);
                Assert.Equal(1, vertex.Y);
            },
            vertex =>
            {
                Assert.Equal("B", vertex.Name);
                Assert.Equal(3, vertex.X);
                Assert.Equal(1, vertex.Y);
            },
            vertex =>
            {
                Assert.Equal("C", vertex.Name);
                Assert.Equal(1, vertex.X);
                Assert.Equal(3, vertex.Y);
            },
            vertex =>
            {
                Assert.Equal("D", vertex.Name);
                Assert.Equal(3, vertex.X);
                Assert.Equal(3, vertex.Y);
            });
    }

    [Fact]
    public void TreatsSubpixelWidthSpriteAsRenderablePreviewArea()
    {
        var state = new DreamcastPvrTaState();

        Assert.Null(state.Accept(CreateWrite("SpriteHeader", 0xA084_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x0000_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x0000_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x0000_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0xFFFF_FFFF)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x0000_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x0000_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x0000_0000)));

        var render = AcceptSpritePacket(
            state,
            ax: 0x437A_0000,
            ay: 0x43A8_0000,
            bx: 0x437A_3800,
            by: 0x43A8_0000,
            cx: 0x437A_3800,
            cy: 0x43A8_C000,
            dx: 0x437A_0000,
            dy: 0x43A8_C000);

        Assert.NotNull(render);
        var sprite = Assert.Single(state.CompletedSprites);
        Assert.Equal(250, sprite.Vertices[0].X);
        Assert.Equal(250, sprite.Vertices[1].X);
        Assert.True(sprite.HasFinitePreviewCoordinates);
        Assert.True(sprite.HasRenderablePreviewArea);
    }

    [Fact]
    public void CompletesSpritePacketWithNonFiniteCoordinatesForDiagnostics()
    {
        var state = new DreamcastPvrTaState();

        Assert.Null(state.Accept(CreateWrite("SpriteHeader", 0xA000_0009)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x8000_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x2088_04C0)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x0000_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0xFF0C_0C0C)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x0000_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x0000_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x0000_0000)));
        var render = AcceptNonFiniteSpritePacket(state);

        Assert.NotNull(render);
        var sprite = Assert.Single(state.CompletedSprites);
        Assert.Equal("0xA0000009", sprite.HeaderValueHex);
        Assert.Equal("0x0861", sprite.Rgb565Hex);
        Assert.False(sprite.HasFinitePreviewCoordinates);
        Assert.False(sprite.HasRenderablePreviewArea);
        Assert.All(sprite.Vertices, vertex => Assert.False(vertex.HasFinitePosition));
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

    private static DreamcastPvrTaRenderCommand? AcceptSpritePacket(DreamcastPvrTaState state)
        => AcceptSpritePacket(
            state,
            ax: 0x3F80_0000,
            ay: 0x3F80_0000,
            bx: 0x4040_0000,
            by: 0x3F80_0000,
            cx: 0x3F80_0000,
            cy: 0x4040_0000,
            dx: 0x4040_0000,
            dy: 0x4040_0000);

    private static DreamcastPvrTaRenderCommand? AcceptSpritePacket(
        DreamcastPvrTaState state,
        uint ax,
        uint ay,
        uint bx,
        uint by,
        uint cx,
        uint cy,
        uint dx,
        uint dy)
    {
        Assert.Null(state.Accept(CreateWrite("VertexEndOfStrip", 0xF000_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", ax)));
        Assert.Null(state.Accept(CreateWrite("Unknown", ay)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x3F80_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", bx)));
        Assert.Null(state.Accept(CreateWrite("Unknown", by)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x3F80_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", cx)));
        Assert.Null(state.Accept(CreateWrite("Unknown", cy)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x3F80_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", dx)));
        Assert.Null(state.Accept(CreateWrite("Unknown", dy)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0)));
        return state.Accept(CreateWrite("Unknown", 0));
    }

    private static DreamcastPvrTaRenderCommand? AcceptNonFiniteSpritePacket(DreamcastPvrTaState state)
    {
        Assert.Null(state.Accept(CreateWrite("VertexEndOfStrip", 0xF000_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0xFFC0_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0xFFC0_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x43B5_8A2C)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0xFFC0_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0xFFC0_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x43B5_8A2C)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0xFFC0_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0xFFC0_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0x43B5_8A2C)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0xFFC0_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0xFFC0_0000)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0)));
        Assert.Null(state.Accept(CreateWrite("Unknown", 0)));
        return state.Accept(CreateWrite("Unknown", 0));
    }

    private static DreamcastPvrTaCommandWrite CreateWrite(string kind, uint value, int listType = 0, string listTypeName = "OpaquePolygon") =>
        new(
            0x1000_0000,
            "0x10000000",
            "TA_INPUT",
            kind,
            listType,
            listTypeName,
            (value & 0x1000_0000) != 0,
            4,
            value,
            $"0x{value:X8}");
}
