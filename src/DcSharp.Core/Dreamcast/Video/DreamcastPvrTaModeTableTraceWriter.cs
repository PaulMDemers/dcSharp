using DcSharp.Core.Dreamcast.Memory;
using DcSharp.Core.Execution;
using System.Globalization;

namespace DcSharp.Core.Dreamcast.Video;

public static class DreamcastPvrTaModeTableTraceWriter
{
    private const int MaxWritesPerTable = 24;

    public static void WriteText(
        TextWriter writer,
        IReadOnlyList<DreamcastPvrTaSpriteSummary> sprites,
        IReadOnlyList<MemoryAccess> memoryReads,
        IReadOnlyList<MemoryAccess> memoryWrites,
        int? limit = null,
        string? previewStatus = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(sprites);
        ArgumentNullException.ThrowIfNull(memoryReads);
        ArgumentNullException.ThrowIfNull(memoryWrites);

        if (limit is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "--pvr-ta-mode-table-log-limit must be zero or greater.");
        }

        var groups = sprites
            .Where(sprite => previewStatus is null || string.Equals(PreviewStatus(sprite), previewStatus, StringComparison.OrdinalIgnoreCase))
            .GroupBy(sprite => new ModeTableKey(
                PreviewStatus(sprite),
                sprite.Region,
                sprite.ListTypeName,
                sprite.HeaderValue,
                sprite.HeaderValueHex,
                sprite.ControlValueHex,
                sprite.HeaderPayload.Mode1,
                sprite.HeaderPayload.Mode1Hex,
                sprite.HeaderPayload.Mode2,
                sprite.HeaderPayload.Mode2Hex,
                sprite.HeaderPayload.Mode3,
                sprite.HeaderPayload.Mode3Hex,
                sprite.HeaderPayload.EffectiveTextureEnabled,
                sprite.HeaderPayload.Mode3Fields.TextureBaseHex,
                sprite.HeaderPayload.Mode3Fields.PixelFormatName))
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.PreviewStatus, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Mode2Hex, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Mode3Hex, StringComparer.Ordinal)
            .ToArray();
        var skipped = limit is { } requestedLimit && requestedLimit < groups.Length
            ? groups.Length - requestedLimit
            : 0;

        writer.WriteLine("# Dreamcast PVR TA mode table provenance");
        writer.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"# sprites={sprites.Count} groups={groups.Length} reads={memoryReads.Count} writes={memoryWrites.Count} skipped={skipped} limit={FormatLimit(limit)} status={previewStatus ?? "all"}"));
        writer.WriteLine("# columns: index status count region list header control effectiveTexture mode1 mode2 mode3 texBase texFormat tableCandidates tableWrites headerPcs controlPcs");

        for (var index = skipped; index < groups.Length; index++)
        {
            var group = groups[index];
            var key = group.Key;
            var candidates = FindCandidates(key, memoryReads).ToArray();
            writer.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"#{index} status={key.PreviewStatus} count={group.Count()} region={key.Region} list={key.ListTypeName ?? "-"} header={key.HeaderHex} control={key.ControlHex} effectiveTexture={key.EffectiveTextureEnabled} mode1={key.Mode1Hex} mode2={key.Mode2Hex} mode3={key.Mode3Hex} texBase={key.TextureBaseHex} texFormat={key.PixelFormatName} tableCandidates={FormatCandidates(candidates)} tableWrites={FormatWrites(candidates, memoryWrites)} headerPcs={FormatDistinct(group.Select(sprite => sprite.HeaderInstructionPcHex))} controlPcs={FormatDistinct(group.Select(sprite => sprite.ControlInstructionPcHex))}"));
        }
    }

    private static IEnumerable<ModeTableCandidate> FindCandidates(ModeTableKey key, IReadOnlyList<MemoryAccess> reads)
    {
        var readWords = reads
            .Where(read => read.Kind == MemoryAccessKind.Read && read.Size == 4)
            .GroupBy(read => read.Address)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var candidates = new Dictionary<uint, ModeTableCandidate>();

        foreach (var read in reads.Where(read => read.Kind == MemoryAccessKind.Read && read.Size == 4 && read.Value == key.Header))
        {
            if (!HasRead(readWords, read.Address + 4, key.Mode1, out var mode1Read) ||
                !HasRead(readWords, read.Address + 8, key.Mode2, out var mode2Read) ||
                !HasRead(readWords, read.Address + 12, key.Mode3, out var mode3Read))
            {
                continue;
            }

            candidates[read.Address] = new ModeTableCandidate(
                read.Address,
                read.Pc,
                mode1Read.Pc,
                mode2Read.Pc,
                mode3Read.Pc);
        }

        foreach (var mode1Read in reads.Where(read => read.Kind == MemoryAccessKind.Read && read.Size == 4 && read.Value == key.Mode1 && read.Address >= 4))
        {
            var baseAddress = mode1Read.Address - 4;
            if (candidates.ContainsKey(baseAddress) ||
                !HasRead(readWords, baseAddress + 8, key.Mode2, out var mode2Read) ||
                !HasRead(readWords, baseAddress + 12, key.Mode3, out var mode3Read))
            {
                continue;
            }

            candidates[baseAddress] = new ModeTableCandidate(
                baseAddress,
                null,
                mode1Read.Pc,
                mode2Read.Pc,
                mode3Read.Pc);
        }

        return candidates.Values.OrderBy(candidate => candidate.BaseAddress);
    }

    private static bool HasRead(
        IReadOnlyDictionary<uint, MemoryAccess[]> readsByAddress,
        uint address,
        uint value,
        out MemoryAccess access)
    {
        if (readsByAddress.TryGetValue(address, out var reads))
        {
            foreach (var read in reads)
            {
                if (read.Value == value)
                {
                    access = read;
                    return true;
                }
            }
        }

        access = default!;
        return false;
    }

    private static string FormatCandidates(IReadOnlyList<ModeTableCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return "-";
        }

        return string.Join(
            "/",
            candidates
                .GroupBy(candidate => candidate.BaseAddress)
                .OrderBy(group => group.Key)
                .Select(group =>
                {
                    var first = group.First();
                    return $"0x{first.BaseAddress:X8}:readPcs={FormatPcSet(group.SelectMany(candidate => new[] { candidate.HeaderReadPc, candidate.Mode1ReadPc, candidate.Mode2ReadPc, candidate.Mode3ReadPc }))}";
                }));
    }

    private static string FormatWrites(IReadOnlyList<ModeTableCandidate> candidates, IReadOnlyList<MemoryAccess> writes)
    {
        var bases = candidates.Select(candidate => candidate.BaseAddress).Distinct().ToArray();
        if (bases.Length == 0)
        {
            return "-";
        }

        var formatted = new List<string>();
        foreach (var baseAddress in bases.OrderBy(address => address))
        {
            var entryWrites = writes
                .Where(write => write.Kind == MemoryAccessKind.Write &&
                    write.Size == 4 &&
                    write.Address >= baseAddress &&
                    write.Address <= baseAddress + 16)
                .GroupBy(write => new WriteKey(write.Address, write.Value, write.Pc, write.Opcode))
                .Select(group => group.First())
                .OrderBy(write => write.Pc ?? uint.MaxValue)
                .ThenBy(write => write.Address)
                .ToArray();
            if (entryWrites.Length == 0)
            {
                continue;
            }

            var skipped = Math.Max(0, entryWrites.Length - MaxWritesPerTable);
            var suffix = skipped == 0 ? string.Empty : $",...(+{skipped})";
            formatted.Add($"0x{baseAddress:X8}:{string.Join(",", entryWrites.Take(MaxWritesPerTable).Select(write => FormatWrite(baseAddress, write)))}{suffix}");
        }

        return formatted.Count == 0 ? "-" : string.Join("/", formatted);
    }

    private static string FormatWrite(uint baseAddress, MemoryAccess write)
    {
        var offset = write.Address - baseAddress;
        var word = (offset / 4) switch
        {
            0 => "header",
            1 => "mode1",
            2 => "mode2",
            3 => "mode3",
            4 => "argb",
            _ => $"word{offset / 4}"
        };
        return $"{word}@+0x{offset:X2}=0x{write.Value:X8},pc={FormatPc(write.Pc)},{DreamcastMemoryAccessProducerFormatter.Format(write)}";
    }

    private static string FormatPcSet(IEnumerable<uint?> pcs)
    {
        var concrete = pcs
            .OfType<uint>()
            .Distinct()
            .OrderBy(pc => pc)
            .Take(8)
            .Select(pc => $"0x{pc:X8}")
            .ToArray();
        return concrete.Length == 0 ? "-" : string.Join(",", concrete);
    }

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

    private static string FormatPc(uint? pc) =>
        pc is { } value ? $"0x{value:X8}" : "-";

    private static string PreviewStatus(DreamcastPvrTaSpriteSummary sprite) =>
        sprite.HasRenderablePreviewArea
            ? "renderable"
            : sprite.HasFinitePreviewCoordinates ? "degenerate" : "nonfinite";

    private static string FormatLimit(int? limit) =>
        limit?.ToString(CultureInfo.InvariantCulture) ?? "all";

    private sealed record ModeTableCandidate(
        uint BaseAddress,
        uint? HeaderReadPc,
        uint? Mode1ReadPc,
        uint? Mode2ReadPc,
        uint? Mode3ReadPc);

    private sealed record WriteKey(uint Address, uint Value, uint? Pc, ushort? Opcode);

    private sealed record ModeTableKey(
        string PreviewStatus,
        string Region,
        string? ListTypeName,
        uint Header,
        string HeaderHex,
        string ControlHex,
        uint Mode1,
        string Mode1Hex,
        uint Mode2,
        string Mode2Hex,
        uint Mode3,
        string Mode3Hex,
        bool EffectiveTextureEnabled,
        string TextureBaseHex,
        string PixelFormatName);
}
