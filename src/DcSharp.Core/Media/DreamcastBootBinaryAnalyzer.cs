using System.Buffers.Binary;

namespace DcSharp.Core.Media;

public static class DreamcastBootBinaryAnalyzer
{
    private const int AnalysisBytes = 4096;
    private const uint DefaultLoadAddress = 0x8C01_0000;

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
        return new DreamcastBootBinaryCandidate(
            layout,
            IsElf(data),
            HasDreamcastStartupStub(data),
            recognized,
            total,
            total == 0 ? 0 : (double)recognized / total,
            nop,
            zero,
            ff,
            HexBytes(data, 32),
            HexWords(data, 8));
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

    private static bool IsRecognizedSh4Opcode(ushort opcode)
    {
        var highNibble = opcode >> 12;
        var lowNibble = opcode & 0xF;

        if (opcode is 0x0008 or 0x0009 or 0x000B or 0x0018 or 0x0019 or 0x001B or 0x0028 or 0x0029 or 0x002B or 0xF3FD or 0xFBFD)
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
    int RecognizedOpcodeCount,
    int TotalOpcodeCount,
    double RecognizedOpcodeRatio,
    int NopCount,
    int ZeroOpcodeCount,
    int FillOpcodeCount,
    string FirstBytesHex,
    string FirstWordsHex);
