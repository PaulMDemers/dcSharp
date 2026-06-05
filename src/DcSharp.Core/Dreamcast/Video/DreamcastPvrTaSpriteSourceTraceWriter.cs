using DcSharp.Core.Execution;
using System.Globalization;

namespace DcSharp.Core.Dreamcast.Video;

public static class DreamcastPvrTaSpriteSourceTraceWriter
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
            throw new ArgumentOutOfRangeException(nameof(limit), "--pvr-ta-sprite-source-log-limit must be zero or greater.");
        }

        var groups = sprites
            .Where(sprite => previewStatus is null || string.Equals(PreviewStatus(sprite), previewStatus, StringComparison.OrdinalIgnoreCase))
            .GroupBy(sprite => new SpriteSourceKey(
                PreviewStatus(sprite),
                sprite.Region,
                sprite.ListTypeName,
                sprite.HeaderValueHex,
                sprite.ControlValueHex,
                sprite.HeaderInstructionPcHex,
                sprite.ControlInstructionPcHex,
                FormatPayloadPcRange(sprite),
                sprite.HeaderPayload.Mode1Hex,
                sprite.HeaderPayload.Mode2Hex,
                sprite.HeaderPayload.Mode3Hex))
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.PreviewStatus, StringComparer.Ordinal)
            .ThenBy(group => group.Key.HeaderInstructionPcHex ?? string.Empty, StringComparer.Ordinal)
            .ToArray();
        var skipped = limit is { } requestedLimit && requestedLimit < groups.Length
            ? groups.Length - requestedLimit
            : 0;

        writer.WriteLine("# Dreamcast PVR TA sprite source trace");
        writer.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"# sprites={sprites.Count} groups={groups.Length} skipped={skipped} limit={FormatLimit(limit)} status={previewStatus ?? "all"}"));
        writer.WriteLine("# columns: index status count region list headerPc controlPc payloadPcRange header control mode1 mode2 mode3 rawW rawH fallbackPx xRanges yRanges zRange uvRanges firstPayload lastPayload");

        for (var index = skipped; index < groups.Length; index++)
        {
            var group = groups[index];
            var values = group.ToArray();
            writer.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"#{index} status={group.Key.PreviewStatus} count={values.Length} region={group.Key.Region} list={group.Key.ListTypeName ?? "-"} headerPc={group.Key.HeaderInstructionPcHex ?? "-"} controlPc={group.Key.ControlInstructionPcHex ?? "-"} payloadPcRange={group.Key.PayloadPcRangeHex} header={group.Key.HeaderValueHex} control={group.Key.ControlValueHex} mode1={group.Key.Mode1Hex} mode2={group.Key.Mode2Hex} mode3={group.Key.Mode3Hex} rawW={FormatRange(values.Select(SpriteWidth))} rawH={FormatRange(values.Select(SpriteHeight))} fallbackPx={FormatRange(values.Select(EstimatedFallbackPixelCount))} xRanges={FormatVertexFloatRanges(values, vertex => vertex.RawX)} yRanges={FormatVertexFloatRanges(values, vertex => vertex.RawY)} zRange={FormatRange(values.SelectMany(sprite => sprite.Vertices.Take(4).Select(vertex => vertex.Z)))} uvRanges={FormatUvRanges(values)} firstPayload={FormatPayloadWords(values.First().PayloadWords)} lastPayload={FormatPayloadWords(values.Last().PayloadWords)}"));
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

    private static string FormatVertexFloatRanges(
        IReadOnlyList<DreamcastPvrTaSpriteSummary> sprites,
        Func<DreamcastPvrTaSpriteVertexSummary, float> selector) =>
        string.Join(
            "/",
            Enumerable.Range(0, 4).Select(index =>
            {
                var name = ((char)('A' + index)).ToString();
                return $"{name}:{FormatRange(sprites.Select(sprite => sprite.Vertices.Count > index ? selector(sprite.Vertices[index]) : float.NaN))}";
            }));

    private static string FormatUvRanges(IReadOnlyList<DreamcastPvrTaSpriteSummary> sprites) =>
        string.Join(
            "/",
            Enumerable.Range(0, 4).Select(index =>
            {
                var name = ((char)('A' + index)).ToString();
                var uRange = FormatRange(sprites.Select(sprite => sprite.Vertices.Count > index ? sprite.Vertices[index].U : float.NaN));
                var vRange = FormatRange(sprites.Select(sprite => sprite.Vertices.Count > index ? sprite.Vertices[index].V : float.NaN));
                return $"{name}:u={uRange},v={vRange}";
            }));

    private static string FormatPayloadWords(IReadOnlyList<DreamcastPvrTaSpritePayloadWordSummary> words) =>
        words.Count == 0
            ? "-"
            : string.Join("/", words.Select(word => $"{word.Name}={word.ValueHex}"));

    private static string FormatRange(IEnumerable<int> values)
    {
        var concreteValues = values.ToArray();
        return concreteValues.Length == 0
            ? "-"
            : $"{concreteValues.Min()}/{FormatFloat((float)concreteValues.Average())}/{concreteValues.Max()}";
    }

    private static string FormatRange(IEnumerable<float> values)
    {
        var concreteValues = values.Where(float.IsFinite).ToArray();
        return concreteValues.Length == 0
            ? "-"
            : $"{FormatFloat(concreteValues.Min())}/{FormatFloat((float)concreteValues.Average())}/{FormatFloat(concreteValues.Max())}";
    }

    private static float SpriteWidth(DreamcastPvrTaSpriteSummary sprite) =>
        SpriteExtent(sprite, vertex => vertex.RawX);

    private static float SpriteHeight(DreamcastPvrTaSpriteSummary sprite) =>
        SpriteExtent(sprite, vertex => vertex.RawY);

    private static float SpriteExtent(DreamcastPvrTaSpriteSummary sprite, Func<DreamcastPvrTaSpriteVertexSummary, float> selector)
    {
        var vertices = sprite.Vertices.Take(4).ToArray();
        if (vertices.Length == 0 || vertices.Any(vertex => !vertex.HasFinitePosition))
        {
            return float.NaN;
        }

        return vertices.Max(selector) - vertices.Min(selector);
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

    private sealed record SpriteSourceKey(
        string PreviewStatus,
        string Region,
        string? ListTypeName,
        string HeaderValueHex,
        string ControlValueHex,
        string? HeaderInstructionPcHex,
        string? ControlInstructionPcHex,
        string PayloadPcRangeHex,
        string Mode1Hex,
        string Mode2Hex,
        string Mode3Hex);
}
