using System.Buffers.Binary;

namespace DcSharp.Core.Media;

public static class DreamcastBootBinaryAnalyzer
{
    private const int AnalysisBytes = 4096;
    private const uint DefaultLoadAddress = 0x8C01_0000;
    private const uint WindowsCeGdromPayloadOffset = 2048;
    private const int WindowsCeHeaderEntryOffsetField = 0x18;
    private const uint WindowsCeExpectedLoadAddress = 0x0C01_0000;

    public static DreamcastBootBinaryAnalysis Analyze(ReadOnlySpan<byte> data, string sourcePath, string sourceKind)
    {
        var original = AnalyzeCandidate(data, "original");
        var descrambledData = DreamcastBootScrambler.Descramble(data);
        var descrambled = AnalyzeCandidate(descrambledData, "descrambled");
        return new DreamcastBootBinaryAnalysis(
            sourcePath,
            sourceKind,
            data.Length,
            DefaultLoadAddress,
            $"0x{DefaultLoadAddress:X8}",
            RecommendLayout(original, descrambled),
            original,
            descrambled);
    }

    private static string RecommendLayout(DreamcastBootBinaryCandidate original, DreamcastBootBinaryCandidate descrambled)
    {
        if (original.IsElf)
        {
            return "original";
        }

        if (!original.IsElf && descrambled.IsElf)
        {
            return "descrambled";
        }

        if (original.HasDreamcastStartupStub)
        {
            return "original";
        }

        if (descrambled.HasDreamcastStartupStub)
        {
            return "descrambled";
        }

        if (original.HasWindowsCeHeader)
        {
            return "original";
        }

        return "ambiguous";
    }

    private static DreamcastBootBinaryCandidate AnalyzeCandidate(ReadOnlySpan<byte> data, string layout)
    {
        var sampleLength = Math.Min(data.Length, AnalysisBytes) & ~1;
        var recognized = 0;
        var nop = 0;
        var zero = 0;
        var ff = 0;
        for (var offset = 0; offset < sampleLength; offset += 2)
        {
            var opcode = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
            if (IsRecognizedSh4Opcode(opcode))
            {
                recognized++;
            }

            if (opcode == 0x0009)
            {
                nop++;
            }

            if (opcode == 0x0000)
            {
                zero++;
            }

            if (opcode == 0xFFFF)
            {
                ff++;
            }
        }

        var total = sampleLength / 2;
        var windowsCe = TryDetectWindowsCeBootInfo(data);
        var windowsCePayloadOffset = windowsCe is not null && data.Length > WindowsCeGdromPayloadOffset
            ? WindowsCeGdromPayloadOffset
            : (uint?)null;
        var suggestedEntryPoint = windowsCe is { } windowsCeInfo
            ? DefaultLoadAddress + (windowsCePayloadOffset is { } payloadOffset && windowsCeInfo.EntryOffset >= payloadOffset
                ? windowsCeInfo.EntryOffset - payloadOffset
                : windowsCeInfo.EntryOffset)
            : DefaultLoadAddress;
        var jumpTargetFileOffset = windowsCe?.EntryJumpTarget is { } jumpTarget && jumpTarget >= DefaultLoadAddress
            ? (uint?)(jumpTarget - DefaultLoadAddress + (windowsCePayloadOffset ?? 0))
            : null;
        var jumpTargetSample = jumpTargetFileOffset is { } targetOffset
            ? OpcodeSampleAt(data, targetOffset)
            : null;

        return new DreamcastBootBinaryCandidate(
            layout,
            IsElf(data),
            HasDreamcastStartupStub(data),
            windowsCe is not null,
            windowsCe?.EntryOffset,
            windowsCe is { } detected ? $"0x{detected.EntryOffset:X}" : null,
            windowsCePayloadOffset,
            windowsCePayloadOffset is { } detectedPayloadOffset ? $"0x{detectedPayloadOffset:X}" : null,
            windowsCe?.EntryJumpTarget,
            windowsCe?.EntryJumpTarget is { } target ? $"0x{target:X8}" : null,
            jumpTargetFileOffset,
            jumpTargetFileOffset is { } resolvedOffset ? $"0x{resolvedOffset:X}" : null,
            jumpTargetSample?.RecognizedOpcodeCount,
            jumpTargetSample?.TotalOpcodeCount,
            jumpTargetSample?.FirstWordsHex,
            suggestedEntryPoint,
            $"0x{suggestedEntryPoint:X8}",
            recognized,
            total,
            total == 0 ? 0 : (double)recognized / total,
            nop,
            zero,
            ff,
            HexBytes(data, 32),
            HexWords(data, 8));
    }

    private static OpcodeSample OpcodeSampleAt(ReadOnlySpan<byte> data, uint offset)
    {
        if (offset >= data.Length)
        {
            return new OpcodeSample(0, 0, string.Empty);
        }

        var sampleLength = Math.Min(data.Length - (int)offset, 32) & ~1;
        var recognized = 0;
        for (var sampleOffset = 0; sampleOffset < sampleLength; sampleOffset += 2)
        {
            var opcode = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice((int)offset + sampleOffset, 2));
            if (IsRecognizedSh4Opcode(opcode))
            {
                recognized++;
            }
        }

