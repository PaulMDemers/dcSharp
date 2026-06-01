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
        memory.WriteUInt32(0xA05F_6930, 1u << 3);
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
        memory.WriteUInt32(0xA05F_6930, 1u << 3);
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

    [Fact]
    public void AdvancesSleepingHardwareToNextTimerInterrupt()
    {
        var memory = new DreamcastMemory();
        var scheduler = new DreamcastEventScheduler(memory, new DreamcastRunOptions(VBlankInterval: 0));

        memory.WriteUInt32(0xFFD8_0008, 5);
        memory.WriteUInt32(0xFFD8_000C, 5);
        memory.WriteUInt16(0xFFD8_0010, 0x20);
        memory.WriteUInt16(0xFFD0_0004, 0xF000);
        memory.Write(0xFFD8_0004, [0x01]);

        scheduler.AdvanceBeforeInstruction(0);

        Assert.True(scheduler.AdvanceAfterSleep());

        var snapshot = scheduler.CreateSnapshot();
        Assert.Equal(6UL, snapshot.HardwareAdvanceTicks);
        Assert.Equal(2UL, snapshot.HardwareAdvanceBatches);
        Assert.Equal(5UL, snapshot.MaxHardwareAdvanceBatch);
        Assert.Equal(5UL, snapshot.IdleAdvanceTicks);
        Assert.Equal(1UL, snapshot.IdleAdvanceBatches);
        Assert.Equal(5UL, snapshot.MaxIdleAdvanceBatch);
        Assert.Equal(1UL, snapshot.IdleTimerWakeCount);
        Assert.Equal(0UL, snapshot.IdleVBlankWakeCount);
        Assert.Equal(0UL, snapshot.IdleInputWakeCount);
        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out _));
        Assert.Equal(0x0400u, eventCode);
    }

    [Fact]
    public void AdvancesSleepingHardwareToEnabledVBlankInterrupt()
    {
        var memory = new DreamcastMemory();
        var scheduler = new DreamcastEventScheduler(memory, new DreamcastRunOptions(VBlankInterval: 5));
        memory.WriteUInt32(0xA05F_6930, 1u << 3);

        scheduler.AdvanceBeforeInstruction(0);

        Assert.True(scheduler.AdvanceAfterSleep());

        var snapshot = scheduler.CreateSnapshot();
        Assert.Equal(5UL, snapshot.HardwareAdvanceTicks);
        Assert.Equal(2UL, snapshot.HardwareAdvanceBatches);
        Assert.Equal(4UL, snapshot.MaxHardwareAdvanceBatch);
        Assert.Equal(4UL, snapshot.IdleAdvanceTicks);
        Assert.Equal(1UL, snapshot.IdleAdvanceBatches);
        Assert.Equal(4UL, snapshot.MaxIdleAdvanceBatch);
        Assert.Equal(0UL, snapshot.IdleTimerWakeCount);
        Assert.Equal(1UL, snapshot.IdleVBlankWakeCount);
        Assert.Equal(0UL, snapshot.IdleInputWakeCount);
        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out _));
        Assert.Equal(0x0320u, eventCode);
    }

    [Fact]
    public void AdvancesIdleHardwareToNextControllerScriptChange()
    {
        var memory = new DreamcastMemory();
        var scheduler = new DreamcastEventScheduler(memory, new DreamcastRunOptions(
            VBlankInterval: 0,
            ControllerAScript: new DreamcastControllerScript(
                new DreamcastControllerScriptFrame(0, DreamcastControllerState.Neutral),
                new DreamcastControllerScriptFrame(5, new DreamcastControllerState(Buttons: DreamcastControllerButtons.Start)))));

        scheduler.AdvanceBeforeInstruction(0);

        Assert.True(scheduler.AdvanceAfterIdle());

        var snapshot = scheduler.CreateSnapshot();
        Assert.Equal(5UL, snapshot.HardwareAdvanceTicks);
        Assert.Equal(2UL, snapshot.HardwareAdvanceBatches);
        Assert.Equal(4UL, snapshot.MaxHardwareAdvanceBatch);
        Assert.Equal(4UL, snapshot.IdleAdvanceTicks);
        Assert.Equal(1UL, snapshot.IdleAdvanceBatches);
        Assert.Equal(4UL, snapshot.MaxIdleAdvanceBatch);
        Assert.Equal(0UL, snapshot.IdleTimerWakeCount);
        Assert.Equal(0UL, snapshot.IdleVBlankWakeCount);
        Assert.Equal(1UL, snapshot.IdleInputWakeCount);
        Assert.Equal(1UL, snapshot.ControllerScriptChanges);
        Assert.Equal(DreamcastControllerButtons.Start, memory.ControllerA.Buttons);

        scheduler.AdvanceBeforeInstruction(1);

        Assert.Equal(DreamcastControllerButtons.Start, memory.ControllerA.Buttons);
        Assert.Equal(1UL, scheduler.CreateSnapshot().ControllerScriptChanges);
    }

    [Fact]
    public void RecordsCpuFastForwardBatches()
    {
        var memory = new DreamcastMemory();
        var scheduler = new DreamcastEventScheduler(memory, new DreamcastRunOptions(VBlankInterval: 0));

        scheduler.RecordCpuFastForward(5);
        scheduler.RecordCpuFastForward(3);

        var snapshot = scheduler.CreateSnapshot();
        Assert.Equal(8UL, snapshot.CpuFastForwardInstructions);
        Assert.Equal(2UL, snapshot.CpuFastForwardBatches);
        Assert.Equal(5UL, snapshot.MaxCpuFastForwardBatch);
    }

    [Fact]
    public void AdvanceAfterCpuFastForwardCatchesHardwareUpToSkippedInstructions()
    {
        var memory = new DreamcastMemory();
        var scheduler = new DreamcastEventScheduler(memory, new DreamcastRunOptions(VBlankInterval: 5));
        memory.WriteUInt32(0xA05F_6930, 1u << 3);

        scheduler.AdvanceBeforeInstruction(0);
        scheduler.AdvanceAfterCpuFastForward(9, 10);

        var snapshot = scheduler.CreateSnapshot();
        Assert.Equal(10UL, snapshot.HardwareAdvanceTicks);
        Assert.Equal(2UL, snapshot.HardwareAdvanceBatches);
        Assert.Equal(9UL, snapshot.MaxHardwareAdvanceBatch);
        Assert.Equal(2UL, snapshot.VBlankEventsRaised);
        Assert.Equal(9UL, snapshot.CpuFastForwardInstructions);
        Assert.Equal(1UL, snapshot.CpuFastForwardBatches);
        Assert.Equal(9UL, snapshot.MaxCpuFastForwardBatch);
    }

    [Fact]
    public void AdvanceAfterCpuFastForwardCanConsumeVBlankScheduleWithoutLatchingEvent()
    {
        var memory = new DreamcastMemory();
        var scheduler = new DreamcastEventScheduler(memory, new DreamcastRunOptions(VBlankInterval: 5));
        memory.WriteUInt32(0xA05F_6930, 1u << 3);

        scheduler.AdvanceBeforeInstruction(0);
        scheduler.AdvanceAfterCpuFastForward(9, 10, latchVBlankEvents: false);

        var snapshot = scheduler.CreateSnapshot();
        Assert.Equal(10UL, snapshot.HardwareAdvanceTicks);
        Assert.Equal(15UL, snapshot.NextVBlankInstruction);
        Assert.Equal(0UL, snapshot.VBlankEventsRaised);
        Assert.Equal(9UL, snapshot.CpuFastForwardInstructions);
        Assert.False(memory.TryGetPendingExternalInterrupt(out _, out _));

        scheduler.AdvanceBeforeInstruction(10);

        Assert.False(memory.TryGetPendingExternalInterrupt(out _, out _));
        Assert.Equal(15UL, scheduler.CreateSnapshot().NextVBlankInstruction);
    }

    [Fact]
    public void ReportsWhetherCpuFastForwardWouldCrossEnabledVBlankWake()
    {
        var memory = new DreamcastMemory();
        var scheduler = new DreamcastEventScheduler(memory, new DreamcastRunOptions(VBlankInterval: 5));
        memory.WriteUInt32(0xA05F_6930, 1u << 3);

        scheduler.AdvanceBeforeInstruction(0);

        Assert.True(scheduler.CanFastForwardWithoutExternalWake(4));
        Assert.False(scheduler.CanFastForwardWithoutExternalWake(5));
    }

    [Fact]
    public void ReportsWhetherCpuFastForwardWouldCrossTimerWake()
    {
        var memory = new DreamcastMemory();
        var scheduler = new DreamcastEventScheduler(memory, new DreamcastRunOptions(VBlankInterval: 0));
        memory.WriteUInt32(0xFFD8_0008, 5);
        memory.WriteUInt32(0xFFD8_000C, 5);
        memory.WriteUInt16(0xFFD8_0010, 0x20);
        memory.WriteUInt16(0xFFD0_0004, 0xF000);
        memory.Write(0xFFD8_0004, [0x01]);

        scheduler.AdvanceBeforeInstruction(0);

        Assert.True(scheduler.CanFastForwardWithoutExternalWake(5));
        Assert.False(scheduler.CanFastForwardWithoutExternalWake(6));
    }

    [Fact]
    public void ReportsWhetherCpuFastForwardWouldCrossInputScriptWake()
    {
        var memory = new DreamcastMemory();
        var scheduler = new DreamcastEventScheduler(memory, new DreamcastRunOptions(
            VBlankInterval: 0,
            ControllerAScript: new DreamcastControllerScript(
                new DreamcastControllerScriptFrame(0, DreamcastControllerState.Neutral),
                new DreamcastControllerScriptFrame(5, new DreamcastControllerState(Buttons: DreamcastControllerButtons.Start)))));

        scheduler.AdvanceBeforeInstruction(0);

        Assert.True(scheduler.CanFastForwardWithoutExternalWake(4));
        Assert.False(scheduler.CanFastForwardWithoutExternalWake(5));
    }
}
