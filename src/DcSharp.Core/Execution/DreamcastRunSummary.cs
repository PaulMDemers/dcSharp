using DcSharp.Core.Cpu;
using DcSharp.Core.Dreamcast.Asic;
using DcSharp.Core.Dreamcast.Audio;
using DcSharp.Core.Dreamcast.Input;
using DcSharp.Core.Dreamcast.Memory;
using DcSharp.Core.Dreamcast.Timer;
using DcSharp.Core.Dreamcast.Video;
using DcSharp.Core.Media;
using System.Text;

namespace DcSharp.Core.Execution;

public sealed record DreamcastRunSummary(
    DreamcastStopReason StopReason,
    string StopDetail,
    ulong InstructionsExecuted,
    uint Pc,
    string PcHex,
    uint Pr,
    string PrHex,
    uint Sr,
    string SrHex,
    uint? StopPc,
    string? StopPcHex,
    ushort? StopOpcode,
    string? StopOpcodeHex,
    DreamcastSymbolSummary? StopSymbol,
    DreamcastLoadSummary Load,
    int DeviceAccessCount,
    IReadOnlyList<DreamcastDeviceAccessDomainSummary> DeviceAccessDomains,
    IReadOnlyList<DreamcastDeviceAccessKindSummary> DeviceAccessKinds,
    IReadOnlyList<DreamcastMemoryAccessSummary> RecentDeviceAccesses,
    int SerialBytes,
    string SerialText,
    IReadOnlyList<DreamcastTraceSummary> TraceTail,
    DreamcastControllerSummary ControllerA,
    DreamcastAsicSummary Asic,
    DreamcastVideoSummary Video,
    DreamcastAudioSummary Audio,
    DreamcastMapleSummary Maple,
    DreamcastGdromSummary Gdrom,
    DreamcastTimerSummary Timer,
    DreamcastSchedulerSummary Scheduler,
    DreamcastCpuSummary Cpu)
{
    public static DreamcastRunSummary FromResult(DreamcastRunResult result, DreamcastRunOptions? options = null, int recentDeviceAccessCount = 16)
    {
        ArgumentNullException.ThrowIfNull(result);
        var controllerInstruction = Math.Max(result.Cpu.InstructionsExecuted, result.Scheduler.HardwareAdvanceTicks);
        var controllerA = ControllerScriptFromMap(options, 0x20)?.StateAt(controllerInstruction)
            ?? options?.ControllerAScript?.StateAt(controllerInstruction)
            ?? ControllerFromMap(options, 0x20)
            ?? options?.ControllerA
            ?? DreamcastControllerState.Neutral;

        return new DreamcastRunSummary(
            result.StopReason,
            result.StopDetail,
            result.Cpu.InstructionsExecuted,
            result.Cpu.Pc,
            Hex32(result.Cpu.Pc),
            result.Cpu.Pr,
            Hex32(result.Cpu.Pr),
            result.Cpu.Sr,
            Hex32(result.Cpu.Sr),
            result.StopPc,
            result.StopPc is { } stopPc ? Hex32(stopPc) : null,
            result.StopOpcode,
            result.StopOpcode is { } stopOpcode ? Hex16(stopOpcode) : null,
            result.StopPc is { } symbolPc ? DreamcastSymbolSummary.FromSymbol(result.Load.FindNearestSymbol(symbolPc), symbolPc) : null,
            DreamcastLoadSummary.FromResult(result),
            result.DeviceAccesses.Count,
            result.DeviceAccesses
                .GroupBy(DreamcastDeviceDomainClassifier.Classify, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new DreamcastDeviceAccessDomainSummary(group.Key, group.Count()))
                .ToArray(),
            result.DeviceAccesses
                .GroupBy(access => access.Kind)
                .OrderBy(group => group.Key)
                .Select(group => new DreamcastDeviceAccessKindSummary(group.Key, group.Count()))
                .ToArray(),
            result.DeviceAccesses
                .TakeLast(Math.Max(0, recentDeviceAccessCount))
                .Select(DreamcastMemoryAccessSummary.FromAccess)
                .ToArray(),
            result.SerialOutput.Count,
            Encoding.ASCII.GetString(result.SerialOutput.ToArray()),
            result.TraceTail.Select(step => DreamcastTraceSummary.FromStep(step, result.Load.FindNearestSymbol(step.Pc))).ToArray(),
            DreamcastControllerSummary.FromState(controllerA),
            DreamcastAsicSummary.FromSnapshot(result.Asic),
            DreamcastVideoSummary.FromSnapshot(result.Video),
            DreamcastAudioSummary.FromSnapshot(result.Audio),
            DreamcastMapleSummary.FromSnapshot(result.Maple),
            DreamcastGdromSummary.FromSnapshot(result.Gdrom ?? DreamcastGdromSnapshot.Empty),
            DreamcastTimerSummary.FromSnapshot(result.Timer ?? DreamcastTimerSnapshot.Empty),
            DreamcastSchedulerSummary.FromSnapshot(result.Scheduler),
            DreamcastCpuSummary.FromSnapshot(result.Cpu));
    }

    private static string Hex32(uint value) => $"0x{value:X8}";
    private static string Hex16(ushort value) => $"0x{value:X4}";
    private static DreamcastControllerScript? ControllerScriptFromMap(DreamcastRunOptions? options, byte address) =>
        options?.ControllerScripts?.GetValueOrDefault(address);

    private static DreamcastControllerState? ControllerFromMap(DreamcastRunOptions? options, byte address) =>
        options?.Controllers?.GetValueOrDefault(address);
}

public sealed record DreamcastCpuSummary(
    uint Pc,
    string PcHex,
    uint Pr,
    string PrHex,
    uint Sr,
    string SrHex,
    uint Gbr,
    string GbrHex,
    uint Vbr,
    string VbrHex,
    uint Spc,
    string SpcHex,
    uint Ssr,
    string SsrHex,
    uint Fpscr,
    string FpscrHex,
    Sh4FpscrSummary FpscrFields,
    uint Tra,
    string TraHex,
    uint Expevt,
    string ExpevtHex,
    uint Intevt,
    string IntevtHex)
{
    public static DreamcastCpuSummary FromSnapshot(Sh4StateSnapshot snapshot) =>
        new(
            snapshot.Pc,
            $"0x{snapshot.Pc:X8}",
            snapshot.Pr,
            $"0x{snapshot.Pr:X8}",
            snapshot.Sr,
            $"0x{snapshot.Sr:X8}",
            snapshot.Gbr,
            $"0x{snapshot.Gbr:X8}",
            snapshot.Vbr,
            $"0x{snapshot.Vbr:X8}",
            snapshot.Spc,
            $"0x{snapshot.Spc:X8}",
            snapshot.Ssr,
            $"0x{snapshot.Ssr:X8}",
            snapshot.Fpscr,
            $"0x{snapshot.Fpscr:X8}",
            Sh4FpscrSummary.FromValue(snapshot.Fpscr),
            snapshot.Tra,
            $"0x{snapshot.Tra:X8}",
            snapshot.Expevt,
            $"0x{snapshot.Expevt:X8}",
            snapshot.Intevt,
            $"0x{snapshot.Intevt:X8}");
}