        return new OpcodeSample(
            recognized,
            sampleLength / 2,
            HexWords(data[(int)offset..], 8));
    }

    private static WindowsCeBootInfo? TryDetectWindowsCeBootInfo(ReadOnlySpan<byte> data)
    {
        var entryOffset = TryDetectWindowsCeEntryOffset(data);
        if (entryOffset is null)
        {
            return null;
        }

        var entry = data.Slice((int)entryOffset.Value);
        var movlOpcode = BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(4, 2));
        var literalOffset = ((entryOffset.Value + 8) & ~3u) + ((uint)(movlOpcode & 0xFF) * 4);
        var entryJumpTarget = literalOffset <= data.Length - 4u
            ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice((int)literalOffset, 4))
            : (uint?)null;

        return new WindowsCeBootInfo(entryOffset.Value, entryJumpTarget);
    }

    private static bool IsElf(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && data[0] == 0x7F && data[1] == (byte)'E' && data[2] == (byte)'L' && data[3] == (byte)'F';

    private static bool HasDreamcastStartupStub(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
        {
            return false;
        }

        for (var offset = 0; offset < 12; offset += 2)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2)) != 0x0009)
            {
                return false;
            }
        }

        return true;
    }

    private static uint? TryDetectWindowsCeEntryOffset(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x810)
        {
            return null;
        }

        var loadAddress = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0x14, 4));
        var entryOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(WindowsCeHeaderEntryOffsetField, 4));
        if (loadAddress != WindowsCeExpectedLoadAddress
            || entryOffset < 0x20
            || entryOffset > data.Length - 0x10
            || (entryOffset & 1) != 0)
        {
            return null;
        }

        var entry = data.Slice((int)entryOffset);
        var words = new ushort[6];
        for (var index = 0; index < words.Length; index++)
        {
            words[index] = BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(index * 2, 2));
        }

        return words is [0x0009, 0x0009, >= 0xD000 and <= 0xD0FF, 0x0009, 0x402B, 0x0009]
            ? entryOffset
            : null;
    }

    private static bool IsRecognizedSh4Opcode(ushort opcode)
    {
        var highNibble = opcode >> 12;
        var lowNibble = opcode & 0xF;

        if (opcode is 0x0008 or 0x0009 or 0x000B or 0x0018 or 0x0019 or 0x001B or 0x0028 or 0x0029 or 0x002B or 0x0038 or 0xF3FD or 0xFBFD)
        {
            return true;
        }

        if (highNibble is 0x1 or 0x5 or 0x7 or 0x9 or 0xA or 0xB or 0xD or 0xE or 0xF)
        {
            return true;
        }

        if (highNibble == 0x2)
        {
            return lowNibble is not 0x3;
        }

        if (highNibble == 0x3)
        {
            return lowNibble is not 0x1 and not 0x9;
        }

        if (highNibble == 0x4)
        {
            return lowNibble is <= 0xB or 0xE or 0xF
                || (opcode & 0xF0FF) is 0x400E or 0x401E or 0x402A or 0x402E;
        }

        if (highNibble == 0x6)
        {
            return true;
        }

        if (highNibble == 0x0)
        {
            return lowNibble is 0x4 or 0x5 or 0x6 or 0x7 or 0xC or 0xD or 0xE or 0xF
                || (opcode & 0xF0FF) is 0x0002 or 0x000A or 0x0012 or 0x001A or 0x0022 or 0x005A or 0x006A;
        }

        return (opcode & 0xFF00) is 0x8000 or 0x8100 or 0x8400 or 0x8500 or 0x8800 or 0x8900 or 0x8B00 or 0x8D00 or 0x8F00 or 0xC300 or 0xC800;
    }

    private static string HexBytes(ReadOnlySpan<byte> data, int count)
    {
        count = Math.Min(data.Length, count);
        return string.Join(" ", data[..count].ToArray().Select(value => value.ToString("X2")));
    }

    private static string HexWords(ReadOnlySpan<byte> data, int count)
    {
        count = Math.Min(data.Length / 2, count);
        var words = new string[count];
        for (var index = 0; index < count; index++)
        {
            words[index] = $"0x{BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(index * 2, 2)):X4}";
        }

        return string.Join(" ", words);
    }

    private sealed record WindowsCeBootInfo(uint EntryOffset, uint? EntryJumpTarget);

    private sealed record OpcodeSample(int RecognizedOpcodeCount, int TotalOpcodeCount, string FirstWordsHex);
}

public sealed record DreamcastBootBinaryAnalysis(
    string SourcePath,
    string SourceKind,
    int Size,
    uint LoadAddress,
    string LoadAddressHex,
    string RecommendedLayout,
    DreamcastBootBinaryCandidate Original,
    DreamcastBootBinaryCandidate Descrambled);

public sealed record DreamcastBootBinaryCandidate(
    string Layout,
    bool IsElf,
    bool HasDreamcastStartupStub,
    bool HasWindowsCeHeader,
    uint? WindowsCeEntryOffset,
    string? WindowsCeEntryOffsetHex,
    uint? WindowsCePayloadOffset,
    string? WindowsCePayloadOffsetHex,
    uint? WindowsCeEntryJumpTarget,
    string? WindowsCeEntryJumpTargetHex,
    uint? WindowsCeEntryJumpTargetFileOffset,
    string? WindowsCeEntryJumpTargetFileOffsetHex,
    int? WindowsCeEntryJumpTargetRecognizedOpcodeCount,
    int? WindowsCeEntryJumpTargetOpcodeCount,
    string? WindowsCeEntryJumpTargetFirstWordsHex,
    uint SuggestedEntryPoint,
    string SuggestedEntryPointHex,
    int RecognizedOpcodeCount,
    int TotalOpcodeCount,
    double RecognizedOpcodeRatio,
    int NopCount,
    int ZeroOpcodeCount,
    int FillOpcodeCount,
    string FirstBytesHex,
    string FirstWordsHex);
