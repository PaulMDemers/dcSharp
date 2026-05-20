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
    ulong ControllerScriptChanges);