public sealed record DreamcastLoadSummary(
    uint EntryPoint,
    string EntryPointHex,
    uint TranslatedEntryPoint,
    string TranslatedEntryPointHex,
    uint LoadedBytes,
    uint ReservedBytes,
    IReadOnlyList<DreamcastLoadedSegmentSummary> Segments,
    int SymbolCount)
{
    public static DreamcastLoadSummary FromResult(DreamcastRunResult result) =>
        new(
            result.Load.EntryPoint,
            $"0x{result.Load.EntryPoint:X8}",
            result.Load.TranslatedEntryPoint,
            $"0x{result.Load.TranslatedEntryPoint:X8}",
            result.Load.LoadedBytes,
            result.Load.ReservedBytes,
            result.Load.LoadedSegments.Select(segment => new DreamcastLoadedSegmentSummary(
                segment.Index,
                segment.VirtualAddress,
                $"0x{segment.VirtualAddress:X8}",
                segment.PhysicalAddress,
                $"0x{segment.PhysicalAddress:X8}",
                segment.FileSize,
                segment.MemorySize,
                segment.Flags,
                $"0x{segment.Flags:X}",
                segment.Alignment)).ToArray(),
            result.Load.Symbols.Count);
}

public sealed record DreamcastLoadedSegmentSummary(
    int Index,
    uint VirtualAddress,
    string VirtualAddressHex,
    uint PhysicalAddress,
    string PhysicalAddressHex,
    uint FileSize,
    uint MemorySize,
    uint Flags,
    string FlagsHex,
    uint Alignment);

public sealed record DreamcastDeviceAccessDomainSummary(string Domain, int Count);

public sealed record DreamcastDeviceAccessKindSummary(MemoryAccessKind Kind, int Count);

public sealed record DreamcastMemoryAccessSummary(
    MemoryAccessKind Kind,
    string Domain,
    uint Address,
    string AddressHex,
    int Size,
    uint Value,
    string ValueHex,
    uint? Pc,
    string? PcHex)
{
    public static DreamcastMemoryAccessSummary FromAccess(MemoryAccess access) =>
        new(
            access.Kind,
            DreamcastDeviceDomainClassifier.Classify(access),
            access.Address,
            $"0x{access.Address:X8}",
            access.Size,
            access.Value,
            $"0x{access.Value:X8}",
            access.Pc,
            access.Pc is { } pc ? $"0x{pc:X8}" : null);
}

public sealed record DreamcastTraceSummary(
    uint Pc,
    string PcHex,
    ushort Opcode,
    string OpcodeHex,
    string Trace,
    DreamcastSymbolSummary? Symbol)
{
    public static DreamcastTraceSummary FromStep(Sh4StepResult step, ElfSymbol? symbol = null) =>
        new(step.Pc, $"0x{step.Pc:X8}", step.Opcode, $"0x{step.Opcode:X4}", step.Trace, DreamcastSymbolSummary.FromSymbol(symbol, step.Pc));
}

public sealed record DreamcastSymbolSummary(
    string Name,
    uint Address,
    string AddressHex,
    uint Offset,
    string OffsetHex,
    string Display)
{
    public static DreamcastSymbolSummary? FromSymbol(ElfSymbol? symbol, uint pc)
    {
        if (symbol is null)
        {
            return null;
        }

        var offset = pc >= symbol.Value ? pc - symbol.Value : 0;
        return new DreamcastSymbolSummary(
            symbol.Name,
            symbol.Value,
            $"0x{symbol.Value:X8}",
            offset,
            $"0x{offset:X}",
            offset == 0 ? symbol.Name : $"{symbol.Name}+0x{offset:X}");
    }
}

public sealed record DreamcastControllerSummary(
    DreamcastControllerButtons Buttons,
    string ButtonsText,
    byte LeftTrigger,
    byte RightTrigger,
    sbyte JoyX,
    sbyte JoyY,
    sbyte Joy2X,
    sbyte Joy2Y)
{
    public static DreamcastControllerSummary FromState(DreamcastControllerState state) =>
        new(
            state.Buttons,
            state.Buttons.ToString(),
            state.LeftTrigger,
            state.RightTrigger,
            state.JoyX,
            state.JoyY,
            state.Joy2X,
            state.Joy2Y);
}

public sealed record DreamcastAsicSummary(
    IReadOnlyList<DreamcastAsicEventRegisterSummary> EventRegisters,
    uint? PendingEventCode,
    string? PendingEventCodeHex,
    int? PendingLevel,
    DreamcastAsicPendingInterruptSummary? PendingInterrupt)
{
    public static DreamcastAsicSummary FromSnapshot(DreamcastAsicSnapshot snapshot) =>
        new(
            snapshot.EventRegisters.Select(DreamcastAsicEventRegisterSummary.FromRegister).ToArray(),
            snapshot.PendingEventCode,
            snapshot.PendingEventCodeHex,
            snapshot.PendingLevel,
            snapshot.PendingInterrupt is { } pending ? DreamcastAsicPendingInterruptSummary.FromInterrupt(pending) : null);
}

public sealed record DreamcastAsicPendingInterruptSummary(
    uint EventCode,
    string EventCodeHex,
    int Level,
    string LevelName,
    int RegisterIndex,
    string RegisterName,
    int Bit,
    uint BitMask,
    string BitMaskHex)
{
    public static DreamcastAsicPendingInterruptSummary FromInterrupt(DreamcastAsicPendingInterruptSnapshot interrupt) =>
        new(
            interrupt.EventCode,
            interrupt.EventCodeHex,
            interrupt.Level,
            interrupt.LevelName,
            interrupt.RegisterIndex,
            interrupt.RegisterName,
            interrupt.Bit,
            interrupt.BitMask,
            interrupt.BitMaskHex);
}

public sealed record DreamcastAsicEventRegisterSummary(
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
    string PendingIrqDHex)
{
    public static DreamcastAsicEventRegisterSummary FromRegister(DreamcastAsicEventRegisterSnapshot register) =>
        new(
            register.Index,
            register.Name,
            register.Ack,
            register.AckHex,
            register.Irq9Mask,
            register.Irq9MaskHex,
            register.IrqBMask,
            register.IrqBMaskHex,
            register.IrqDMask,
            register.IrqDMaskHex,
            register.PendingIrq9,
            register.PendingIrq9Hex,
            register.PendingIrqB,
            register.PendingIrqBHex,
            register.PendingIrqD,
            register.PendingIrqDHex);
}

