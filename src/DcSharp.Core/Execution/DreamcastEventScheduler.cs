using DcSharp.Core.Dreamcast.Memory;
using DcSharp.Core.Dreamcast.Input;

namespace DcSharp.Core.Execution;

public sealed class DreamcastEventScheduler
{
    private readonly DreamcastMemory memory;
    private readonly ulong vblankInterval;
    private readonly DreamcastControllerScript? controllerAScript;
    private ulong nextVblankInstruction;

    public DreamcastEventScheduler(DreamcastMemory memory, DreamcastRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(options);

        this.memory = memory;
        vblankInterval = options.VBlankInterval;
        controllerAScript = options.ControllerAScript;
        nextVblankInstruction = vblankInterval;
    }

    public void AdvanceBeforeInstruction(ulong instructionsExecuted)
    {
        ApplyInputScripts(instructionsExecuted);
        RaiseDueVBlankEvents(instructionsExecuted);
        memory.AdvanceHardware(1);
    }

    private void ApplyInputScripts(ulong instructionsExecuted)
    {
        if (controllerAScript is not null)
        {
            memory.ControllerA = controllerAScript.StateAt(instructionsExecuted);
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
            nextVblankInstruction += vblankInterval;
        }
    }
}
