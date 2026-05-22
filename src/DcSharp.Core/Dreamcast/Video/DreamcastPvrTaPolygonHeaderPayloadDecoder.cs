namespace DcSharp.Core.Dreamcast.Video;

public static class DreamcastPvrTaPolygonHeaderPayloadDecoder
{
    public const int PayloadWordCount = 7;

    public static IReadOnlyList<DreamcastPvrTaPolygonHeaderPayload> Decode(IReadOnlyList<DreamcastPvrTaCommandWrite> writes)
    {
        ArgumentNullException.ThrowIfNull(writes);

        var stream = DreamcastPvrTaStreamDecoder.Decode(writes);
        var decoded = new List<DreamcastPvrTaPolygonHeaderPayload>();
        PendingPolygonHeaderPayload? pending = null;

        foreach (var write in stream)
        {
            if (write.Role == "Control")
            {
                pending = string.Equals(write.ControlKind, "PolygonHeader", StringComparison.Ordinal)
                    ? new PendingPolygonHeaderPayload(write.Write)
                    : null;
                continue;
            }

            if (pending is null || write.PayloadWordIndex is not { } index)
            {
                continue;
            }

            if (index == 0 && IsVertexShortcut(write.Write))
            {
                pending = null;
                continue;
            }

            pending.Words[index] = write.Write.Value;
            pending.WordSeen[index] = true;
            if (pending.WordSeen.Count(seen => seen) == PayloadWordCount)
            {
                decoded.Add(DecodePayload(pending.Header, pending.Words));
                pending = null;
            }
        }

        return decoded;
    }

    public static DreamcastPvrTaPolygonHeaderPayload DecodePayload(DreamcastPvrTaCommandWrite header, IReadOnlyList<uint> words)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(words);
        if (words.Count != PayloadWordCount)
        {
            throw new ArgumentException($"PVR TA polygon header payload must contain {PayloadWordCount} words.", nameof(words));
        }

        return CreatePayload(header, words);
    }

    private static bool IsVertexShortcut(DreamcastPvrTaCommandWrite write) =>
        string.Equals(write.Kind, "Vertex", StringComparison.Ordinal)
        || string.Equals(write.Kind, "VertexEndOfStrip", StringComparison.Ordinal);

    private static DreamcastPvrTaPolygonHeaderPayload CreatePayload(DreamcastPvrTaCommandWrite header, IReadOnlyList<uint> words) =>
        new(
            header.Region,
            header.ListType,
            header.ListTypeName,
            header.Value,
            header.ValueHex,
            words[0],
            Hex32(words[0]),
            DecodeMode1(words[0]),
            words[1],
            Hex32(words[1]),
            DecodeMode2(words[1]),
            words[2],
            Hex32(words[2]),
            DecodeMode3(words[2]),
            words[3],
            Hex32(words[3]),
            words[4],
            Hex32(words[4]),
            words[5],
            Hex32(words[5]),
            words[6],
            Hex32(words[6]));

    private static DreamcastPvrTaPolygonHeaderMode1 DecodeMode1(uint value)
    {
        var culling = (int)((value >> 27) & 0x3);
        var depthCompare = (int)((value >> 29) & 0x7);
        return new DreamcastPvrTaPolygonHeaderMode1(
            (value & 0x0200_0000u) != 0,
            (value & 0x0400_0000u) != 0,
            culling,
            CullingName(culling),
            depthCompare,
            DepthCompareName(depthCompare));
    }

    private static DreamcastPvrTaPolygonHeaderMode2 DecodeMode2(uint value)
    {
        var textureVSize = (int)(value & 0x7);
        var textureUSize = (int)((value >> 3) & 0x7);
        var textureShading = (int)((value >> 6) & 0x3);
        var filterMode = (int)((value >> 13) & 0x3);
        var fogType = (int)((value >> 22) & 0x3);
        var blendDst = (int)((value >> 26) & 0x7);
        var blendSrc = (int)((value >> 29) & 0x7);
        return new DreamcastPvrTaPolygonHeaderMode2(
            textureVSize,
            TextureSizeName(textureVSize),
            textureUSize,
            TextureSizeName(textureUSize),
            textureShading,
            TextureShadingName(textureShading),
            (int)((value >> 8) & 0xF),
            (value & 0x0000_1000u) != 0,
            filterMode,
            FilterModeName(filterMode),
            (value & 0x0000_8000u) != 0,
            (value & 0x0001_0000u) != 0,
            (value & 0x0002_0000u) != 0,
            (value & 0x0004_0000u) != 0,
            (value & 0x0008_0000u) != 0,
            (value & 0x0010_0000u) != 0,
            (value & 0x0020_0000u) != 0,
            fogType,
            FogTypeName(fogType),
            (value & 0x0100_0000u) != 0,
            (value & 0x0200_0000u) != 0,
            blendDst,
            BlendModeName(blendDst),
            blendSrc,
            BlendModeName(blendSrc));
    }

    private static DreamcastPvrTaPolygonHeaderMode3 DecodeMode3(uint value)
    {
        var pixelFormat = (int)((value >> 27) & 0x7);
        return new DreamcastPvrTaPolygonHeaderMode3(
            value & 0x01FF_FFFFu,
            Hex32(value & 0x01FF_FFFFu),
            (value & 0x0200_0000u) != 0,
            (value & 0x0400_0000u) != 0,
            pixelFormat,
            PixelFormatName(pixelFormat),
            (value & 0x4000_0000u) != 0,
            (value & 0x8000_0000u) != 0);
    }

    private static string CullingName(int value) =>
        value switch
        {
            0 => "None",
            1 => "Small",
            2 => "Ccw",
            _ => "Cw"
        };

    private static string DepthCompareName(int value) =>
        value switch
        {
            0 => "Never",
            1 => "Less",
            2 => "Equal",
            3 => "LessOrEqual",
            4 => "Greater",
            5 => "NotEqual",
            6 => "GreaterOrEqual",
            _ => "Always"
        };

    private static string TextureSizeName(int value) =>
        value switch
        {
            0 => "8",
            1 => "16",
            2 => "32",
            3 => "64",
            4 => "128",
            5 => "256",
            6 => "512",
            _ => "1024"
        };

    private static string TextureShadingName(int value) =>
        value switch
        {
            0 => "Replace",
            1 => "Modulate",
            2 => "Decal",
            _ => "ModulateAlpha"
        };

    private static string FilterModeName(int value) =>
        value switch
        {
            0 => "Nearest",
            1 => "Bilinear",
            2 => "Trilinear1",
            _ => "Trilinear2"
        };

    private static string FogTypeName(int value) =>
        value switch
        {
            0 => "Table",
            1 => "Vertex",
            2 => "Disabled",
            _ => "Table2"
        };

    private static string BlendModeName(int value) =>
        value switch
        {
            0 => "Zero",
            1 => "One",
            2 => "DestColor",
            3 => "InverseDestColor",
            4 => "SrcAlpha",
            5 => "InverseSrcAlpha",
            6 => "DestAlpha",
            _ => "InverseDestAlpha"
        };

    private static string PixelFormatName(int value) =>
        value switch
        {
            0 => "Argb1555",
            1 => "Rgb565",
            2 => "Argb4444",
            3 => "Yuv422",
            4 => "Bump",
            5 => "Palette4Bpp",
            6 => "Palette8Bpp",
            _ => "Reserved"
        };

    private static string Hex32(uint value) => $"0x{value:X8}";

    private sealed record PendingPolygonHeaderPayload(DreamcastPvrTaCommandWrite Header)
    {
        public uint[] Words { get; } = new uint[PayloadWordCount];
        public bool[] WordSeen { get; } = new bool[PayloadWordCount];
    }
}