public sealed record DreamcastTimerSummary(
    IReadOnlyList<DreamcastTimerChannelSummary> Channels,
    uint? PendingEventCode,
    string? PendingEventCodeHex,
    int? PendingChannel,
    int? PendingPriority,
    DreamcastTimerPendingInterruptSummary? PendingInterrupt)
{
    public static DreamcastTimerSummary FromSnapshot(DreamcastTimerSnapshot snapshot) =>
        new(
            snapshot.Channels.Select(DreamcastTimerChannelSummary.FromChannel).ToArray(),
            snapshot.PendingEventCode,
            snapshot.PendingEventCodeHex,
            snapshot.PendingChannel,
            snapshot.PendingPriority,
            snapshot.PendingInterrupt is { } pending ? DreamcastTimerPendingInterruptSummary.FromInterrupt(pending) : null);
}

public sealed record DreamcastTimerPendingInterruptSummary(
    uint EventCode,
    string EventCodeHex,
    int Channel,
    int Priority)
{
    public static DreamcastTimerPendingInterruptSummary FromInterrupt(DreamcastTimerPendingInterruptSnapshot interrupt) =>
        new(
            interrupt.EventCode,
            interrupt.EventCodeHex,
            interrupt.Channel,
            interrupt.Priority);
}

public sealed record DreamcastTimerChannelSummary(
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
    bool InterruptEnabled)
{
    public static DreamcastTimerChannelSummary FromChannel(DreamcastTimerChannelSnapshot channel) =>
        new(
            channel.Channel,
            channel.Constant,
            channel.ConstantHex,
            channel.Counter,
            channel.CounterHex,
            channel.Control,
            channel.ControlHex,
            channel.Priority,
            channel.Running,
            channel.UnderflowPending,
            channel.InterruptEnabled);
}

public sealed record DreamcastVideoSummary(
    int VramBytes,
    ulong NonZeroBytes,
    uint Fnv1A32,
    string Fnv1A32Hex,
    uint? FirstNonZeroOffset,
    string? FirstNonZeroOffsetHex,
    IReadOnlyList<DreamcastVideoSampleSummary> Samples,
    IReadOnlyList<DreamcastPvrRegisterValueSummary> PvrRegisters,
    int PvrRegisterAccessCount,
    IReadOnlyList<DreamcastPvrRegisterAccessSummary> RecentPvrRegisterAccesses,
    int PvrTaCommandWriteCount,
    IReadOnlyList<DreamcastPvrTaCommandWriteSummary> RecentPvrTaCommandWrites,
    IReadOnlyList<DreamcastPvrTaStreamWriteSummary> RecentPvrTaStreamWrites,
    IReadOnlyList<DreamcastPvrTaPolygonHeaderPayloadSummary> PvrTaPolygonHeaderPayloads,
    IReadOnlyList<DreamcastPvrTaRealVertexPayloadSummary> PvrTaRealVertexPayloads,
    IReadOnlyList<DreamcastPvrTaParameterHeaderSummary> RecentPvrTaParameterHeaders,
    IReadOnlyList<DreamcastPvrTaListSummary> PvrTaLists,
    IReadOnlyList<DreamcastPvrTaStripSummary> PvrTaStrips,
    IReadOnlyList<DreamcastPvrTaSpriteSummary> PvrTaSprites,
    IReadOnlyList<DreamcastPvrTaCommandKindSummary> PvrTaCommandKinds)
{
    public int PvrTaRenderableSpriteCount => PvrTaSprites.Count(sprite => sprite.HasRenderablePreviewArea);

    public int PvrTaDegenerateSpriteCount => PvrTaSprites.Count(sprite => sprite.HasFinitePreviewCoordinates && !sprite.HasRenderablePreviewArea);

    public int PvrTaNonfiniteSpriteCount => PvrTaSprites.Count(sprite => !sprite.HasFinitePreviewCoordinates);

    public static DreamcastVideoSummary FromSnapshot(DreamcastVideoSnapshot snapshot, int recentCount = 32) =>
        new(
            snapshot.VramBytes,
            snapshot.NonZeroBytes,
            snapshot.Fnv1A32,
            snapshot.Fnv1A32Hex,
            snapshot.FirstNonZeroOffset,
            snapshot.FirstNonZeroOffsetHex,
            snapshot.Samples.Select(sample => new DreamcastVideoSampleSummary(
                sample.Name,
                sample.Offset,
                sample.OffsetHex,
                sample.Rgb565,
                sample.Rgb565Hex)).ToArray(),
            snapshot.PvrRegisters.Select(DreamcastPvrRegisterValueSummary.FromRegister).ToArray(),
            snapshot.PvrRegisterAccesses.Count,
            snapshot.PvrRegisterAccesses.TakeLast(Math.Max(0, recentCount)).Select(DreamcastPvrRegisterAccessSummary.FromAccess).ToArray(),
            snapshot.PvrTaCommandWrites.Count,
            snapshot.PvrTaCommandWrites.TakeLast(Math.Max(0, recentCount)).Select(DreamcastPvrTaCommandWriteSummary.FromWrite).ToArray(),
            DreamcastPvrTaStreamDecoder.Decode(snapshot.PvrTaCommandWrites)
                .TakeLast(Math.Max(0, recentCount))
                .Select(DreamcastPvrTaStreamWriteSummary.FromWrite)
                .ToArray(),
            DreamcastPvrTaPolygonHeaderPayloadDecoder.Decode(snapshot.PvrTaCommandWrites)
                .Select(DreamcastPvrTaPolygonHeaderPayloadSummary.FromPayload)
                .ToArray(),
            DreamcastPvrTaRealVertexPayloadDecoder.Decode(snapshot.PvrTaCommandWrites)
                .Select(DreamcastPvrTaRealVertexPayloadSummary.FromPayload)
                .ToArray(),
            snapshot.PvrTaCommandWrites.TakeLast(Math.Max(0, recentCount)).Select(DreamcastPvrTaParameterHeaderSummary.FromWrite).ToArray(),
            snapshot.PvrTaCommandWrites
                .GroupBy(write => new PvrTaListKey(write.Region, write.ListType, write.ListTypeName))
                .OrderBy(group => group.Key.Region, StringComparer.Ordinal)
                .ThenBy(group => group.Key.ListType ?? int.MaxValue)
                .ThenBy(group => group.Key.ListTypeName ?? string.Empty, StringComparer.Ordinal)
                .Select(group => new DreamcastPvrTaListSummary(
                    group.Key.Region,
                    group.Key.ListType,
                    group.Key.ListTypeName,
                    group.Count(),
                    group.Count(write => string.Equals(write.Kind, "PolygonHeader", StringComparison.Ordinal)),
                    group.Count(write => string.Equals(write.Kind, "Vertex", StringComparison.Ordinal)),
                    group.Count(write => string.Equals(write.Kind, "VertexEndOfStrip", StringComparison.Ordinal))))
                .ToArray(),
            snapshot.PvrTaStrips.Select(DreamcastPvrTaStripSummary.FromStrip).ToArray(),
            snapshot.PvrTaSprites.Select(DreamcastPvrTaSpriteSummary.FromSprite).ToArray(),
            snapshot.PvrTaCommandWrites
                .GroupBy(write => write.Kind, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new DreamcastPvrTaCommandKindSummary(group.Key, group.Count()))
                .ToArray());
}

