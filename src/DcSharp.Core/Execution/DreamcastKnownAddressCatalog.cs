namespace DcSharp.Core.Execution;

public sealed record DreamcastKnownAddress(
    string Name,
    uint Start,
    uint EndInclusive,
    string StartHex,
    string EndHex,
    string Category,
    string Description,
    uint Offset,
    string OffsetHex,
    string Display);

public static class DreamcastKnownAddressCatalog
{
    private static readonly Entry[] Entries =
    [
        Entry.Point(0x8C00_0000, "Firmware.DefaultCallback", "Firmware", "Default firmware callback/HLE stub entry."),
        Entry.Point(0x8C00_00E8, "Firmware.SystemVector", "Firmware", "BIOS system vector target used by several boot/error paths."),

        Entry.Range(0x8C00_86A8, 0x8C00_86DE, "IPBIN.GlyphDescriptorScan", "IP.BIN code", "Boot-splash glyph descriptor scan."),
        Entry.Range(0x8C00_89F6, 0x8C00_8A74, "IPBIN.GlyphBitDispatch", "IP.BIN code", "Boot-splash glyph bit dispatch and loop tail."),
        Entry.Range(0x8C00_8AD0, 0x8C00_8B08, "IPBIN.GlyphDrawHelper", "IP.BIN code", "Boot-splash glyph draw helper."),
        Entry.Range(0x8C00_8EE4, 0x8C00_8F4E, "IPBIN.PatternFill", "IP.BIN code", "Boot-splash framebuffer pattern fill."),

        Entry.Range(0xA05F_6800, 0xA05F_68FF, "ASIC.SystemBlock", "MMIO", "System ASIC register block."),
        Entry.Range(0xA05F_6900, 0xA05F_693F, "ASIC.EventRegisters", "MMIO", "ASIC event ACK/mask registers."),
        Entry.Range(0xA05F_7000, 0xA05F_70FF, "G2.BusControl", "MMIO", "G2 bus control/status register block."),
        Entry.Range(0xA05F_7800, 0xA05F_787F, "G2.DmaChannelRegisters", "MMIO", "G2 DMA channel register/status blocks."),
        Entry.Range(0xA05F_8000, 0xA05F_81FF, "PVR.Registers", "MMIO", "PVR/TA register block."),
        Entry.Range(0xA070_0000, 0xA071_FFFF, "AICA.Registers", "MMIO", "AICA register window."),
        Entry.Range(0xA080_0000, 0xA09F_FFFF, "AICA.SoundRamUncached", "Memory", "Uncached AICA sound RAM alias."),

        Entry.Point(0x8C18_33A4, "SA2.AicaWorkGlobal", "SA2 data", "Pointer to SA2 AICA work/control area."),
        Entry.Point(0x8C18_3544, "SA2.G2DmaTablePointer", "SA2 data", "Pointer to SA2 G2 DMA status table base."),
        Entry.Range(0x8C2A_21E0, 0x8C2A_22FF, "SA2.G2DmaStatusTable", "SA2 data", "Observed G2 DMA channel/status table."),

        Entry.Range(0x8C13_56D8, 0x8C13_5838, "SA2.G2PioReadWrapper", "SA2 code", "G2/AICA PIO read wrapper and status bookkeeping."),
        Entry.Range(0x8C13_5BC0, 0x8C13_5C16, "SA2.G2PioWriteUpload", "SA2 code", "AICA RAM upload/write helper."),
        Entry.Range(0x8C15_3A90, 0x8C15_3AE2, "SA2.AicaWorkPollWrapper", "SA2 code", "IRQ-side AICA work-poll wrapper."),
        Entry.Range(0x8C15_43A0, 0x8C15_43EE, "SA2.AicaWordReadWrapper", "SA2 code", "AICA word-read wrapper around G2 PIO reads."),
        Entry.Range(0x8C15_500C, 0x8C15_5022, "SA2.AicaActiveCallbackNoPending", "SA2 code", "Active AICA callback no-pending return path."),
        Entry.Range(0x8C15_AFBC, 0x8C15_AFD0, "SA2.AicaRegisterPairStatusProbe", "SA2 code", "AICA register-pair status wrapper tail."),
        Entry.Range(0x8C15_B200, 0x8C15_B234, "SA2.G2DmaStatusSetFunction", "SA2 code", "G2 DMA channel status-set caller function."),
        Entry.Range(0x8C15_B24C, 0x8C15_B27C, "SA2.G2DmaInactiveStatusWrapper", "SA2 code", "G2 DMA inactive-status probe wrapper."),
        Entry.Range(0x8C15_B604, 0x8C15_B690, "SA2.AicaNoWorkSlotScan", "SA2 code", "AICA no-work slot scan and cleanup path."),
        Entry.Range(0x8C15_B8EC, 0x8C15_B940, "SA2.AicaNameGroup", "SA2 code", "AICA name/group descriptor setup path."),
        Entry.Range(0x8C15_C4DE, 0x8C15_C856, "SA2.AicaChannelSetup", "SA2 code", "AICA channel setup, descriptor copy, and active/inactive channel paths."),
        Entry.Range(0x8C16_B478, 0x8C16_B5B6, "SA2.AicaActiveWorkCallback", "SA2 code", "Active AICA work callback/copy/status path."),
        Entry.Range(0x8C16_BF10, 0x8C16_BF48, "SA2.AicaByteReadAdapter", "SA2 code", "AICA byte-read adapter used by active-work status polling."),
        Entry.Range(0x8C16_C1A4, 0x8C16_C35E, "SA2.AicaActiveWorkEmptyFieldScan", "SA2 code", "AICA active-work empty-field scan."),
        Entry.Range(0x8C16_C3B8, 0x8C16_C67E, "SA2.AicaWorkEntryStatusScan", "SA2 code", "AICA work-entry status scan."),
        Entry.Range(0x8C16_C6F8, 0x8C16_C838, "SA2.AicaEmptyWorkTableScan", "SA2 code", "AICA empty work-table scan."),
        Entry.Range(0x8C17_09E0, 0x8C17_09FA, "SA2.G2DmaStatusClearHelper", "SA2 code", "G2 DMA channel status-clear helper."),
        Entry.Range(0x8C17_0A98, 0x8C17_0AE0, "SA2.G2DmaStatusSetHelper", "SA2 code", "G2 DMA channel status-set helper."),
        Entry.Range(0x8C17_0BBC, 0x8C17_0BF4, "SA2.G2DmaInactiveStatusProbe", "SA2 code", "G2 DMA inactive-status probe helper.")
    ];

