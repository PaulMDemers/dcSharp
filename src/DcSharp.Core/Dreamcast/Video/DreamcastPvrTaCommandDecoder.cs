namespace DcSharp.Core.Dreamcast.Video;

public static class DreamcastPvrTaCommandDecoder
{
    public static DreamcastPvrTaCommandInfo Decode(string region, uint value)
    {
        if (!string.Equals(region, "TA_INPUT", StringComparison.Ordinal))
        {
            return new DreamcastPvrTaCommandInfo("YuvConverterData", null, null, false);
        }

        var listType = (int)((value >> 24) & 0x7);
        var listName = ListTypeName(listType);
        var endOfStrip = (value & 0x1000_0000) != 0;
        var kind = (value & 0xF000_0000u) switch
        {
            0xF000_0000u => "VertexEndOfStrip",
            0xE000_0000u => "Vertex",
            _ => HeaderKind(value)
        };

        return new DreamcastPvrTaCommandInfo(kind, listType, listName, endOfStrip);
    }

    private static string HeaderKind(uint value)
    {
        var headerType = (value >> 29) & 0x7;
        return headerType switch
        {
            1 => "UserClip",
            4 when (value & 0x0084_0000u) == 0x0084_0000u => "PolygonHeader",
            4 => "ModifierVolume",
            5 => "SpriteHeader",
            _ => "Unknown"
        };
    }

    private static string? ListTypeName(int listType) => listType switch
    {
        0 => "OpaquePolygon",
        1 => "OpaqueModifier",
        2 => "TranslucentPolygon",
        3 => "TranslucentModifier",
        4 => "PunchThroughPolygon",
        _ => null
    };
}

public sealed record DreamcastPvrTaCommandInfo(
    string Kind,
    int? ListType,
    string? ListTypeName,
    bool EndOfStrip);