public sealed record DreamcastVideoSampleSummary(
    string Name,
    uint Offset,
    string OffsetHex,
    ushort Rgb565,
    string Rgb565Hex);

public sealed record DreamcastPvrRegisterValueSummary(
    uint Offset,
    string OffsetHex,
    string Name,
    uint Value,
    string ValueHex)
{
    public static DreamcastPvrRegisterValueSummary FromRegister(DreamcastPvrRegisterValue register) =>
        new(register.Offset, register.OffsetHex, register.Name, register.Value, register.ValueHex);
}

public sealed record DreamcastPvrRegisterAccessSummary(
    MemoryAccessKind Kind,
    uint Address,
    string AddressHex,
    uint Offset,
    string OffsetHex,
    string Name,
    int Size,
    uint Value,
    string ValueHex)
{
    public static DreamcastPvrRegisterAccessSummary FromAccess(DreamcastPvrRegisterAccess access) =>
        new(access.Kind, access.Address, access.AddressHex, access.Offset, access.OffsetHex, access.Name, access.Size, access.Value, access.ValueHex);
}

public sealed record DreamcastPvrTaCommandWriteSummary(
    uint Address,
    string AddressHex,
    string Region,
    string Kind,
    int? ListType,
    string? ListTypeName,
    bool EndOfStrip,
    int Size,
    uint Value,
    string ValueHex,
    uint? InstructionPc = null,
    string? InstructionPcHex = null)
{
    public static DreamcastPvrTaCommandWriteSummary FromWrite(DreamcastPvrTaCommandWrite write) =>
        new(
            write.Address,
            write.AddressHex,
            write.Region,
            write.Kind,
            write.ListType,
            write.ListTypeName,
            write.EndOfStrip,
            write.Size,
            write.Value,
            write.ValueHex,
            write.InstructionPc,
            write.InstructionPcHex);
}

public sealed record DreamcastPvrTaStreamWriteSummary(
    uint Address,
    string AddressHex,
    string Region,
    string Role,
    string Kind,
    int Size,
    uint Value,
    string ValueHex,
    string ControlKind,
    uint ControlValue,
    string ControlValueHex,
    int? PayloadWordIndex,
    int? PayloadWordsRemaining,
    string? PayloadWordName,
    uint? InstructionPc = null,
    string? InstructionPcHex = null)
{
    public static DreamcastPvrTaStreamWriteSummary FromWrite(DreamcastPvrTaStreamWrite write) =>
        new(
            write.Write.Address,
            write.Write.AddressHex,
            write.Write.Region,
            write.Role,
            write.Write.Kind,
            write.Write.Size,
            write.Write.Value,
            write.Write.ValueHex,
            write.ControlKind,
            write.ControlValue,
            write.ControlValueHex,
            write.PayloadWordIndex,
            write.PayloadWordsRemaining,
            write.PayloadWordName,
            write.Write.InstructionPc,
            write.Write.InstructionPcHex);
}

public sealed record DreamcastPvrTaPolygonHeaderPayloadSummary(
    string Region,
    int? ListType,
    string? ListTypeName,
    uint HeaderValue,
    string HeaderValueHex,
    uint Mode1,
    string Mode1Hex,
    DreamcastPvrTaPolygonHeaderMode1 Mode1Fields,
    uint Mode2,
    string Mode2Hex,
    DreamcastPvrTaPolygonHeaderMode2 Mode2Fields,
    uint Mode3,
    string Mode3Hex,
    DreamcastPvrTaPolygonHeaderMode3 Mode3Fields,
    uint Parameter0,
    string Parameter0Hex,
    uint Parameter1,
    string Parameter1Hex,
    uint Parameter2,
    string Parameter2Hex,
    uint Parameter3,
    string Parameter3Hex)
{
    public static DreamcastPvrTaPolygonHeaderPayloadSummary FromPayload(DreamcastPvrTaPolygonHeaderPayload payload) =>
        new(
            payload.Region,
            payload.ListType,
            payload.ListTypeName,
            payload.HeaderValue,
            payload.HeaderValueHex,
            payload.Mode1,
            payload.Mode1Hex,
            payload.Mode1Fields,
            payload.Mode2,
            payload.Mode2Hex,
            payload.Mode2Fields,
            payload.Mode3,
            payload.Mode3Hex,
            payload.Mode3Fields,
            payload.Parameter0,
            payload.Parameter0Hex,
            payload.Parameter1,
            payload.Parameter1Hex,
            payload.Parameter2,
            payload.Parameter2Hex,
            payload.Parameter3,
            payload.Parameter3Hex);
}

public sealed record DreamcastPvrTaRealVertexPayloadSummary(
    string Region,
    int? ListType,
    string? ListTypeName,
    uint ControlValue,
    string ControlValueHex,
    bool EndOfStrip,
    uint XValue,
    string XValueHex,
    float X,
    int RoundedX,
    uint YValue,
    string YValueHex,
    float Y,
    int RoundedY,
    uint ZValue,
    string ZValueHex,
    float Z,
    uint UValue,
    string UValueHex,
    float U,
    uint VValue,
    string VValueHex,
    float V,
    uint Argb,
    string ArgbHex,
    ushort Rgb565,
    string Rgb565Hex,
    uint OffsetArgb,
    string OffsetArgbHex)
{
    public static DreamcastPvrTaRealVertexPayloadSummary FromPayload(DreamcastPvrTaRealVertexPayload payload) =>
        new(
            payload.Region,
            payload.ListType,
            payload.ListTypeName,
            payload.ControlValue,
            payload.ControlValueHex,
            payload.EndOfStrip,
            payload.XValue,
            payload.XValueHex,
            payload.X,
            payload.RoundedX,
            payload.YValue,
            payload.YValueHex,
            payload.Y,
            payload.RoundedY,
            payload.ZValue,
            payload.ZValueHex,
            payload.Z,
            payload.UValue,
            payload.UValueHex,
            payload.U,
            payload.VValue,
            payload.VValueHex,
            payload.V,
            payload.Argb,
            payload.ArgbHex,
            payload.Rgb565,
            payload.Rgb565Hex,
            payload.OffsetArgb,
            payload.OffsetArgbHex);
}

