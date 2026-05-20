using DcSharp.Core.Dreamcast.Memory;

namespace DcSharp.Core.Execution;

public sealed class DreamcastEventScheduler
{
    private readonly DreamcastMemory memory;
    private readonly ulong vblankInterval;
    private ulong nextVblankInstruction;

    public DreamcastEventScheduler(DreamcastMemory memory, DreamcastRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(options);

        this.memory = memory;
        vblankInterval = options.VBlankInterval;
        nextVblankInstruction = vblankInterval;
    }

    public void AdvanceBeforeInstruction(ulong instructionsExecuted)
    {
        RaiseDueVBlankEvents(instructionsExecuted);
        memory.AdvanceHardware(1);
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
