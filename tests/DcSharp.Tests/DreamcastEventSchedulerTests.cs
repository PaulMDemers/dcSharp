using DcSharp.Core.Dreamcast.Memory;
using DcSharp.Core.Execution;

namespace DcSharp.Tests;

public class DreamcastEventSchedulerTests
{
    [Fact]
    public void RaisesVBlankAtConfiguredInstructionBoundary()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0xA05F_6910, 1u << 3);
        var scheduler = new DreamcastEventScheduler(memory, new DreamcastRunOptions(VBlankInterval: 3));

        scheduler.AdvanceBeforeInstruction(0);
        scheduler.AdvanceBeforeInstruction(1);
        scheduler.AdvanceBeforeInstruction(2);

        Assert.False(memory.TryGetPendingExternalInterrupt(out _, out _));

        scheduler.AdvanceBeforeInstruction(3);

        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0320u, eventCode);
        Assert.Equal(9, level);
    }

    [Fact]
    public void CanDisableSyntheticVBlank()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0xA05F_6910, 1u << 3);
        var scheduler = new DreamcastEventScheduler(memory, new DreamcastRunOptions(VBlankInterval: 0));

        scheduler.AdvanceBeforeInstruction(200_000);

        Assert.False(memory.TryGetPendingExternalInterrupt(out _, out _));
    }

    [Fact]
    public void AdvancesHardwareTimers()
    {
        var memory = new DreamcastMemory();
        var scheduler = new DreamcastEventScheduler(memory, new DreamcastRunOptions(VBlankInterval: 0));

        memory.WriteUInt32(0xFFD8_0014, 2);
        memory.WriteUInt32(0xFFD8_0018, 2);
        memory.WriteUInt16(0xFFD8_001C, 0);
        memory.Write(0xFFD8_0004, [0x02]);

        scheduler.AdvanceBeforeInstruction(0);
        scheduler.AdvanceBeforeInstruction(1);
        scheduler.AdvanceBeforeInstruction(2);

        Assert.Equal(0x0100, memory.ReadUInt16(0xFFD8_001C));
    }
}
