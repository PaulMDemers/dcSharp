using DcSharp.Core.Execution;
using System.Globalization;

namespace DcSharp.Core.Dreamcast.Video;

public static class DreamcastPvrTaSpriteLogWriter
{
    public static void WriteText(
        TextWriter writer,
        IReadOnlyList<DreamcastPvrTaSpriteSummary> sprites,
        int? limit = null,
        string? previewStatus = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(sprites);

        if (limit is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "--pvr-ta-sprite-log-limit must be zero or greater.");
        }

        var indexed = sprites
            .Select((sprite, index) => new IndexedSprite(index, sprite))
            .Where(entry => previewStatus is null || string.Equals(PreviewStatus(entry.Sprite), previewStatus, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var skipped = limit is { } requestedLimit && requestedLimit < indexed.Length
            ? indexed.Length - requestedLimit
            : 0;

        writer.WriteLine("# Dreamcast PVR TA sprites");
        writer.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"# sprites={sprites.Count} matched={indexed.Length} skipped={skipped} limit={FormatLimit(limit)} status={previewStatus ?? "all"}"));
        writer.WriteLine("# columns: index status region list headerPc controlPc payloadPcRange header control color argb texture cmdTexture effectiveTexture uv16 mode1 mode2 mode3 texBase texSize texFormat texLayout texFilter texShading alpha rawSize intSize fallbackPixels points rawPoints payloadWords");

