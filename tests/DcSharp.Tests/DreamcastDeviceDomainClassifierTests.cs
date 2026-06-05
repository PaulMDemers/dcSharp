using DcSharp.Core.Dreamcast.Memory;

namespace DcSharp.Tests;

public class DreamcastDeviceDomainClassifierTests
{
    [Theory]
    [InlineData(0xA05F_8044u, MemoryAccessKind.Write, "pvr")]
    [InlineData(0x1000_0000u, MemoryAccessKind.Write, "pvr")]
    [InlineData(0x1100_0000u, MemoryAccessKind.Write, "pvr")]
    [InlineData(0x1300_0000u, MemoryAccessKind.Write, "pvr")]
    [InlineData(0xA070_0000u, MemoryAccessKind.Write, "aica")]
    [InlineData(0xA080_0000u, MemoryAccessKind.Write, "aica")]
    [InlineData(0xA05F_6C18u, MemoryAccessKind.Write, "maple")]
    [InlineData(0xA05F_6910u, MemoryAccessKind.Write, "asic")]
    [InlineData(0xFFE8_000Cu, MemoryAccessKind.Write, "scif")]
    [InlineData(0xFFD8_0010u, MemoryAccessKind.Read, "tmu")]
    [InlineData(0xFFD0_0004u, MemoryAccessKind.Read, "sh4")]
    [InlineData(0xFF00_0028u, MemoryAccessKind.Read, "sh4")]
    [InlineData(0xFF00_0038u, MemoryAccessKind.Write, "sh4")]
    [InlineData(0xE000_0000u, MemoryAccessKind.Write, "sh4")]
    [InlineData(0x0000_000Cu, MemoryAccessKind.UnmappedWrite, "unmapped")]
    public void ClassifiesDeviceAccessDomains(uint address, MemoryAccessKind kind, string expectedDomain)
    {
        var access = new MemoryAccess(kind, address, 4, 0);

        Assert.Equal(expectedDomain, DreamcastDeviceDomainClassifier.Classify(access));
    }
}
