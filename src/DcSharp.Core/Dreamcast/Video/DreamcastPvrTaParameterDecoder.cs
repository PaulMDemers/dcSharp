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
            ExpectedPayloadWords(command.Kind));
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
    int? ExpectedPayloadWords)
{
    public bool HasKnownPayloadLength => ExpectedPayloadWords is not null;
}
