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
        writer.WriteLine("# columns: index status region list headerPc controlPc payloadPcRange header control color argb texture points rawPoints payloadWords");

        for (var index = skipped; index < indexed.Length; index++)
        {
            var entry = indexed[index];
            var sprite = entry.Sprite;
            writer.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"#{entry.Index} status={PreviewStatus(sprite)} region={sprite.Region} list={sprite.ListTypeName ?? "-"} headerPc={sprite.HeaderInstructionPcHex ?? "-"} controlPc={sprite.ControlInstructionPcHex ?? "-"} payloadPcRange={FormatPayloadPcRange(sprite)} header={sprite.HeaderValueHex} control={sprite.ControlValueHex} color={sprite.Rgb565Hex} argb={sprite.HeaderPayload.ArgbHex} texture={sprite.HeaderPayload.Mode1Fields.TextureEnabled} points={FormatPoints(sprite.Vertices)} rawPoints={FormatRawPoints(sprite.Vertices)} payloadWords={FormatPayloadWords(sprite.PayloadWords)}"));
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

    private static string FormatFloat(float value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatLimit(int? limit) =>
        limit?.ToString(CultureInfo.InvariantCulture) ?? "all";

    private sealed record IndexedSprite(int Index, DreamcastPvrTaSpriteSummary Sprite);
}
