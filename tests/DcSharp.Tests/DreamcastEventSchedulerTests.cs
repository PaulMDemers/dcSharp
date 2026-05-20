using DcSharp.Core.Dreamcast.Memory;
using DcSharp.Core.Dreamcast.Input;
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
        Assert.Equal(1UL, scheduler.CreateSnapshot().VBlankEventsRaised);
        Assert.Equal(6UL, scheduler.CreateSnapshot().NextVBlankInstruction);
    }

    [Fact]
    public void CanDisableSyntheticVBlank()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0xA05F_6910, 1u << 3);
        var scheduler = new DreamcastEventScheduler(memory, new DreamcastRunOptions(VBlankInterval: 0));

        scheduler.AdvanceBeforeInstruction(200_000);

        Assert.False(memory.TryGetPendingExternalInterrupt(out _, out _));
        Assert.Equal(0UL, scheduler.CreateSnapshot().VBlankEventsRaised);
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
        Assert.Equal(3UL, scheduler.CreateSnapshot().HardwareAdvanceTicks);
        Assert.Equal(3UL, scheduler.CreateSnapshot().HardwareAdvanceBatches);
        Assert.Equal(1UL, scheduler.CreateSnapshot().MaxHardwareAdvanceBatch);
    }

    [Fact]
    public void CoalescesHardwareTimerAdvancementWhenInstructionCountSkipsAhead()
    {
        var memory = new DreamcastMemory();
        var scheduler = new DreamcastEventScheduler(memory, new DreamcastRunOptions(VBlankInterval: 0));

        memory.WriteUInt32(0xFFD8_0014, 5);
        memory.WriteUInt32(0xFFD8_0018, 5);
        memory.WriteUInt16(0xFFD8_001C, 0);
        memory.Write(0xFFD8_0004, [0x02]);

        scheduler.AdvanceBeforeInstruction(0);
        scheduler.AdvanceBeforeInstruction(5);

        var snapshot = scheduler.CreateSnapshot();
        Assert.Equal(0x0100, memory.ReadUInt16(0xFFD8_001C));
        Assert.Equal(6UL, snapshot.HardwareAdvanceTicks);
        Assert.Equal(2UL, snapshot.HardwareAdvanceBatches);
        Assert.Equal(5UL, snapshot.MaxHardwareAdvanceBatch);
    }

    [Fact]
    public void AppliesControllerScriptAtInstructionBoundary()
    {
        var memory = new DreamcastMemory();
        var scheduler = new DreamcastEventScheduler(memory, new DreamcastRunOptions(
            VBlankInterval: 0,
            ControllerAScript: new DreamcastControllerScript(
                new DreamcastControllerScriptFrame(0, DreamcastControllerState.Neutral),
                new DreamcastControllerScriptFrame(5, new DreamcastControllerState(Buttons: DreamcastControllerButtons.Start)))));

        scheduler.AdvanceBeforeInstruction(4);
        Assert.Equal(DreamcastControllerButtons.None, memory.ControllerA.Buttons);

        scheduler.AdvanceBeforeInstruction(5);
        Assert.Equal(DreamcastControllerButtons.Start, memory.ControllerA.Buttons);
        Assert.Equal(1UL, scheduler.CreateSnapshot().ControllerScriptChanges);
    }

    [Fact]
    public void AppliesMappedControllerScriptAtInstructionBoundary()
    {
        var memory = new DreamcastMemory();
        var scheduler = new DreamcastEventScheduler(memory, new DreamcastRunOptions(
            VBlankInterval: 0,
            ControllerScripts: new Dictionary<byte, DreamcastControllerScript>
            {
                [0x40] = new(
                    new DreamcastControllerScriptFrame(0, DreamcastControllerState.Neutral),
                    new DreamcastControllerScriptFrame(5, new DreamcastControllerState(Buttons: DreamcastControllerButtons.B)))
            }));

        scheduler.AdvanceBeforeInstruction(4);
        Assert.Equal(DreamcastControllerButtons.None, memory.GetController(0x40)?.Buttons);

        scheduler.AdvanceBeforeInstruction(5);
        Assert.Equal(DreamcastControllerButtons.B, memory.GetController(0x40)?.Buttons);
        Assert.Equal(1UL, scheduler.CreateSnapshot().ControllerScriptChanges);
    }
}
