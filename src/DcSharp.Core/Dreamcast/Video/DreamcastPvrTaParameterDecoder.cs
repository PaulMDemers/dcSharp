namespace DcSharp.Core.Dreamcast.Video;

public static class DreamcastPvrTaParameterDecoder
{
    public static DreamcastPvrTaParameterHeader Decode(string region, uint value)
    {
        var command = DreamcastPvrTaCommandDecoder.Decode(region, value);
        var parameterType = string.Equals(region, "TA_INPUT", StringComparison.Ordinal)
            ? (int)((value >> 29) & 0x7)
            : (int?)null;

        return new DreamcastPvrTaParameterHeader(
            region,
            value,
            $"0x{value:X8}",
            command.Kind,
            parameterType,
            command.ListType,
            command.ListTypeName,
            command.EndOfStrip,
            ExpectedPayloadWords(command.Kind),
            string.Equals(command.Kind, "PolygonHeader", StringComparison.Ordinal) ? DecodePolygonHeaderCommand(value) : null);
    }

    private static int? ExpectedPayloadWords(string kind) =>
        kind switch
        {
            "Vertex" or "VertexEndOfStrip" => null,
            "PolygonHeader" => 7,
            "SpriteHeader" => 7,
            "ModifierVolume" => 7,
            "UserClip" => 7,
            "YuvConverterData" => 0,
            _ => null
        };

    private static DreamcastPvrTaPolygonHeaderCommand DecodePolygonHeaderCommand(uint value)
    {
        var colorFormat = (int)((value >> 4) & 0x3);
        var clipMode = (int)((value >> 16) & 0x3);
        var stripLength = (int)((value >> 18) & 0x3);
        return new DreamcastPvrTaPolygonHeaderCommand(
            (value & 0x0000_0001u) != 0,
            (value & 0x0000_0002u) != 0,
            (value & 0x0000_0004u) != 0,
            (value & 0x0000_0008u) != 0,
            colorFormat,
            ColorFormatName(colorFormat),
            (value & 0x0000_0040u) != 0,
            (value & 0x0000_0080u) != 0,
            clipMode,
            ClipModeName(clipMode),
            stripLength,
            StripLengthName(stripLength),
            (value & 0x0080_0000u) != 0);
    }

    private static string ColorFormatName(int colorFormat) =>
        colorFormat switch
        {
            0 => "ArgbPacked",
            1 => "FourFloats",
            2 => "Intensity",
            _ => "IntensityPrevious"
        };

    private static string ClipModeName(int clipMode) =>
        clipMode switch
        {
            0 => "Disabled",
            2 => "Inside",
            3 => "Outside",
            _ => "Reserved"
        };

    private static string StripLengthName(int stripLength) =>
        stripLength switch
        {
            0 => "Strip1",
            1 => "Strip2",
            2 => "Strip4",
            _ => "Strip6"
        };
}

public sealed record DreamcastPvrTaParameterHeader(
    string Region,
    uint Value,
    string ValueHex,
    string Kind,
    int? ParameterType,
    int? ListType,
    string? ListTypeName,
    bool EndOfStrip,
    int? ExpectedPayloadWords,
    DreamcastPvrTaPolygonHeaderCommand? PolygonHeaderCommand)
{
    public bool HasKnownPayloadLength => ExpectedPayloadWords is not null;
}

public sealed record DreamcastPvrTaPolygonHeaderCommand(
    bool Uv16Bit,
    bool Gouraud,
    bool OffsetColorEnabled,
    bool TextureEnabled,
    int ColorFormat,
    string ColorFormatName,
    bool ModifierNormal,
    bool ModifierEnabled,
    int ClipMode,
    string ClipModeName,
    int StripLength,
    string StripLengthName,
    bool AutoStripLength);
