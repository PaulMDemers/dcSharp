namespace DcSharp.Core.Dreamcast.Video;

public sealed class DreamcastPvrTaState
{
    private bool inRenderableOpaqueList;
    private uint currentHeaderValue;
    private readonly DreamcastPvrTaDiagnosticVertexPacketDecoder vertexDecoder = new();
    private readonly List<DreamcastPvrTaVertex> currentVertices = [];
    private readonly List<DreamcastPvrTaStrip> completedStrips = [];

    public IReadOnlyList<DreamcastPvrTaStrip> CompletedStrips => completedStrips;

    public DreamcastPvrTaRenderCommand? Accept(DreamcastPvrTaCommandWrite write)
    {
        if (!IsOpaqueInput(write))
        {
            ResetStrip();
            return null;
        }

        if (string.Equals(write.Kind, "PolygonHeader", StringComparison.Ordinal))
        {
            inRenderableOpaqueList = true;
            currentHeaderValue = write.Value;
            ResetStrip();
            return null;
        }

        if (!inRenderableOpaqueList)
        {
            return null;
        }

        if (vertexDecoder.HasPending)
        {
            return AcceptVertexPayload(write);
        }

        if (string.Equals(write.Kind, "Vertex", StringComparison.Ordinal))
        {
            vertexDecoder.Begin(write, endOfStrip: false);
            return null;
        }

        if (string.Equals(write.Kind, "VertexEndOfStrip", StringComparison.Ordinal))
        {
            vertexDecoder.Begin(write, endOfStrip: true);
            return null;
        }

        ResetStrip();
        return null;
    }

    private DreamcastPvrTaRenderCommand? AcceptVertexPayload(DreamcastPvrTaCommandWrite write)
    {
        if (!vertexDecoder.AcceptPayload(write.Value, out var vertex))
        {
            return null;
        }

        if (vertex is null || vertex.Rgb565 == 0)
        {
            ResetStrip();
            return null;
        }

        var color = vertex.Rgb565;
        if (currentVertices.Count > 0 && currentVertices[0].Rgb565 != color)
        {
            ResetStrip();
            return null;
        }

        currentVertices.Add(vertex);
        if (!vertex.EndOfStrip)
        {
            return null;
        }

        var canRender = currentVertices.Count >= 3;
        var strip = canRender
            ? new DreamcastPvrTaStrip(
                write.Region,
                write.ListType,
                write.ListTypeName,
                currentHeaderValue,
                $"0x{currentHeaderValue:X8}",
                color,
                $"0x{color:X4}",
                currentVertices.ToArray())
            : null;
        if (strip is not null)
        {
            completedStrips.Add(strip);
        }

        var result = strip is not null ? new DreamcastPvrTaRenderCommand(strip) : null;
        ResetStrip();
        return result;
    }

    private static bool IsOpaqueInput(DreamcastPvrTaCommandWrite write) =>
        string.Equals(write.Region, "TA_INPUT", StringComparison.Ordinal)
        && string.Equals(write.ListTypeName, "OpaquePolygon", StringComparison.Ordinal);

    private void ResetStrip()
    {
        vertexDecoder.Reset();
        currentVertices.Clear();
    }
}

public sealed record DreamcastPvrTaVertex(
    int X,
    int Y,
    bool EndOfStrip,
    ushort Rgb565,
    string Rgb565Hex,
    uint ControlValue,
    string ControlValueHex,
    uint XValue,
    string XValueHex,
    uint YValue,
    string YValueHex,
    uint ColorValue,
    string ColorValueHex);

public sealed record DreamcastPvrTaStrip(
    string Region,
    int? ListType,
    string? ListTypeName,
    uint HeaderValue,
    string HeaderValueHex,
    ushort Rgb565,
    string Rgb565Hex,
    IReadOnlyList<DreamcastPvrTaVertex> Vertices);

public sealed record DreamcastPvrTaRenderCommand(DreamcastPvrTaStrip Strip)
{
    public ushort Rgb565 => Strip.Rgb565;
}
