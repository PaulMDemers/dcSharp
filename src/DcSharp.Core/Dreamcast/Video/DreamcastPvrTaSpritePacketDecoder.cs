namespace DcSharp.Core.Dreamcast.Video;

public sealed class DreamcastPvrTaSpritePacketDecoder
{
    private PendingSpritePacket? pending;

    public bool HasPending => pending is not null;

    public void Begin(
        DreamcastPvrTaCommandWrite header,
        DreamcastPvrTaSpriteHeaderPayload headerPayload,
        DreamcastPvrTaCommandWrite control,
        bool endOfStrip) =>
        pending = new PendingSpritePacket(header, headerPayload, control, endOfStrip);

    public bool AcceptPayload(uint value, out DreamcastPvrTaSprite? sprite)
    {
        sprite = null;
        if (pending is null)
        {
            return false;
        }

        pending = pending.AcceptPayload(value);
        if (!pending.IsComplete)
        {
            return false;
        }

        sprite = pending.ToSprite();
        pending = null;
        return true;
    }

    public void Reset() =>
        pending = null;

    private sealed record PendingSpritePacket(
        DreamcastPvrTaCommandWrite Header,
        DreamcastPvrTaSpriteHeaderPayload HeaderPayload,
        DreamcastPvrTaCommandWrite Control,
        bool EndOfStrip,
        uint? AxValue = null,
        uint? AyValue = null,
        uint? AzValue = null,
        uint? BxValue = null,
        uint? ByValue = null,
        uint? BzValue = null,
        uint? CxValue = null,
        uint? CyValue = null,
        uint? CzValue = null,
        uint? DxValue = null,
        uint? DyValue = null,
        uint? Dummy0 = null,
        uint? Dummy1 = null,
        uint? Dummy2 = null,
        uint? Dummy3 = null)
    {
        public bool IsComplete =>
            AxValue is not null
            && AyValue is not null
            && AzValue is not null
            && BxValue is not null
            && ByValue is not null
            && BzValue is not null
            && CxValue is not null
            && CyValue is not null
            && CzValue is not null
            && DxValue is not null
            && DyValue is not null
            && Dummy0 is not null
            && Dummy1 is not null
            && Dummy2 is not null
            && Dummy3 is not null;

        public PendingSpritePacket AcceptPayload(uint value)
        {
            if (AxValue is null)
            {
                return this with { AxValue = value };
            }

            if (AyValue is null)
            {
                return this with { AyValue = value };
            }

            if (AzValue is null)
            {
                return this with { AzValue = value };
            }

            if (BxValue is null)
            {
                return this with { BxValue = value };
            }

            if (ByValue is null)
            {
                return this with { ByValue = value };
            }

            if (BzValue is null)
            {
                return this with { BzValue = value };
            }

            if (CxValue is null)
            {
                return this with { CxValue = value };
            }

            if (CyValue is null)
            {
                return this with { CyValue = value };
            }

            if (CzValue is null)
            {
                return this with { CzValue = value };
            }

            if (DxValue is null)
            {
                return this with { DxValue = value };
            }

            if (DyValue is null)
            {
                return this with { DyValue = value };
            }

            if (Dummy0 is null)
            {
                return this with { Dummy0 = value };
            }

            if (Dummy1 is null)
            {
                return this with { Dummy1 = value };
            }

            if (Dummy2 is null)
            {
                return this with { Dummy2 = value };
            }

            return this with { Dummy3 = value };
        }

        public DreamcastPvrTaSprite ToSprite()
        {
            var rgb565 = Argb8888ToRgb565(HeaderPayload.Argb);
            return new DreamcastPvrTaSprite(
                Header.Region,
                Header.ListType,
                Header.ListTypeName,
                Header.Value,
                Header.ValueHex,
                HeaderPayload,
                Control.Value,
                Control.ValueHex,
                EndOfStrip,
                rgb565,
                $"0x{rgb565:X4}",
                [
                    CreateVertex(AxValue ?? 0, AyValue ?? 0, AzValue ?? 0, "A"),
                    CreateVertex(BxValue ?? 0, ByValue ?? 0, BzValue ?? 0, "B"),
                    CreateVertex(CxValue ?? 0, CyValue ?? 0, CzValue ?? 0, "C"),
                    CreateVertex(DxValue ?? 0, DyValue ?? 0, InterpolateDz(AzValue ?? 0, BzValue ?? 0, CzValue ?? 0), "D")
                ]);
        }

        private static DreamcastPvrTaSpriteVertex CreateVertex(uint xValue, uint yValue, uint zValue, string name) =>
            new(
                name,
                DecodeFloatCoordinate(xValue),
                DecodeFloatCoordinate(yValue),
                BitConverter.UInt32BitsToSingle(zValue),
                zValue,
                $"0x{zValue:X8}",
                xValue,
                $"0x{xValue:X8}",
                yValue,
                $"0x{yValue:X8}");

        private static uint InterpolateDz(uint azValue, uint bzValue, uint czValue)
        {
            var az = BitConverter.UInt32BitsToSingle(azValue);
            var bz = BitConverter.UInt32BitsToSingle(bzValue);
            var cz = BitConverter.UInt32BitsToSingle(czValue);
            return BitConverter.SingleToUInt32Bits(((az + bz) + cz) / 3.0f);
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
