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
    public void CanRenderStripPreviewInScreenCoordinates()
    {
        var vram = new byte[4096];
        var strip = CreateStrip(0x07E0, [(4, 3), (5, 3), (4, 4)]);

        DreamcastPvrPreviewRenderer.RenderStrip(strip, vram, useScreenCoordinates: true);

        Assert.Equal(0x0000, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 4, 3));
        Assert.Equal(0x07E0, ReadRgb565(vram, 5, 3));
        Assert.Equal(0x07E0, ReadRgb565(vram, 4, 4));
    }

    [Fact]
    public void RendersContinuationTrianglesAfterFirstThreeVertices()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 8];
        var strip = CreateStrip(0x07E0, [(1, 1), (3, 1), (1, 3), (3, 3)]);

        DreamcastPvrPreviewRenderer.RenderStrip(strip, vram);

        Assert.Equal(0x07E0, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 2, 2));
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
    public void LaterSpriteOverwritesStripPreviewPixels()
    {
        var vram = new byte[4096];

        DreamcastPvrPreviewRenderer.RenderStrip(CreateStrip(0xF800, [(1, 1), (3, 1), (1, 3)], argb: 0xFFFF_0000), vram);
        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0x07E0,
                [(1, 1, 0.0f, 0.0f), (3, 1, 0.0f, 0.0f), (3, 3, 0.0f, 0.0f), (1, 3, 0.0f, 0.0f)],
                argb: 0xFF00_FF00),
            vram);

        Assert.Equal(0x07E0, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 1, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 0, 1));
    }

    [Fact]
    public void SkipsSpriteWithNonFinitePreviewCoordinates()
    {
        var vram = new byte[4096];
        var sprite = CreateSprite(
            0x07E0,
            [(1, 1, 0.0f, 0.0f), (3, 1, 0.0f, 0.0f), (3, 3, 0.0f, 0.0f), (1, 3, 0.0f, 0.0f)],
            argb: 0xFF00_FF00);
        var invalidSprite = sprite with
        {
            Vertices = sprite.Vertices
                .Select(vertex => vertex with
                {
                    XValue = 0xFFC0_0000,
                    XValueHex = "0xFFC00000"
                })
                .ToArray()
        };

        Assert.False(invalidSprite.HasFinitePreviewCoordinates);
        Assert.False(invalidSprite.HasRenderablePreviewArea);

        DreamcastPvrPreviewRenderer.RenderSprite(invalidSprite, vram);

        Assert.Equal(0x0000, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x0000, ReadRgb565(vram, 1, 0));
        Assert.Equal(0x0000, ReadRgb565(vram, 0, 1));
    }

    [Fact]
    public void RendersFiniteDegenerateSpriteAsVerticalPreviewLine()
    {
        var vram = new byte[4096];
        var sprite = CreateSprite(
            0x07E0,
            [(4, 4, 0.0f, 0.0f), (4, 4, 0.0f, 0.0f), (4, 6, 0.0f, 1.0f), (4, 6, 0.0f, 1.0f)],
            argb: 0xFF00_FF00);

        Assert.True(sprite.HasFinitePreviewCoordinates);
        Assert.False(sprite.HasRenderablePreviewArea);

        DreamcastPvrPreviewRenderer.RenderSprite(sprite, vram);

        Assert.Equal(0x07E0, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 0, 1));
        Assert.Equal(0x07E0, ReadRgb565(vram, 0, 2));
        Assert.Equal(0x0000, ReadRgb565(vram, 1, 0));
    }

    [Fact]
    public void RendersFiniteDegenerateSpriteAsHorizontalPreviewLine()
    {
        var vram = new byte[4096];
        var sprite = CreateSprite(
            0x001F,
            [(5, 7, 0.0f, 0.0f), (7, 7, 1.0f, 0.0f), (7, 7, 1.0f, 0.0f), (5, 7, 0.0f, 0.0f)],
            argb: 0xFF00_00FF);

        Assert.True(sprite.HasFinitePreviewCoordinates);
        Assert.False(sprite.HasRenderablePreviewArea);

        DreamcastPvrPreviewRenderer.RenderSprite(sprite, vram);

        Assert.Equal(0x001F, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x001F, ReadRgb565(vram, 1, 0));
        Assert.Equal(0x001F, ReadRgb565(vram, 2, 0));
        Assert.Equal(0x0000, ReadRgb565(vram, 0, 1));
    }

    [Fact]
    public void CanRenderSpritePreviewInScreenCoordinates()
    {
        var vram = new byte[4096];

        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0x07E0,
                [(4, 3, 0.0f, 0.0f), (5, 3, 0.0f, 0.0f), (5, 4, 0.0f, 0.0f), (4, 4, 0.0f, 0.0f)],
                argb: 0xFF00_FF00),
            vram,
            useScreenCoordinates: true);

        Assert.Equal(0x0000, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 4, 3));
        Assert.Equal(0x07E0, ReadRgb565(vram, 5, 3));
        Assert.Equal(0x07E0, ReadRgb565(vram, 4, 4));
    }

    [Fact]
    public void CanRenderSpritePreviewWithWideStride()
    {
        const int previewWidth = 640;
        var vram = new byte[previewWidth * 8 * 2];

        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0x07E0,
                [(500, 3, 0.0f, 0.0f), (501, 3, 0.0f, 0.0f), (501, 4, 0.0f, 0.0f), (500, 4, 0.0f, 0.0f)],
                argb: 0xFF00_FF00),
            vram,
            previewWidth,
            useScreenCoordinates: true);

        Assert.Equal(0x07E0, ReadRgb565(vram, 500, 3, previewWidth));
        Assert.Equal(0x07E0, ReadRgb565(vram, 501, 3, previewWidth));
        Assert.Equal(0x07E0, ReadRgb565(vram, 500, 4, previewWidth));
        Assert.Equal(0x0000, ReadRgb565(vram, 319, 3, previewWidth));
    }

    [Fact]
    public void CanRenderSpritePreviewAtTargetPixelOffset()
    {
        var vram = new byte[4096];

        var stats = DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0x07E0,
                [(1, 1, 0.0f, 0.0f), (2, 1, 0.0f, 0.0f), (2, 2, 0.0f, 0.0f), (1, 2, 0.0f, 0.0f)],
                argb: 0xFF00_FF00),
            vram,
            DreamcastPvrPreviewRenderer.Width,
            useScreenCoordinates: false,
            targetPixelOffset: 16);

        Assert.Equal(0x0000, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 16, 0));
        Assert.Equal(1, stats.SpriteCalls);
        Assert.True(stats.PixelWriteAttempts >= stats.PixelsWritten);
        Assert.True(stats.PixelsWritten > 0);
        Assert.True(stats.UniquePixelsWritten > 0);
        Assert.True(stats.UniquePixelsWritten <= stats.PixelsWritten);
        Assert.Equal(0, stats.ZeroRgbWritePixels);
        Assert.Equal(0, stats.SubpixelFallbacks);
    }

    [Fact]
    public void RendersSubpixelSpriteFootprintForThinRenderableQuad()
    {
        const int previewWidth = 640;
        var vram = new byte[previewWidth * 8 * 2];

        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0x07E0,
                [(250, 2, 0.0f, 0.0f), (250, 2, 0.0f, 0.0f), (250, 4, 0.0f, 0.0f), (250, 4, 0.0f, 0.0f)],
                argb: 0xFF00_FF00,
                xValues:
                [
                    0x437A_0000,
                    0x437A_3800,
                    0x437A_3800,
                    0x437A_0000
                ]),
            vram,
            previewWidth,
            useScreenCoordinates: true);

        Assert.Equal(0x07E0, ReadRgb565(vram, 250, 3, previewWidth));
    }

    [Fact]
    public void RendersThinSpriteFootprintAcrossSpannedColumns()
    {
        const int previewWidth = 640;
        var vram = new byte[previewWidth * 8 * 2];

        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0x07E0,
                [(251, 2, 0.0f, 0.0f), (251, 2, 0.0f, 0.0f), (251, 4, 0.0f, 0.0f), (251, 4, 0.0f, 0.0f)],
                argb: 0xFF00_FF00,
                xValues:
                [
                    SingleToUInt32Bits(250.75f),
                    SingleToUInt32Bits(251.25f),
                    SingleToUInt32Bits(251.25f),
                    SingleToUInt32Bits(250.75f)
                ]),
            vram,
            previewWidth,
            useScreenCoordinates: true);

        Assert.Equal(0x07E0, ReadRgb565(vram, 250, 3, previewWidth));
        Assert.Equal(0x07E0, ReadRgb565(vram, 251, 3, previewWidth));
        Assert.Equal(0x0000, ReadRgb565(vram, 252, 3, previewWidth));
    }

    [Fact]
    public void LaterStripOverwritesSpritePreviewPixels()
    {
        var vram = new byte[4096];

        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0x07E0,
                [(1, 1, 0.0f, 0.0f), (3, 1, 0.0f, 0.0f), (3, 3, 0.0f, 0.0f), (1, 3, 0.0f, 0.0f)],
                argb: 0xFF00_FF00),
            vram);
        DreamcastPvrPreviewRenderer.RenderStrip(CreateStrip(0xF800, [(1, 1), (3, 1), (1, 3)], argb: 0xFFFF_0000), vram);

        Assert.Equal(0xF800, ReadRgb565(vram, 0, 0));
        Assert.Equal(0xF800, ReadRgb565(vram, 1, 0));
        Assert.Equal(0xF800, ReadRgb565(vram, 0, 1));
    }

    [Fact]
    public void TranslucentListBlendsOverOpaquePreviewPixels()
    {
        var vram = new byte[4096];

        DreamcastPvrPreviewRenderer.RenderStrip(CreateStrip(0x07E0, [(1, 1), (2, 1), (1, 2)], argb: 0xFF00_FF00), vram);
        DreamcastPvrPreviewRenderer.RenderStrip(
            CreateStrip(
                0xF800,
                [(1, 1), (2, 1), (1, 2)],
                listType: 2,
                listTypeName: "TranslucentPolygon",
                headerValue: 0x8284_0000,
                argb: 0x80FF_0000,
                alphaEnabled: true,
                blendSrc: "SrcAlpha",
                blendDst: "InverseSrcAlpha"),
            vram);

        Assert.Equal(0x83E0, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x83E0, ReadRgb565(vram, 1, 0));
        Assert.Equal(0x83E0, ReadRgb565(vram, 0, 1));
    }

    [Fact]
    public void TranslucentSpriteListBlendsArgbAlphaWhenAlphaModeIsDisabled()
    {
        var vram = new byte[4096];

        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0x07E0,
                [(1, 1, 0.0f, 0.0f), (3, 1, 0.0f, 0.0f), (3, 3, 0.0f, 0.0f), (1, 3, 0.0f, 0.0f)],
                argb: 0xFF00_FF00),
            vram);
        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0xF800,
                [(1, 1, 0.0f, 0.0f), (3, 1, 0.0f, 0.0f), (3, 3, 0.0f, 0.0f), (1, 3, 0.0f, 0.0f)],
                argb: 0x80FF_0000,
                listType: 2,
                listTypeName: "TranslucentPolygon",
                headerValue: 0xA200_0001),
            vram);

        Assert.Equal(0x83E0, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x83E0, ReadRgb565(vram, 1, 0));
        Assert.Equal(0x83E0, ReadRgb565(vram, 0, 1));
    }

    [Fact]
    public void PunchThroughListDiscardsLowAlphaPreviewPixels()
    {
        var vram = new byte[4096];

        DreamcastPvrPreviewRenderer.RenderStrip(CreateStrip(0x07E0, [(1, 1), (2, 1), (1, 2)], argb: 0xFF00_FF00), vram);
        DreamcastPvrPreviewRenderer.RenderStrip(
            CreateStrip(
                0xF800,
                [(1, 1), (2, 1), (1, 2)],
                listType: 4,
                listTypeName: "PunchThroughPolygon",
                headerValue: 0x8484_0000,
                argb: 0x00FF_0000),
            vram);

        Assert.Equal(0x07E0, ReadRgb565(vram, 0, 0));

        DreamcastPvrPreviewRenderer.RenderStrip(
            CreateStrip(
                0x001F,
                [(1, 1), (2, 1), (1, 2)],
                listType: 4,
                listTypeName: "PunchThroughPolygon",
                headerValue: 0x8484_0000,
                argb: 0xFF00_00FF),
            vram);

        Assert.Equal(0x001F, ReadRgb565(vram, 0, 0));
    }

    [Fact]
    public void InterpolatesGouraudVertexColors()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 8];

        DreamcastPvrPreviewRenderer.RenderStrip(
            CreateStrip(
                0xF800,
                [(1, 1), (3, 1), (1, 3)],
                gouraud: true,
                vertexColors: [0xF800, 0x07E0, 0x001F]),
            vram);

        Assert.Equal(0xF800, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 2, 0));
        Assert.Equal(0x001F, ReadRgb565(vram, 0, 2));
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
    public void SamplesRgb565TextureForSprite()
    {
        var vram = new byte[4096];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, 0, 0, 0xF800);
        WriteTexturePixel(vram, textureBase, 7, 0, 0x07E0);
        WriteTexturePixel(vram, textureBase, 4, 4, 0xFFFF);

        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0xFFFF,
                [(1, 1, 0.0f, 0.0f), (3, 1, 1.0f, 0.0f), (3, 3, 1.0f, 1.0f), (1, 3, 0.0f, 1.0f)],
                textureEnabled: true,
                nonTwiddled: true,
                textureBase: textureBase),
            vram);

        Assert.Equal(0xF800, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 2, 0));
        Assert.Equal(0xFFFF, ReadRgb565(vram, 1, 1));
    }

    [Fact]
    public void RendersSkewedSpriteAsQuad()
    {
        var vram = new byte[4096];

        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0x07E0,
                [(2, 1, 0.0f, 0.0f), (3, 2, 0.0f, 0.0f), (2, 3, 0.0f, 0.0f), (1, 2, 0.0f, 0.0f)],
                argb: 0xFF00_FF00),
            vram);

        Assert.Equal(0x0000, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 1, 0));
        Assert.Equal(0x0000, ReadRgb565(vram, 2, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 0, 1));
        Assert.Equal(0x07E0, ReadRgb565(vram, 1, 1));
    }

    [Fact]
    public void SamplesTextureAcrossSkewedSpriteQuad()
    {
        var vram = new byte[4096];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, 0, 0, 0xF800);
        WriteTexturePixel(vram, textureBase, 7, 0, 0x07E0);
        WriteTexturePixel(vram, textureBase, 0, 7, 0x001F);
        WriteTexturePixel(vram, textureBase, 4, 4, 0xFFFF);

        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0xFFFF,
                [(2, 1, 0.0f, 0.0f), (3, 2, 1.0f, 0.0f), (2, 3, 1.0f, 1.0f), (1, 2, 0.0f, 1.0f)],
                textureEnabled: true,
                nonTwiddled: true,
                textureBase: textureBase),
            vram);

        Assert.Equal(0x0000, ReadRgb565(vram, 0, 0));
        Assert.Equal(0xF800, ReadRgb565(vram, 1, 0));
        Assert.Equal(0x0000, ReadRgb565(vram, 2, 0));
        Assert.Equal(0x001F, ReadRgb565(vram, 0, 1));
        Assert.Equal(0xFFFF, ReadRgb565(vram, 1, 1));
        Assert.Equal(0x07E0, ReadRgb565(vram, 2, 1));
    }

    [Fact]
    public void SamplesTwiddledRgb565TextureForSprite()
    {
        var vram = new byte[4096];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, TwiddledTextureIndex(0, 0), 0xF800);
        WriteTexturePixel(vram, textureBase, TwiddledTextureIndex(7, 0), 0x07E0);
        WriteTexturePixel(vram, textureBase, TwiddledTextureIndex(4, 4), 0xFFFF);

        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0xFFFF,
                [(1, 1, 0.0f, 0.0f), (3, 1, 1.0f, 0.0f), (3, 3, 1.0f, 1.0f), (1, 3, 0.0f, 1.0f)],
                textureEnabled: true,
                nonTwiddled: false,
                textureBase: textureBase),
            vram);

        Assert.Equal(0xF800, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 2, 0));
        Assert.Equal(0xFFFF, ReadRgb565(vram, 1, 1));
    }

    [Fact]
    public void SamplesArgb1555TextureForSprite()
    {
        var vram = new byte[4096];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, 0, 0, 0xFC00);
        WriteTexturePixel(vram, textureBase, 7, 0, 0x83E0);
        WriteTexturePixel(vram, textureBase, 4, 4, 0xFFFF);

        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0xFFFF,
                [(1, 1, 0.0f, 0.0f), (3, 1, 1.0f, 0.0f), (3, 3, 1.0f, 1.0f), (1, 3, 0.0f, 1.0f)],
                textureEnabled: true,
                nonTwiddled: true,
                pixelFormat: 0,
                textureBase: textureBase),
            vram);

        Assert.Equal(0xF800, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 2, 0));
        Assert.Equal(0xFFFF, ReadRgb565(vram, 1, 1));
    }

    [Fact]
    public void SamplesArgb4444TextureForSprite()
    {
        var vram = new byte[4096];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, 0, 0, 0xFF00);
        WriteTexturePixel(vram, textureBase, 7, 0, 0xF0F0);
        WriteTexturePixel(vram, textureBase, 4, 4, 0xFFFF);

        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0xFFFF,
                [(1, 1, 0.0f, 0.0f), (3, 1, 1.0f, 0.0f), (3, 3, 1.0f, 1.0f), (1, 3, 0.0f, 1.0f)],
                textureEnabled: true,
                nonTwiddled: true,
                pixelFormat: 2,
                textureBase: textureBase),
            vram);

        Assert.Equal(0xF800, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 2, 0));
        Assert.Equal(0xFFFF, ReadRgb565(vram, 1, 1));
    }

    [Fact]
    public void UsesArgb4444TextureAlphaForSpriteSourceBlend()
    {
        var vram = new byte[4096];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, 0, 0, 0x8F00);
        WriteTexturePixel(vram, textureBase, 7, 0, 0x8F00);
        WriteTexturePixel(vram, textureBase, 4, 4, 0x8F00);

        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0x07E0,
                [(1, 1, 0.0f, 0.0f), (3, 1, 1.0f, 0.0f), (3, 3, 1.0f, 1.0f), (1, 3, 0.0f, 1.0f)],
                argb: 0xFF00_FF00),
            vram);
        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0xFFFF,
                [(1, 1, 0.0f, 0.0f), (3, 1, 1.0f, 0.0f), (3, 3, 1.0f, 1.0f), (1, 3, 0.0f, 1.0f)],
                argb: 0xFFFF_FFFF,
                alphaEnabled: true,
                blendSrc: "SrcAlpha",
                blendDst: "InverseSrcAlpha",
                textureEnabled: true,
                nonTwiddled: true,
                pixelFormat: 2,
                textureBase: textureBase),
            vram);

        Assert.Equal(0x8BA0, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x8BA0, ReadRgb565(vram, 2, 0));
        Assert.Equal(0x8BA0, ReadRgb565(vram, 1, 1));
    }

    [Fact]
    public void ModulatesTextureColorForSprite()
    {
        var vram = new byte[4096];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, 0, 0, 0xFFFF);
        WriteTexturePixel(vram, textureBase, 7, 0, 0xFFFF);
        WriteTexturePixel(vram, textureBase, 4, 4, 0xFFFF);

        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0x07E0,
                [(1, 1, 0.0f, 0.0f), (3, 1, 1.0f, 0.0f), (3, 3, 1.0f, 1.0f), (1, 3, 0.0f, 1.0f)],
                argb: 0xFF00_FF00,
                textureEnabled: true,
                nonTwiddled: true,
                textureShading: "Modulate",
                textureBase: textureBase),
            vram);

        Assert.Equal(0x07E0, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 2, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 1, 1));
    }

    [Fact]
    public void DecalsTextureAlphaForSprite()
    {
        var vram = new byte[4096];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, 0, 0, 0x8F00);
        WriteTexturePixel(vram, textureBase, 7, 0, 0x8F00);
        WriteTexturePixel(vram, textureBase, 4, 4, 0x8F00);

        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0x07E0,
                [(1, 1, 0.0f, 0.0f), (3, 1, 1.0f, 0.0f), (3, 3, 1.0f, 1.0f), (1, 3, 0.0f, 1.0f)],
                argb: 0xFF00_FF00,
                textureEnabled: true,
                nonTwiddled: true,
                textureShading: "Decal",
                pixelFormat: 2,
                textureBase: textureBase),
            vram);

        Assert.Equal(0x8BA0, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x8BA0, ReadRgb565(vram, 2, 0));
        Assert.Equal(0x8BA0, ReadRgb565(vram, 1, 1));
    }

    [Fact]
    public void ModulatesTextureAlphaForSprite()
    {
        var vram = new byte[4096];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, 0, 0, 0xFFFF);
        WriteTexturePixel(vram, textureBase, 7, 0, 0xFFFF);
        WriteTexturePixel(vram, textureBase, 4, 4, 0xFFFF);

        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0x07E0,
                [(1, 1, 0.0f, 0.0f), (3, 1, 1.0f, 0.0f), (3, 3, 1.0f, 1.0f), (1, 3, 0.0f, 1.0f)],
                argb: 0xFF00_FF00,
                textureEnabled: true,
                nonTwiddled: true,
                textureShading: "ModulateAlpha",
                textureBase: textureBase),
            vram);

        Assert.Equal(0x07E0, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 2, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 1, 1));
    }

    [Fact]
    public void RepeatsUnclampedTextureCoordinatesForSprite()
    {
        var vram = new byte[4096];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, 2, 0, 0xF800);
        WriteTexturePixel(vram, textureBase, 0, 2, 0x001F);

        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0xFFFF,
                [(1, 1, 1.25f, 0.0f), (3, 1, 1.25f, 0.0f), (3, 3, 0.0f, 1.25f), (1, 3, 0.0f, 1.25f)],
                textureEnabled: true,
                nonTwiddled: true,
                uClamp: false,
                vClamp: false,
                textureBase: textureBase),
            vram);

        Assert.Equal(0xF800, ReadRgb565(vram, 0, 0));
        Assert.Equal(0xF800, ReadRgb565(vram, 2, 0));
        Assert.Equal(0x001F, ReadRgb565(vram, 1, 2));
    }

    [Fact]
    public void DoesNotSampleSpriteTextureWhenOnlyCommandCarriesUvPayload()
    {
        var vram = new byte[4096];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, 0, 0, 0xF800);
        WriteTexturePixel(vram, textureBase, 7, 0, 0x07E0);
        WriteTexturePixel(vram, textureBase, 4, 4, 0xFFFF);

        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0x001F,
                [(1, 1, 0.0f, 0.0f), (3, 1, 1.0f, 0.0f), (3, 3, 1.0f, 1.0f), (1, 3, 0.0f, 1.0f)],
                textureEnabled: false,
                nonTwiddled: true,
                textureBase: textureBase,
                headerValue: 0xA084_0009),
            vram);

        Assert.Equal(0x001F, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x001F, ReadRgb565(vram, 2, 0));
        Assert.Equal(0x001F, ReadRgb565(vram, 1, 1));
    }

    [Fact]
    public void FlipsClampedTextureCoordinatesForSprite()
    {
        var vram = new byte[4096];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, 7, 7, 0xF800);
        WriteTexturePixel(vram, textureBase, 0, 7, 0x07E0);
        WriteTexturePixel(vram, textureBase, 4, 4, 0xFFFF);

        DreamcastPvrPreviewRenderer.RenderSprite(
            CreateSprite(
                0xFFFF,
                [(1, 1, 0.0f, 0.0f), (3, 1, 1.0f, 0.0f), (3, 3, 1.0f, 1.0f), (1, 3, 0.0f, 1.0f)],
                textureEnabled: true,
                nonTwiddled: true,
                uFlip: true,
                vFlip: true,
                textureBase: textureBase),
            vram);

        Assert.Equal(0xF800, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 2, 0));
        Assert.Equal(0xFFFF, ReadRgb565(vram, 1, 1));
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
    public void SamplesEncodedSixteenBySixteenTextureSize()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 8];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, 0, 0, 16, 0xF800);
        WriteTexturePixel(vram, textureBase, 15, 0, 16, 0x07E0);
        WriteTexturePixel(vram, textureBase, 0, 15, 16, 0x001F);

        DreamcastPvrPreviewRenderer.RenderStrip(
            CreateStrip(
                0xFFFF,
                [(1, 1), (2, 1), (1, 2)],
                textureEnabled: true,
                nonTwiddled: true,
                textureUSize: 1,
                textureVSize: 1,
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

    [Fact]
    public void SamplesArgb4444TextureAsRgb565PreviewPixels()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 4];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, 0, 0, 0xFF00);
        WriteTexturePixel(vram, textureBase, 7, 0, 0xF0F0);
        WriteTexturePixel(vram, textureBase, 0, 7, 0xF00F);

        DreamcastPvrPreviewRenderer.RenderStrip(
            CreateStrip(
                0xFFFF,
                [(1, 1), (2, 1), (1, 2)],
                textureEnabled: true,
                nonTwiddled: true,
                pixelFormat: 2,
                textureBase: textureBase),
            vram);

        Assert.Equal(0xF800, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 1, 0));
        Assert.Equal(0x001F, ReadRgb565(vram, 0, 1));
        Assert.Equal(0x0000, ReadRgb565(vram, 1, 1));
    }

    [Fact]
    public void RepeatsUnclampedTextureCoordinates()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 4];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, 2, 0, 0xF800);
        WriteTexturePixel(vram, textureBase, 0, 2, 0x001F);

        DreamcastPvrPreviewRenderer.RenderStrip(
            CreateStrip(
                0xFFFF,
                [(1, 1), (2, 1), (1, 2)],
                textureEnabled: true,
                nonTwiddled: true,
                uClamp: false,
                vClamp: false,
                uvs: [(1.25f, 0.0f), (1.25f, 0.0f), (0.0f, 1.25f)],
                textureBase: textureBase),
            vram);

        Assert.Equal(0xF800, ReadRgb565(vram, 0, 0));
        Assert.Equal(0xF800, ReadRgb565(vram, 1, 0));
        Assert.Equal(0x001F, ReadRgb565(vram, 0, 1));
        Assert.Equal(0x0000, ReadRgb565(vram, 1, 1));
    }

    [Fact]
    public void FlipsClampedTextureCoordinates()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 4];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, 7, 7, 0xF800);
        WriteTexturePixel(vram, textureBase, 0, 7, 0x07E0);
        WriteTexturePixel(vram, textureBase, 7, 0, 0x001F);

        DreamcastPvrPreviewRenderer.RenderStrip(
            CreateStrip(
                0xFFFF,
                [(1, 1), (2, 1), (1, 2)],
                textureEnabled: true,
                nonTwiddled: true,
                uFlip: true,
                vFlip: true,
                textureBase: textureBase),
            vram);

        Assert.Equal(0xF800, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 1, 0));
        Assert.Equal(0x001F, ReadRgb565(vram, 0, 1));
        Assert.Equal(0x0000, ReadRgb565(vram, 1, 1));
    }

    [Fact]
    public void ModulatesTextureColorWithVertexColor()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 4];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, 0, 0, 0xFFFF);
        WriteTexturePixel(vram, textureBase, 7, 0, 0xFFFF);
        WriteTexturePixel(vram, textureBase, 0, 7, 0xFFFF);

        DreamcastPvrPreviewRenderer.RenderStrip(
            CreateStrip(
                0x07E0,
                [(1, 1), (2, 1), (1, 2)],
                textureEnabled: true,
                nonTwiddled: true,
                textureShading: "Modulate",
                textureBase: textureBase),
            vram);

        Assert.Equal(0x07E0, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 1, 0));
        Assert.Equal(0x07E0, ReadRgb565(vram, 0, 1));
        Assert.Equal(0x0000, ReadRgb565(vram, 1, 1));
    }

    [Fact]
    public void SamplesBilinearTextureWhenFilterModeRequestsIt()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 8];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, 3, 3, 0xF800);
        WriteTexturePixel(vram, textureBase, 4, 3, 0x07E0);
        WriteTexturePixel(vram, textureBase, 3, 4, 0x001F);
        WriteTexturePixel(vram, textureBase, 4, 4, 0xFFFF);

        DreamcastPvrPreviewRenderer.RenderStrip(
            CreateStrip(
                0xFFFF,
                [(1, 1), (3, 1), (1, 3)],
                textureEnabled: true,
                nonTwiddled: true,
                filterMode: "Bilinear",
                textureBase: textureBase),
            vram);

        Assert.Equal(0x8410, ReadRgb565(vram, 1, 1));
    }

    [Fact]
    public void DecalsTextureAlphaOverVertexColor()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 4];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, 0, 0, 0x8F00);
        WriteTexturePixel(vram, textureBase, 7, 0, 0x8F00);
        WriteTexturePixel(vram, textureBase, 0, 7, 0x8F00);

        DreamcastPvrPreviewRenderer.RenderStrip(
            CreateStrip(
                0x07E0,
                [(1, 1), (2, 1), (1, 2)],
                textureEnabled: true,
                nonTwiddled: true,
                textureShading: "Decal",
                pixelFormat: 2,
                textureBase: textureBase),
            vram);

        Assert.Equal(0x8BA0, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x8BA0, ReadRgb565(vram, 1, 0));
        Assert.Equal(0x8BA0, ReadRgb565(vram, 0, 1));
        Assert.Equal(0x0000, ReadRgb565(vram, 1, 1));
    }

    [Fact]
    public void UsesArgb4444TextureAlphaForSourceBlend()
    {
        var vram = new byte[DreamcastPvrPreviewRenderer.Width * 4];
        const uint textureBase = 0x400;
        WriteTexturePixel(vram, textureBase, 0, 0, 0x8F00);
        WriteTexturePixel(vram, textureBase, 7, 0, 0x8F00);
        WriteTexturePixel(vram, textureBase, 0, 7, 0x8F00);

        DreamcastPvrPreviewRenderer.RenderStrip(CreateStrip(0x07E0, [(1, 1), (2, 1), (1, 2)], argb: 0xFF00_FF00), vram);
        DreamcastPvrPreviewRenderer.RenderStrip(
            CreateStrip(
                0xFFFF,
                [(1, 1), (2, 1), (1, 2)],
                argb: 0xFFFF_FFFF,
                alphaEnabled: true,
                blendSrc: "SrcAlpha",
                blendDst: "InverseSrcAlpha",
                textureEnabled: true,
                nonTwiddled: true,
                pixelFormat: 2,
                textureBase: textureBase),
            vram);

        Assert.Equal(0x8BA0, ReadRgb565(vram, 0, 0));
        Assert.Equal(0x8BA0, ReadRgb565(vram, 1, 0));
        Assert.Equal(0x8BA0, ReadRgb565(vram, 0, 1));
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
        bool uClamp = true,
        bool vClamp = true,
        bool uFlip = false,
        bool vFlip = false,
        bool textureAlphaDisabled = false,
        string textureShading = "Replace",
        string filterMode = "Nearest",
        uint textureUSize = 0,
        uint textureVSize = 0,
        uint pixelFormat = 1,
        uint textureBase = 0,
        IReadOnlyList<(float U, float V)>? uvs = null,
        bool gouraud = false,
        IReadOnlyList<ushort>? vertexColors = null,
        int listType = 0,
        string listTypeName = "OpaquePolygon",
        uint? headerValue = null) =>
        new(
            "TA_INPUT",
            listType,
            listTypeName,
            headerValue ?? (gouraud ? 0x8084_0002u : 0x8084_0000u),
            $"0x{headerValue ?? (gouraud ? 0x8084_0002u : 0x8084_0000u):X8}",
            CreateHeaderPayload(culling, depthCompare, depthWriteDisabled, alphaEnabled, blendSrc, blendDst, textureEnabled, nonTwiddled, uClamp, vClamp, uFlip, vFlip, textureAlphaDisabled, textureShading, filterMode, textureUSize, textureVSize, pixelFormat, textureBase, listType, listTypeName, headerValue ?? 0x8084_0000u),
            color,
            $"0x{color:X4}",
            points.Select((point, index) => new DreamcastPvrTaVertex(
                point.X,
                point.Y,
                z,
                SingleToUInt32Bits(z),
                $"0x{SingleToUInt32Bits(z):X8}",
                VertexU(point, points, uvs, index),
                SingleToUInt32Bits(VertexU(point, points, uvs, index)),
                $"0x{SingleToUInt32Bits(VertexU(point, points, uvs, index)):X8}",
                VertexV(point, points, uvs, index),
                SingleToUInt32Bits(VertexV(point, points, uvs, index)),
                $"0x{SingleToUInt32Bits(VertexV(point, points, uvs, index)):X8}",
                index == points.Count - 1,
                VertexColorAt(color, vertexColors, index),
                $"0x{VertexColorAt(color, vertexColors, index):X4}",
                index == points.Count - 1 ? 0xF000_0000 : 0xE000_0000,
                index == points.Count - 1 ? "0xF0000000" : "0xE0000000",
                (uint)point.X << 16,
                $"0x{(uint)point.X << 16:X8}",
                (uint)point.Y << 16,
                $"0x{(uint)point.Y << 16:X8}",
                argb ?? VertexColorAt(color, vertexColors, index),
                $"0x{argb ?? VertexColorAt(color, vertexColors, index):X8}")).ToArray());

    private static ushort VertexColorAt(ushort color, IReadOnlyList<ushort>? vertexColors, int index) =>
        vertexColors is null ? color : vertexColors[index];

    private static DreamcastPvrTaSprite CreateSprite(
        ushort color,
        IReadOnlyList<(int X, int Y, float U, float V)> points,
        uint argb = 0xFFFF_FFFF,
        bool alphaEnabled = false,
        string blendSrc = "One",
        string blendDst = "Zero",
        bool textureEnabled = false,
        bool nonTwiddled = false,
        bool uClamp = true,
        bool vClamp = true,
        bool uFlip = false,
        bool vFlip = false,
        string textureShading = "Replace",
        uint pixelFormat = 1,
        uint textureBase = 0,
        int listType = 0,
        string listTypeName = "OpaquePolygon",
        uint headerValue = 0xA084_0001,
        IReadOnlyList<uint>? xValues = null)
    {
        var mode1 = textureEnabled ? 0x0200_0000u : 0;
        var mode2 = (BlendBits(blendSrc) << 29)
            | (BlendBits(blendDst) << 26)
            | (TextureShadingBits(textureShading) << 6)
            | (textureEnabled && vClamp ? 0x0000_8000u : 0)
            | (textureEnabled && uClamp ? 0x0001_0000u : 0)
            | (textureEnabled && vFlip ? 0x0002_0000u : 0)
            | (textureEnabled && uFlip ? 0x0004_0000u : 0)
            | (alphaEnabled ? 0x0010_0000u : 0);
        var mode3 = textureBase | (pixelFormat << 27) | (nonTwiddled ? 0x0400_0000u : 0);
        var header = new DreamcastPvrTaCommandWrite(
            0x1000_0000,
            "0x10000000",
            "TA_INPUT",
            "SpriteHeader",
            listType,
            listTypeName,
            false,
            4,
            headerValue,
            $"0x{headerValue:X8}");
        var payload = DreamcastPvrTaSpriteHeaderPayload.FromPayload(header, [mode1, mode2, mode3, argb, 0, 0, 0]);
        return new DreamcastPvrTaSprite(
            "TA_INPUT",
            listType,
            listTypeName,
            headerValue,
            $"0x{headerValue:X8}",
            null,
            null,
            payload,
            0xF000_0000,
            "0xF0000000",
            null,
            null,
            null,
            null,
            null,
            null,
            true,
            color,
            $"0x{color:X4}",
            [],
            points.Select((point, index) =>
            {
                var xValue = xValues?[index] ?? SingleToUInt32Bits((float)point.X);
                return new DreamcastPvrTaSpriteVertex(
                    ((char)('A' + index)).ToString(),
                    point.X,
                    point.Y,
                    1.0f,
                    SingleToUInt32Bits(1.0f),
                    "0x3F800000",
                    xValue,
                    $"0x{xValue:X8}",
                    SingleToUInt32Bits((float)point.Y),
                    $"0x{SingleToUInt32Bits((float)point.Y):X8}",
                    point.U,
                    point.V,
                    0,
                    "0x00000000");
            }).ToArray());
    }

    private static DreamcastPvrTaPolygonHeaderPayload? CreateHeaderPayload(
        string? culling,
        string? depthCompare,
        bool depthWriteDisabled,
        bool alphaEnabled,
        string blendSrc,
        string blendDst,
        bool textureEnabled,
        bool nonTwiddled,
        bool uClamp,
        bool vClamp,
        bool uFlip,
        bool vFlip,
        bool textureAlphaDisabled,
        string textureShading,
        string filterMode,
        uint textureUSize,
        uint textureVSize,
        uint pixelFormat,
        uint textureBase,
        int listType,
        string listTypeName,
        uint headerValue)
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
            | textureVSize
            | (textureUSize << 3)
            | (TextureShadingBits(textureShading) << 6)
            | (FilterModeBits(filterMode) << 13)
            | (textureEnabled && vClamp ? 0x0000_8000u : 0)
            | (textureEnabled && uClamp ? 0x0001_0000u : 0)
            | (textureEnabled && vFlip ? 0x0002_0000u : 0)
            | (textureEnabled && uFlip ? 0x0004_0000u : 0)
            | (textureAlphaDisabled ? 0x0008_0000u : 0)
            | (alphaEnabled ? 0x0010_0000u : 0);
        var mode3 = textureBase | (pixelFormat << 27) | (nonTwiddled ? 0x0400_0000u : 0);
        var header = new DreamcastPvrTaCommandWrite(
            0x1000_0000,
            "0x10000000",
            "TA_INPUT",
            "PolygonHeader",
            listType,
            listTypeName,
            false,
            4,
            headerValue,
            $"0x{headerValue:X8}");
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

    private static uint TextureShadingBits(string textureShading) =>
        textureShading switch
        {
            "Replace" => 0,
            "Modulate" => 1,
            "Decal" => 2,
            "ModulateAlpha" => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(textureShading), textureShading, "Unknown texture shading mode.")
        };

    private static uint FilterModeBits(string filterMode) =>
        filterMode switch
        {
            "Nearest" => 0,
            "Bilinear" => 1,
            "Trilinear1" => 2,
            "Trilinear2" => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(filterMode), filterMode, "Unknown filter mode.")
        };

    private static float[] CreateDepthBuffer(byte[] vram)
    {
        var depth = new float[vram.Length / 2];
        Array.Fill(depth, float.NaN);
        return depth;
    }

    private static uint SingleToUInt32Bits(float value) =>
        BitConverter.SingleToUInt32Bits(value);

    private static void WriteTexturePixel(byte[] vram, uint textureBase, int x, int y, ushort value) =>
        WriteTexturePixel(vram, textureBase, x, y, 8, value);

    private static void WriteTexturePixel(byte[] vram, uint textureBase, int x, int y, int textureWidth, ushort value)
    {
        var offset = (int)textureBase + (((y * textureWidth) + x) * 2);
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

    private static float VertexU(
        (int X, int Y) point,
        IReadOnlyList<(int X, int Y)> points,
        IReadOnlyList<(float U, float V)>? uvs,
        int index) =>
        uvs is null ? TextureU(point, points) : uvs[index].U;

    private static float VertexV(
        (int X, int Y) point,
        IReadOnlyList<(int X, int Y)> points,
        IReadOnlyList<(float U, float V)>? uvs,
        int index) =>
        uvs is null ? TextureV(point, points) : uvs[index].V;

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
        => ReadRgb565(vram, x, y, DreamcastPvrPreviewRenderer.Width);

    private static ushort ReadRgb565(byte[] vram, int x, int y, int previewWidth)
    {
        var offset = ((y * previewWidth) + x) * 2;
        return (ushort)(vram[offset] | (vram[offset + 1] << 8));
    }
}
