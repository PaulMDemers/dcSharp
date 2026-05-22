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

    [Fact]
    public void LessDepthCompareOverwritesFartherPreviewPixels()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 4];
        var depth = CreateDepthBuffer(vram);

        DreamcastPvrPreviewRenderer.RenderStrip(CreateStrip(0x07E0, [(1, 1), (2, 1), (1, 2)], z: 0.5f, depthCompare: "Always"), vram, depth);
        DreamcastPvrPreviewRenderer.RenderStrip(CreateStrip(0xF800, [(1, 1), (2, 1), (1, 2)], z: 0.25f, depthCompare: "Less"), vram, depth);

        Assert.Equal(0xF800, ReadRgb565(vram, 0, 0));
    }

    [Fact]
    public void GreaterDepthCompareRejectsFartherPreviewPixels()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 4];
        var depth = CreateDepthBuffer(vram);

        DreamcastPvrPreviewRenderer.RenderStrip(CreateStrip(0x07E0, [(1, 1), (2, 1), (1, 2)], z: 0.5f, depthCompare: "Always"), vram, depth);
        DreamcastPvrPreviewRenderer.RenderStrip(CreateStrip(0xF800, [(1, 1), (2, 1), (1, 2)], z: 0.25f, depthCompare: "Greater"), vram, depth);

        Assert.Equal(0x07E0, ReadRgb565(vram, 0, 0));
    }

    [Fact]
    public void DepthWriteDisabledDoesNotUpdatePreviewDepth()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 4];
        var depth = CreateDepthBuffer(vram);

        DreamcastPvrPreviewRenderer.RenderStrip(CreateStrip(0x07E0, [(1, 1), (2, 1), (1, 2)], z: 0.5f, depthCompare: "Always", depthWriteDisabled: true), vram, depth);
        DreamcastPvrPreviewRenderer.RenderStrip(CreateStrip(0xF800, [(1, 1), (2, 1), (1, 2)], z: 0.25f, depthCompare: "Greater"), vram, depth);

        Assert.Equal(0xF800, ReadRgb565(vram, 0, 0));
    }

    [Fact]
    public void AlphaBlendUsesSourceAlphaAndDestinationPixel()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 4];

        DreamcastPvrPreviewRenderer.RenderStrip(CreateStrip(0x07E0, [(1, 1), (2, 1), (1, 2)], argb: 0xFF00_FF00), vram);
        DreamcastPvrPreviewRenderer.RenderStrip(
            CreateStrip(
                0xF800,
                [(1, 1), (2, 1), (1, 2)],
                argb: 0x80FF_0000,
                alphaEnabled: true,
                blendSrc: "SrcAlpha",
                blendDst: "InverseSrcAlpha"),
            vram);

        Assert.Equal(0x83E0, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x83E0, ReadRgb565(vram, 1, 0));
        Assert.Equal(0x83E0, ReadRgb565(vram, 0, 1));
        Assert.Equal(0x0000, ReadRgb565(vram, 1, 1));
    }

    [Fact]
    public void SamplesRgb565TextureWhenModeEnablesSimpleTexture()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 4];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, 0, 0, 0xF800);
        WriteTexturePixel(vram, textureBase, 7, 0, 0x07E0);
        WriteTexturePixel(vram, textureBase, 0, 7, 0x001F);

        DreamcastPvrPreviewRenderer.RenderStrip(
            CreateStrip(
                0xFFFF,
                [(1, 1), (2, 1), (1, 2)],
                textureEnabled: true,
                nonTwiddled: true,
                textureBase: textureBase),
            vram);

        Assert.Equal(0xF800, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 1, 0));
        Assert.Equal(0x001F, ReadRgb565(vram, 0, 1));
        Assert.Equal(0x0000, ReadRgb565(vram, 1, 1));
    }

    [Fact]
    public void SamplesTwiddledRgb565TextureWhenModeUsesTwiddledLayout()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 4];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, TwiddledTextureIndex(0, 0), 0xF800);
        WriteTexturePixel(vram, textureBase, TwiddledTextureIndex(7, 0), 0x07E0);
        WriteTexturePixel(vram, textureBase, TwiddledTextureIndex(0, 7), 0x001F);

        DreamcastPvrPreviewRenderer.RenderStrip(
            CreateStrip(
                0xFFFF,
                [(1, 1), (2, 1), (1, 2)],
                textureEnabled: true,
                nonTwiddled: false,
                textureBase: textureBase),
            vram);

        Assert.Equal(0xF800, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 1, 0));
        Assert.Equal(0x001F, ReadRgb565(vram, 0, 1));
        Assert.Equal(0x0000, ReadRgb565(vram, 1, 1));
    }

    [Fact]
    public void SamplesArgb1555TextureAsRgb565PreviewPixels()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 4];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, 0, 0, 0xFC00);
        WriteTexturePixel(vram, textureBase, 7, 0, 0x83E0);
        WriteTexturePixel(vram, textureBase, 0, 7, 0x801F);

        DreamcastPvrPreviewRenderer.RenderStrip(
            CreateStrip(
                0xFFFF,
                [(1, 1), (2, 1), (1, 2)],
                textureEnabled: true,
                nonTwiddled: true,
                pixelFormat: 0,
                textureBase: textureBase),
            vram);

        Assert.Equal(0xF800, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 1, 0));
        Assert.Equal(0x001F, ReadRgb565(vram, 0, 1));
        Assert.Equal(0x0000, ReadRgb565(vram, 1, 1));
    }

    private static DreamcastPvrTaStrip CreateStrip(
        ushort color,
        IReadOnlyList<(int X, int Y)> points,
        string? culling = null,
        float z = 1.0f,
        string? depthCompare = null,
        bool depthWriteDisabled = false,
        uint? argb = null,
        bool alphaEnabled = false,
        string blendSrc = "One",
        string blendDst = "Zero",
        bool textureEnabled = false,
        bool nonTwiddled = false,
        uint pixelFormat = 1,
        uint textureBase = 0) =>
        new(
            "TA_INPUT",
            0,
            "OpaquePolygon",
            0x8084_0000,
            "0x80840000",
            CreateHeaderPayload(culling, depthCompare, depthWriteDisabled, alphaEnabled, blendSrc, blendDst, textureEnabled, nonTwiddled, pixelFormat, textureBase),
            color,
            $"0x{color:X4}",
            points.Select((point, index) => new DreamcastPvrTaVertex(
                point.X,
                point.Y,
                z,
                SingleToUInt32Bits(z),
                $"0x{SingleToUInt32Bits(z):X8}",
                TextureU(point, points),
                SingleToUInt32Bits(TextureU(point, points)),
                $"0x{SingleToUInt32Bits(TextureU(point, points)):X8}",
                TextureV(point, points),
                SingleToUInt32Bits(TextureV(point, points)),
                $"0x{SingleToUInt32Bits(TextureV(point, points)):X8}",
                index == points.Count - 1,
                color,
                $"0x{color:X4}",
                index == points.Count - 1 ? 0xF000_0000 : 0xE000_0000,
                index == points.Count - 1 ? "0xF0000000" : "0xE0000000",
                (uint)point.X << 16,
                $"0x{(uint)point.X << 16:X8}",
                (uint)point.Y << 16,
                $"0x{(uint)point.Y << 16:X8}",
                argb ?? color,
                $"0x{argb ?? color:X8}")).ToArray());

    private static DreamcastPvrTaPolygonHeaderPayload? CreateHeaderPayload(
        string? culling,
        string? depthCompare,
        bool depthWriteDisabled,
        bool alphaEnabled,
        string blendSrc,
        string blendDst,
        bool textureEnabled,
        bool nonTwiddled,
        uint pixelFormat,
        uint textureBase)
    {
        if (culling is null && depthCompare is null && !depthWriteDisabled && !alphaEnabled && !textureEnabled)
        {
            return null;
        }

        var cullingBits = (culling ?? "None") switch
        {
            "None" => 0u,
            "Small" => 1u,
            "Ccw" => 2u,
            "Cw" => 3u,
            _ => throw new ArgumentOutOfRangeException(nameof(culling), culling, "Unknown culling mode.")
        };
        var depthCompareBits = (depthCompare ?? "Never") switch
        {
            "Never" => 0u,
            "Less" => 1u,
            "Equal" => 2u,
            "LessOrEqual" => 3u,
            "Greater" => 4u,
            "NotEqual" => 5u,
            "GreaterOrEqual" => 6u,
            "Always" => 7u,
            _ => throw new ArgumentOutOfRangeException(nameof(depthCompare), depthCompare, "Unknown depth compare mode.")
        };
        var mode1 = (depthCompareBits << 29)
            | (cullingBits << 27)
            | (textureEnabled ? 0x0200_0000u : 0)
            | (depthWriteDisabled ? 0x0400_0000u : 0);
        var mode2 = BlendBits(blendSrc) << 29
            | BlendBits(blendDst) << 26
            | (alphaEnabled ? 0x0010_0000u : 0);
        var mode3 = textureBase | (pixelFormat << 27) | (nonTwiddled ? 0x0400_0000u : 0);
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
        return DreamcastPvrTaPolygonHeaderPayloadDecoder.DecodePayload(header, [mode1, mode2, mode3, 0, 0, 0, 0]);
    }

    private static uint BlendBits(string blend) =>
        blend switch
        {
            "Zero" => 0,
            "One" => 1,
            "DestColor" => 2,
            "InverseDestColor" => 3,
            "SrcAlpha" => 4,
            "InverseSrcAlpha" => 5,
            "DestAlpha" => 6,
            "InverseDestAlpha" => 7,
            _ => throw new ArgumentOutOfRangeException(nameof(blend), blend, "Unknown blend mode.")
        };

    private static float[] CreateDepthBuffer(byte[] vram)
    {
        var depth = new float[vram.Length / 2];
        Array.Fill(depth, float.NaN);
        return depth;
    }

    private static uint SingleToUInt32Bits(float value) =>
        BitConverter.SingleToUInt32Bits(value);

    private static void WriteTexturePixel(byte[] vram, uint textureBase, int x, int y, ushort value)
    {
        var offset = (int)textureBase + (((y * 8) + x) * 2);
        vram[offset] = (byte)(value & 0xFF);
        vram[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteTexturePixel(byte[] vram, uint textureBase, int texelIndex, ushort value)
    {
        var offset = (int)textureBase + (texelIndex * 2);
        vram[offset] = (byte)(value & 0xFF);
        vram[offset + 1] = (byte)(value >> 8);
    }

    private static int TwiddledTextureIndex(int x, int y)
    {
        var index = 0;
        for (var bit = 0; bit < 16; bit++)
        {
            index |= ((x >> bit) & 1) << (bit * 2);
            index |= ((y >> bit) & 1) << ((bit * 2) + 1);
        }

        return index;
    }

    private static float TextureU((int X, int Y) point, IReadOnlyList<(int X, int Y)> points)
    {
        var minX = points.Min(vertex => vertex.X);
        var maxX = points.Max(vertex => vertex.X);
        return maxX == minX ? 0.0f : (point.X - minX) / (float)(maxX - minX);
    }

    private static float TextureV((int X, int Y) point, IReadOnlyList<(int X, int Y)> points)
    {
        var minY = points.Min(vertex => vertex.Y);
        var maxY = points.Max(vertex => vertex.Y);
        return maxY == minY ? 0.0f : (point.Y - minY) / (float)(maxY - minY);
    }

    private static ushort ReadRgb565(byte[] vram, int x, int y)
    {
        var offset = ((y * DreamcastPvrPreviewRenderer.Width) + x) * 2;
        return (ushort)(vram[offset] | (vram[offset + 1] << 8));
    }
}