public sealed record DreamcastPvrTaParameterHeaderSummary(
    string Region,
    uint Value,
    string ValueHex,
    string Kind,
    int? ParameterType,
    int? ListType,
    string? ListTypeName,
    bool EndOfStrip,
    int? ExpectedPayloadWords,
    bool HasKnownPayloadLength,
    DreamcastPvrTaPolygonHeaderCommandSummary? PolygonHeaderCommand)
{
    public static DreamcastPvrTaParameterHeaderSummary FromWrite(DreamcastPvrTaCommandWrite write)
    {
        var header = DreamcastPvrTaParameterDecoder.Decode(write.Region, write.Value);
        return FromHeader(header);
    }

    public static DreamcastPvrTaParameterHeaderSummary FromWriteSummary(DreamcastPvrTaCommandWriteSummary write)
    {
        var header = DreamcastPvrTaParameterDecoder.Decode(write.Region, write.Value);
        return FromHeader(header);
    }

    private static DreamcastPvrTaParameterHeaderSummary FromHeader(DreamcastPvrTaParameterHeader header) =>
        new(
            header.Region,
            header.Value,
            header.ValueHex,
            header.Kind,
            header.ParameterType,
            header.ListType,
            header.ListTypeName,
            header.EndOfStrip,
            header.ExpectedPayloadWords,
            header.HasKnownPayloadLength,
            header.PolygonHeaderCommand is { } command ? DreamcastPvrTaPolygonHeaderCommandSummary.FromCommand(command) : null);
}

public sealed record DreamcastPvrTaPolygonHeaderCommandSummary(
    bool Uv16Bit,
    bool Gouraud,
    bool OffsetColorEnabled,
    bool TextureEnabled,
    int ColorFormat,
    string ColorFormatName,
    bool ModifierNormal,
    bool ModifierEnabled,
    int ClipMode,
    string ClipModeName,
    int StripLength,
    string StripLengthName,
    bool AutoStripLength)
{
    public static DreamcastPvrTaPolygonHeaderCommandSummary FromCommand(DreamcastPvrTaPolygonHeaderCommand command) =>
        new(
            command.Uv16Bit,
            command.Gouraud,
            command.OffsetColorEnabled,
            command.TextureEnabled,
            command.ColorFormat,
            command.ColorFormatName,
            command.ModifierNormal,
            command.ModifierEnabled,
            command.ClipMode,
            command.ClipModeName,
            command.StripLength,
            command.StripLengthName,
            command.AutoStripLength);
}

public sealed record DreamcastPvrTaListSummary(
    string Region,
    int? ListType,
    string? ListTypeName,
    int CommandCount,
    int PolygonHeaderCount,
    int VertexCount,
    int VertexEndOfStripCount);

public sealed record DreamcastPvrTaVertexSummary(
    int X,
    int Y,
    float Z,
    uint ZValue,
    string ZValueHex,
    float U,
    uint UValue,
    string UValueHex,
    float V,
    uint VValue,
    string VValueHex,
    bool EndOfStrip,
    ushort Rgb565,
    string Rgb565Hex,
    uint ControlValue,
    string ControlValueHex,
    uint XValue,
    string XValueHex,
    uint YValue,
    string YValueHex,
    uint ColorValue,
    string ColorValueHex)
{
    public static DreamcastPvrTaVertexSummary FromVertex(DreamcastPvrTaVertex vertex) =>
        new(
            vertex.X,
            vertex.Y,
            vertex.Z,
            vertex.ZValue,
            vertex.ZValueHex,
            vertex.U,
            vertex.UValue,
            vertex.UValueHex,
            vertex.V,
            vertex.VValue,
            vertex.VValueHex,
            vertex.EndOfStrip,
            vertex.Rgb565,
            vertex.Rgb565Hex,
            vertex.ControlValue,
            vertex.ControlValueHex,
            vertex.XValue,
            vertex.XValueHex,
            vertex.YValue,
            vertex.YValueHex,
            vertex.ColorValue,
            vertex.ColorValueHex);
}

public sealed record DreamcastPvrTaStripSummary(
    string Region,
    int? ListType,
    string? ListTypeName,
    uint HeaderValue,
    string HeaderValueHex,
    DreamcastPvrTaPolygonHeaderPayloadSummary? HeaderPayload,
    ushort Rgb565,
    string Rgb565Hex,
    int VertexCount,
    IReadOnlyList<DreamcastPvrTaVertexSummary> Vertices)
{
    public static DreamcastPvrTaStripSummary FromStrip(DreamcastPvrTaStrip strip) =>
        new(
            strip.Region,
            strip.ListType,
            strip.ListTypeName,
            strip.HeaderValue,
            strip.HeaderValueHex,
            strip.HeaderPayload is null ? null : DreamcastPvrTaPolygonHeaderPayloadSummary.FromPayload(strip.HeaderPayload),
            strip.Rgb565,
            strip.Rgb565Hex,
            strip.Vertices.Count,
            strip.Vertices.Select(DreamcastPvrTaVertexSummary.FromVertex).ToArray());
}

public sealed record DreamcastPvrTaSpriteHeaderPayloadSummary(
    uint Mode1,
    string Mode1Hex,
    uint Mode2,
    string Mode2Hex,
    uint Mode3,
    string Mode3Hex,
    uint Argb,
    string ArgbHex,
    uint OffsetArgb,
    string OffsetArgbHex,
    uint Dummy0,
    string Dummy0Hex,
    uint Dummy1,
    string Dummy1Hex,
    DreamcastPvrTaPolygonHeaderMode1 Mode1Fields,
    DreamcastPvrTaPolygonHeaderMode2 Mode2Fields,
    DreamcastPvrTaPolygonHeaderMode3 Mode3Fields)
{
    public static DreamcastPvrTaSpriteHeaderPayloadSummary FromPayload(DreamcastPvrTaSpriteHeaderPayload payload) =>
        new(
            payload.Mode1,
            payload.Mode1Hex,
            payload.Mode2,
            payload.Mode2Hex,
            payload.Mode3,
            payload.Mode3Hex,
            payload.Argb,
            payload.ArgbHex,
            payload.OffsetArgb,
            payload.OffsetArgbHex,
            payload.Dummy0,
            payload.Dummy0Hex,
            payload.Dummy1,
            payload.Dummy1Hex,
            payload.Mode1Fields,
            payload.Mode2Fields,
            payload.Mode3Fields);
}

