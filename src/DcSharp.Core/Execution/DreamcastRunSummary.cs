using DcSharp.Core.Cpu;
using DcSharp.Core.Dreamcast.Asic;
using DcSharp.Core.Dreamcast.Audio;
using DcSharp.Core.Dreamcast.Input;
using DcSharp.Core.Dreamcast.Memory;
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
    DreamcastSchedulerSummary Scheduler)
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
            DreamcastSchedulerSummary.FromSnapshot(result.Scheduler));
    }

    private static string Hex32(uint value) => $"0x{value:X8}";
    private static string Hex16(ushort value) => $"0x{value:X4}";
    private static DreamcastControllerScript? ControllerScriptFromMap(DreamcastRunOptions? options, byte address) =>
        options?.ControllerScripts?.GetValueOrDefault(address);

    private static DreamcastControllerState? ControllerFromMap(DreamcastRunOptions? options, byte address) =>
        options?.Controllers?.GetValueOrDefault(address);
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
    string ValueHex)
{
    public static DreamcastMemoryAccessSummary FromAccess(MemoryAccess access) =>
        new(access.Kind, DreamcastDeviceDomainClassifier.Classify(access), access.Address, $"0x{access.Address:X8}", access.Size, access.Value, $"0x{access.Value:X8}");
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
    IReadOnlyList<DreamcastPvrTaParameterHeaderSummary> RecentPvrTaParameterHeaders,
    IReadOnlyList<DreamcastPvrTaListSummary> PvrTaLists,
    IReadOnlyList<DreamcastPvrTaStripSummary> PvrTaStrips,
    IReadOnlyList<DreamcastPvrTaCommandKindSummary> PvrTaCommandKinds)
{
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
    string ValueHex)
{
    public static DreamcastPvrTaCommandWriteSummary FromWrite(DreamcastPvrTaCommandWrite write) =>
        new(write.Address, write.AddressHex, write.Region, write.Kind, write.ListType, write.ListTypeName, write.EndOfStrip, write.Size, write.Value, write.ValueHex);
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
    int? PayloadWordsRemaining)
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
            write.PayloadWordsRemaining);
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
    bool HasKnownPayloadLength)
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
            header.HasKnownPayloadLength);
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
            strip.Rgb565,
            strip.Rgb565Hex,
            strip.Vertices.Count,
            strip.Vertices.Select(DreamcastPvrTaVertexSummary.FromVertex).ToArray());
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
    byte Volume,
    bool Active,
    bool KeyOn,
    bool KeyOnExecute)
{
    public static DreamcastAicaChannelSummary FromChannel(DreamcastAicaChannelSnapshot channel) =>
        new(
            channel.Channel,
            channel.Control,
            channel.ControlHex,
            channel.SampleFormat,
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
            channel.Volume,
            channel.Active,
            channel.KeyOn,
            channel.KeyOnExecute);
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
