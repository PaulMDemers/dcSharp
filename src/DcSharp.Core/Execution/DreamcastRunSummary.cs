using DcSharp.Core.Cpu;
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
    IReadOnlyList<DreamcastMemoryAccessSummary> RecentDeviceAccesses,
    int SerialBytes,
    string SerialText,
    IReadOnlyList<DreamcastTraceSummary> TraceTail,
    DreamcastControllerSummary ControllerA,
    DreamcastVideoSummary Video,
    DreamcastAudioSummary Audio,
    DreamcastSchedulerSummary Scheduler)
{
    public static DreamcastRunSummary FromResult(DreamcastRunResult result, DreamcastRunOptions? options = null, int recentDeviceAccessCount = 16)
    {
        ArgumentNullException.ThrowIfNull(result);
        var controllerA = options?.ControllerAScript?.StateAt(result.Cpu.InstructionsExecuted)
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
                .TakeLast(Math.Max(0, recentDeviceAccessCount))
                .Select(DreamcastMemoryAccessSummary.FromAccess)
                .ToArray(),
            result.SerialOutput.Count,
            Encoding.ASCII.GetString(result.SerialOutput.ToArray()),
            result.TraceTail.Select(step => DreamcastTraceSummary.FromStep(step, result.Load.FindNearestSymbol(step.Pc))).ToArray(),
            DreamcastControllerSummary.FromState(controllerA),
            DreamcastVideoSummary.FromSnapshot(result.Video),
            DreamcastAudioSummary.FromSnapshot(result.Audio),
            DreamcastSchedulerSummary.FromSnapshot(result.Scheduler));
    }

    private static string Hex32(uint value) => $"0x{value:X8}";
    private static string Hex16(ushort value) => $"0x{value:X4}";
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

public sealed record DreamcastVideoSummary(
    int VramBytes,
    ulong NonZeroBytes,
    uint Fnv1A32,
    string Fnv1A32Hex,
    uint? FirstNonZeroOffset,
    string? FirstNonZeroOffsetHex,
    IReadOnlyList<DreamcastVideoSampleSummary> Samples,
    int PvrRegisterAccessCount,
    IReadOnlyList<DreamcastPvrRegisterAccessSummary> RecentPvrRegisterAccesses,
    int PvrTaCommandWriteCount,
    IReadOnlyList<DreamcastPvrTaCommandWriteSummary> RecentPvrTaCommandWrites,
    IReadOnlyList<DreamcastPvrTaCommandKindSummary> PvrTaCommandKinds)
{
    public static DreamcastVideoSummary FromSnapshot(DreamcastVideoSnapshot snapshot, int recentCount = 16) =>
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
            snapshot.PvrRegisterAccesses.Count,
            snapshot.PvrRegisterAccesses.TakeLast(Math.Max(0, recentCount)).Select(DreamcastPvrRegisterAccessSummary.FromAccess).ToArray(),
            snapshot.PvrTaCommandWrites.Count,
            snapshot.PvrTaCommandWrites.TakeLast(Math.Max(0, recentCount)).Select(DreamcastPvrTaCommandWriteSummary.FromWrite).ToArray(),
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

public sealed record DreamcastPvrTaCommandKindSummary(string Kind, int Count);

public sealed record DreamcastAudioSummary(
    int AudioRamBytes,
    ulong NonZeroBytes,
    uint Fnv1A32,
    string Fnv1A32Hex,
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
            snapshot.RegisterAccesses.Count,
            snapshot.RegisterAccesses.TakeLast(Math.Max(0, recentCount)).Select(DreamcastAicaRegisterAccessSummary.FromAccess).ToArray(),
            snapshot.Channels.Select(DreamcastAicaChannelSummary.FromChannel).ToArray(),
            snapshot.Channels.Count(channel => channel.Active));
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

public sealed record DreamcastSchedulerSummary(
    ulong VBlankInterval,
    ulong NextVBlankInstruction,
    ulong VBlankEventsRaised,
    ulong HardwareAdvanceTicks,
    ulong HardwareAdvanceBatches,
    ulong MaxHardwareAdvanceBatch,
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
            snapshot.ControllerScriptChanges);
}