public sealed record DreamcastPvrTaSpriteVertexSummary(
    string Name,
    int X,
    int Y,
    float Z,
    uint ZValue,
    string ZValueHex,
    uint XValue,
    string XValueHex,
    uint YValue,
    string YValueHex,
    float U,
    float V,
    uint UvValue,
    string UvValueHex,
    bool HasFinitePosition)
{
    public static DreamcastPvrTaSpriteVertexSummary FromVertex(DreamcastPvrTaSpriteVertex vertex) =>
        new(
            vertex.Name,
            vertex.X,
            vertex.Y,
            vertex.Z,
            vertex.ZValue,
            vertex.ZValueHex,
            vertex.XValue,
            vertex.XValueHex,
            vertex.YValue,
            vertex.YValueHex,
            vertex.U,
            vertex.V,
            vertex.UvValue,
            vertex.UvValueHex,
            vertex.HasFinitePosition);
}

public sealed record DreamcastPvrTaSpriteSummary(
    string Region,
    int? ListType,
    string? ListTypeName,
    uint HeaderValue,
    string HeaderValueHex,
    DreamcastPvrTaSpriteHeaderPayloadSummary HeaderPayload,
    uint ControlValue,
    string ControlValueHex,
    bool EndOfStrip,
    ushort Rgb565,
    string Rgb565Hex,
    bool HasFinitePreviewCoordinates,
    bool HasRenderablePreviewArea,
    int VertexCount,
    IReadOnlyList<DreamcastPvrTaSpriteVertexSummary> Vertices)
{
    public static DreamcastPvrTaSpriteSummary FromSprite(DreamcastPvrTaSprite sprite) =>
        new(
            sprite.Region,
            sprite.ListType,
            sprite.ListTypeName,
            sprite.HeaderValue,
            sprite.HeaderValueHex,
            DreamcastPvrTaSpriteHeaderPayloadSummary.FromPayload(sprite.HeaderPayload),
            sprite.ControlValue,
            sprite.ControlValueHex,
            sprite.EndOfStrip,
            sprite.Rgb565,
            sprite.Rgb565Hex,
            sprite.HasFinitePreviewCoordinates,
            sprite.HasRenderablePreviewArea,
            sprite.Vertices.Count,
            sprite.Vertices.Select(DreamcastPvrTaSpriteVertexSummary.FromVertex).ToArray());
}

public sealed record DreamcastPvrTaCommandKindSummary(string Kind, int Count);

internal sealed record PvrTaListKey(string Region, int? ListType, string? ListTypeName);

public sealed record DreamcastAudioSummary(
    int AudioRamBytes,
    ulong NonZeroBytes,
    uint Fnv1A32,
    string Fnv1A32Hex,
    IReadOnlyList<DreamcastAicaRegisterValueSummary> Registers,
    int RegisterAccessCount,
    IReadOnlyList<DreamcastAicaRegisterAccessSummary> RecentRegisterAccesses,
    IReadOnlyList<DreamcastAicaChannelSummary> Channels,
    int ActiveChannelCount)
{
    public static DreamcastAudioSummary FromSnapshot(DreamcastAudioSnapshot snapshot, int recentCount = 16) =>
        new(
            snapshot.AudioRamBytes,
            snapshot.NonZeroBytes,
            snapshot.Fnv1A32,
            snapshot.Fnv1A32Hex,
            snapshot.Registers.Select(DreamcastAicaRegisterValueSummary.FromRegister).ToArray(),
            snapshot.RegisterAccesses.Count,
            snapshot.RegisterAccesses.TakeLast(Math.Max(0, recentCount)).Select(DreamcastAicaRegisterAccessSummary.FromAccess).ToArray(),
            snapshot.Channels.Select(DreamcastAicaChannelSummary.FromChannel).ToArray(),
            snapshot.Channels.Count(channel => channel.Active));
}

public sealed record DreamcastAicaRegisterValueSummary(
    uint Offset,
    string OffsetHex,
    string Name,
    int? Channel,
    uint Value,
    string ValueHex)
{
    public static DreamcastAicaRegisterValueSummary FromRegister(DreamcastAicaRegisterValue register) =>
        new(register.Offset, register.OffsetHex, register.Name, register.Channel, register.Value, register.ValueHex);
}

public sealed record DreamcastAicaRegisterAccessSummary(
    MemoryAccessKind Kind,
    uint Address,
    string AddressHex,
    uint Offset,
    string OffsetHex,
    string Name,
    int? Channel,
    int Size,
    uint Value,
    string ValueHex)
{
    public static DreamcastAicaRegisterAccessSummary FromAccess(DreamcastAicaRegisterAccess access) =>
        new(access.Kind, access.Address, access.AddressHex, access.Offset, access.OffsetHex, access.Name, access.Channel, access.Size, access.Value, access.ValueHex);
}

public sealed record DreamcastAicaChannelSummary(
    int Channel,
    uint Control,
    string ControlHex,
    string SampleFormat,
    bool Compressed,
    bool Streamed,
    bool LoopEnabled,
    uint SampleAddress,
    string SampleAddressHex,
    uint SampleAddressLow,
    string SampleAddressLowHex,
    uint LoopStart,
    string LoopStartHex,
    uint LoopEnd,
    string LoopEndHex,
    uint Pitch,
    string PitchHex,
    byte Pan,
    byte PanSendLevel,
    byte PanPosition,
    byte LeftBalance,
    byte RightBalance,
    byte Volume,
    bool Active,
    bool KeyOn,
    bool KeyOnExecute,
    int SampleStrideBytes,
    ulong PlaybackPosition,
    string PlaybackPositionHex,
    ulong PlaybackBytePosition,
    string PlaybackBytePositionHex,
    ulong PlaybackSamplesAdvanced,
    ulong PlaybackBytesAdvanced,
    bool PlaybackStoppedAtLoopEnd)
{
    public static DreamcastAicaChannelSummary FromChannel(DreamcastAicaChannelSnapshot channel) =>
        new(
            channel.Channel,
            channel.Control,
            channel.ControlHex,
            channel.SampleFormat,
            channel.Compressed,
            channel.Streamed,
            channel.LoopEnabled,
            channel.SampleAddress,
            channel.SampleAddressHex,
            channel.SampleAddressLow,
            channel.SampleAddressLowHex,
            channel.LoopStart,
            channel.LoopStartHex,
            channel.LoopEnd,
            channel.LoopEndHex,
            channel.Pitch,
            channel.PitchHex,
            channel.Pan,
            channel.PanSendLevel,
            channel.PanPosition,
            channel.LeftBalance,
            channel.RightBalance,
            channel.Volume,
            channel.Active,
            channel.KeyOn,
            channel.KeyOnExecute,
            channel.SampleStrideBytes,
            channel.PlaybackPosition,
            channel.PlaybackPositionHex,
            channel.PlaybackBytePosition,
            channel.PlaybackBytePositionHex,
            channel.PlaybackSamplesAdvanced,
            channel.PlaybackBytesAdvanced,
            channel.PlaybackStoppedAtLoopEnd);
}

