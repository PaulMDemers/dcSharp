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

    public bool AcceptPayload(DreamcastPvrTaCommandWrite write, out DreamcastPvrTaSprite? sprite)
    {
        sprite = null;
        if (pending is null)
        {
            return false;
        }

        pending = pending.AcceptPayload(write);
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
        uint? Dummy3 = null,
        uint? FirstPayloadInstructionPc = null,
        string? FirstPayloadInstructionPcHex = null,
        uint? LastPayloadInstructionPc = null,
        string? LastPayloadInstructionPcHex = null)
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

        public PendingSpritePacket AcceptPayload(DreamcastPvrTaCommandWrite write)
        {
            var value = write.Value;
            var firstPayloadPc = FirstPayloadInstructionPc ?? write.InstructionPc;
            var firstPayloadPcHex = FirstPayloadInstructionPcHex ?? write.InstructionPcHex;
            if (AxValue is null)
            {
                return this with
                {
                    AxValue = value,
                    FirstPayloadInstructionPc = firstPayloadPc,
                    FirstPayloadInstructionPcHex = firstPayloadPcHex,
                    LastPayloadInstructionPc = write.InstructionPc,
                    LastPayloadInstructionPcHex = write.InstructionPcHex
                };
            }

            if (AyValue is null)
            {
                return WithPayload(Y: value);
            }

            if (AzValue is null)
            {
                return WithPayload(Z: value);
            }

            if (BxValue is null)
            {
                return WithPayload(Bx: value);
            }

            if (ByValue is null)
            {
                return WithPayload(By: value);
            }

            if (BzValue is null)
            {
                return WithPayload(Bz: value);
            }

            if (CxValue is null)
            {
                return WithPayload(Cx: value);
            }

            if (CyValue is null)
            {
                return WithPayload(Cy: value);
            }

            if (CzValue is null)
            {
                return WithPayload(Cz: value);
            }

            if (DxValue is null)
            {
                return WithPayload(Dx: value);
            }

            if (DyValue is null)
            {
                return WithPayload(Dy: value);
            }

            if (Dummy0 is null)
            {
                return WithPayload(Dummy0Value: value);
            }

            if (Dummy1 is null)
            {
                return WithPayload(Dummy1Value: value);
            }

            if (Dummy2 is null)
            {
                return WithPayload(Dummy2Value: value);
            }

            return WithPayload(Dummy3Value: value);

            PendingSpritePacket WithPayload(
                uint? Y = null,
                uint? Z = null,
                uint? Bx = null,
                uint? By = null,
                uint? Bz = null,
                uint? Cx = null,
                uint? Cy = null,
                uint? Cz = null,
                uint? Dx = null,
                uint? Dy = null,
                uint? Dummy0Value = null,
                uint? Dummy1Value = null,
                uint? Dummy2Value = null,
                uint? Dummy3Value = null) =>
                this with
                {
                    AyValue = Y ?? AyValue,
                    AzValue = Z ?? AzValue,
                    BxValue = Bx ?? BxValue,
                    ByValue = By ?? ByValue,
                    BzValue = Bz ?? BzValue,
                    CxValue = Cx ?? CxValue,
                    CyValue = Cy ?? CyValue,
                    CzValue = Cz ?? CzValue,
                    DxValue = Dx ?? DxValue,
                    DyValue = Dy ?? DyValue,
                    Dummy0 = Dummy0Value ?? Dummy0,
                    Dummy1 = Dummy1Value ?? Dummy1,
                    Dummy2 = Dummy2Value ?? Dummy2,
                    Dummy3 = Dummy3Value ?? Dummy3,
                    LastPayloadInstructionPc = write.InstructionPc,
                    LastPayloadInstructionPcHex = write.InstructionPcHex
                };
        }

        public DreamcastPvrTaSprite ToSprite()
        {
            var rgb565 = Argb8888ToRgb565(HeaderPayload.Argb);
            var hasTexturePayload = HasTexturePayload(HeaderPayload.HeaderValue);
            (float U, float V, uint Value) aUv = hasTexturePayload ? DecodePackedUv(Dummy1 ?? 0) : (0.0f, 0.0f, 0u);
            (float U, float V, uint Value) bUv = hasTexturePayload ? DecodePackedUv(Dummy2 ?? 0) : (0.0f, 0.0f, 0u);
            (float U, float V, uint Value) cUv = hasTexturePayload ? DecodePackedUv(Dummy3 ?? 0) : (0.0f, 0.0f, 0u);
            (float U, float V, uint Value) dUv = hasTexturePayload
                ? (aUv.U + cUv.U - bUv.U, aUv.V + cUv.V - bUv.V, 0u)
                : (0.0f, 0.0f, 0u);
            return new DreamcastPvrTaSprite(
                Header.Region,
                Header.ListType,
                Header.ListTypeName,
                Header.Value,
                Header.ValueHex,
                Header.InstructionPc,
                Header.InstructionPcHex,
                HeaderPayload,
                Control.Value,
                Control.ValueHex,
                Control.InstructionPc,
                Control.InstructionPcHex,
                FirstPayloadInstructionPc,
                FirstPayloadInstructionPcHex,
                LastPayloadInstructionPc,
                LastPayloadInstructionPcHex,
                EndOfStrip,
                rgb565,
                $"0x{rgb565:X4}",
                PayloadWords(),
                [
                    CreateVertex(AxValue ?? 0, AyValue ?? 0, AzValue ?? 0, "A", aUv.U, aUv.V, aUv.Value),
                    CreateVertex(BxValue ?? 0, ByValue ?? 0, BzValue ?? 0, "B", bUv.U, bUv.V, bUv.Value),
                    CreateVertex(CxValue ?? 0, CyValue ?? 0, CzValue ?? 0, "C", cUv.U, cUv.V, cUv.Value),
                    CreateVertex(DxValue ?? 0, DyValue ?? 0, InterpolateDz(AzValue ?? 0, BzValue ?? 0, CzValue ?? 0), "D", dUv.U, dUv.V, dUv.Value)
                ]);
        }

        private IReadOnlyList<DreamcastPvrTaSpritePayloadWord> PayloadWords() =>
        [
            PayloadWord("Ax", AxValue),
            PayloadWord("Ay", AyValue),
            PayloadWord("Az", AzValue),
            PayloadWord("Bx", BxValue),
            PayloadWord("By", ByValue),
            PayloadWord("Bz", BzValue),
            PayloadWord("Cx", CxValue),
            PayloadWord("Cy", CyValue),
            PayloadWord("Cz", CzValue),
            PayloadWord("Dx", DxValue),
            PayloadWord("Dy", DyValue),
            PayloadWord("Dummy0", Dummy0),
            PayloadWord("Dummy1", Dummy1),
            PayloadWord("Dummy2", Dummy2),
            PayloadWord("Dummy3", Dummy3)
        ];

        private static DreamcastPvrTaSpritePayloadWord PayloadWord(string name, uint? value)
        {
            var raw = value ?? 0;
            return new(name, raw, $"0x{raw:X8}");
        }

        private static DreamcastPvrTaSpriteVertex CreateVertex(uint xValue, uint yValue, uint zValue, string name, float u, float v, uint uvValue) =>
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
                $"0x{yValue:X8}",
                u,
                v,
                uvValue,
                $"0x{uvValue:X8}");

        private static (float U, float V, uint Value) DecodePackedUv(uint value)
        {
            var u = BitConverter.UInt32BitsToSingle(value & 0xFFFF_0000u);
            var v = BitConverter.UInt32BitsToSingle((value & 0x0000_FFFFu) << 16);
            return (u, v, value);
        }

        private static bool HasTexturePayload(uint headerValue) =>
            (headerValue & 0x0000_0008u) != 0;

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
