using DcSharp.Core.Execution;
using System.Globalization;

namespace DcSharp.Core.Dreamcast.Video;

public static class DreamcastPvrTaTextureModeTraceWriter
{
    public static void WriteText(
        TextWriter writer,
        IReadOnlyList<DreamcastPvrTaSpriteSummary> sprites,
        ReadOnlySpan<byte> vram,
        int? limit = null,
        string? previewStatus = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(sprites);

        if (limit is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "--pvr-ta-texture-mode-log-limit must be zero or greater.");
        }

        var groups = sprites
            .Where(sprite => previewStatus is null || string.Equals(PreviewStatus(sprite), previewStatus, StringComparison.OrdinalIgnoreCase))
            .GroupBy(sprite => new TextureModeKey(
                PreviewStatus(sprite),
                sprite.Region,
                sprite.ListTypeName,
                sprite.HeaderValueHex,
                sprite.ControlValueHex,
                sprite.HeaderPayload.Mode1Hex,
                sprite.HeaderPayload.Mode2Hex,
                sprite.HeaderPayload.Mode3Hex,
                sprite.HeaderPayload.EffectiveTextureEnabled,
                sprite.HeaderPayload.Mode2Fields.TextureUSizeName,
                sprite.HeaderPayload.Mode2Fields.TextureVSizeName,
                sprite.HeaderPayload.Mode3Fields.TextureBaseHex,
                sprite.HeaderPayload.Mode3Fields.PixelFormatName,
                sprite.HeaderPayload.Mode3Fields.NonTwiddled,
                sprite.HeaderPayload.Mode3Fields.VqEnabled,
                sprite.HeaderPayload.Mode3Fields.MipMapEnabled))
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.PreviewStatus, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Mode2Hex, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Mode3Hex, StringComparer.Ordinal)
            .ToArray();
        var skipped = limit is { } requestedLimit && requestedLimit < groups.Length
            ? groups.Length - requestedLimit
            : 0;

        writer.WriteLine("# Dreamcast PVR TA texture mode candidates");
        writer.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"# sprites={sprites.Count} groups={groups.Length} skipped={skipped} limit={FormatLimit(limit)} status={previewStatus ?? "all"} vramBytes={vram.Length}"));
        writer.WriteLine("# columns: index status count region list header control effectiveTexture mode1 mode2 mode3 decodedBase texSize texFormat texLayout vq mip candidates headerPcs controlPcs");

        for (var index = skipped; index < groups.Length; index++)
        {
            var group = groups[index];
            var first = group.First();
            var textureBytes = TextureFootprintBytes(first);
            writer.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"#{index} status={group.Key.PreviewStatus} count={group.Count()} region={group.Key.Region} list={group.Key.ListTypeName ?? "-"} header={group.Key.HeaderValueHex} control={group.Key.ControlValueHex} effectiveTexture={group.Key.EffectiveTextureEnabled} mode1={group.Key.Mode1Hex} mode2={group.Key.Mode2Hex} mode3={group.Key.Mode3Hex} decodedBase={group.Key.DecodedTextureBaseHex} texSize={group.Key.TextureUSizeName}x{group.Key.TextureVSizeName} texFormat={group.Key.PixelFormatName} texLayout={(group.Key.NonTwiddled ? "nonTwiddled" : "twiddled")} vq={group.Key.VqEnabled} mip={group.Key.MipMapEnabled} candidates={FormatCandidates(first, vram, textureBytes)} headerPcs={FormatDistinct(group.Select(sprite => sprite.HeaderInstructionPcHex))} controlPcs={FormatDistinct(group.Select(sprite => sprite.ControlInstructionPcHex))}"));
        }
    }

    private static string FormatCandidates(DreamcastPvrTaSpriteSummary sprite, ReadOnlySpan<byte> vram, int textureBytes)
    {
        var seen = new HashSet<uint>();
        var formatted = new List<string>();
        foreach (var candidate in CandidateBases(sprite))
        {
            if (!seen.Add(candidate.Address))
            {
                continue;
            }

            formatted.Add(FormatCandidate(candidate, vram, textureBytes));
        }

        return string.Join("/", formatted);
    }

    private static IEnumerable<TextureBaseCandidate> CandidateBases(DreamcastPvrTaSpriteSummary sprite)
    {
        var mode2 = sprite.HeaderPayload.Mode2;
        var mode3 = sprite.HeaderPayload.Mode3;
        var mode3Address = mode3 & 0x01FF_FFFFu;
        var mode2Low21 = mode2 & 0x001F_FFFFu;
        var mode2Low16 = mode2 & 0x0000_FFFFu;

        yield return new("decodedMode3", sprite.HeaderPayload.Mode3Fields.TextureBase);
        yield return new("mode3Low25Shift3", mode3Address << 3);
        yield return new("mode3Low25Shift2", mode3Address << 2);
        yield return new("mode2Low21", mode2Low21);
        yield return new("mode2Low21Shift3", mode2Low21 << 3);
        yield return new("mode2Low16", mode2Low16);
        yield return new("mode2Low16Shift3", mode2Low16 << 3);
    }

    private static string FormatCandidate(TextureBaseCandidate candidate, ReadOnlySpan<byte> vram, int textureBytes)
    {
        var inBounds = candidate.Address < vram.Length;
        if (!inBounds)
        {
            return $"{candidate.Name}@0x{candidate.Address:X8}:inBounds=False";
        }

        var length = Math.Min(Math.Max(textureBytes, 2), vram.Length - (int)candidate.Address);
        var span = vram.Slice((int)candidate.Address, length);
        var nonZero = 0;
        int? firstNonZero = null;
        for (var index = 0; index < span.Length; index++)
        {
            if (span[index] == 0)
            {
                continue;
            }

            nonZero++;
            firstNonZero ??= index;
        }

        var word0 = span.Length >= 2
            ? (ushort)(span[0] | (span[1] << 8))
            : (ushort?)null;
        var first = firstNonZero is { } offset ? $"0x{candidate.Address + (uint)offset:X8}" : "-";
        return $"{candidate.Name}@0x{candidate.Address:X8}:inBounds=True:bytes={length}:nonZero={nonZero}:first={first}:word0={FormatHex(word0)}";
    }

    private static int TextureFootprintBytes(DreamcastPvrTaSpriteSummary sprite)
    {
        var width = TextureSize(sprite.HeaderPayload.Mode2Fields.TextureUSize);
        var height = TextureSize(sprite.HeaderPayload.Mode2Fields.TextureVSize);
        if (width == 0 || height == 0)
        {
            return 0;
        }

        return width * height * 2;
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

    private static string FormatDistinct(IEnumerable<string?> values)
    {
        var distinct = values
            .Select(value => value ?? "-")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .Take(8)
            .ToArray();
        return string.Join(",", distinct);
    }

    private static string FormatHex(ushort? value) =>
        value is null ? "-" : $"0x{value.Value:X4}";

    private static string PreviewStatus(DreamcastPvrTaSpriteSummary sprite) =>
        sprite.HasRenderablePreviewArea
            ? "renderable"
            : sprite.HasFinitePreviewCoordinates ? "degenerate" : "nonfinite";

    private static string FormatLimit(int? limit) =>
        limit?.ToString(CultureInfo.InvariantCulture) ?? "all";

    private sealed record TextureBaseCandidate(string Name, uint Address);

    private sealed record TextureModeKey(
        string PreviewStatus,
        string Region,
        string? ListTypeName,
        string HeaderValueHex,
        string ControlValueHex,
        string Mode1Hex,
        string Mode2Hex,
        string Mode3Hex,
        bool EffectiveTextureEnabled,
        string TextureUSizeName,
        string TextureVSizeName,
        string DecodedTextureBaseHex,
        string PixelFormatName,
        bool NonTwiddled,
        bool VqEnabled,
        bool MipMapEnabled);
}