public sealed record DreamcastMapleSummary(
    int TransferCount,
    int DeviceInfoCount,
    int GetConditionCount,
    int DmaBatchCount,
    int DescriptorLimitHitCount,
    IReadOnlyList<DreamcastMapleDmaBatchSummary> RecentDmaBatches,
    IReadOnlyList<DreamcastMapleDmaTransferSummary> RecentTransfers)
{
    public DreamcastMapleSummary(
        int transferCount,
        int deviceInfoCount,
        int getConditionCount,
        IReadOnlyList<DreamcastMapleDmaTransferSummary> recentTransfers)
        : this(transferCount, deviceInfoCount, getConditionCount, 0, 0, [], recentTransfers)
    {
    }

    public static DreamcastMapleSummary FromSnapshot(DreamcastMapleSnapshot snapshot, int recentCount = 16) =>
        new(
            snapshot.Transfers.Count,
            snapshot.Transfers.Count(transfer => transfer.CommandName == "DeviceInfo"),
            snapshot.Transfers.Count(transfer => transfer.CommandName == "GetCondition"),
            snapshot.DmaBatches.Count,
            snapshot.DmaBatches.Count(batch => batch.HitDescriptorLimit),
            snapshot.DmaBatches.TakeLast(Math.Max(0, recentCount)).Select(DreamcastMapleDmaBatchSummary.FromBatch).ToArray(),
            snapshot.Transfers.TakeLast(Math.Max(0, recentCount)).Select(DreamcastMapleDmaTransferSummary.FromTransfer).ToArray());
}

public sealed record DreamcastMapleDmaBatchSummary(
    uint DescriptorAddress,
    string DescriptorAddressHex,
    int DescriptorsScanned,
    int TransferCount,
    bool Completed,
    bool HitDescriptorLimit,
    uint LastDescriptorAddress,
    string LastDescriptorAddressHex)
{
    public static DreamcastMapleDmaBatchSummary FromBatch(DreamcastMapleDmaBatch batch) =>
        new(
            batch.DescriptorAddress,
            batch.DescriptorAddressHex,
            batch.DescriptorsScanned,
            batch.TransferCount,
            batch.Completed,
            batch.HitDescriptorLimit,
            batch.LastDescriptorAddress,
            batch.LastDescriptorAddressHex);
}

public sealed record DreamcastMapleDmaTransferSummary(
    uint DescriptorAddress,
    string DescriptorAddressHex,
    uint Header,
    string HeaderHex,
    uint ReceiveBufferAddress,
    string ReceiveBufferAddressHex,
    byte Command,
    string CommandName,
    byte Destination,
    string DestinationHex,
    string DestinationName,
    byte Response,
    string ResponseName,
    int ResponseBytes,
    DreamcastControllerSummary? ControllerState)
{
    public static DreamcastMapleDmaTransferSummary FromTransfer(DreamcastMapleDmaTransfer transfer) =>
        new(
            transfer.DescriptorAddress,
            transfer.DescriptorAddressHex,
            transfer.Header,
            transfer.HeaderHex,
            transfer.ReceiveBufferAddress,
            transfer.ReceiveBufferAddressHex,
            transfer.Command,
            transfer.CommandName,
            transfer.Destination,
            transfer.DestinationHex,
            transfer.DestinationName,
            transfer.Response,
            transfer.ResponseName,
            transfer.ResponseBytes,
            transfer.ControllerState is { } state ? DreamcastControllerSummary.FromState(state) : null);
}

public sealed record DreamcastGdromSummary(
    bool HasMedia,
    int? SectorSize,
    ulong? SectorCount,
    uint? LeadoutFad,
    string? LeadoutFadHex,
    IReadOnlyList<DreamcastMediaTrackSummary> MediaTracks,
    int ReadCommandCount,
    int SuccessfulReadCommandCount,
    int FailedReadCommandCount,
    int BytesRead,
    int TocCommandCount,
    int SuccessfulTocCommandCount,
    int FailedTocCommandCount,
    int StatusCommandCount,
    int SuccessfulStatusCommandCount,
    int FailedStatusCommandCount,
    int SectorModeCommandCount,
    int SuccessfulSectorModeCommandCount,
    int FailedSectorModeCommandCount,
    IReadOnlyList<DreamcastGdromReadCommandSummary> RecentReadCommands,
    IReadOnlyList<DreamcastGdromTocCommandSummary> RecentTocCommands,
    IReadOnlyList<DreamcastGdromStatusCommandSummary> RecentStatusCommands,
    IReadOnlyList<DreamcastGdromSectorModeCommandSummary> RecentSectorModeCommands,
    IReadOnlyList<DreamcastGdromCommandActivitySummary> RecentCommandActivities)
{
    public static DreamcastGdromSummary FromSnapshot(DreamcastGdromSnapshot snapshot, int recentCount = 16) =>
        new(
            snapshot.HasMedia,
            snapshot.SectorSize,
            snapshot.SectorCount,
            snapshot.LeadoutFad,
            snapshot.LeadoutFadHex,
            snapshot.MediaTracks.Select(DreamcastMediaTrackSummary.FromTrack).ToArray(),
            snapshot.ReadCommands.Count,
            snapshot.ReadCommands.Count(command => command.Success),
            snapshot.ReadCommands.Count(command => !command.Success),
            snapshot.ReadCommands.Sum(command => command.BytesRead),
            snapshot.TocCommands.Count,
            snapshot.TocCommands.Count(command => command.Success),
            snapshot.TocCommands.Count(command => !command.Success),
            snapshot.StatusCommands.Count,
            snapshot.StatusCommands.Count(command => command.Success),
            snapshot.StatusCommands.Count(command => !command.Success),
            snapshot.SectorModeCommands.Count,
            snapshot.SectorModeCommands.Count(command => command.Success),
            snapshot.SectorModeCommands.Count(command => !command.Success),
            snapshot.ReadCommands.TakeLast(Math.Max(0, recentCount)).Select(DreamcastGdromReadCommandSummary.FromCommand).ToArray(),
            snapshot.TocCommands.TakeLast(Math.Max(0, recentCount)).Select(DreamcastGdromTocCommandSummary.FromCommand).ToArray(),
            snapshot.StatusCommands.TakeLast(Math.Max(0, recentCount)).Select(DreamcastGdromStatusCommandSummary.FromCommand).ToArray(),
            snapshot.SectorModeCommands.TakeLast(Math.Max(0, recentCount)).Select(DreamcastGdromSectorModeCommandSummary.FromCommand).ToArray(),
            snapshot.CommandActivities.TakeLast(Math.Max(0, recentCount)).Select(DreamcastGdromCommandActivitySummary.FromActivity).ToArray());
}

