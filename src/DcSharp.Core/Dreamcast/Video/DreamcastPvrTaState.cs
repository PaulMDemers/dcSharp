namespace DcSharp.Core.Dreamcast.Video;

public sealed class DreamcastPvrTaState
{
    private const int PolygonHeaderPayloadWords = 7;
    private bool inRenderableOpaqueList;
    private bool awaitingHeaderPayloadOrShortcut;
    private bool inRealStream;
    private int headerPayloadWordsRemaining;
    private uint currentHeaderValue;
    private readonly DreamcastPvrTaDiagnosticVertexPacketDecoder diagnosticVertexDecoder = new();
    private readonly DreamcastPvrTaRealVertexPacketDecoder realVertexDecoder = new();
    private readonly List<DreamcastPvrTaVertex> currentVertices = [];
    private readonly List<DreamcastPvrTaStrip> completedStrips = [];

    public IReadOnlyList<DreamcastPvrTaStrip> CompletedStrips => completedStrips;

    public DreamcastPvrTaRenderCommand? Accept(DreamcastPvrTaCommandWrite write)
    {
        if (!string.Equals(write.Region, "TA_INPUT", StringComparison.Ordinal))
        {
            ResetStrip();
            return null;
        }

        if (IsOpaqueInput(write) && string.Equals(write.Kind, "PolygonHeader", StringComparison.Ordinal))
        {
            ResetStrip();
            inRenderableOpaqueList = true;
            currentHeaderValue = write.Value;
            awaitingHeaderPayloadOrShortcut = true;
            return null;
        }

        if (!inRenderableOpaqueList)
        {
            return null;
        }

        if (awaitingHeaderPayloadOrShortcut)
        {
            awaitingHeaderPayloadOrShortcut = false;
            if (!IsVertexControl(write))
            {
                inRealStream = true;
                headerPayloadWordsRemaining = PolygonHeaderPayloadWords - 1;
                return null;
            }
        }

        if (headerPayloadWordsRemaining > 0)
        {
            headerPayloadWordsRemaining--;
            return null;
        }

        if (realVertexDecoder.HasPending)
        {
            return AcceptRealVertexPayload(write);
        }

        if (diagnosticVertexDecoder.HasPending)
        {
            return AcceptDiagnosticVertexPayload(write);
        }

        if (IsOpaqueInput(write) && IsVertexControl(write))
        {
            var endOfStrip = string.Equals(write.Kind, "VertexEndOfStrip", StringComparison.Ordinal);
            if (inRealStream)
            {
                realVertexDecoder.Begin(write, endOfStrip);
            }
            else
            {
                diagnosticVertexDecoder.Begin(write, endOfStrip);
            }

            return null;
        }

        ResetStrip();
        return null;
    }

    private DreamcastPvrTaRenderCommand? AcceptDiagnosticVertexPayload(DreamcastPvrTaCommandWrite write)
    {
        if (!diagnosticVertexDecoder.AcceptPayload(write.Value, out var vertex))
        {
            return null;
        }

        return AcceptVertex(write, vertex);
    }

    private DreamcastPvrTaRenderCommand? AcceptRealVertexPayload(DreamcastPvrTaCommandWrite write)
    {
        if (!realVertexDecoder.AcceptPayload(write.Value, out var vertex))
        {
            return null;
        }

        return AcceptVertex(write, vertex);
    }

    private DreamcastPvrTaRenderCommand? AcceptVertex(DreamcastPvrTaCommandWrite write, DreamcastPvrTaVertex? vertex)
    {
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

    private static bool IsVertexControl(DreamcastPvrTaCommandWrite write) =>
        string.Equals(write.Kind, "Vertex", StringComparison.Ordinal)
        || string.Equals(write.Kind, "VertexEndOfStrip", StringComparison.Ordinal);

    private static bool IsOpaqueInput(DreamcastPvrTaCommandWrite write) =>
        string.Equals(write.Region, "TA_INPUT", StringComparison.Ordinal)
        && string.Equals(write.ListTypeName, "OpaquePolygon", StringComparison.Ordinal);

    private void ResetStrip()
    {
        awaitingHeaderPayloadOrShortcut = false;
        inRealStream = false;
        headerPayloadWordsRemaining = 0;
        diagnosticVertexDecoder.Reset();
        realVertexDecoder.Reset();
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
