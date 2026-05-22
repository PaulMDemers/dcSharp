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
                if (IsInsideTriangle(new Vector2(x, y), a, b, c))
                {
                    var pixelIndex = PreviewPixelIndex(x, y);
                    if (!PassesDepth(strip, vertices, pixelIndex, depthBuffer, useDepth))
                    {
                        continue;
                    }

                    WriteRgb565Pixel(vram, pixelIndex, strip.Rgb565);
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
}
