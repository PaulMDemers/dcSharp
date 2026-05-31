namespace DcSharp.Core.Cpu;

public sealed record Sh4FpscrSummary(
    uint Value,
    string ValueHex,
    string RoundingMode,
    string Flags,
    string Enables,
    string Causes,
    string Controls,
    bool DenormalAsZero,
    bool DoublePrecision,
    bool DoubleTransferSize,
    bool RegisterBank,
    string Display)
{
    public static Sh4FpscrSummary FromValue(uint value)
    {
        var flags = FormatExceptionBits(value & Sh4State.FpscrFlagMask);
        var enables = FormatExceptionBits((value & Sh4State.FpscrEnableMask) >> 5);
        var causes = FormatExceptionBits((value & Sh4State.FpscrCauseMask) >> 10);
        var controls = FormatControls(value);
        var roundingMode = (value & Sh4State.FpscrRoundToZeroBit) != 0 ? "zero" : "nearest";

        return new Sh4FpscrSummary(
            value,
            $"0x{value:X8}",
            roundingMode,
            flags,
            enables,
            causes,
            controls,
            (value & Sh4State.FpscrDnBit) != 0,
            (value & Sh4State.FpscrPrBit) != 0,
            (value & Sh4State.FpscrSzBit) != 0,
            (value & Sh4State.FpscrFrBit) != 0,
            $"rm={roundingMode}, flags={flags}, enables={enables}, causes={causes}, controls={controls}");
    }

    private static string FormatExceptionBits(uint value)
    {
        var parts = new string[5];
        var count = 0;
        if ((value & Sh4State.FpscrFlagInvalidBit) != 0)
        {
            parts[count++] = "V";
        }

        if ((value & Sh4State.FpscrFlagDivisionByZeroBit) != 0)
        {
            parts[count++] = "Z";
        }

        if ((value & Sh4State.FpscrFlagOverflowBit) != 0)
        {
            parts[count++] = "O";
        }

        if ((value & Sh4State.FpscrFlagUnderflowBit) != 0)
        {
            parts[count++] = "U";
        }

        if ((value & Sh4State.FpscrFlagInexactBit) != 0)
        {
            parts[count++] = "I";
        }

        return count == 0 ? "none" : string.Join('/', parts.AsSpan(0, count).ToArray());
    }

    private static string FormatControls(uint value)
    {
        var parts = new string[4];
        var count = 0;
        if ((value & Sh4State.FpscrDnBit) != 0)
        {
            parts[count++] = "DN";
        }

        if ((value & Sh4State.FpscrPrBit) != 0)
        {
            parts[count++] = "PR";
        }

        if ((value & Sh4State.FpscrSzBit) != 0)
        {
            parts[count++] = "SZ";
        }

        if ((value & Sh4State.FpscrFrBit) != 0)
        {
            parts[count++] = "FR";
        }

        return count == 0 ? "none" : string.Join('/', parts.AsSpan(0, count).ToArray());
    }
}
