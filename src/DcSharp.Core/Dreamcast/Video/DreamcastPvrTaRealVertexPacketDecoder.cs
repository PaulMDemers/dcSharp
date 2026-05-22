namespace DcSharp.Core.Dreamcast.Video;

public sealed class DreamcastPvrTaRealVertexPacketDecoder
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
        uint? ZValue = null,
        uint? UValue = null,
        uint? VValue = null,
        uint? ArgbValue = null,
        uint? OargbValue = null)
    {
        public bool IsComplete =>
            XValue is not null
            && YValue is not null
            && ZValue is not null
            && UValue is not null
            && VValue is not null
            && ArgbValue is not null
            && OargbValue is not null;

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

            if (ZValue is null)
            {
                return this with { ZValue = value };
            }

            if (UValue is null)
            {
                return this with { UValue = value };
            }

            if (VValue is null)
            {
                return this with { VValue = value };
            }

            if (ArgbValue is null)
            {
                return this with { ArgbValue = value };
            }

            return this with { OargbValue = value };
        }

        public DreamcastPvrTaVertex ToVertex()
        {
            var xValue = XValue ?? 0;
            var yValue = YValue ?? 0;
            var argbValue = ArgbValue ?? 0;
            var color = Argb8888ToRgb565(argbValue);
            return new DreamcastPvrTaVertex(
                DecodeFloatCoordinate(xValue),
                DecodeFloatCoordinate(yValue),
                EndOfStrip,
                color,
                $"0x{color:X4}",
                Control.Value,
                Control.ValueHex,
                xValue,
                $"0x{xValue:X8}",
                yValue,
                $"0x{yValue:X8}",
                argbValue,
                $"0x{argbValue:X8}");
        }

        private static int DecodeFloatCoordinate(uint value) =>
            (int)MathF.Round(BitConverter.UInt32BitsToSingle(value));

        private static ushort Argb8888ToRgb565(uint value)
        {
            var red = (value >> 16) & 0xFF;
            var green = (value >> 8) & 0xFF;
            var blue = value & 0xFF;
            return (ushort)(((red >> 3) << 11) | ((green >> 2) << 5) | (blue >> 3));
        }
    }
}
