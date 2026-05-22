namespace DcSharp.Core.Dreamcast.Video;

public sealed class DreamcastPvrTaState
{
    private bool inRenderableOpaqueList;
    private uint currentHeaderValue;
    private PendingVertexPacket? pendingVertex;
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

        if (pendingVertex is not null)
        {
            return AcceptVertexPayload(write);
        }

        if (string.Equals(write.Kind, "Vertex", StringComparison.Ordinal))
        {
            pendingVertex = new PendingVertexPacket(write, EndOfStrip: false);
            return null;
        }

        if (string.Equals(write.Kind, "VertexEndOfStrip", StringComparison.Ordinal))
        {
            pendingVertex = new PendingVertexPacket(write, EndOfStrip: true);
            return null;
        }

        ResetStrip();
        return null;
    }

    private DreamcastPvrTaRenderCommand? AcceptVertexPayload(DreamcastPvrTaCommandWrite write)
    {
        if (pendingVertex is null)
        {
            return null;
        }

        pendingVertex = pendingVertex.AcceptPayload(write.Value);
        if (!pendingVertex.IsComplete)
        {
            return null;
        }

        var vertex = pendingVertex.ToVertex();
        pendingVertex = null;
        if (vertex.Rgb565 == 0)
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
        pendingVertex = null;
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

internal sealed record PendingVertexPacket(
    DreamcastPvrTaCommandWrite Control,
    bool EndOfStrip,
    uint? XValue = null,
    uint? YValue = null,
    uint? ColorValue = null)
{
    public bool IsComplete => XValue is not null && YValue is not null && ColorValue is not null;

    public PendingVertexPacket AcceptPayload(uint value)
    {
        if (XValue is null)
        {
            return this with { XValue = value };
        }

        if (YValue is null)
        {
            return this with { YValue = value };
        }

        return this with { ColorValue = value };
    }

    public DreamcastPvrTaVertex ToVertex()
    {
        var xValue = XValue ?? 0;
        var yValue = YValue ?? 0;
        var colorValue = ColorValue ?? 0;
        var color = (ushort)(colorValue & 0xFFFF);
        return new DreamcastPvrTaVertex(
            DecodeSigned16Dot16(xValue),
            DecodeSigned16Dot16(yValue),
            EndOfStrip,
            color,
            $"0x{color:X4}",
            Control.Value,
            Control.ValueHex,
            xValue,
            $"0x{xValue:X8}",
            yValue,
            $"0x{yValue:X8}",
            colorValue,
            $"0x{colorValue:X8}");
    }

    private static int DecodeSigned16Dot16(uint value) =>
        unchecked((short)(value >> 16));
}

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