    public static DreamcastKnownAddress? Find(uint address)
    {
        var best = default(Entry);
        var bestLength = uint.MaxValue;
        foreach (var entry in Entries)
        {
            if (address < entry.Start || address > entry.EndInclusive)
            {
                continue;
            }

            var length = entry.EndInclusive - entry.Start;
            if (length < bestLength)
            {
                best = entry;
                bestLength = length;
            }
        }

        return best.Name is null ? null : best.ToKnownAddress(address);
    }

    public static string Format(uint address)
    {
        var known = Find(address);
        return known is null ? string.Empty : $" ; {known.Display} [{known.Category}]";
    }

    private readonly record struct Entry(uint Start, uint EndInclusive, string Name, string Category, string Description)
    {
        public static Entry Point(uint address, string name, string category, string description) =>
            new(address, address, name, category, description);

        public static Entry Range(uint start, uint endInclusive, string name, string category, string description) =>
            new(start, endInclusive, name, category, description);

        public DreamcastKnownAddress ToKnownAddress(uint address)
        {
            var offset = address >= Start ? address - Start : 0;
            return new DreamcastKnownAddress(
                Name,
                Start,
                EndInclusive,
                $"0x{Start:X8}",
                $"0x{EndInclusive:X8}",
                Category,
                Description,
                offset,
                $"0x{offset:X}",
                offset == 0 ? Name : $"{Name}+0x{offset:X}");
        }
    }
}
