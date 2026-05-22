namespace DcSharp.Core.Dreamcast.Video;

public sealed class DreamcastPvrTaState
{
    private bool inRenderableOpaqueList;
    private uint currentHeaderValue;
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

        if (string.Equals(write.Kind, "Vertex", StringComparison.Ordinal))
        {
            return AcceptVertex(write, endOfStrip: false);
        }

        if (string.Equals(write.Kind, "VertexEndOfStrip", StringComparison.Ordinal))
        {
            return AcceptVertex(write, endOfStrip: true);
        }

        ResetStrip();
        return null;
    }

    private DreamcastPvrTaRenderCommand? AcceptVertex(DreamcastPvrTaCommandWrite write, bool endOfStrip)
    {
        var color = (ushort)(write.Value & 0xFFFF);
        if (color == 0)
        {
            ResetStrip();
            return null;
        }

        if (currentVertices.Count > 0 && currentVertices[0].Rgb565 != color)
        {
            ResetStrip();
            return null;
        }

        currentVertices.Add(DreamcastPvrTaVertex.FromWrite(write));
        if (!endOfStrip)
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
        currentVertices.Clear();
    }
}

public sealed record DreamcastPvrTaVertex(
    int X,
    int Y,
    ushort Rgb565,
    string Rgb565Hex,
    uint Value,
    string ValueHex)
{
    public static DreamcastPvrTaVertex FromWrite(DreamcastPvrTaCommandWrite write)
    {
        var color = (ushort)(write.Value & 0xFFFF);
        return new DreamcastPvrTaVertex(
            (int)((write.Value >> 20) & 0xF),
            (int)((write.Value >> 16) & 0xF),
            color,
            $"0x{color:X4}",
            write.Value,
            write.ValueHex);
    }
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
