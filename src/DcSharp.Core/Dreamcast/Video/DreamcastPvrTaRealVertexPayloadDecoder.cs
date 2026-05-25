namespace DcSharp.Core.Dreamcast.Video;

public static class DreamcastPvrTaRealVertexPayloadDecoder
{
    private const int VertexPayloadWords = 7;
    private const int PolygonHeaderPayloadWords = 7;

    public static IReadOnlyList<DreamcastPvrTaRealVertexPayload> Decode(IReadOnlyList<DreamcastPvrTaCommandWrite> writes)
    {
        ArgumentNullException.ThrowIfNull(writes);

        var stream = DreamcastPvrTaStreamDecoder.Decode(writes);
        var decoded = new List<DreamcastPvrTaRealVertexPayload>();
        PendingPolygonHeader? pendingHeader = null;
        PendingVertex? pendingVertex = null;
        DreamcastPvrTaCommandWrite? activePolygonHeader = null;

        foreach (var write in stream)
        {
            if (write.Role == "Control")
            {
                pendingVertex = null;
                if (string.Equals(write.ControlKind, "PolygonHeader", StringComparison.Ordinal))
                {
                    pendingHeader = new PendingPolygonHeader(write.Write);
                    activePolygonHeader = null;
                    continue;
                }

                pendingHeader = null;
                if (activePolygonHeader is not null && IsVertexControl(write.Write))
                {
                    pendingVertex = new PendingVertex(activePolygonHeader, write.Write);
                }

                continue;
            }

            if (pendingHeader is not null && write.PayloadWordIndex is { } headerIndex)
            {
                if (headerIndex == 0 && IsVertexControl(write.Write))
                {
                    pendingHeader = null;
                    activePolygonHeader = null;
                    continue;
                }

                pendingHeader.PayloadWordsSeen++;
                if (pendingHeader.PayloadWordsSeen == PolygonHeaderPayloadWords)
                {
                    activePolygonHeader = IsRenderableInput(pendingHeader.Header) ? pendingHeader.Header : null;
                    pendingHeader = null;
                }

                continue;
            }

            if (pendingVertex is not null && write.PayloadWordIndex is { } vertexIndex)
            {
                pendingVertex.Words[vertexIndex] = write.Write.Value;
                pendingVertex.WordSeen[vertexIndex] = true;
                if (pendingVertex.WordSeen.Count(seen => seen) == VertexPayloadWords)
                {
                    decoded.Add(CreatePayload(pendingVertex));
                    pendingVertex = null;
                }
            }
        }

        return decoded;
    }

    private static DreamcastPvrTaRealVertexPayload CreatePayload(PendingVertex pending)
    {
        var x = BitConverter.UInt32BitsToSingle(pending.Words[0]);
        var y = BitConverter.UInt32BitsToSingle(pending.Words[1]);
        var z = BitConverter.UInt32BitsToSingle(pending.Words[2]);
        var u = BitConverter.UInt32BitsToSingle(pending.Words[3]);
        var v = BitConverter.UInt32BitsToSingle(pending.Words[4]);
        var color = Argb8888ToRgb565(pending.Words[5]);
        return new DreamcastPvrTaRealVertexPayload(
            pending.Header.Region,
            pending.Header.ListType,
            pending.Header.ListTypeName,
            pending.Control.Value,
            pending.Control.ValueHex,
            pending.Control.EndOfStrip,
            pending.Words[0],
            Hex32(pending.Words[0]),
            x,
            (int)MathF.Round(x),
            pending.Words[1],
            Hex32(pending.Words[1]),
            y,
            (int)MathF.Round(y),
            pending.Words[2],
            Hex32(pending.Words[2]),
            z,
            pending.Words[3],
            Hex32(pending.Words[3]),
            u,
            pending.Words[4],
            Hex32(pending.Words[4]),
            v,
            pending.Words[5],
            Hex32(pending.Words[5]),
            color,
            $"0x{color:X4}",
            pending.Words[6],
            Hex32(pending.Words[6]));
    }

    private static bool IsVertexControl(DreamcastPvrTaCommandWrite write) =>
        string.Equals(write.Kind, "Vertex", StringComparison.Ordinal)
        || string.Equals(write.Kind, "VertexEndOfStrip", StringComparison.Ordinal);

    private static bool IsRenderableInput(DreamcastPvrTaCommandWrite write) =>
        string.Equals(write.Region, "TA_INPUT", StringComparison.Ordinal)
        && write.ListTypeName is "OpaquePolygon" or "TranslucentPolygon" or "PunchThroughPolygon";

    private static ushort Argb8888ToRgb565(uint value)
    {
        var red = (value >> 16) & 0xFF;
        var green = (value >> 8) & 0xFF;
        var blue = value & 0xFF;
        return (ushort)(((red >> 3) << 11) | ((green >> 2) << 5) | (blue >> 3));
    }

    private static string Hex32(uint value) => $"0x{value:X8}";

    private sealed record PendingPolygonHeader(DreamcastPvrTaCommandWrite Header)
    {
        public int PayloadWordsSeen { get; set; }
    }

    private sealed record PendingVertex(DreamcastPvrTaCommandWrite Header, DreamcastPvrTaCommandWrite Control)
    {
        public uint[] Words { get; } = new uint[VertexPayloadWords];
        public bool[] WordSeen { get; } = new bool[VertexPayloadWords];
    }
}

public sealed record DreamcastPvrTaRealVertexPayload(
    string Region,
    int? ListType,
    string? ListTypeName,
    uint ControlValue,
    string ControlValueHex,
    bool EndOfStrip,
    uint XValue,
    string XValueHex,
    float X,
    int RoundedX,
    uint YValue,
    string YValueHex,
    float Y,
    int RoundedY,
    uint ZValue,
    string ZValueHex,
    float Z,
    uint UValue,
    string UValueHex,
    float U,
    uint VValue,
    string VValueHex,
    float V,
    uint Argb,
    string ArgbHex,
    ushort Rgb565,
    string Rgb565Hex,
    uint OffsetArgb,
    string OffsetArgbHex);
