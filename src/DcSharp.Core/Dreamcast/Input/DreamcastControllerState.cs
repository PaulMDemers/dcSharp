namespace DcSharp.Core.Dreamcast.Input;

public sealed record DreamcastControllerState(
    DreamcastControllerButtons Buttons = DreamcastControllerButtons.None,
    byte LeftTrigger = 0,
    byte RightTrigger = 0,
    sbyte JoyX = 0,
    sbyte JoyY = 0,
    sbyte Joy2X = 0,
    sbyte Joy2Y = 0)
{
    public static DreamcastControllerState Neutral { get; } = new();
}

public sealed record DreamcastControllerScript(IReadOnlyList<DreamcastControllerScriptFrame> Frames)
{
    public DreamcastControllerScript(params DreamcastControllerScriptFrame[] frames)
        : this((IReadOnlyList<DreamcastControllerScriptFrame>)frames)
    {
    }

    public DreamcastControllerState StateAt(ulong instruction)
    {
        if (Frames.Count == 0)
        {
            return DreamcastControllerState.Neutral;
        }

        var state = DreamcastControllerState.Neutral;
        foreach (var frame in Frames.OrderBy(frame => frame.FromInstruction))
        {
            if (frame.FromInstruction > instruction)
            {
                return state;
            }

            state = frame.State;
        }

        return state;
    }

    public ulong? NextFrameInstructionAfter(ulong instruction)
    {
        ulong? next = null;
        foreach (var frame in Frames)
        {
            if (frame.FromInstruction <= instruction)
            {
                continue;
            }

            next = next is { } existing ? Math.Min(existing, frame.FromInstruction) : frame.FromInstruction;
        }

        return next;
    }
}

public sealed record DreamcastControllerScriptFrame(ulong FromInstruction, DreamcastControllerState State);

[Flags]
public enum DreamcastControllerButtons : ushort
{
    None = 0,
    C = 1 << 0,
    B = 1 << 1,
    A = 1 << 2,
    Start = 1 << 3,
    DPadUp = 1 << 4,
    DPadDown = 1 << 5,
    DPadLeft = 1 << 6,
    DPadRight = 1 << 7,
    Z = 1 << 8,
    Y = 1 << 9,
    X = 1 << 10,
    D = 1 << 11,
    DPad2Up = 1 << 12,
    DPad2Down = 1 << 13,
    DPad2Left = 1 << 14,
    DPad2Right = 1 << 15
}
