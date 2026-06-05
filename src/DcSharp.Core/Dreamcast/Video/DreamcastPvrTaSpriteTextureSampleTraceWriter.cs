using DcSharp.Core.Execution;
using System.Globalization;

namespace DcSharp.Core.Dreamcast.Video;

public static class DreamcastPvrTaSpriteTextureSampleTraceWriter
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
            throw new ArgumentOutOfRangeException(nameof(limit), "--pvr-ta-sprite-texture-sample-log-limit must be zero or greater.");
        }

        var indexed = sprites
            .Select((sprite, index) => new IndexedSprite(index, sprite))
            .Where(entry => previewStatus is null || string.Equals(PreviewStatus(entry.Sprite), previewStatus, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var skipped = limit is { } requestedLimit && requestedLimit < indexed.Length
            ? indexed.Length - requestedLimit
            : 0;

        writer.WriteLine("# Dreamcast PVR TA sprite texture samples");
        writer.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"# sprites={sprites.Count} matched={indexed.Length} skipped={skipped} limit={FormatLimit(limit)} status={previewStatus ?? "all"} vramBytes={vram.Length}"));
        writer.WriteLine("# columns: index sample status region list headerPc controlPc payloadPcRange header control color effectiveTexture mode1 mode2 mode3 texBase texSize texFormat texLayout texFilter texShading alpha uv adjustedUv texel texelIndex byteOffset rawTexel rgb565 sampleAlpha inBounds sampleable");

        for (var index = skipped; index < indexed.Length; index++)
        {
            var entry = indexed[index];
            var sprite = entry.Sprite;
            var samples = SpriteSamples(sprite).ToArray();
            if (samples.Length == 0)
            {
                WriteSample(writer, entry.Index, sprite, null, SampleTexture(sprite, vram, 0.0f, 0.0f));
                continue;
            }

            foreach (var sample in samples)
            {
                WriteSample(writer, entry.Index, sprite, sample, SampleTexture(sprite, vram, sample.U, sample.V));
            }
        }
    }

    private static IEnumerable<SpriteUvSample> SpriteSamples(DreamcastPvrTaSpriteSummary sprite)
    {
        var vertices = sprite.Vertices.Take(4).ToArray();
        if (vertices.Length > 0)
        {
            yield return new SpriteUvSample(
                "fallbackAvg",
                vertices.Average(vertex => vertex.U),
                vertices.Average(vertex => vertex.V));
        }

        foreach (var vertex in vertices)
        {
            yield return new SpriteUvSample(vertex.Name, vertex.U, vertex.V);
        }
    }

    private static void WriteSample(
        TextWriter writer,
        int index,
        DreamcastPvrTaSpriteSummary sprite,
        SpriteUvSample? uvSample,
        TextureSample sample)
    {
        writer.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"#{index} sample={uvSample?.Name ?? "-"} status={PreviewStatus(sprite)} region={sprite.Region} list={sprite.ListTypeName ?? "-"} headerPc={sprite.HeaderInstructionPcHex ?? "-"} controlPc={sprite.ControlInstructionPcHex ?? "-"} payloadPcRange={FormatPayloadPcRange(sprite)} header={sprite.HeaderValueHex} control={sprite.ControlValueHex} color={sprite.Rgb565Hex} effectiveTexture={sprite.HeaderPayload.EffectiveTextureEnabled} mode1={sprite.HeaderPayload.Mode1Hex} mode2={sprite.HeaderPayload.Mode2Hex} mode3={sprite.HeaderPayload.Mode3Hex} texBase={sprite.HeaderPayload.Mode3Fields.TextureBaseHex} texSize={FormatTextureSize(sprite)} texFormat={sprite.HeaderPayload.Mode3Fields.PixelFormatName} texLayout={FormatTextureLayout(sprite)} texFilter={sprite.HeaderPayload.Mode2Fields.FilterModeName} texShading={sprite.HeaderPayload.Mode2Fields.TextureShadingName} alpha={FormatAlpha(sprite)} uv={FormatUv(uvSample?.U, uvSample?.V)} adjustedUv={FormatUv(sample.AdjustedU, sample.AdjustedV)} texel={FormatTexel(sample.TexelX, sample.TexelY)} texelIndex={FormatNumber(sample.TexelIndex)} byteOffset={FormatHex(sample.ByteOffset)} rawTexel={FormatHex(sample.RawTexel)} rgb565={FormatHex(sample.Rgb565)} sampleAlpha={FormatAlphaValue(sample.Alpha)} inBounds={sample.InBounds} sampleable={sample.Sampleable}"));
    }

    private static TextureSample SampleTexture(DreamcastPvrTaSpriteSummary sprite, ReadOnlySpan<byte> vram, float sourceU, float sourceV)
    {
        if (!CanSampleSpriteTexture(sprite))
        {
            return TextureSample.NotSampleable;
        }

        var mode2 = sprite.HeaderPayload.Mode2Fields;
        var mode3 = sprite.HeaderPayload.Mode3Fields;
        var textureWidth = TextureSize(mode2.TextureUSize);
        var textureHeight = TextureSize(mode2.TextureVSize);
        var u = TextureCoordinate(sourceU, mode2.UClamp, mode2.UFlip);
        var v = TextureCoordinate(sourceV, mode2.VClamp, mode2.VFlip);
        var texelX = Math.Clamp((int)MathF.Round(u * (textureWidth - 1)), 0, textureWidth - 1);
        var texelY = Math.Clamp((int)MathF.Round(v * (textureHeight - 1)), 0, textureHeight - 1);
        var texelIndex = mode3.NonTwiddled
            ? (texelY * textureWidth) + texelX
            : TwiddledTextureIndex(texelX, texelY);
        var byteOffset = (long)mode3.TextureBase + ((long)texelIndex * 2);
        var inBounds = byteOffset >= 0 && byteOffset + 1 < vram.Length;
        if (!inBounds)
        {
            return new TextureSample(true, u, v, texelX, texelY, texelIndex, byteOffset, null, null, null, false);
        }

        var rawTexel = (ushort)(vram[(int)byteOffset] | (vram[(int)byteOffset + 1] << 8));
        var textureAlphaEnabled = !mode2.TextureAlphaDisabled;
        return mode3.PixelFormatName switch
        {
            "Rgb565" => new TextureSample(true, u, v, texelX, texelY, texelIndex, byteOffset, rawTexel, rawTexel, null, true),
            "Argb1555" => new TextureSample(true, u, v, texelX, texelY, texelIndex, byteOffset, rawTexel, Argb1555ToRgb565(rawTexel), textureAlphaEnabled ? Argb1555Alpha(rawTexel) : null, true),
            "Argb4444" => new TextureSample(true, u, v, texelX, texelY, texelIndex, byteOffset, rawTexel, Argb4444ToRgb565(rawTexel), textureAlphaEnabled ? Argb4444Alpha(rawTexel) : null, true),
            _ => new TextureSample(false, u, v, texelX, texelY, texelIndex, byteOffset, rawTexel, null, null, true)
        };
    }

    private static bool CanSampleSpriteTexture(DreamcastPvrTaSpriteSummary sprite) =>
        sprite.HeaderPayload.EffectiveTextureEnabled
        && !sprite.HeaderPayload.Mode3Fields.VqEnabled
        && !sprite.HeaderPayload.Mode3Fields.MipMapEnabled
        && TextureSize(sprite.HeaderPayload.Mode2Fields.TextureUSize) > 0
        && TextureSize(sprite.HeaderPayload.Mode2Fields.TextureVSize) > 0;

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

    private static ushort Argb1555ToRgb565(ushort value)
    {
        var red = (value >> 10) & 0x1F;
        var green = (value >> 5) & 0x1F;
        var blue = value & 0x1F;
        return (ushort)((red << 11) | (((green << 1) | (green >> 4)) << 5) | blue);
    }

    private static byte Argb1555Alpha(ushort value) =>
        (value & 0x8000) == 0 ? byte.MinValue : byte.MaxValue;

    private static ushort Argb4444ToRgb565(ushort value)
    {
        var red = (value >> 8) & 0x0F;
        var green = (value >> 4) & 0x0F;
        var blue = value & 0x0F;
        return (ushort)((Expand4To5(red) << 11) | (Expand4To6(green) << 5) | Expand4To5(blue));
    }

    private static byte Argb4444Alpha(ushort value)
    {
        var alpha = (value >> 12) & 0x0F;
        return (byte)((alpha << 4) | alpha);
    }

    private static int Expand4To5(int value) =>
        (value << 1) | (value >> 3);

    private static int Expand4To6(int value) =>
        (value << 2) | (value >> 2);

    private static string PreviewStatus(DreamcastPvrTaSpriteSummary sprite) =>
        sprite.HasRenderablePreviewArea
            ? "renderable"
            : sprite.HasFinitePreviewCoordinates ? "degenerate" : "nonfinite";

    private static string FormatPayloadPcRange(DreamcastPvrTaSpriteSummary sprite) =>
        sprite.FirstPayloadInstructionPcHex == sprite.LastPayloadInstructionPcHex
            ? sprite.FirstPayloadInstructionPcHex ?? "-"
            : $"{sprite.FirstPayloadInstructionPcHex ?? "-"}-{sprite.LastPayloadInstructionPcHex ?? "-"}";

    private static string FormatTextureSize(DreamcastPvrTaSpriteSummary sprite) =>
        $"{sprite.HeaderPayload.Mode2Fields.TextureUSizeName}x{sprite.HeaderPayload.Mode2Fields.TextureVSizeName}";

    private static string FormatTextureLayout(DreamcastPvrTaSpriteSummary sprite) =>
        sprite.HeaderPayload.Mode3Fields.NonTwiddled ? "nonTwiddled" : "twiddled";

    private static string FormatAlpha(DreamcastPvrTaSpriteSummary sprite) =>
        $"poly={sprite.HeaderPayload.Mode2Fields.AlphaEnabled}/tex={!sprite.HeaderPayload.Mode2Fields.TextureAlphaDisabled}";

    private static string FormatUv(float? u, float? v) =>
        u is null || v is null
            ? "-"
            : $"{FormatFloat(u.Value)},{FormatFloat(v.Value)}";

    private static string FormatTexel(int? x, int? y) =>
        x is null || y is null
            ? "-"
            : $"{x.Value.ToString(CultureInfo.InvariantCulture)},{y.Value.ToString(CultureInfo.InvariantCulture)}";

    private static string FormatHex(long? value) =>
        value is null ? "-" : $"0x{value.Value:X8}";

    private static string FormatHex(ushort? value) =>
        value is null ? "-" : $"0x{value.Value:X4}";

    private static string FormatNumber(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string FormatAlphaValue(byte? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string FormatFloat(float value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatLimit(int? limit) =>
        limit?.ToString(CultureInfo.InvariantCulture) ?? "all";

    private sealed record IndexedSprite(int Index, DreamcastPvrTaSpriteSummary Sprite);

    private sealed record SpriteUvSample(string Name, float U, float V);

    private sealed record TextureSample(
        bool Sampleable,
        float? AdjustedU,
        float? AdjustedV,
        int? TexelX,
        int? TexelY,
        int? TexelIndex,
        long? ByteOffset,
        ushort? RawTexel,
        ushort? Rgb565,
        byte? Alpha,
        bool InBounds)
    {
        public static TextureSample NotSampleable { get; } = new(false, null, null, null, null, null, null, null, null, null, false);
    }
}
