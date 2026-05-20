using DcSharp.Core.Dreamcast.Memory;
using DcSharp.Core.Dreamcast.Input;

namespace DcSharp.Core.Execution;

public sealed class DreamcastEventScheduler
{
    private readonly DreamcastMemory memory;
    private readonly ulong vblankInterval;
    private readonly DreamcastControllerScript? controllerAScript;
    private ulong nextVblankInstruction;
    private ulong vblankEventsRaised;
    private ulong hardwareAdvanceTicks;
    private ulong hardwareAdvanceBatches;
    private ulong maxHardwareAdvanceBatch;
    private ulong controllerScriptChanges;
    private DreamcastControllerState lastControllerA;

    public DreamcastEventScheduler(DreamcastMemory memory, DreamcastRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(options);

        this.memory = memory;
        vblankInterval = options.VBlankInterval;
        controllerAScript = options.ControllerAScript;
        nextVblankInstruction = vblankInterval;
        lastControllerA = memory.ControllerA;
    }

    public DreamcastSchedulerSnapshot CreateSnapshot() =>
        new(
            vblankInterval,
            nextVblankInstruction,
            vblankEventsRaised,
            hardwareAdvanceTicks,
            hardwareAdvanceBatches,
            maxHardwareAdvanceBatch,
            controllerScriptChanges);

    public void AdvanceBeforeInstruction(ulong instructionsExecuted)
    {
        ApplyInputScripts(instructionsExecuted);
        RaiseDueVBlankEvents(instructionsExecuted);
        AdvanceHardwareThrough(instructionsExecuted);
    }

    private void AdvanceHardwareThrough(ulong instructionsExecuted)
    {
        var targetTicks = instructionsExecuted == ulong.MaxValue
            ? ulong.MaxValue
            : instructionsExecuted + 1;
        if (targetTicks <= hardwareAdvanceTicks)
        {
            return;
        }

        var batch = targetTicks - hardwareAdvanceTicks;
        memory.AdvanceHardware(batch);
        hardwareAdvanceTicks = targetTicks;
        hardwareAdvanceBatches++;
        maxHardwareAdvanceBatch = Math.Max(maxHardwareAdvanceBatch, batch);
    }

    private void ApplyInputScripts(ulong instructionsExecuted)
    {
        if (controllerAScript is not null)
        {
            var state = controllerAScript.StateAt(instructionsExecuted);
            if (state != lastControllerA)
            {
                controllerScriptChanges++;
                lastControllerA = state;
            }

            memory.ControllerA = state;
        }
    }

    private void RaiseDueVBlankEvents(ulong instructionsExecuted)
    {
        if (vblankInterval == 0 || instructionsExecuted == 0)
        {
            return;
        }

        while (instructionsExecuted >= nextVblankInstruction)
        {
            memory.RaiseVBlankBegin();
            vblankEventsRaised++;
            nextVblankInstruction += vblankInterval;
        }
    }
}

public sealed record DreamcastSchedulerSnapshot(
    ulong VBlankInterval,
    ulong NextVBlankInstruction,
    ulong VBlankEventsRaised,
    ulong HardwareAdvanceTicks,
    ulong HardwareAdvanceBatches,
    ulong MaxHardwareAdvanceBatch,
    ulong ControllerScriptChanges);
