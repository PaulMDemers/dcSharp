namespace DcSharp.Core.Dreamcast.Timer;

public sealed record DreamcastTimerSnapshot(
    IReadOnlyList<DreamcastTimerChannelSnapshot> Channels,
    uint? PendingEventCode,
    string? PendingEventCodeHex,
    int? PendingChannel,
    int? PendingPriority,
    DreamcastTimerPendingInterruptSnapshot? PendingInterrupt)
{
    public static DreamcastTimerSnapshot Empty { get; } = new([], null, null, null, null, null);
}

public sealed record DreamcastTimerPendingInterruptSnapshot(
    uint EventCode,
    string EventCodeHex,
    int Channel,
    int Priority);

public sealed record DreamcastTimerChannelSnapshot(
    int Channel,
    uint Constant,
    string ConstantHex,
    uint Counter,
    string CounterHex,
    uint Control,
    string ControlHex,
    int Priority,
    bool Running,
    bool UnderflowPending,
    bool InterruptEnabled);
