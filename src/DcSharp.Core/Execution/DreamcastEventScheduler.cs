using DcSharp.Core.Dreamcast.Memory;
using DcSharp.Core.Dreamcast.Input;

namespace DcSharp.Core.Execution;

public sealed class DreamcastEventScheduler
{
    private readonly DreamcastMemory memory;
    private readonly ulong vblankInterval;
    private readonly Dictionary<byte, DreamcastControllerScript> controllerScripts = [];
    private readonly Dictionary<byte, DreamcastControllerState> lastControllerStates = [];
    private ulong nextVblankInstruction;
    private ulong vblankEventsRaised;
    private ulong hardwareAdvanceTicks;
    private ulong hardwareAdvanceBatches;
    private ulong maxHardwareAdvanceBatch;
    private ulong cpuFastForwardInstructions;
    private ulong cpuFastForwardBatches;
    private ulong maxCpuFastForwardBatch;
    private ulong controllerScriptChanges;

    public DreamcastEventScheduler(DreamcastMemory memory, DreamcastRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(options);

        this.memory = memory;
        vblankInterval = options.VBlankInterval;
        nextVblankInstruction = vblankInterval;

        AddControllerScript(DreamcastMemory.MaplePortAUnit0Address, options.ControllerAScript);
        if (options.ControllerScripts is not null)
        {
            foreach (var (address, script) in options.ControllerScripts)
            {
                AddControllerScript(address, script);
            }
        }
    }

    public DreamcastSchedulerSnapshot CreateSnapshot() =>
        new(
            vblankInterval,
            nextVblankInstruction,
            vblankEventsRaised,
            hardwareAdvanceTicks,
            hardwareAdvanceBatches,
            maxHardwareAdvanceBatch,
            cpuFastForwardInstructions,
            cpuFastForwardBatches,
            maxCpuFastForwardBatch,
            controllerScriptChanges);

    public void AdvanceBeforeInstruction(ulong instructionsExecuted)
    {
        var effectiveTicks = Math.Max(instructionsExecuted, hardwareAdvanceTicks);
        ApplyInputScripts(effectiveTicks);
        RaiseDueVBlankEvents(effectiveTicks);
        AdvanceHardwareThrough(instructionsExecuted);
    }

    public bool AdvanceAfterSleep()
    {
        return AdvanceAfterIdle();
    }

    public bool AdvanceAfterIdle()
    {
        var nextTimerInterrupt = memory.TicksUntilNextTimerInterrupt();
        var nextVblank = vblankInterval == 0 || !memory.IsVBlankBeginInterruptEnabled() ? null : TicksUntilNextVBlank();
        var nextInputChange = TicksUntilNextInputScriptChange();
        var ticks = MinNonZero(nextTimerInterrupt, nextVblank, nextInputChange);
        if (ticks is null or 0)
        {
            return false;
        }

        var targetTicks = SaturatingAdd(hardwareAdvanceTicks, ticks.Value);
        ApplyInputScripts(targetTicks);
        RaiseDueVBlankEvents(targetTicks);
        AdvanceHardwareTo(targetTicks);
        return true;
    }

    public void RecordCpuFastForward(ulong instructions)
    {
        if (instructions == 0)
        {
            return;
        }

        cpuFastForwardInstructions += instructions;
        cpuFastForwardBatches++;
        maxCpuFastForwardBatch = Math.Max(maxCpuFastForwardBatch, instructions);
    }

    private void AdvanceHardwareThrough(ulong instructionsExecuted)
    {
        var targetTicks = instructionsExecuted == ulong.MaxValue
            ? ulong.MaxValue
            : instructionsExecuted + 1;
        AdvanceHardwareTo(targetTicks);
    }

    private void AdvanceHardwareTo(ulong targetTicks)
    {
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

    private ulong? TicksUntilNextVBlank()
    {
        if (nextVblankInstruction <= hardwareAdvanceTicks)
        {
            return 0;
        }

        return nextVblankInstruction - hardwareAdvanceTicks;
    }

    private ulong? TicksUntilNextInputScriptChange()
    {
        ulong? ticks = null;
        foreach (var script in controllerScripts.Values)
        {
            var nextChange = script.NextFrameInstructionAfter(hardwareAdvanceTicks);
            if (nextChange is null)
            {
                continue;
            }

            var scriptTicks = nextChange <= hardwareAdvanceTicks
                ? 0
                : nextChange.Value - hardwareAdvanceTicks;
            ticks = ticks is { } existing ? Math.Min(existing, scriptTicks) : scriptTicks;
        }

        return ticks;
    }

    private static ulong? MinNonZero(params ulong?[] values)
    {
        ulong? min = null;
        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }

            min = min is { } existing ? Math.Min(existing, value.Value) : value.Value;
        }

        return min;
    }

    private static ulong SaturatingAdd(ulong left, ulong right)
    {
        var result = left + right;
        return result < left ? ulong.MaxValue : result;
    }

    private void ApplyInputScripts(ulong instructionsExecuted)
    {
        foreach (var (address, script) in controllerScripts)
        {
            var state = script.StateAt(instructionsExecuted);
            if (state != lastControllerStates[address])
            {
                controllerScriptChanges++;
                lastControllerStates[address] = state;
            }

            memory.SetController(address, state);
        }
    }

    private void AddControllerScript(byte address, DreamcastControllerScript? script)
    {
        if (script is null)
        {
            return;
        }

        controllerScripts[address] = script;
        lastControllerStates[address] = memory.GetController(address) ?? DreamcastControllerState.Neutral;
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
    ulong CpuFastForwardInstructions,
    ulong CpuFastForwardBatches,
    ulong MaxCpuFastForwardBatch,
    ulong ControllerScriptChanges);
