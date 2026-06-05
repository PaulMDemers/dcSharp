using DcSharp.Core.Dreamcast.Memory;
using DcSharp.Core.Execution;
using System.Globalization;

namespace DcSharp.Core.Dreamcast.Video;

public static class DreamcastPvrTaSpriteStoreQueueTraceWriter
{
    public static void WriteText(
        TextWriter writer,
        IReadOnlyList<DreamcastPvrTaSpriteSummary> sprites,
        IReadOnlyList<MemoryAccess> storeQueueWrites,
        int? limit = null,
        string? previewStatus = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(sprites);
        ArgumentNullException.ThrowIfNull(storeQueueWrites);

        if (limit is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "--pvr-ta-sprite-sq-log-limit must be zero or greater.");
        }

        var packets = BuildPackets(storeQueueWrites);
        var matches = MatchSprites(sprites, packets, previewStatus).ToArray();
        var skipped = limit is { } requestedLimit && requestedLimit < matches.Length
            ? matches.Length - requestedLimit
            : 0;

        writer.WriteLine("# Dreamcast PVR TA sprite store-queue provenance");
        writer.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"# sprites={sprites.Count} sqWrites={storeQueueWrites.Count} sqPackets={packets.Count} matched={matches.Length} skipped={skipped} limit={FormatLimit(limit)} status={previewStatus ?? "all"}"));
        writer.WriteLine("# columns: index status region list headerPc controlFlushPc payloadFlushPcRange sqBase producerPcRange header control mode1 mode2 mode3 rawSize fallbackPixels controlProducer payloadProducers payloadWords rawPoints");

        for (var index = skipped; index < matches.Length; index++)
        {
            var match = matches[index];
            var sprite = match.Sprite;
            var packet = match.Packet;
            writer.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"#{match.SpriteIndex} status={PreviewStatus(sprite)} region={sprite.Region} list={sprite.ListTypeName ?? "-"} headerPc={sprite.HeaderInstructionPcHex ?? "-"} controlFlushPc={sprite.ControlInstructionPcHex ?? "-"} payloadFlushPcRange={FormatPayloadPcRange(sprite)} sqBase=0x{packet.BaseAddress:X8} producerPcRange={FormatProducerPcRange(packet.Words)} header={sprite.HeaderValueHex} control={sprite.ControlValueHex} mode1={sprite.HeaderPayload.Mode1Hex} mode2={sprite.HeaderPayload.Mode2Hex} mode3={sprite.HeaderPayload.Mode3Hex} rawSize={FormatRawSize(sprite)} fallbackPixels={EstimatedFallbackPixelCount(sprite)} controlProducer={FormatProducer(packet.Words[0], "Control")} payloadProducers={FormatPayloadProducers(sprite, packet)} payloadWords={FormatPayloadWords(sprite.PayloadWords)} rawPoints={FormatRawPoints(sprite.Vertices)}"));
        }
    }

    private static IReadOnlyList<StoreQueuePacket> BuildPackets(IReadOnlyList<MemoryAccess> writes)
    {
        var packets = new List<StoreQueuePacket>();
        var ordered = writes
            .Where(write => write.Kind == MemoryAccessKind.Write && write.Size == 4 && IsStoreQueueAddress(write.Address))
            .ToArray();

        for (var index = 0; index <= ordered.Length - StoreQueuePacket.WordCount; index++)
        {
            var first = ordered[index];
            var baseAddress = first.Address & 0xFFFF_FFE0u;
            if ((first.Address & 0x1Fu) != 0)
            {
                continue;
            }

            var words = new StoreQueueProducerWord[StoreQueuePacket.WordCount];
            var complete = true;
            for (var wordIndex = 0; wordIndex < StoreQueuePacket.WordCount; wordIndex++)
            {
                var write = ordered[index + wordIndex];
                var expectedAddress = baseAddress + (uint)(wordIndex * 4);
                if (write.Address != expectedAddress)
                {
                    complete = false;
                    break;
                }

                words[wordIndex] = new StoreQueueProducerWord(
                    wordIndex,
                    write.Address,
                    write.Value,
                    write.Pc);
            }

            if (complete)
            {
                packets.Add(new StoreQueuePacket(baseAddress, words));
            }
        }

        return packets;
    }

    private static IEnumerable<SpriteStoreQueueMatch> MatchSprites(
        IReadOnlyList<DreamcastPvrTaSpriteSummary> sprites,
        IReadOnlyList<StoreQueuePacket> packets,
        string? previewStatus)
    {
        var nextPacketIndex = 0;
        for (var spriteIndex = 0; spriteIndex < sprites.Count; spriteIndex++)
        {
            var sprite = sprites[spriteIndex];
            if (previewStatus is not null && !string.Equals(PreviewStatus(sprite), previewStatus, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var packetIndex = FindPacket(sprite, packets, nextPacketIndex);
            if (packetIndex < 0)
            {
                continue;
            }

            nextPacketIndex = packetIndex + 1;
            yield return new SpriteStoreQueueMatch(spriteIndex, sprite, packets[packetIndex]);
        }
    }

    private static int FindPacket(
        DreamcastPvrTaSpriteSummary sprite,
        IReadOnlyList<StoreQueuePacket> packets,
        int startIndex)
    {
        for (var index = startIndex; index < packets.Count; index++)
        {
            if (PacketMatches(sprite, packets[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool PacketMatches(DreamcastPvrTaSpriteSummary sprite, StoreQueuePacket packet)
    {
        if (packet.Words[0].Value != sprite.ControlValue)
        {
            return false;
        }

        if (sprite.PayloadWords.Count > StoreQueuePacket.PayloadWordCount)
        {
            return false;
        }

        for (var index = 0; index < sprite.PayloadWords.Count; index++)
        {
            if (packet.Words[index + 1].Value != sprite.PayloadWords[index].Value)
            {
                return false;
            }
        }

        return true;
    }

    private static string PreviewStatus(DreamcastPvrTaSpriteSummary sprite) =>
        sprite.HasRenderablePreviewArea
            ? "renderable"
            : sprite.HasFinitePreviewCoordinates ? "degenerate" : "nonfinite";

    private static string FormatPayloadPcRange(DreamcastPvrTaSpriteSummary sprite) =>
        sprite.FirstPayloadInstructionPcHex == sprite.LastPayloadInstructionPcHex
            ? sprite.FirstPayloadInstructionPcHex ?? "-"
            : $"{sprite.FirstPayloadInstructionPcHex ?? "-"}-{sprite.LastPayloadInstructionPcHex ?? "-"}";

    private static string FormatProducerPcRange(IReadOnlyList<StoreQueueProducerWord> words)
    {
        var pcs = words.Select(word => word.Pc).OfType<uint>().ToArray();
        if (pcs.Length == 0)
        {
            return "-";
        }

        var min = pcs.Min();
        var max = pcs.Max();
        return min == max ? $"0x{min:X8}" : $"0x{min:X8}-0x{max:X8}";
    }

    private static string FormatProducer(StoreQueueProducerWord word, string name) =>
        $"{name}@0x{word.Address:X8}=0x{word.Value:X8},pc={FormatPc(word.Pc)}";

    private static string FormatPayloadProducers(DreamcastPvrTaSpriteSummary sprite, StoreQueuePacket packet) =>
        sprite.PayloadWords.Count == 0
            ? "-"
            : string.Join(
                "/",
                sprite.PayloadWords.Select((word, index) => FormatProducer(packet.Words[index + 1], word.Name)));

    private static string FormatPayloadWords(IReadOnlyList<DreamcastPvrTaSpritePayloadWordSummary> words) =>
        words.Count == 0
            ? "-"
            : string.Join("/", words.Select(word => $"{word.Name}={word.ValueHex}"));

    private static string FormatRawPoints(IReadOnlyList<DreamcastPvrTaSpriteVertexSummary> vertices) =>
        string.Join("/", vertices.Select(vertex => $"{vertex.Name}:{vertex.XValueHex},{vertex.YValueHex},z={vertex.ZValueHex}"));

    private static string FormatRawSize(DreamcastPvrTaSpriteSummary sprite) =>
        $"{FormatFloat(SpriteExtent(sprite, vertex => vertex.RawX))}x{FormatFloat(SpriteExtent(sprite, vertex => vertex.RawY))}";

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

    private static string FormatPc(uint? pc) =>
        pc is { } value ? $"0x{value:X8}" : "-";

    private static bool IsStoreQueueAddress(uint address) =>
        address is >= 0xE000_0000u and < 0xE400_0000u;

    private sealed record StoreQueuePacket(uint BaseAddress, IReadOnlyList<StoreQueueProducerWord> Words)
    {
        public const int WordCount = 16;
        public const int PayloadWordCount = WordCount - 1;
    }

    private sealed record StoreQueueProducerWord(int Index, uint Address, uint Value, uint? Pc);

    private sealed record SpriteStoreQueueMatch(
        int SpriteIndex,
        DreamcastPvrTaSpriteSummary Sprite,
        StoreQueuePacket Packet);
}
