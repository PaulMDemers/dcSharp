namespace DcSharp.Core.Dreamcast.Asic;

public sealed record DreamcastAsicSnapshot(
    IReadOnlyList<DreamcastAsicEventRegisterSnapshot> EventRegisters,
    uint? PendingEventCode,
    string? PendingEventCodeHex,
    int? PendingLevel,
    DreamcastAsicPendingInterruptSnapshot? PendingInterrupt);

public sealed record DreamcastAsicPendingInterruptSnapshot(
    uint EventCode,
    string EventCodeHex,
    int Level,
    string LevelName,
    int RegisterIndex,
    string RegisterName,
    int Bit,
    uint BitMask,
    string BitMaskHex);

public sealed record DreamcastAsicEventRegisterSnapshot(
    int Index,
    string Name,
    uint Ack,
    string AckHex,
    uint Irq9Mask,
    string Irq9MaskHex,
    uint IrqBMask,
    string IrqBMaskHex,
    uint IrqDMask,
    string IrqDMaskHex,
    uint PendingIrq9,
    string PendingIrq9Hex,
    uint PendingIrqB,
    string PendingIrqBHex,
    uint PendingIrqD,
    string PendingIrqDHex);