public sealed record DreamcastPvrTaPolygonHeaderPayload(
    string Region,
    int? ListType,
    string? ListTypeName,
    uint HeaderValue,
    string HeaderValueHex,
    uint Mode1,
    string Mode1Hex,
    DreamcastPvrTaPolygonHeaderMode1 Mode1Fields,
    uint Mode2,
    string Mode2Hex,
    DreamcastPvrTaPolygonHeaderMode2 Mode2Fields,
    uint Mode3,
    string Mode3Hex,
    DreamcastPvrTaPolygonHeaderMode3 Mode3Fields,
    uint Parameter0,
    string Parameter0Hex,
    uint Parameter1,
    string Parameter1Hex,
    uint Parameter2,
    string Parameter2Hex,
    uint Parameter3,
    string Parameter3Hex);

public sealed record DreamcastPvrTaPolygonHeaderMode1(
    bool TextureEnabled,
    bool DepthWriteDisabled,
    int Culling,
    string CullingName,
    int DepthCompare,
    string DepthCompareName);

public sealed record DreamcastPvrTaPolygonHeaderMode2(
    int TextureVSize,
    string TextureVSizeName,
    int TextureUSize,
    string TextureUSizeName,
    int TextureShading,
    string TextureShadingName,
    int MipMapBias,
    bool SuperSampling,
    int FilterMode,
    string FilterModeName,
    bool VClamp,
    bool UClamp,
    bool VFlip,
    bool UFlip,
    bool TextureAlphaDisabled,
    bool AlphaEnabled,
    bool FogClamp,
    int FogType,
    string FogTypeName,
    bool BlendDstAccumulation2,
    bool BlendSrcAccumulation2,
    int BlendDst,
    string BlendDstName,
    int BlendSrc,
    string BlendSrcName);

public sealed record DreamcastPvrTaPolygonHeaderMode3(
    uint TextureBase,
    string TextureBaseHex,
    bool TextureStride32,
    bool NonTwiddled,
    int PixelFormat,
    string PixelFormatName,
    bool VqEnabled,
    bool MipMapEnabled);
