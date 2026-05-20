using DcSharp.Core.Dreamcast.Memory;

namespace DcSharp.Tests;

public class DreamcastMemoryTests
{
    [Theory]
    [InlineData(0x8C01_0000u, 0x0C01_0000u)]
    [InlineData(0xAC01_0000u, 0x0C01_0000u)]
    [InlineData(0x0C01_0000u, 0x0C01_0000u)]
    public void TranslatesDreamcastRamMirrors(uint address, uint expectedPhysical)
    {
        Assert.Equal(expectedPhysical, DreamcastMemory.TranslateAddress(address));
    }

    [Fact]
    public void WritesThroughP1MirrorIntoSystemRam()
    {
        var memory = new DreamcastMemory();

        memory.Write(0x8C01_0000, [0xC0, 0xFF, 0xEE]);

        Assert.Equal(0xC0, memory.ReadByte(0x0C01_0000));
        Assert.Equal(0xFF, memory.ReadByte(0xAC01_0001));
        Assert.Equal(0xEE, memory.ReadByte(0x8C01_0002));
    }

    [Fact]
    public void MirrorsSecondSixteenMegabyteRamAperture()
    {
        var memory = new DreamcastMemory();

        memory.Write(0xACFF_FFFF, [0xBA]);
        memory.Write(0xADFF_FFFF, [0xAB]);

        Assert.Equal(0xAB, memory.ReadByte(0xACFF_FFFF));
        Assert.Equal(0xAB, memory.ReadByte(0xADFF_FFFF));
    }

    [Fact]
    public void TimerCounterUnderflowSetsControlFlag()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xFFD8_0014, 2);
        memory.WriteUInt32(0xFFD8_0018, 2);
        memory.WriteUInt16(0xFFD8_001C, 0);
        memory.Write(0xFFD8_0004, [0x02]);

        memory.AdvanceHardware(3);

        Assert.Equal(0x0100, memory.ReadUInt16(0xFFD8_001C));
    }

    [Fact]
    public void TimerUnderflowReportsPendingInternalInterruptWhenEnabled()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt16(0xFFD0_0004, 15 << 12);
        memory.WriteUInt32(0xFFD8_0008, 1);
        memory.WriteUInt32(0xFFD8_000C, 1);
        memory.WriteUInt16(0xFFD8_0010, 0x0020);
        memory.Write(0xFFD8_0004, [0x01]);

        memory.AdvanceHardware(2);

        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0400u, eventCode);
        Assert.Equal(15, level);
    }

    [Fact]
    public void RaisedVBlankReportsPendingIrq9WhenEnabled()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xA05F_6910, 1u << 3);
        memory.RaiseVBlankBegin();

        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0320u, eventCode);
        Assert.Equal(9, level);
    }

    [Fact]
    public void MapleDmaDevInfoWritesControllerResponseAndRaisesEvent()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0x8C02_0000, 0x8000_0000);
        memory.WriteUInt32(0x8C02_0004, 0x0C03_0000);
        memory.WriteUInt32(0x8C02_0008, 0x0000_2001);
        memory.WriteUInt32(0xA05F_6910, 1u << 12);
        memory.WriteUInt32(0xA05F_6C04, 0x0C02_0000);

        memory.WriteUInt32(0xA05F_6C18, 1);

        Assert.Equal(5, memory.ReadByte(0x8C03_0000));
        Assert.Equal(28, memory.ReadByte(0x8C03_0003));
        Assert.Equal(0x0100_0000u, memory.ReadUInt32(0x8C03_0004));
        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0320u, eventCode);
        Assert.Equal(9, level);
    }

    [Fact]
    public void MapleDmaGetConditionWritesNeutralControllerState()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0x8C02_0000, 0x8000_0001);
        memory.WriteUInt32(0x8C02_0004, 0x0C03_0000);
        memory.WriteUInt32(0x8C02_0008, 0x0100_2009);
        memory.WriteUInt32(0x8C02_000C, 0x0100_0000);
        memory.WriteUInt32(0xA05F_6C04, 0x0C02_0000);

        memory.WriteUInt32(0xA05F_6C18, 1);

        Assert.Equal(8, memory.ReadByte(0x8C03_0000));
        Assert.Equal(3, memory.ReadByte(0x8C03_0003));
        Assert.Equal(0x0100_0000u, memory.ReadUInt32(0x8C03_0004));
        Assert.Equal(0x0000_FFFFu, memory.ReadUInt32(0x8C03_0008));
        Assert.Equal(0x8080_8080u, memory.ReadUInt32(0x8C03_000C));
    }

    [Fact]
    public void MapleDmaUnknownDeviceWritesNoResponse()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0x8C02_0000, 0x8000_0000);
        memory.WriteUInt32(0x8C02_0004, 0x0C03_0000);
        memory.WriteUInt32(0x8C02_0008, 0x0000_6001);
        memory.WriteUInt32(0xA05F_6C04, 0x0C02_0000);

        memory.WriteUInt32(0xA05F_6C18, 1);

        Assert.Equal(0xFF, memory.ReadByte(0x8C03_0000));
    }
}