public sealed record DreamcastMediaTrackSummary(
    int TrackNumber,
    uint StartFad,
    string StartFadHex,
    int Control,
    ulong SectorCount)
{
    public static DreamcastMediaTrackSummary FromTrack(DreamcastMediaTrackInfo track) =>
        new(track.TrackNumber, track.StartFad, track.StartFadHex, track.Control, track.SectorCount);
}

public sealed record DreamcastGdromReadCommandSummary(
    uint ParameterAddress,
    string ParameterAddressHex,
    uint? Sector,
    string? SectorHex,
    uint? Destination,
    string? DestinationHex,
    uint? SectorCount,
    int? SectorSize,
    int BytesRequested,
    int BytesRead,
    bool Success,
    string Status)
{
    public static DreamcastGdromReadCommandSummary FromCommand(DreamcastGdromReadCommand command) =>
        new(
            command.ParameterAddress,
            command.ParameterAddressHex,
            command.Sector,
            command.SectorHex,
            command.Destination,
            command.DestinationHex,
            command.SectorCount,
            command.SectorSize,
            command.BytesRequested,
            command.BytesRead,
            command.Success,
            command.Status);
}

public sealed record DreamcastGdromTocCommandSummary(
    uint ParameterAddress,
    string ParameterAddressHex,
    uint? BufferAddress,
    string? BufferAddressHex,
    int? FirstTrack,
    int? LastTrack,
    uint? DataTrackStartFad,
    string? DataTrackStartFadHex,
    uint? LeadoutFad,
    string? LeadoutFadHex,
    bool Success,
    string Status)
{
    public static DreamcastGdromTocCommandSummary FromCommand(DreamcastGdromTocCommand command) =>
        new(
            command.ParameterAddress,
            command.ParameterAddressHex,
            command.BufferAddress,
            command.BufferAddressHex,
            command.FirstTrack,
            command.LastTrack,
            command.DataTrackStartFad,
            command.DataTrackStartFadHex,
            command.LeadoutFad,
            command.LeadoutFadHex,
            command.Success,
            command.Status);
}

public sealed record DreamcastGdromStatusCommandSummary(
    uint BufferAddress,
    string BufferAddressHex,
    int StatusCode,
    string StatusName,
    int DiscType,
    string DiscTypeName,
    bool Success,
    string Status)
{
    public static DreamcastGdromStatusCommandSummary FromCommand(DreamcastGdromStatusCommand command) =>
        new(
            command.BufferAddress,
            command.BufferAddressHex,
            command.StatusCode,
            command.StatusName,
            command.DiscType,
            command.DiscTypeName,
            command.Success,
            command.Status);
}

public sealed record DreamcastGdromSectorModeCommandSummary(
    uint ParameterAddress,
    string ParameterAddressHex,
    int Request,
    string RequestName,
    int SectorPart,
    string SectorPartHex,
    int CdXa,
    int SectorSize,
    bool Success,
    string Status)
{
    public static DreamcastGdromSectorModeCommandSummary FromCommand(DreamcastGdromSectorModeCommand command) =>
        new(
            command.ParameterAddress,
            command.ParameterAddressHex,
            command.Request,
            command.RequestName,
            command.SectorPart,
            command.SectorPartHex,
            command.CdXa,
            command.SectorSize,
            command.Success,
            command.Status);
}

public sealed record DreamcastGdromCommandActivitySummary(
    string Operation,
    uint? CommandId,
    uint? Command,
    string? CommandHex,
    string? CommandName,
    uint? ParameterAddress,
    string? ParameterAddressHex,
    uint? StatusAddress,
    string? StatusAddressHex,
    int? Response,
    string? ResponseName,
    int? Status0,
    int? Status1,
    int? TransferredBytes,
    int? AtaStatus,
    string Status)
{
    public static DreamcastGdromCommandActivitySummary FromActivity(DreamcastGdromCommandActivity activity) =>
        new(
            activity.Operation,
            activity.CommandId,
            activity.Command,
            activity.CommandHex,
            activity.CommandName,
            activity.ParameterAddress,
            activity.ParameterAddressHex,
            activity.StatusAddress,
            activity.StatusAddressHex,
            activity.Response,
            activity.ResponseName,
            activity.Status0,
            activity.Status1,
            activity.TransferredBytes,
            activity.AtaStatus,
            activity.Status);
}

public sealed record DreamcastSchedulerSummary(
    ulong VBlankInterval,
    ulong NextVBlankInstruction,
    ulong VBlankEventsRaised,
    ulong HardwareAdvanceTicks,
    ulong HardwareAdvanceBatches,
    ulong MaxHardwareAdvanceBatch,
    ulong IdleAdvanceTicks,
    ulong IdleAdvanceBatches,
    ulong MaxIdleAdvanceBatch,
    ulong IdleTimerWakeCount,
    ulong IdleVBlankWakeCount,
    ulong IdleInputWakeCount,
    ulong CpuFastForwardInstructions,
    ulong CpuFastForwardBatches,
    ulong MaxCpuFastForwardBatch,
    ulong ControllerScriptChanges)
{
    public static DreamcastSchedulerSummary FromSnapshot(DreamcastSchedulerSnapshot snapshot) =>
        new(
            snapshot.VBlankInterval,
            snapshot.NextVBlankInstruction,
            snapshot.VBlankEventsRaised,
            snapshot.HardwareAdvanceTicks,
            snapshot.HardwareAdvanceBatches,
            snapshot.MaxHardwareAdvanceBatch,
            snapshot.IdleAdvanceTicks,
            snapshot.IdleAdvanceBatches,
            snapshot.MaxIdleAdvanceBatch,
            snapshot.IdleTimerWakeCount,
            snapshot.IdleVBlankWakeCount,
            snapshot.IdleInputWakeCount,
            snapshot.CpuFastForwardInstructions,
            snapshot.CpuFastForwardBatches,
            snapshot.MaxCpuFastForwardBatch,
            snapshot.ControllerScriptChanges);
}
