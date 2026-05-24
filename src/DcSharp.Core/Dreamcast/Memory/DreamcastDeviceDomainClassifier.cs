namespace DcSharp.Core.Dreamcast.Memory;

public static class DreamcastDeviceDomainClassifier
{
    public const string Aica = "aica";
    public const string Asic = "asic";
    public const string Holly = "holly";
    public const string Maple = "maple";
    public const string Pvr = "pvr";
    public const string Scif = "scif";
    public const string Sh4 = "sh4";
    public const string Tmu = "tmu";
    public const string Unmapped = "unmapped";
    public const string Other = "other";

    public static string Classify(MemoryAccess access)
    {
        if (access.Kind is MemoryAccessKind.UnmappedRead or MemoryAccessKind.UnmappedWrite)
        {
            return Unmapped;
        }

        var address = access.Address;
        var physical = DreamcastMemory.TranslateAddress(address);

        if (address is >= 0xFFE8_0000 and < 0xFFE8_0100)
        {
            return Scif;
        }

        if (address is >= 0xFFD8_0000 and < 0xFFD8_0030)
        {
            return Tmu;
        }

        if (address is >= 0xFFD0_0000 and < 0xFFD0_0100)
        {
            return Sh4;
        }

        if (address is 0xFF00_0024 or 0xFF00_0028)
        {
            return Sh4;
        }

        if (physical is >= 0x1000_0000 and < 0x1100_0000)
        {
            return Pvr;
        }

        if (physical is >= 0x0070_0000 and < 0x00A0_0000)
        {
            return Aica;
        }

        if (physical is >= 0x005F_8000 and < 0x005F_A000)
        {
            return Pvr;
        }

        if (physical is >= 0x005F_6C00 and < 0x005F_6D00)
        {
            return Maple;
        }

        if (physical is >= 0x005F_6900 and < 0x005F_6940)
        {
            return Asic;
        }

        if (physical is >= 0x005F_0000 and < 0x0060_0000)
        {
            return Holly;
        }

        return Other;
    }
}