        for (var index = skipped; index < indexed.Length; index++)
        {
            var entry = indexed[index];
            var sprite = entry.Sprite;
            writer.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"#{entry.Index} status={PreviewStatus(sprite)} region={sprite.Region} list={sprite.ListTypeName ?? "-"} headerPc={sprite.HeaderInstructionPcHex ?? "-"} controlPc={sprite.ControlInstructionPcHex ?? "-"} payloadPcRange={FormatPayloadPcRange(sprite)} header={sprite.HeaderValueHex} control={sprite.ControlValueHex} color={sprite.Rgb565Hex} argb={sprite.HeaderPayload.ArgbHex} texture={sprite.HeaderPayload.Mode1Fields.TextureEnabled} cmdTexture={sprite.HeaderPayload.HasTexturePayload} effectiveTexture={sprite.HeaderPayload.EffectiveTextureEnabled} uv16={HasPackedUv(sprite.HeaderValue)} mode1={sprite.HeaderPayload.Mode1Hex} mode2={sprite.HeaderPayload.Mode2Hex} mode3={sprite.HeaderPayload.Mode3Hex} texBase={sprite.HeaderPayload.Mode3Fields.TextureBaseHex} texSize={FormatTextureSize(sprite)} texFormat={sprite.HeaderPayload.Mode3Fields.PixelFormatName} texLayout={FormatTextureLayout(sprite)} texFilter={sprite.HeaderPayload.Mode2Fields.FilterModeName} texShading={sprite.HeaderPayload.Mode2Fields.TextureShadingName} alpha={FormatAlpha(sprite)} rawSize={FormatRawSize(sprite)} intSize={FormatIntegerSize(sprite)} fallbackPixels={EstimatedFallbackPixelCount(sprite)} points={FormatPoints(sprite.Vertices)} rawPoints={FormatRawPoints(sprite.Vertices)} payloadWords={FormatPayloadWords(sprite.PayloadWords)}"));
        }
    }

    private static string PreviewStatus(DreamcastPvrTaSpriteSummary sprite) =>
        sprite.HasRenderablePreviewArea
            ? "renderable"
            : sprite.HasFinitePreviewCoordinates ? "degenerate" : "nonfinite";

    private static string FormatPayloadPcRange(DreamcastPvrTaSpriteSummary sprite) =>
        sprite.FirstPayloadInstructionPcHex == sprite.LastPayloadInstructionPcHex
            ? sprite.FirstPayloadInstructionPcHex ?? "-"
            : $"{sprite.FirstPayloadInstructionPcHex ?? "-"}-{sprite.LastPayloadInstructionPcHex ?? "-"}";

    private static string FormatPoints(IReadOnlyList<DreamcastPvrTaSpriteVertexSummary> vertices) =>
        string.Join("/", vertices.Select(vertex => $"{vertex.Name}:{vertex.X},{vertex.Y}:{FormatFloat(vertex.U)},{FormatFloat(vertex.V)}"));

    private static string FormatRawPoints(IReadOnlyList<DreamcastPvrTaSpriteVertexSummary> vertices) =>
        string.Join("/", vertices.Select(vertex => $"{vertex.Name}:{vertex.XValueHex},{vertex.YValueHex},z={vertex.ZValueHex}"));

    private static string FormatPayloadWords(IReadOnlyList<DreamcastPvrTaSpritePayloadWordSummary> words) =>
        words.Count == 0
            ? "-"
            : string.Join("/", words.Select(word => $"{word.Name}={word.ValueHex}"));

    private static string FormatTextureSize(DreamcastPvrTaSpriteSummary sprite) =>
        $"{sprite.HeaderPayload.Mode2Fields.TextureUSizeName}x{sprite.HeaderPayload.Mode2Fields.TextureVSizeName}";

    private static string FormatTextureLayout(DreamcastPvrTaSpriteSummary sprite) =>
        sprite.HeaderPayload.Mode3Fields.NonTwiddled ? "nonTwiddled" : "twiddled";

    private static string FormatAlpha(DreamcastPvrTaSpriteSummary sprite) =>
        $"poly={sprite.HeaderPayload.Mode2Fields.AlphaEnabled}/tex={!sprite.HeaderPayload.Mode2Fields.TextureAlphaDisabled}";

    private static string FormatRawSize(DreamcastPvrTaSpriteSummary sprite) =>
        $"{FormatFloat(SpriteExtent(sprite, vertex => vertex.RawX))}x{FormatFloat(SpriteExtent(sprite, vertex => vertex.RawY))}";

    private static string FormatIntegerSize(DreamcastPvrTaSpriteSummary sprite) =>
        $"{IntegerExtent(sprite, vertex => vertex.X)}x{IntegerExtent(sprite, vertex => vertex.Y)}";

    private static float SpriteExtent(DreamcastPvrTaSpriteSummary sprite, Func<DreamcastPvrTaSpriteVertexSummary, float> selector)
    {
        var vertices = sprite.Vertices.Take(4).ToArray();
        if (vertices.Length == 0 || vertices.Any(vertex => !vertex.HasFinitePosition))
        {
            return float.NaN;
        }

        return vertices.Max(selector) - vertices.Min(selector);
    }

    private static int IntegerExtent(DreamcastPvrTaSpriteSummary sprite, Func<DreamcastPvrTaSpriteVertexSummary, int> selector)
    {
        var vertices = sprite.Vertices.Take(4).ToArray();
        return vertices.Length == 0 ? 0 : vertices.Max(selector) - vertices.Min(selector);
    }

    private static int EstimatedFallbackPixelCount(DreamcastPvrTaSpriteSummary sprite)
    {
        const int previewWidth = 640;
        var vertices = sprite.Vertices.Take(4).ToArray();
        if (vertices.Length == 0 || vertices.Any(vertex => !vertex.HasFinitePosition))
        {
            return 0;
        }

        var minX = vertices.Min(vertex => vertex.RawX);
        var minY = vertices.Min(vertex => vertex.RawY);
        var maxX = vertices.Max(vertex => vertex.RawX);
        var maxY = vertices.Max(vertex => vertex.RawY);
        var width = maxX - minX;
        var height = maxY - minY;
        if (!float.IsFinite(width) || !float.IsFinite(height))
        {
            return 0;
        }

        if (width < height)
        {
            var startX = Math.Clamp((int)MathF.Floor(minX), 0, previewWidth - 1);
            var endX = Math.Clamp((int)MathF.Floor(maxX), 0, previewWidth - 1);
            var startY = Math.Max((int)MathF.Floor(minY), 0);
            var endY = Math.Max((int)MathF.Ceiling(maxY), 0);
            return Math.Max(0, endX - startX + 1) * Math.Max(0, endY - startY + 1);
        }

        var fallbackStartY = Math.Max((int)MathF.Floor(minY), 0);
        var fallbackEndY = Math.Max((int)MathF.Floor(maxY), 0);
        var fallbackStartX = Math.Clamp((int)MathF.Floor(minX), 0, previewWidth - 1);
        var fallbackEndX = Math.Clamp((int)MathF.Ceiling(maxX), 0, previewWidth - 1);
        return Math.Max(0, fallbackEndX - fallbackStartX + 1) * Math.Max(0, fallbackEndY - fallbackStartY + 1);
    }

    private static string FormatFloat(float value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatLimit(int? limit) =>
        limit?.ToString(CultureInfo.InvariantCulture) ?? "all";

    private static bool HasPackedUv(uint headerValue) =>
        (headerValue & 0x0000_0001u) != 0;

    private sealed record IndexedSprite(int Index, DreamcastPvrTaSpriteSummary Sprite);
}
