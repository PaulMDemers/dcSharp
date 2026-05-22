namespace DcSharp.Core.Dreamcast.Video;

public sealed class DreamcastPvrTaState
{
    private bool inRenderableOpaqueList;
    private ushort? pendingColor;
    private int verticesInCurrentStrip;

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

        if (pendingColor is { } existing && existing != color)
        {
            ResetStrip();
            return null;
        }

        pendingColor = color;
        verticesInCurrentStrip++;
        if (!endOfStrip)
        {
            return null;
        }

        var canRender = verticesInCurrentStrip >= 3;
        var result = canRender ? new DreamcastPvrTaRenderCommand(color) : null;
        ResetStrip();
        return result;
    }

    private static bool IsOpaqueInput(DreamcastPvrTaCommandWrite write) =>
        string.Equals(write.Region, "TA_INPUT", StringComparison.Ordinal)
        && string.Equals(write.ListTypeName, "OpaquePolygon", StringComparison.Ordinal);

    private void ResetStrip()
    {
        pendingColor = null;
        verticesInCurrentStrip = 0;
    }
}

public sealed record DreamcastPvrTaRenderCommand(ushort Rgb565);
