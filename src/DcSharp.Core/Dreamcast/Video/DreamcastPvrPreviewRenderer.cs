using System.Numerics;

namespace DcSharp.Core.Dreamcast.Video;

public static class DreamcastPvrPreviewRenderer
{
    public const int Width = 320;

    public static void RenderStrip(DreamcastPvrTaStrip strip, Span<byte> vram) =>
        RenderStrip(strip, vram, [], useDepth: false);

    public static void RenderStrip(DreamcastPvrTaStrip strip, Span<byte> vram, Span<float> depthBuffer) =>
        RenderStrip(strip, vram, depthBuffer, useDepth: true);

    private static void RenderStrip(DreamcastPvrTaStrip strip, Span<byte> vram, Span<float> depthBuffer, bool useDepth)
    {
        var vertices = strip.Vertices.Take(3).ToArray();
        if (vertices.Length < 3)
        {
            return;
        }

        var minX = vertices.Min(vertex => vertex.X);
        var minY = vertices.Min(vertex => vertex.Y);
        var a = new Vector2(vertices[0].X - minX, vertices[0].Y - minY);
        var b = new Vector2(vertices[1].X - minX, vertices[1].Y - minY);
        var c = new Vector2(vertices[2].X - minX, vertices[2].Y - minY);
        if (IsCulled(strip, a, b, c))
        {
            return;
        }

        var maxX = (int)MathF.Max(a.X, MathF.Max(b.X, c.X));
        var maxY = (int)MathF.Max(a.Y, MathF.Max(b.Y, c.Y));

        for (var y = 0; y <= maxY; y++)
        {
            for (var x = 0; x <= maxX; x++)
            {
                var point = new Vector2(x, y);
                if (IsInsideTriangle(point, a, b, c))
                {
                    var pixelIndex = PreviewPixelIndex(x, y);
                    if (!PassesDepth(strip, vertices, pixelIndex, depthBuffer, useDepth))
                    {
                        continue;
                    }

                    WritePreviewPixel(strip, vertices, point, a, b, c, vram, pixelIndex);
                    WriteDepth(strip, vertices, pixelIndex, depthBuffer, useDepth);
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

    private static int PreviewPixelIndex(int x, int y) =>
        (y * Width) + x;

    private static void WritePreviewPixel(
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
        if (mode2?.AlphaEnabled == true)
        {
            source = source with
            {
                Rgb565 = BlendRgb565(
                    source.Rgb565,
                    ReadRgb565Pixel(vram, pixelIndex),
                    SourceAlpha(vertices, source.Alpha),
                    mode2.BlendSrcName,
                    mode2.BlendDstName)
            };
        }

        WriteRgb565Pixel(vram, pixelIndex, source.Rgb565);
    }

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
            return new DreamcastPvrPreviewSourceSample(strip.Rgb565, null);
        }

        var width = TextureSize(payload.Mode2Fields.TextureUSize);
        var height = TextureSize(payload.Mode2Fields.TextureVSize);
        if (width <= 0 || height <= 0)
        {
            return new DreamcastPvrPreviewSourceSample(strip.Rgb565, null);
        }

        var (weightA, weightB, weightC) = Barycentric(point, a, b, c);
        var u = (vertices[0].U * weightA) + (vertices[1].U * weightB) + (vertices[2].U * weightC);
        var v = (vertices[0].V * weightA) + (vertices[1].V * weightB) + (vertices[2].V * weightC);
        var texelX = Math.Clamp((int)MathF.Round(u * (width - 1)), 0, width - 1);
        var texelY = Math.Clamp((int)MathF.Round(v * (height - 1)), 0, height - 1);
        var texelIndex = payload.Mode3Fields.NonTwiddled
            ? (texelY * width) + texelX
            : TwiddledTextureIndex(texelX, texelY);
        var textureOffset = checked((int)payload.Mode3Fields.TextureBase + (texelIndex * 2));
        var texel = ReadRgb565Pixel(vram, textureOffset / 2);
        var textureAlphaEnabled = !payload.Mode2Fields.TextureAlphaDisabled;
        return payload.Mode3Fields.PixelFormatName switch
        {
            "Rgb565" => new DreamcastPvrPreviewSourceSample(texel, null),
            "Argb1555" => new DreamcastPvrPreviewSourceSample(Argb1555ToRgb565(texel), textureAlphaEnabled ? Argb1555Alpha(texel) : null),
            "Argb4444" => new DreamcastPvrPreviewSourceSample(Argb4444ToRgb565(texel), textureAlphaEnabled ? Argb4444Alpha(texel) : null),
            _ => new DreamcastPvrPreviewSourceSample(strip.Rgb565, null)
        };
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

    private static byte SourceAlpha(IReadOnlyList<DreamcastPvrTaVertex> vertices, byte? textureAlpha)
    {
        var vertexAlpha = vertices.Count == 0 ? byte.MaxValue : (byte)(vertices[0].ColorValue >> 24);
        return textureAlpha is null
            ? vertexAlpha
            : (byte)(((textureAlpha.Value * vertexAlpha) + 127) / 255);
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

    private sealed record DreamcastPvrPreviewSourceSample(ushort Rgb565, byte? Alpha);
}
