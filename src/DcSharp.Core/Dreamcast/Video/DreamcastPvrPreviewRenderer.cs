using System.Numerics;

namespace DcSharp.Core.Dreamcast.Video;

public static class DreamcastPvrPreviewRenderer
{
    public const int Width = 320;

    public static void RenderStrip(DreamcastPvrTaStrip strip, Span<byte> vram) =>
        RenderStrip(strip, vram, [], useDepth: false, useScreenCoordinates: false, Width);

    public static void RenderStrip(DreamcastPvrTaStrip strip, Span<byte> vram, bool useScreenCoordinates) =>
        RenderStrip(strip, vram, [], useDepth: false, useScreenCoordinates, Width);

    public static void RenderStrip(DreamcastPvrTaStrip strip, Span<byte> vram, int previewWidth, bool useScreenCoordinates) =>
        RenderStrip(strip, vram, [], useDepth: false, useScreenCoordinates, previewWidth);

    public static void RenderStrip(DreamcastPvrTaStrip strip, Span<byte> vram, Span<float> depthBuffer) =>
        RenderStrip(strip, vram, depthBuffer, useDepth: true, useScreenCoordinates: false, Width);

    public static void RenderStrip(DreamcastPvrTaStrip strip, Span<byte> vram, Span<float> depthBuffer, bool useScreenCoordinates) =>
        RenderStrip(strip, vram, depthBuffer, useDepth: true, useScreenCoordinates, Width);

    public static void RenderStrip(DreamcastPvrTaStrip strip, Span<byte> vram, Span<float> depthBuffer, int previewWidth, bool useScreenCoordinates) =>
        RenderStrip(strip, vram, depthBuffer, useDepth: true, useScreenCoordinates, previewWidth);

    public static void RenderSprite(DreamcastPvrTaSprite sprite, Span<byte> vram) =>
        RenderSprite(sprite, vram, useScreenCoordinates: false);

    public static void RenderSprite(DreamcastPvrTaSprite sprite, Span<byte> vram, bool useScreenCoordinates)
        => RenderSprite(sprite, vram, Width, useScreenCoordinates);

    public static void RenderSprite(DreamcastPvrTaSprite sprite, Span<byte> vram, int previewWidth, bool useScreenCoordinates)
    {
        if (sprite.Rgb565 == 0 || !sprite.HasFinitePreviewCoordinates)
        {
            return;
        }

        if (!sprite.HasRenderablePreviewArea)
        {
            RenderDegenerateSprite(sprite, vram, previewWidth, useScreenCoordinates);
            return;
        }

        var originX = useScreenCoordinates ? 0 : sprite.Vertices.Take(4).Min(vertex => vertex.X);
        var originY = useScreenCoordinates ? 0 : sprite.Vertices.Take(4).Min(vertex => vertex.Y);
        var vertices = sprite.Vertices
            .Take(4)
            .Select(vertex => new DreamcastPvrPreviewSpriteVertex(vertex.X - originX, vertex.Y - originY, vertex.U, vertex.V))
            .ToArray();
        var centerX = vertices.Average(vertex => vertex.X);
        var centerY = vertices.Average(vertex => vertex.Y);
        var ordered = vertices
            .OrderBy(vertex => MathF.Atan2(vertex.Y - centerY, vertex.X - centerX))
            .ToArray();
        var minPreviewX = Math.Clamp((int)MathF.Floor(ordered.Min(vertex => vertex.X)), 0, previewWidth - 1);
        var minPreviewY = Math.Max((int)MathF.Floor(ordered.Min(vertex => vertex.Y)), 0);
        var maxPreviewX = Math.Clamp((int)MathF.Ceiling(ordered.Max(vertex => vertex.X)), 0, previewWidth - 1);
        var maxPreviewY = Math.Max((int)MathF.Ceiling(ordered.Max(vertex => vertex.Y)), 0);

        for (var y = minPreviewY; y <= maxPreviewY; y++)
        {
            for (var x = minPreviewX; x <= maxPreviewX; x++)
            {
                var point = new Vector2(x, y);
                if (!TryInterpolateSpriteUv(point, ordered[0], ordered[1], ordered[2], out var sourceU, out var sourceV)
                    && !TryInterpolateSpriteUv(point, ordered[0], ordered[2], ordered[3], out sourceU, out sourceV))
                {
                    continue;
                }

                var pixelIndex = PreviewPixelIndex(x, y, previewWidth);
                WriteSpritePreviewPixel(sprite, vram, pixelIndex, sourceU, sourceV);
            }
        }
    }

