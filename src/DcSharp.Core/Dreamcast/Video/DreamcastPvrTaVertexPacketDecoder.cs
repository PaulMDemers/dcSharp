namespace DcSharp.Core.Dreamcast.Video;

public sealed class DreamcastPvrTaVertexPacketDecoder
{
    private PendingVertexPacket? pending;

    public bool HasPending => pending is not null;

    public void Begin(DreamcastPvrTaCommandWrite control, bool endOfStrip) =>
        pending = new PendingVertexPacket(control, endOfStrip);

    public bool AcceptPayload(uint value, out DreamcastPvrTaVertex? vertex)
    {
        vertex = null;
        if (pending is null)
        {
            return false;
        }

        pending = pending.AcceptPayload(value);
        if (!pending.IsComplete)
        {
            return false;
        }

        vertex = pending.ToVertex();
        pending = null;
        return true;
    }

    public void Reset() =>
        pending = null;

    private sealed record PendingVertexPacket(
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
}
