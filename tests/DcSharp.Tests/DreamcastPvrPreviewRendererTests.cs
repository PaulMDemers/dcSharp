using DcSharp.Core.Dreamcast.Video;

namespace DcSharp.Tests;

public class DreamcastPvrPreviewRendererTests
{
    [Fact]
    public void RendersSmallTrianglePreview()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 4];
        var strip = CreateStrip(0xF800, [(1, 1), (2, 1), (1, 2)]);

        DreamcastPvrPreviewRenderer.RenderStrip(strip, vram);

        Assert.Equal(0xF800, ReadRgb565(vram, 0, 0));
        Assert.Equal(0xF800, ReadRgb565(vram, 1, 0));
        Assert.Equal(0xF800, ReadRgb565(vram, 0, 1));
        Assert.Equal(0x0000, ReadRgb565(vram, 1, 1));
    }

    [Fact]
    public void RendersWiderTrianglePreview()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 4];
        var strip = CreateStrip(0x07E0, [(1, 1), (3, 1), (1, 2)]);

        DreamcastPvrPreviewRenderer.RenderStrip(strip, vram);

        Assert.Equal(0x07E0, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 1, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 2, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 0, 1));
        Assert.Equal(0x0000, ReadRgb565(vram, 1, 1));
    }

    [Fact]
    public void IgnoresIncompleteStrips()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 2];
        var strip = CreateStrip(0xF800, [(1, 1), (2, 1)]);

        DreamcastPvrPreviewRenderer.RenderStrip(strip, vram);

        Assert.All(vram, value => Assert.Equal(0, value));
    }

    [Fact]
    public void RendersCounterClockwiseCullingForAcceptedWinding()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 4];
        var strip = CreateStrip(0x07E0, [(1, 1), (2, 1), (1, 2)], culling: "Ccw");

        DreamcastPvrPreviewRenderer.RenderStrip(strip, vram);

        Assert.Equal(0x07E0, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 1, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 0, 1));
    }

    [Fact]
    public void CullsClockwiseModeForOppositeWinding()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 4];
        var strip = CreateStrip(0xF800, [(1, 1), (2, 1), (1, 2)], culling: "Cw");

        DreamcastPvrPreviewRenderer.RenderStrip(strip, vram);

        Assert.All(vram, value => Assert.Equal(0, value));
    }

    [Fact]
    public void DoesNotCullWhenHeaderPayloadIsAbsent()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 4];
        var strip = CreateStrip(0xF800, [(1, 1), (2, 1), (1, 2)], culling: null);

        DreamcastPvrPreviewRenderer.RenderStrip(strip, vram);

        Assert.Equal(0xF800, ReadRgb565(vram, 0, 0));
    }

    private static DreamcastPvrTaStrip CreateStrip(ushort color, IReadOnlyList<(int X, int Y)> points, string? culling = null) =>
        new(
            "TA_INPUT",
            0,
            "OpaquePolygon",
            0x8084_0000,
            "0x80840000",
            CreateHeaderPayload(culling),
            color,
            $"0x{color:X4}",
            points.Select((point, index) => new DreamcastPvrTaVertex(
                point.X,
                point.Y,
                index == points.Count - 1,
                color,
                $"0x{color:X4}",
                index == points.Count - 1 ? 0xF000_0000 : 0xE000_0000,
                index == points.Count - 1 ? "0xF0000000" : "0xE0000000",
                (uint)point.X << 16,
                $"0x{(uint)point.X << 16:X8}",
                (uint)point.Y << 16,
                $"0x{(uint)point.Y << 16:X8}",
                color,
                $"0x{color:X8}")).ToArray());

    private static DreamcastPvrTaPolygonHeaderPayload? CreateHeaderPayload(string? culling)
    {
        if (culling is null)
        {
            return null;
        }

        var cullingBits = culling switch
        {
            "None" => 0u,
            "Small" => 1u,
            "Ccw" => 2u,
            "Cw" => 3u,
            _ => throw new ArgumentOutOfRangeException(nameof(culling), culling, "Unknown culling mode.")
        };
        var header = new DreamcastPvrTaCommandWrite(
            0x1000_0000,
            "0x10000000",
            "TA_INPUT",
            "PolygonHeader",
            0,
            "OpaquePolygon",
            false,
            4,
            0x8084_0000,
            "0x80840000");
        return DreamcastPvrTaPolygonHeaderPayloadDecoder.DecodePayload(header, [(cullingBits << 27), 0, 0, 0, 0, 0, 0]);
    }

    private static ushort ReadRgb565(byte[] vram, int x, int y)
    {
        var offset = ((y * DreamcastPvrPreviewRenderer.Width) + x) * 2;
        return (ushort)(vram[offset] | (vram[offset + 1] << 8));
    }
}