    private static void RenderDegenerateSprite(DreamcastPvrTaSprite sprite, Span<byte> vram, int previewWidth, bool useScreenCoordinates)
    {
        var originX = useScreenCoordinates ? 0 : sprite.Vertices.Take(4).Min(vertex => vertex.X);
        var originY = useScreenCoordinates ? 0 : sprite.Vertices.Take(4).Min(vertex => vertex.Y);
        var vertices = sprite.Vertices
            .Take(4)
            .Select(vertex => new DreamcastPvrPreviewSpriteVertex(vertex.X - originX, vertex.Y - originY, vertex.U, vertex.V))
            .ToArray();

        for (var index = 0; index < vertices.Length; index++)
        {
            DrawSpritePreviewLine(sprite, vertices[index], vertices[(index + 1) % vertices.Length], vram, previewWidth);
        }
    }

    private static void DrawSpritePreviewLine(
        DreamcastPvrTaSprite sprite,
        DreamcastPvrPreviewSpriteVertex a,
        DreamcastPvrPreviewSpriteVertex b,
        Span<byte> vram,
        int previewWidth)
    {
        var x0 = (int)MathF.Round(a.X);
        var y0 = (int)MathF.Round(a.Y);
        var x1 = (int)MathF.Round(b.X);
        var y1 = (int)MathF.Round(b.Y);
        var steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));

        for (var step = 0; step <= steps; step++)
        {
            var weight = steps == 0 ? 0.0f : step / (float)steps;
            var x = (int)MathF.Round(Lerp(x0, x1, weight));
            var y = (int)MathF.Round(Lerp(y0, y1, weight));
            if (x < 0 || x >= previewWidth || y < 0)
            {
                continue;
            }

            var u = Lerp(a.U, b.U, weight);
            var v = Lerp(a.V, b.V, weight);
            WriteSpritePreviewPixel(sprite, vram, PreviewPixelIndex(x, y, previewWidth), u, v);
        }
    }

    private static bool WriteSpritePreviewPixel(
        DreamcastPvrTaSprite sprite,
        Span<byte> vram,
        int pixelIndex,
        float sourceU,
        float sourceV)
    {
        var source = SpriteSourceSample(sprite, vram, sourceU, sourceV);
        if (IsPunchThrough(sprite)
            && SourceAlpha((byte)(sprite.HeaderPayload.Argb >> 24), source.Alpha, source.AlphaMultipliesVertex) < 128)
        {
            return false;
        }

        if (sprite.HeaderPayload.Mode2Fields.AlphaEnabled)
        {
            source = source with
            {
                Rgb565 = BlendRgb565(
                    source.Rgb565,
                    ReadRgb565Pixel(vram, pixelIndex),
                    SourceAlpha((byte)(sprite.HeaderPayload.Argb >> 24), source.Alpha, source.AlphaMultipliesVertex),
                    sprite.HeaderPayload.Mode2Fields.BlendSrcName,
                    sprite.HeaderPayload.Mode2Fields.BlendDstName)
            };
        }

        WriteRgb565Pixel(vram, pixelIndex, source.Rgb565);
        return true;
    }

    private static bool TryInterpolateSpriteUv(
        Vector2 point,
        DreamcastPvrPreviewSpriteVertex a,
        DreamcastPvrPreviewSpriteVertex b,
        DreamcastPvrPreviewSpriteVertex c,
        out float u,
        out float v)
    {
        var pointA = new Vector2(a.X, a.Y);
        var pointB = new Vector2(b.X, b.Y);
        var pointC = new Vector2(c.X, c.Y);
        if (!IsInsideTriangle(point, pointA, pointB, pointC))
        {
            u = 0.0f;
            v = 0.0f;
            return false;
        }

        var (weightA, weightB, weightC) = Barycentric(point, pointA, pointB, pointC);
        u = (a.U * weightA) + (b.U * weightB) + (c.U * weightC);
        v = (a.V * weightA) + (b.V * weightB) + (c.V * weightC);
        return true;
    }

    private static DreamcastPvrPreviewSourceSample SpriteSourceSample(DreamcastPvrTaSprite sprite, ReadOnlySpan<byte> vram, float sourceU, float sourceV)
    {
        if (!sprite.HeaderPayload.Mode1Fields.TextureEnabled
            || sprite.HeaderPayload.Mode3Fields.VqEnabled
            || sprite.HeaderPayload.Mode3Fields.MipMapEnabled)
        {
            return new DreamcastPvrPreviewSourceSample(sprite.Rgb565, null);
        }

        var textureWidth = TextureSize(sprite.HeaderPayload.Mode2Fields.TextureUSize);
        var textureHeight = TextureSize(sprite.HeaderPayload.Mode2Fields.TextureVSize);
        if (textureWidth <= 0 || textureHeight <= 0)
        {
            return new DreamcastPvrPreviewSourceSample(sprite.Rgb565, null);
        }

        var u = TextureCoordinate(sourceU, sprite.HeaderPayload.Mode2Fields.UClamp, sprite.HeaderPayload.Mode2Fields.UFlip);
        var v = TextureCoordinate(sourceV, sprite.HeaderPayload.Mode2Fields.VClamp, sprite.HeaderPayload.Mode2Fields.VFlip);
        var textureSample = SampleTexture(sprite.HeaderPayload.Mode2Fields, sprite.HeaderPayload.Mode3Fields, vram, u, v, textureWidth, textureHeight);
        return textureSample is null
            ? new DreamcastPvrPreviewSourceSample(sprite.Rgb565, null)
            : ApplyTextureShading(sprite.Rgb565, textureSample, sprite.HeaderPayload.Mode2Fields.TextureShadingName);
    }

    private static void RenderStrip(
        DreamcastPvrTaStrip strip,
        Span<byte> vram,
        Span<float> depthBuffer,
        bool useDepth,
        bool useScreenCoordinates,
        int previewWidth)
    {
        if (strip.Vertices.Count < 3)
        {
            return;
        }

        var originX = useScreenCoordinates ? 0 : strip.Vertices.Min(vertex => vertex.X);
        var originY = useScreenCoordinates ? 0 : strip.Vertices.Min(vertex => vertex.Y);
        for (var index = 0; index <= strip.Vertices.Count - 3; index++)
        {
            var vertices = strip.Vertices.Skip(index).Take(3).ToArray();
            RenderTriangle(strip, vertices, originX, originY, vram, depthBuffer, useDepth, previewWidth);
        }
    }

    private static void RenderTriangle(
        DreamcastPvrTaStrip strip,
        IReadOnlyList<DreamcastPvrTaVertex> vertices,
        int originX,
        int originY,
        Span<byte> vram,
        Span<float> depthBuffer,
        bool useDepth,
        int previewWidth)
    {
        var a = new Vector2(vertices[0].X - originX, vertices[0].Y - originY);
        var b = new Vector2(vertices[1].X - originX, vertices[1].Y - originY);
        var c = new Vector2(vertices[2].X - originX, vertices[2].Y - originY);
        if (IsCulled(strip, a, b, c))
        {
            return;
        }

        var minPreviewX = Math.Clamp((int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))), 0, previewWidth - 1);
        var minPreviewY = Math.Max((int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))), 0);
        var maxPreviewX = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))), 0, previewWidth - 1);
        var maxPreviewY = Math.Max((int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))), 0);

        for (var y = minPreviewY; y <= maxPreviewY; y++)
        {
            for (var x = minPreviewX; x <= maxPreviewX; x++)
            {
                var point = new Vector2(x, y);
                if (IsInsideTriangle(point, a, b, c))
                {
                    var pixelIndex = PreviewPixelIndex(x, y, previewWidth);
                    if (!PassesDepth(strip, vertices, pixelIndex, depthBuffer, useDepth))
                    {
                        continue;
                    }

                    if (WritePreviewPixel(strip, vertices, point, a, b, c, vram, pixelIndex))
                    {
                        WriteDepth(strip, vertices, pixelIndex, depthBuffer, useDepth);
                    }
                }
            }
        }
    }

    private static bool PassesDepth(
        DreamcastPvrTaStrip strip,
        IReadOnlyList<DreamcastPvrTaVertex> vertices,
        int pixelIndex,
        Span<float> depthBuffer,
        bool useDepth)
    {
        if (!useDepth || pixelIndex >= depthBuffer.Length)
        {
            return true;
        }

        var depthCompareName = strip.HeaderPayload?.Mode1Fields.DepthCompareName;
        if (depthCompareName is null || string.Equals(depthCompareName, "Never", StringComparison.Ordinal))
        {
            return true;
        }

        var incoming = PreviewDepth(vertices);
        var current = depthBuffer[pixelIndex];
        if (float.IsNaN(current))
        {
            return true;
        }

        const float epsilon = 0.0001f;
        return depthCompareName switch
        {
            "Less" => incoming < current,
            "Equal" => MathF.Abs(incoming - current) <= epsilon,
            "LessOrEqual" => incoming < current || MathF.Abs(incoming - current) <= epsilon,
            "Greater" => incoming > current,
            "NotEqual" => MathF.Abs(incoming - current) > epsilon,
            "GreaterOrEqual" => incoming > current || MathF.Abs(incoming - current) <= epsilon,
            "Always" => true,
            _ => true
        };
    }

    private static void WriteDepth(
        DreamcastPvrTaStrip strip,
        IReadOnlyList<DreamcastPvrTaVertex> vertices,
        int pixelIndex,
        Span<float> depthBuffer,
        bool useDepth)
    {
        if (!useDepth
            || pixelIndex >= depthBuffer.Length
            || strip.HeaderPayload?.Mode1Fields.DepthWriteDisabled == true
            || strip.HeaderPayload?.Mode1Fields.DepthCompareName is null
            || string.Equals(strip.HeaderPayload.Mode1Fields.DepthCompareName, "Never", StringComparison.Ordinal))
        {
            return;
        }

        depthBuffer[pixelIndex] = PreviewDepth(vertices);
    }

    private static float PreviewDepth(IReadOnlyList<DreamcastPvrTaVertex> vertices) =>
        (vertices[0].Z + vertices[1].Z + vertices[2].Z) / 3.0f;

    private static bool IsCulled(DreamcastPvrTaStrip strip, Vector2 a, Vector2 b, Vector2 c)
    {
        var cullingName = strip.HeaderPayload?.Mode1Fields.CullingName;
        if (cullingName is null || string.Equals(cullingName, "None", StringComparison.Ordinal))
        {
            return false;
        }

        var signedArea = EdgeFunction(a, b, c);
        return cullingName switch
        {
            "Small" => signedArea == 0,
            "Ccw" => signedArea > 0,
            "Cw" => signedArea < 0,
            _ => false
        };
    }

    private static bool IsInsideTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        var edge0 = EdgeFunction(a, b, point);
        var edge1 = EdgeFunction(b, c, point);
        var edge2 = EdgeFunction(c, a, point);
        return (edge0 >= 0 && edge1 >= 0 && edge2 >= 0)
            || (edge0 <= 0 && edge1 <= 0 && edge2 <= 0);
    }

    private static float EdgeFunction(Vector2 a, Vector2 b, Vector2 point) =>
        ((point.X - a.X) * (b.Y - a.Y)) - ((point.Y - a.Y) * (b.X - a.X));

    private static int PreviewPixelIndex(int x, int y, int previewWidth) =>
        (y * previewWidth) + x;

    private static bool WritePreviewPixel(
        DreamcastPvrTaStrip strip,
        IReadOnlyList<DreamcastPvrTaVertex> vertices,
        Vector2 point,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Span<byte> vram,
        int pixelIndex)
    {
        var source = SourceSample(strip, vertices, point, a, b, c, vram);
        var mode2 = strip.HeaderPayload?.Mode2Fields;
        if (IsPunchThrough(strip) && SourceAlpha(vertices, source.Alpha, source.AlphaMultipliesVertex) < 128)
        {
            return false;
        }

        if (mode2?.AlphaEnabled == true)
        {
            source = source with
            {
                Rgb565 = BlendRgb565(
                    source.Rgb565,
                    ReadRgb565Pixel(vram, pixelIndex),
                    SourceAlpha(vertices, source.Alpha, source.AlphaMultipliesVertex),
                    mode2.BlendSrcName,
                    mode2.BlendDstName)
            };
        }

        WriteRgb565Pixel(vram, pixelIndex, source.Rgb565);
        return true;
    }

    private static bool IsPunchThrough(DreamcastPvrTaStrip strip) =>
        string.Equals(strip.ListTypeName, "PunchThroughPolygon", StringComparison.Ordinal);

    private static bool IsPunchThrough(DreamcastPvrTaSprite sprite) =>
        string.Equals(sprite.ListTypeName, "PunchThroughPolygon", StringComparison.Ordinal);

    private static DreamcastPvrPreviewSourceSample SourceSample(
        DreamcastPvrTaStrip strip,
        IReadOnlyList<DreamcastPvrTaVertex> vertices,
        Vector2 point,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        ReadOnlySpan<byte> vram)
    {
        var payload = strip.HeaderPayload;
        if (payload is null
            || !payload.Mode1Fields.TextureEnabled
            || payload.Mode3Fields.VqEnabled
            || payload.Mode3Fields.MipMapEnabled)
        {
            return new DreamcastPvrPreviewSourceSample(VertexColor(strip, vertices, point, a, b, c), null);
        }

        var width = TextureSize(payload.Mode2Fields.TextureUSize);
        var height = TextureSize(payload.Mode2Fields.TextureVSize);
        if (width <= 0 || height <= 0)
        {
            return new DreamcastPvrPreviewSourceSample(VertexColor(strip, vertices, point, a, b, c), null);
        }

        var (weightA, weightB, weightC) = Barycentric(point, a, b, c);
        var vertexColor = VertexColor(strip, vertices, weightA, weightB, weightC);
        var u = TextureCoordinate(
            (vertices[0].U * weightA) + (vertices[1].U * weightB) + (vertices[2].U * weightC),
            payload.Mode2Fields.UClamp,
            payload.Mode2Fields.UFlip);
        var v = TextureCoordinate(
            (vertices[0].V * weightA) + (vertices[1].V * weightB) + (vertices[2].V * weightC),
            payload.Mode2Fields.VClamp,
            payload.Mode2Fields.VFlip);
        var textureSample = SampleTexture(payload, vram, u, v, width, height);
        if (textureSample is null)
        {
            return new DreamcastPvrPreviewSourceSample(vertexColor, null);
        }

        return ApplyTextureShading(vertexColor, textureSample, payload.Mode2Fields.TextureShadingName);
    }

    private static ushort VertexColor(
        DreamcastPvrTaStrip strip,
        IReadOnlyList<DreamcastPvrTaVertex> vertices,
        Vector2 point,
        Vector2 a,
        Vector2 b,
        Vector2 c)
    {
        if (!strip.Gouraud)
        {
            return strip.Rgb565;
        }

        var (weightA, weightB, weightC) = Barycentric(point, a, b, c);
        return VertexColor(strip, vertices, weightA, weightB, weightC);
    }

    private static ushort VertexColor(
        DreamcastPvrTaStrip strip,
        IReadOnlyList<DreamcastPvrTaVertex> vertices,
        float weightA,
        float weightB,
        float weightC)
    {
        if (!strip.Gouraud)
        {
            return strip.Rgb565;
        }

        var (redA, greenA, blueA) = Rgb565ToRgb888(vertices[0].Rgb565);
        var (redB, greenB, blueB) = Rgb565ToRgb888(vertices[1].Rgb565);
        var (redC, greenC, blueC) = Rgb565ToRgb888(vertices[2].Rgb565);
        return Rgb888ToRgb565(
            ClampToByte((redA * weightA) + (redB * weightB) + (redC * weightC)),
            ClampToByte((greenA * weightA) + (greenB * weightB) + (greenC * weightC)),
            ClampToByte((blueA * weightA) + (blueB * weightB) + (blueC * weightC)));
    }

    private static DreamcastPvrPreviewSourceSample? SampleTexture(
        DreamcastPvrTaPolygonHeaderPayload payload,
        ReadOnlySpan<byte> vram,
        float u,
        float v,
        int width,
        int height) =>
        SampleTexture(payload.Mode2Fields, payload.Mode3Fields, vram, u, v, width, height);

    private static DreamcastPvrPreviewSourceSample? SampleTexture(
        DreamcastPvrTaPolygonHeaderMode2 mode2,
        DreamcastPvrTaPolygonHeaderMode3 mode3,
        ReadOnlySpan<byte> vram,
        float u,
        float v,
        int width,
        int height) =>
        string.Equals(mode2.FilterModeName, "Bilinear", StringComparison.Ordinal)
            ? SampleBilinearTexture(mode2, mode3, vram, u, v, width, height)
            : SampleNearestTexture(mode2, mode3, vram, u, v, width, height);

    private static DreamcastPvrPreviewSourceSample? SampleNearestTexture(
        DreamcastPvrTaPolygonHeaderMode2 mode2,
        DreamcastPvrTaPolygonHeaderMode3 mode3,
        ReadOnlySpan<byte> vram,
        float u,
        float v,
        int width,
        int height)
    {
        var texelX = Math.Clamp((int)MathF.Round(u * (width - 1)), 0, width - 1);
        var texelY = Math.Clamp((int)MathF.Round(v * (height - 1)), 0, height - 1);
        return ReadTextureSample(mode2, mode3, vram, width, texelX, texelY);
    }

    private static DreamcastPvrPreviewSourceSample? SampleBilinearTexture(
        DreamcastPvrTaPolygonHeaderMode2 mode2,
        DreamcastPvrTaPolygonHeaderMode3 mode3,
        ReadOnlySpan<byte> vram,
        float u,
        float v,
        int width,
        int height)
    {
        var texelX = Math.Clamp(u * (width - 1), 0.0f, width - 1);
        var texelY = Math.Clamp(v * (height - 1), 0.0f, height - 1);
        var x0 = Math.Clamp((int)MathF.Floor(texelX), 0, width - 1);
        var y0 = Math.Clamp((int)MathF.Floor(texelY), 0, height - 1);
        var x1 = Math.Clamp(x0 + 1, 0, width - 1);
        var y1 = Math.Clamp(y0 + 1, 0, height - 1);
        var xWeight = texelX - x0;
        var yWeight = texelY - y0;

        var topLeft = ReadTextureSample(mode2, mode3, vram, width, x0, y0);
        var topRight = ReadTextureSample(mode2, mode3, vram, width, x1, y0);
        var bottomLeft = ReadTextureSample(mode2, mode3, vram, width, x0, y1);
        var bottomRight = ReadTextureSample(mode2, mode3, vram, width, x1, y1);
        return topLeft is null || topRight is null || bottomLeft is null || bottomRight is null
            ? null
            : InterpolateSamples(topLeft, topRight, bottomLeft, bottomRight, xWeight, yWeight);
    }

    private static DreamcastPvrPreviewSourceSample? ReadTextureSample(
        DreamcastPvrTaPolygonHeaderMode2 mode2,
        DreamcastPvrTaPolygonHeaderMode3 mode3,
        ReadOnlySpan<byte> vram,
        int width,
        int x,
        int y)
    {
        var texelIndex = mode3.NonTwiddled
            ? (y * width) + x
            : TwiddledTextureIndex(x, y);
        var textureOffset = checked((int)mode3.TextureBase + (texelIndex * 2));
        var texel = ReadRgb565Pixel(vram, textureOffset / 2);
        var textureAlphaEnabled = !mode2.TextureAlphaDisabled;
        return mode3.PixelFormatName switch
        {
            "Rgb565" => new DreamcastPvrPreviewSourceSample(texel, null),
            "Argb1555" => new DreamcastPvrPreviewSourceSample(Argb1555ToRgb565(texel), textureAlphaEnabled ? Argb1555Alpha(texel) : null),
            "Argb4444" => new DreamcastPvrPreviewSourceSample(Argb4444ToRgb565(texel), textureAlphaEnabled ? Argb4444Alpha(texel) : null),
            _ => null
        };
    }

    private static DreamcastPvrPreviewSourceSample InterpolateSamples(
        DreamcastPvrPreviewSourceSample topLeft,
        DreamcastPvrPreviewSourceSample topRight,
        DreamcastPvrPreviewSourceSample bottomLeft,
        DreamcastPvrPreviewSourceSample bottomRight,
        float xWeight,
        float yWeight)
    {
        var (topLeftRed, topLeftGreen, topLeftBlue) = Rgb565ToRgb888(topLeft.Rgb565);
        var (topRightRed, topRightGreen, topRightBlue) = Rgb565ToRgb888(topRight.Rgb565);
        var (bottomLeftRed, bottomLeftGreen, bottomLeftBlue) = Rgb565ToRgb888(bottomLeft.Rgb565);
        var (bottomRightRed, bottomRightGreen, bottomRightBlue) = Rgb565ToRgb888(bottomRight.Rgb565);
        var red = InterpolateChannel(topLeftRed, topRightRed, bottomLeftRed, bottomRightRed, xWeight, yWeight);
        var green = InterpolateChannel(topLeftGreen, topRightGreen, bottomLeftGreen, bottomRightGreen, xWeight, yWeight);
        var blue = InterpolateChannel(topLeftBlue, topRightBlue, bottomLeftBlue, bottomRightBlue, xWeight, yWeight);

        var hasAlpha = topLeft.Alpha is not null
            || topRight.Alpha is not null
            || bottomLeft.Alpha is not null
            || bottomRight.Alpha is not null;
        var alpha = hasAlpha
            ? InterpolateChannel(
                topLeft.Alpha ?? byte.MaxValue,
                topRight.Alpha ?? byte.MaxValue,
                bottomLeft.Alpha ?? byte.MaxValue,
                bottomRight.Alpha ?? byte.MaxValue,
                xWeight,
                yWeight)
            : (byte?)null;
        return new DreamcastPvrPreviewSourceSample(Rgb888ToRgb565(red, green, blue), alpha);
    }

    private static byte InterpolateChannel(
        byte topLeft,
        byte topRight,
        byte bottomLeft,
        byte bottomRight,
        float xWeight,
        float yWeight)
    {
        var top = Lerp(topLeft, topRight, xWeight);
        var bottom = Lerp(bottomLeft, bottomRight, xWeight);
        return ClampToByte(Lerp(top, bottom, yWeight));
    }

    private static float Lerp(float a, float b, float weight) =>
        a + ((b - a) * weight);

    private static DreamcastPvrPreviewSourceSample ApplyTextureShading(
        ushort vertexColor,
        DreamcastPvrPreviewSourceSample texture,
        string textureShadingName) =>
        textureShadingName switch
        {
            "Modulate" => texture with
            {
                Rgb565 = ModulateRgb565(vertexColor, texture.Rgb565),
                AlphaMultipliesVertex = false
            },
            "Decal" => new DreamcastPvrPreviewSourceSample(
                DecalRgb565(vertexColor, texture.Rgb565, texture.Alpha ?? byte.MaxValue),
                null),
            "ModulateAlpha" => texture with { Rgb565 = ModulateRgb565(vertexColor, texture.Rgb565) },
            _ => texture with { AlphaMultipliesVertex = false }
        };

    private static ushort ModulateRgb565(ushort source, ushort texture)
    {
        var (sourceRed, sourceGreen, sourceBlue) = Rgb565ToRgb888(source);
        var (textureRed, textureGreen, textureBlue) = Rgb565ToRgb888(texture);
        return Rgb888ToRgb565(
            ClampToByte((sourceRed * textureRed) / 255.0f),
            ClampToByte((sourceGreen * textureGreen) / 255.0f),
            ClampToByte((sourceBlue * textureBlue) / 255.0f));
    }

    private static ushort DecalRgb565(ushort source, ushort texture, byte textureAlpha) =>
        BlendRgb565(texture, source, textureAlpha, "SrcAlpha", "InverseSrcAlpha");

    private static float TextureCoordinate(float value, bool clamp, bool flip)
    {
        var coordinate = flip ? 1.0f - value : value;
        return clamp ? Math.Clamp(coordinate, 0.0f, 1.0f) : RepeatTextureCoordinate(coordinate);
    }

    private static float RepeatTextureCoordinate(float value)
    {
        if (!float.IsFinite(value))
        {
            return 0.0f;
        }

        var wrapped = value - MathF.Floor(value);
        return MathF.Abs(wrapped) < 0.0001f && value > 0.0f ? 1.0f : wrapped;
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

    private static int TextureSize(int encoded) =>
        encoded switch
        {
            0 => 8,
            1 => 16,
            2 => 32,
            3 => 64,
            4 => 128,
            5 => 256,
            6 => 512,
            _ => 1024
        };

    private static (float A, float B, float C) Barycentric(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        var denominator = EdgeFunction(a, b, c);
        if (MathF.Abs(denominator) < 0.0001f)
        {
            return (1.0f, 0.0f, 0.0f);
        }

        var weightA = EdgeFunction(b, c, point) / denominator;
        var weightB = EdgeFunction(c, a, point) / denominator;
        var weightC = EdgeFunction(a, b, point) / denominator;
        return (weightA, weightB, weightC);
    }

    private static ushort BlendRgb565(ushort source, ushort destination, byte sourceAlpha, string sourceBlend, string destinationBlend)
    {
        var (sourceRed, sourceGreen, sourceBlue) = Rgb565ToRgb888(source);
        var (destinationRed, destinationGreen, destinationBlue) = Rgb565ToRgb888(destination);
        var red = BlendChannel(sourceRed, destinationRed, sourceAlpha, sourceBlend, destinationBlend);
        var green = BlendChannel(sourceGreen, destinationGreen, sourceAlpha, sourceBlend, destinationBlend);
        var blue = BlendChannel(sourceBlue, destinationBlue, sourceAlpha, sourceBlend, destinationBlend);
        return Rgb888ToRgb565(red, green, blue);
    }

    private static byte BlendChannel(byte source, byte destination, byte sourceAlpha, string sourceBlend, string destinationBlend)
    {
        var sourceFactor = BlendFactor(sourceBlend, sourceAlpha, source, destination);
        var destinationFactor = BlendFactor(destinationBlend, sourceAlpha, source, destination);
        return ClampToByte((source * sourceFactor) + (destination * destinationFactor));
    }

    private static float BlendFactor(string blend, byte sourceAlpha, byte source, byte destination) =>
        blend switch
        {
            "Zero" => 0.0f,
            "One" => 1.0f,
            "DestColor" => destination / 255.0f,
            "InverseDestColor" => 1.0f - (destination / 255.0f),
            "SrcAlpha" => sourceAlpha / 255.0f,
            "InverseSrcAlpha" => 1.0f - (sourceAlpha / 255.0f),
            "DestAlpha" => 1.0f,
            "InverseDestAlpha" => 0.0f,
            _ => 1.0f
        };

    private static byte SourceAlpha(
        IReadOnlyList<DreamcastPvrTaVertex> vertices,
        byte? textureAlpha,
        bool textureAlphaMultipliesVertex) =>
        SourceAlpha(vertices.Count == 0 ? byte.MaxValue : (byte)(vertices[0].ColorValue >> 24), textureAlpha, textureAlphaMultipliesVertex);

    private static byte SourceAlpha(
        byte vertexAlpha,
        byte? textureAlpha,
        bool textureAlphaMultipliesVertex)
    {
        if (textureAlpha is null)
        {
            return vertexAlpha;
        }

        return textureAlphaMultipliesVertex
            ? (byte)(((textureAlpha.Value * vertexAlpha) + 127) / 255)
            : textureAlpha.Value;
    }

    private static (byte Red, byte Green, byte Blue) Rgb565ToRgb888(ushort value) =>
        (
            Expand5((value >> 11) & 0x1F),
            Expand6((value >> 5) & 0x3F),
            Expand5(value & 0x1F));

    private static ushort Argb1555ToRgb565(ushort value)
    {
        var red = (value >> 10) & 0x1F;
        var green = (value >> 5) & 0x1F;
        var blue = value & 0x1F;
        return (ushort)((red << 11) | (((green << 1) | (green >> 4)) << 5) | blue);
    }

    private static byte Argb1555Alpha(ushort value) =>
        (value & 0x8000) == 0 ? (byte)0 : byte.MaxValue;

    private static ushort Argb4444ToRgb565(ushort value)
    {
        var red = (value >> 8) & 0xF;
        var green = (value >> 4) & 0xF;
        var blue = value & 0xF;
        return (ushort)((Expand4To5(red) << 11) | (Expand4To6(green) << 5) | Expand4To5(blue));
    }

    private static byte Argb4444Alpha(ushort value) =>
        Expand4To8((value >> 12) & 0xF);

    private static byte Expand4To8(int value) =>
        (byte)((value << 4) | value);

    private static int Expand4To5(int value) =>
        (value << 1) | (value >> 3);

    private static int Expand4To6(int value) =>
        (value << 2) | (value >> 2);

    private static byte Expand5(int value) =>
        (byte)((value << 3) | (value >> 2));

    private static byte Expand6(int value) =>
        (byte)((value << 2) | (value >> 4));

    private static ushort Rgb888ToRgb565(byte red, byte green, byte blue) =>
        (ushort)(((red >> 3) << 11) | ((green >> 2) << 5) | (blue >> 3));

    private static byte ClampToByte(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value), 0, 255);

    private static ushort ReadRgb565Pixel(ReadOnlySpan<byte> vram, int pixelIndex)
    {
        var offset = pixelIndex * 2;
        return offset + 1 >= vram.Length ? (ushort)0 : (ushort)(vram[offset] | (vram[offset + 1] << 8));
    }

    private static void WriteRgb565Pixel(Span<byte> vram, int pixelIndex, ushort color)
    {
        var offset = pixelIndex * 2;
        if (offset + 1 >= vram.Length)
        {
            return;
        }

        vram[offset] = (byte)(color & 0xFF);
        vram[offset + 1] = (byte)(color >> 8);
    }

    private sealed record DreamcastPvrPreviewSourceSample(
        ushort Rgb565,
        byte? Alpha,
        bool AlphaMultipliesVertex = true);

    private sealed record DreamcastPvrPreviewSpriteVertex(
        float X,
        float Y,
        float U,
        float V);
}
