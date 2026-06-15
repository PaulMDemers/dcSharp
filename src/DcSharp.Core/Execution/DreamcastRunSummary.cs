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
    int PvrDmaTransferCount,
    IReadOnlyList<DreamcastPvrDmaTransferSummary> RecentPvrDmaTransfers,
    int StoreQueueFlushCount,
    IReadOnlyList<DreamcastStoreQueueFlushSummary> RecentStoreQueueFlushes,
    int PvrTaCommandWriteCount,
    IReadOnlyList<DreamcastPvrTaCommandWriteSummary> RecentPvrTaCommandWrites,
    IReadOnlyList<DreamcastPvrTaStreamWriteSummary> RecentPvrTaStreamWrites,
    IReadOnlyList<DreamcastPvrTaPolygonHeaderPayloadSummary> PvrTaPolygonHeaderPayloads,
    IReadOnlyList<DreamcastPvrTaRealVertexPayloadSummary> PvrTaRealVertexPayloads,
    IReadOnlyList<DreamcastPvrTaParameterHeaderSummary> RecentPvrTaParameterHeaders,
    IReadOnlyList<DreamcastPvrTaListSummary> PvrTaLists,
    IReadOnlyList<DreamcastPvrTaStripSummary> PvrTaStrips,
    IReadOnlyList<DreamcastPvrTaSpriteSummary> PvrTaSprites,
    IReadOnlyList<DreamcastPvrTaCommandKindSummary> PvrTaCommandKinds,
    DreamcastPvrTaAssemblyDiagnosticsSummary PvrTaAssemblyDiagnostics,
    DreamcastPvrPreviewRenderStatsSummary PvrPreviewRenderStats)
{
    public int PvrTaRenderableSpriteCount => PvrTaSprites.Count(sprite => sprite.HasRenderablePreviewArea);

    public int PvrTaDegenerateSpriteCount => PvrTaSprites.Count(sprite => sprite.HasFinitePreviewCoordinates && !sprite.HasRenderablePreviewArea);

    public int PvrTaNonfiniteSpriteCount => PvrTaSprites.Count(sprite => !sprite.HasFinitePreviewCoordinates);

    public IReadOnlyList<DreamcastPvrTaSpriteSourceGroupSummary> PvrTaSpriteSourceGroups =>
        DreamcastPvrTaSpriteSourceGroupSummary.FromSprites(PvrTaSprites);

    public IReadOnlyList<DreamcastPvrTaSpriteShapeGroupSummary> PvrTaSpriteShapeGroups =>
        DreamcastPvrTaSpriteShapeGroupSummary.FromSprites(PvrTaSprites);

    public DreamcastPvrTaDiagnosticsSummary PvrTaDiagnostics =>
        DreamcastPvrTaDiagnosticsSummary.FromVideo(this);

    public DreamcastPvrDisplaySummary PvrDisplay =>
        DreamcastPvrDisplaySummary.FromRegisters(PvrRegisters);

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
            snapshot.PvrDmaTransfers.Count,
            snapshot.PvrDmaTransfers.TakeLast(Math.Max(0, recentCount)).Select(DreamcastPvrDmaTransferSummary.FromTransfer).ToArray(),
            snapshot.StoreQueueFlushes.Count,
            snapshot.StoreQueueFlushes.TakeLast(Math.Max(0, recentCount)).Select(DreamcastStoreQueueFlushSummary.FromFlush).ToArray(),
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
                .ToArray(),
            DreamcastPvrTaAssemblyDiagnosticsSummary.FromDiagnostics(snapshot.PvrTaAssemblyDiagnostics),
            DreamcastPvrPreviewRenderStatsSummary.FromStats(snapshot.PvrPreviewRenderStats));

    internal static string GetPvrTaSpritePreviewStatus(DreamcastPvrTaSpriteSummary sprite) =>
        sprite.HasRenderablePreviewArea
            ? "renderable"
            : sprite.HasFinitePreviewCoordinates ? "degenerate" : "nonfinite";
}

public sealed record DreamcastPvrTaDiagnosticsSummary(
    int PreviewWidth,
    ulong FramebufferNonZeroBytes,
    string FramebufferChecksumHex,
    string? FirstNonZeroOffsetHex,
    int StripCount,
    int StripTriangleCount,
    int SpriteCount,
    int RenderableSpriteCount,
    int DegenerateSpriteCount,
    int NonfiniteSpriteCount,
    int DroppedShortStripCount,
    int DroppedZeroColorPrimitiveCount,
    int DroppedMixedFlatColorStripCount,
    DreamcastPvrPreviewRenderStatsSummary PreviewRenderStats,
    DreamcastPvrTaBoundsSummary StripBounds,
    DreamcastPvrTaBoundsSummary SpriteBounds,
    DreamcastPvrTaBoundsSummary CombinedBounds,
    IReadOnlyList<DreamcastPvrTaTextureModeGroupSummary> TextureModes)
{
    public static DreamcastPvrTaDiagnosticsSummary FromVideo(DreamcastVideoSummary video)
    {
        const int previewWidth = 640;
        var stripBounds = DreamcastPvrTaBoundsSummary.FromGroups(
            video.PvrTaStrips.Select(strip => strip.Vertices.Select(vertex => ((float)vertex.X, (float)vertex.Y))));
        var spriteBounds = DreamcastPvrTaBoundsSummary.FromGroups(
            video.PvrTaSprites
                .Where(sprite => sprite.HasFinitePreviewCoordinates)
                .Select(sprite => sprite.Vertices.Take(4).Select(vertex => (vertex.RawX, vertex.RawY))));
        var combinedBounds = DreamcastPvrTaBoundsSummary.Combine(stripBounds, spriteBounds);

        return new(
            previewWidth,
            video.NonZeroBytes,
            video.Fnv1A32Hex,
            video.FirstNonZeroOffsetHex,
            video.PvrTaStrips.Count,
            video.PvrTaStrips.Sum(strip => Math.Max(0, strip.VertexCount - 2)),
            video.PvrTaSprites.Count,
            video.PvrTaRenderableSpriteCount,
            video.PvrTaDegenerateSpriteCount,
            video.PvrTaNonfiniteSpriteCount,
            video.PvrTaAssemblyDiagnostics.DroppedShortStripCount,
            video.PvrTaAssemblyDiagnostics.DroppedZeroColorPrimitiveCount,
            video.PvrTaAssemblyDiagnostics.DroppedMixedFlatColorStripCount,
            video.PvrPreviewRenderStats,
            stripBounds,
            spriteBounds,
            combinedBounds,
            DreamcastPvrTaTextureModeGroupSummary.FromVideo(video));
    }
}

public sealed record DreamcastPvrPreviewRenderStatsSummary(
    int SpriteCalls,
    int PixelWriteAttempts,
    int PixelsWritten,
    int UniquePixelsWritten,
    int ZeroRgbWritePixels,
    int AlphaBlendedPixels,
    int PunchThroughRejectedPixels,
    int SubpixelFallbacks,
    int OutOfBoundsWritePixels,
    int TextureSampledPixels = 0,
    int ZeroAlphaTexturePixels = 0)
{
    public static DreamcastPvrPreviewRenderStatsSummary FromStats(DreamcastPvrPreviewRenderStats stats) =>
        new(
            stats.SpriteCalls,
            stats.PixelWriteAttempts,
            stats.PixelsWritten,
            stats.UniquePixelsWritten,
            stats.ZeroRgbWritePixels,
            stats.AlphaBlendedPixels,
            stats.PunchThroughRejectedPixels,
            stats.SubpixelFallbacks,
            stats.OutOfBoundsWritePixels,
            stats.TextureSampledPixels,
            stats.ZeroAlphaTexturePixels);
}

public sealed record DreamcastPvrTaAssemblyDiagnosticsSummary(
    int DroppedShortStripCount,
    int DroppedZeroColorPrimitiveCount,
    int DroppedMixedFlatColorStripCount)
{
    public static DreamcastPvrTaAssemblyDiagnosticsSummary FromDiagnostics(DreamcastPvrTaAssemblyDiagnostics diagnostics) =>
        new(
            diagnostics.DroppedShortStripCount,
            diagnostics.DroppedZeroColorPrimitiveCount,
            diagnostics.DroppedMixedFlatColorStripCount);
}

public sealed record DreamcastPvrTaBoundsSummary(
    bool HasBounds,
    int SourceCount,
    float? MinX,
    float? MinY,
    float? MaxX,
    float? MaxY,
    int NegativeXCount,
    int RightClippedCount,
    int NegativeYCount,
    int ZeroWidthCount,
    int ZeroHeightCount)
{
    public static DreamcastPvrTaBoundsSummary Empty { get; } = new(false, 0, null, null, null, null, 0, 0, 0, 0, 0);

    public static DreamcastPvrTaBoundsSummary FromGroups(IEnumerable<IEnumerable<(float X, float Y)>> groups)
    {
        const int previewWidth = 640;
        var sourceCount = 0;
        float? minX = null;
        float? minY = null;
        float? maxX = null;
        float? maxY = null;
        var negativeXCount = 0;
        var rightClippedCount = 0;
        var negativeYCount = 0;
        var zeroWidthCount = 0;
        var zeroHeightCount = 0;

        foreach (var group in groups)
        {
            var points = group.ToArray();
            if (points.Length == 0)
            {
                continue;
            }

            sourceCount++;
            var groupMinX = points.Min(point => point.X);
            var groupMinY = points.Min(point => point.Y);
            var groupMaxX = points.Max(point => point.X);
            var groupMaxY = points.Max(point => point.Y);

            minX = minX is null ? groupMinX : Math.Min(minX.Value, groupMinX);
            minY = minY is null ? groupMinY : Math.Min(minY.Value, groupMinY);
            maxX = maxX is null ? groupMaxX : Math.Max(maxX.Value, groupMaxX);
            maxY = maxY is null ? groupMaxY : Math.Max(maxY.Value, groupMaxY);

            if (groupMinX < 0)
            {
                negativeXCount++;
            }

            if (groupMaxX >= previewWidth)
            {
                rightClippedCount++;
            }

            if (groupMinY < 0)
            {
                negativeYCount++;
            }

            if (groupMinX == groupMaxX)
            {
                zeroWidthCount++;
            }

            if (groupMinY == groupMaxY)
            {
                zeroHeightCount++;
            }
        }

        return sourceCount == 0
            ? Empty
            : new(true, sourceCount, minX, minY, maxX, maxY, negativeXCount, rightClippedCount, negativeYCount, zeroWidthCount, zeroHeightCount);
    }

    public static DreamcastPvrTaBoundsSummary Combine(params DreamcastPvrTaBoundsSummary[] bounds)
    {
        var populated = bounds.Where(bound => bound.HasBounds).ToArray();
        if (populated.Length == 0)
        {
            return Empty;
        }

        return new(
            true,
            populated.Sum(bound => bound.SourceCount),
            populated.Min(bound => bound.MinX),
            populated.Min(bound => bound.MinY),
            populated.Max(bound => bound.MaxX),
            populated.Max(bound => bound.MaxY),
            populated.Sum(bound => bound.NegativeXCount),
            populated.Sum(bound => bound.RightClippedCount),
            populated.Sum(bound => bound.NegativeYCount),
            populated.Sum(bound => bound.ZeroWidthCount),
            populated.Sum(bound => bound.ZeroHeightCount));
    }
}

public sealed record DreamcastPvrTaTextureModeGroupSummary(
    string PrimitiveKind,
    string? ListTypeName,
    bool TextureEnabled,
    bool VqEnabled,
    bool MipMapEnabled,
    bool NonTwiddled,
    string PixelFormatName,
    int Count)
{
    public static IReadOnlyList<DreamcastPvrTaTextureModeGroupSummary> FromVideo(DreamcastVideoSummary video) =>
        video.PvrTaStrips
            .Select(strip => FromStrip(strip))
            .Concat(video.PvrTaSprites.Select(FromSprite))
            .GroupBy(mode => new
            {
                mode.PrimitiveKind,
                mode.ListTypeName,
                mode.TextureEnabled,
                mode.VqEnabled,
                mode.MipMapEnabled,
                mode.NonTwiddled,
                mode.PixelFormatName
            })
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.PrimitiveKind, StringComparer.Ordinal)
            .ThenBy(group => group.Key.ListTypeName ?? string.Empty, StringComparer.Ordinal)
            .Select(group => new DreamcastPvrTaTextureModeGroupSummary(
                group.Key.PrimitiveKind,
                group.Key.ListTypeName,
                group.Key.TextureEnabled,
                group.Key.VqEnabled,
                group.Key.MipMapEnabled,
                group.Key.NonTwiddled,
                group.Key.PixelFormatName,
                group.Count()))
            .ToArray();

    private static DreamcastPvrTaTextureModeGroupSummary FromStrip(DreamcastPvrTaStripSummary strip) =>
        strip.HeaderPayload is { } payload
            ? FromPayload("strip", strip.ListTypeName, payload.Mode1Fields.TextureEnabled, payload.Mode3Fields)
            : new("strip", strip.ListTypeName, false, false, false, false, "none", 1);

    private static DreamcastPvrTaTextureModeGroupSummary FromSprite(DreamcastPvrTaSpriteSummary sprite) =>
        FromPayload("sprite", sprite.ListTypeName, sprite.HeaderPayload.EffectiveTextureEnabled, sprite.HeaderPayload.Mode3Fields);

    private static DreamcastPvrTaTextureModeGroupSummary FromPayload(
        string primitiveKind,
        string? listTypeName,
        bool textureEnabled,
        DreamcastPvrTaPolygonHeaderMode3 mode3) =>
        new(
            primitiveKind,
            listTypeName,
            textureEnabled,
            mode3.VqEnabled,
            mode3.MipMapEnabled,
            mode3.NonTwiddled,
            mode3.PixelFormatName,
            1);
}

public sealed record DreamcastPvrTaSpriteSourceGroupSummary(
    string PreviewStatus,
    int Count,
    uint? HeaderInstructionPc,
    string? HeaderInstructionPcHex,
    uint? ControlInstructionPc,
    string? ControlInstructionPcHex,
    uint? FirstPayloadInstructionPc,
    string? FirstPayloadInstructionPcHex,
    uint? LastPayloadInstructionPc,
    string? LastPayloadInstructionPcHex,
    string PayloadInstructionPcRangeHex)
{
    public static IReadOnlyList<DreamcastPvrTaSpriteSourceGroupSummary> FromSprites(IReadOnlyList<DreamcastPvrTaSpriteSummary> sprites) =>
        sprites
            .GroupBy(sprite => new PvrTaSpriteSourceGroupKey(
                DreamcastVideoSummary.GetPvrTaSpritePreviewStatus(sprite),
                sprite.HeaderInstructionPc,
                sprite.HeaderInstructionPcHex,
                sprite.ControlInstructionPc,
                sprite.ControlInstructionPcHex,
                sprite.FirstPayloadInstructionPc,
                sprite.FirstPayloadInstructionPcHex,
                sprite.LastPayloadInstructionPc,
                sprite.LastPayloadInstructionPcHex))
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.PreviewStatus, StringComparer.Ordinal)
            .ThenBy(group => group.Key.HeaderInstructionPcHex ?? string.Empty, StringComparer.Ordinal)
            .Select(group => new DreamcastPvrTaSpriteSourceGroupSummary(
                group.Key.PreviewStatus,
                group.Count(),
                group.Key.HeaderInstructionPc,
                group.Key.HeaderInstructionPcHex,
                group.Key.ControlInstructionPc,
                group.Key.ControlInstructionPcHex,
                group.Key.FirstPayloadInstructionPc,
                group.Key.FirstPayloadInstructionPcHex,
                group.Key.LastPayloadInstructionPc,
                group.Key.LastPayloadInstructionPcHex,
                FormatPayloadPcRange(group.Key.FirstPayloadInstructionPcHex, group.Key.LastPayloadInstructionPcHex)))
            .ToArray();

    private static string FormatPayloadPcRange(string? firstPayloadInstructionPcHex, string? lastPayloadInstructionPcHex) =>
        firstPayloadInstructionPcHex == lastPayloadInstructionPcHex
            ? firstPayloadInstructionPcHex ?? "-"
            : $"{firstPayloadInstructionPcHex ?? "-"}-{lastPayloadInstructionPcHex ?? "-"}";
}

public sealed record DreamcastPvrTaSpriteShapeGroupSummary(
    string PreviewStatus,
    string? ListTypeName,
    string Rgb565Hex,
    string ArgbHex,
    bool TextureEnabled,
    bool TexturePayload,
    bool Uv16Bit,
    string WidthBucket,
    string HeightBucket,
    float MinWidth,
    float AverageWidth,
    float MaxWidth,
    float MinHeight,
    float AverageHeight,
    float MaxHeight,
    int MinFallbackPixels,
    float AverageFallbackPixels,
    int MaxFallbackPixels,
    int Count,
    uint? HeaderInstructionPc,
    string? HeaderInstructionPcHex,
    uint? ControlInstructionPc,
    string? ControlInstructionPcHex,
    uint? FirstPayloadInstructionPc,
    string? FirstPayloadInstructionPcHex,
    uint? LastPayloadInstructionPc,
    string? LastPayloadInstructionPcHex,
    string PayloadInstructionPcRangeHex)
{
    public static IReadOnlyList<DreamcastPvrTaSpriteShapeGroupSummary> FromSprites(IReadOnlyList<DreamcastPvrTaSpriteSummary> sprites) =>
        sprites
            .GroupBy(sprite => new PvrTaSpriteShapeGroupKey(
                DreamcastVideoSummary.GetPvrTaSpritePreviewStatus(sprite),
                sprite.ListTypeName,
                sprite.Rgb565Hex,
                sprite.HeaderPayload.ArgbHex,
                sprite.HeaderPayload.Mode1Fields.TextureEnabled,
                HasSpriteTexturePayload(sprite.HeaderValue),
                HasSpritePackedUv(sprite.HeaderValue),
                SizeBucket(SpriteWidth(sprite)),
                SizeBucket(SpriteHeight(sprite)),
                sprite.HeaderInstructionPc,
                sprite.HeaderInstructionPcHex,
                sprite.ControlInstructionPc,
                sprite.ControlInstructionPcHex,
                sprite.FirstPayloadInstructionPc,
                sprite.FirstPayloadInstructionPcHex,
                sprite.LastPayloadInstructionPc,
                sprite.LastPayloadInstructionPcHex))
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.PreviewStatus, StringComparer.Ordinal)
            .ThenBy(group => group.Key.ListTypeName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(group => group.Key.WidthBucket, StringComparer.Ordinal)
            .ThenBy(group => group.Key.HeightBucket, StringComparer.Ordinal)
            .Select(group =>
            {
                var widths = group.Select(SpriteWidth).Where(float.IsFinite).ToArray();
                var heights = group.Select(SpriteHeight).Where(float.IsFinite).ToArray();
                var fallbackPixels = group.Select(EstimatedFallbackPixelCount).ToArray();
                return new DreamcastPvrTaSpriteShapeGroupSummary(
                    group.Key.PreviewStatus,
                    group.Key.ListTypeName,
                    group.Key.Rgb565Hex,
                    group.Key.ArgbHex,
                    group.Key.TextureEnabled,
                    group.Key.TexturePayload,
                    group.Key.Uv16Bit,
                    group.Key.WidthBucket,
                    group.Key.HeightBucket,
                    MinOrNaN(widths),
                    AverageOrNaN(widths),
                    MaxOrNaN(widths),
                    MinOrNaN(heights),
                    AverageOrNaN(heights),
                    MaxOrNaN(heights),
                    fallbackPixels.Length == 0 ? 0 : fallbackPixels.Min(),
                    fallbackPixels.Length == 0 ? 0.0f : (float)fallbackPixels.Average(),
                    fallbackPixels.Length == 0 ? 0 : fallbackPixels.Max(),
                    group.Count(),
                    group.Key.HeaderInstructionPc,
                    group.Key.HeaderInstructionPcHex,
                    group.Key.ControlInstructionPc,
                    group.Key.ControlInstructionPcHex,
                    group.Key.FirstPayloadInstructionPc,
                    group.Key.FirstPayloadInstructionPcHex,
                    group.Key.LastPayloadInstructionPc,
                    group.Key.LastPayloadInstructionPcHex,
                    FormatPayloadPcRange(group.Key.FirstPayloadInstructionPcHex, group.Key.LastPayloadInstructionPcHex));
            })
            .ToArray();

    private static float SpriteWidth(DreamcastPvrTaSpriteSummary sprite) =>
        SpriteExtent(sprite, vertex => vertex.RawX);

    private static float SpriteHeight(DreamcastPvrTaSpriteSummary sprite) =>
        SpriteExtent(sprite, vertex => vertex.RawY);

    private static float SpriteExtent(DreamcastPvrTaSpriteSummary sprite, Func<DreamcastPvrTaSpriteVertexSummary, float> selector)
    {
        var vertices = sprite.Vertices.Take(4).ToArray();
        if (vertices.Length == 0 || vertices.Any(vertex => !vertex.HasFinitePosition))
        {
            return float.NaN;
        }

        return vertices.Max(selector) - vertices.Min(selector);
    }

    private static string SizeBucket(float value)
    {
        if (!float.IsFinite(value))
        {
            return "nonfinite";
        }

        if (value <= 0.0f)
        {
            return "0";
        }

        if (value < 1.0f)
        {
            return "<1";
        }

        var lower = 1;
        var upper = 2;
        while (value >= upper && upper < 1024)
        {
            lower = upper;
            upper *= 2;
        }

        return upper >= 1024 && value >= upper ? ">=1024" : $"{lower}-{upper}";
    }

    private static int EstimatedFallbackPixelCount(DreamcastPvrTaSpriteSummary sprite)
    {
        const int previewWidth = 640;
        var vertices = sprite.Vertices.Take(4).ToArray();
        if (vertices.Length == 0 || vertices.Any(vertex => !vertex.HasFinitePosition))
        {
            return 0;
        }

        var minX = vertices.Min(vertex => vertex.RawX);
        var minY = vertices.Min(vertex => vertex.RawY);
        var maxX = vertices.Max(vertex => vertex.RawX);
        var maxY = vertices.Max(vertex => vertex.RawY);
        var width = maxX - minX;
        var height = maxY - minY;
        if (!float.IsFinite(width) || !float.IsFinite(height))
        {
            return 0;
        }

        if (width < height)
        {
            var startX = Math.Clamp((int)MathF.Floor(minX), 0, previewWidth - 1);
            var endX = Math.Clamp((int)MathF.Floor(maxX), 0, previewWidth - 1);
            var startY = Math.Max((int)MathF.Floor(minY), 0);
            var endY = Math.Max((int)MathF.Ceiling(maxY), 0);
            return Math.Max(0, endX - startX + 1) * Math.Max(0, endY - startY + 1);
        }

        var fallbackStartY = Math.Max((int)MathF.Floor(minY), 0);
        var fallbackEndY = Math.Max((int)MathF.Floor(maxY), 0);
        var fallbackStartX = Math.Clamp((int)MathF.Floor(minX), 0, previewWidth - 1);
        var fallbackEndX = Math.Clamp((int)MathF.Ceiling(maxX), 0, previewWidth - 1);
        return Math.Max(0, fallbackEndX - fallbackStartX + 1) * Math.Max(0, fallbackEndY - fallbackStartY + 1);
    }

    private static float MinOrNaN(IReadOnlyList<float> values) =>
        values.Count == 0 ? float.NaN : values.Min();

    private static float AverageOrNaN(IReadOnlyList<float> values) =>
        values.Count == 0 ? float.NaN : (float)values.Average();

    private static float MaxOrNaN(IReadOnlyList<float> values) =>
        values.Count == 0 ? float.NaN : values.Max();

    private static string FormatPayloadPcRange(string? firstPayloadInstructionPcHex, string? lastPayloadInstructionPcHex) =>
        firstPayloadInstructionPcHex == lastPayloadInstructionPcHex
            ? firstPayloadInstructionPcHex ?? "-"
            : $"{firstPayloadInstructionPcHex ?? "-"}-{lastPayloadInstructionPcHex ?? "-"}";

    private static bool HasSpriteTexturePayload(uint headerValue) =>
        (headerValue & 0x0000_0008u) != 0;

    private static bool HasSpritePackedUv(uint headerValue) =>
        (headerValue & 0x0000_0001u) != 0;
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

public sealed record DreamcastPvrDisplaySummary(
    string? FramebufferAddressHex,
    string? InterlacedFramebufferAddressHex,
    string? RenderAddressHex,
    string? AlternateRenderAddressHex,
    DreamcastPvrRegisterRangeSummary? PixelClipX,
    DreamcastPvrRegisterRangeSummary? PixelClipY,
    string? FramebufferSizeHex,
    string? BitmapXHex,
    string? BitmapYHex,
    string? FramebufferConfig1Hex,
    string? FramebufferConfig2Hex,
    string? VideoConfigHex,
    string? ScalerConfigHex,
    string? PaletteConfigHex)
{
    public bool HasConfiguredState =>
        FramebufferAddressHex is not null
        || InterlacedFramebufferAddressHex is not null
        || RenderAddressHex is not null
        || AlternateRenderAddressHex is not null
        || PixelClipX is not null
        || PixelClipY is not null
        || FramebufferSizeHex is not null
        || BitmapXHex is not null
        || BitmapYHex is not null
        || FramebufferConfig1Hex is not null
        || FramebufferConfig2Hex is not null
        || VideoConfigHex is not null
        || ScalerConfigHex is not null
        || PaletteConfigHex is not null;

    public static DreamcastPvrDisplaySummary FromRegisters(IReadOnlyList<DreamcastPvrRegisterValueSummary> registers) =>
        new(
            FormatAddress(Find(registers, "PVR_FB_ADDR")),
            FormatAddress(Find(registers, "PVR_FB_IL_ADDR")),
            FormatAddress(Find(registers, "PVR_RENDER_ADDR")),
            FormatAddress(Find(registers, "PVR_RENDER_ADDR_2")),
            DecodeRange(Find(registers, "PVR_PCLIP_X")),
            DecodeRange(Find(registers, "PVR_PCLIP_Y")),
            Find(registers, "PVR_FB_SIZE")?.ValueHex,
            Find(registers, "PVR_BITMAP_X")?.ValueHex,
            Find(registers, "PVR_BITMAP_Y")?.ValueHex,
            Find(registers, "PVR_FB_CFG_1")?.ValueHex,
            Find(registers, "PVR_FB_CFG_2")?.ValueHex,
            Find(registers, "PVR_VIDEO_CFG")?.ValueHex,
            Find(registers, "PVR_SCALER_CFG")?.ValueHex,
            Find(registers, "PVR_PALETTE_CFG")?.ValueHex);

    private static DreamcastPvrRegisterValueSummary? Find(IReadOnlyList<DreamcastPvrRegisterValueSummary> registers, string name) =>
        registers.FirstOrDefault(register => string.Equals(register.Name, name, StringComparison.Ordinal));

    private static string? FormatAddress(DreamcastPvrRegisterValueSummary? register) =>
        register is null ? null : $"0x{register.Value & 0x00FF_FFFFu:X6}";

    private static DreamcastPvrRegisterRangeSummary? DecodeRange(DreamcastPvrRegisterValueSummary? register)
    {
        if (register is null)
        {
            return null;
        }

        var start = (int)(register.Value & 0x3FFu);
        var end = (int)((register.Value >> 16) & 0x3FFu);
        return new DreamcastPvrRegisterRangeSummary(start, end, $"{start}-{end}");
    }
}

public sealed record DreamcastPvrRegisterRangeSummary(int Start, int End, string Display);

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

public sealed record DreamcastPvrDmaTransferSummary(
    uint SourceAddress,
    string SourceAddressHex,
    uint DestinationAddress,
    string DestinationAddressHex,
    uint ByteCount,
    bool Completed,
    string Status)
{
    public static DreamcastPvrDmaTransferSummary FromTransfer(DreamcastPvrDmaTransfer transfer) =>
        new(
            transfer.SourceAddress,
            transfer.SourceAddressHex,
            transfer.DestinationAddress,
            transfer.DestinationAddressHex,
            transfer.ByteCount,
            transfer.Completed,
            transfer.Status);
}

public sealed record DreamcastStoreQueueFlushSummary(
    int QueueIndex,
    uint SourceAddress,
    string SourceAddressHex,
    uint DestinationAddress,
    string DestinationAddressHex,
    uint QacrAddress,
    string QacrAddressHex,
    uint QacrValue,
    string QacrValueHex,
    IReadOnlyList<uint> Words,
    IReadOnlyList<string> WordHex,
    uint? InstructionPc = null,
    string? InstructionPcHex = null)
{
    public static DreamcastStoreQueueFlushSummary FromFlush(DreamcastStoreQueueFlush flush) =>
        new(
            flush.QueueIndex,
            flush.SourceAddress,
            flush.SourceAddressHex,
            flush.DestinationAddress,
            flush.DestinationAddressHex,
            flush.QacrAddress,
            flush.QacrAddressHex,
            flush.QacrValue,
            flush.QacrValueHex,
            flush.Words,
            flush.WordHex,
            flush.InstructionPc,
            flush.InstructionPcHex);
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
    bool HasTexturePayload,
    bool EffectiveTextureEnabled,
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
            payload.HasTexturePayload,
            payload.EffectiveTextureEnabled,
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
    public float RawX => BitConverter.UInt32BitsToSingle(XValue);
    public float RawY => BitConverter.UInt32BitsToSingle(YValue);

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

public sealed record DreamcastPvrTaSpritePayloadWordSummary(
    string Name,
    uint Value,
    string ValueHex)
{
    public static DreamcastPvrTaSpritePayloadWordSummary FromWord(DreamcastPvrTaSpritePayloadWord word) =>
        new(word.Name, word.Value, word.ValueHex);
}

public sealed record DreamcastPvrTaSpriteSummary(
    string Region,
    int? ListType,
    string? ListTypeName,
    uint HeaderValue,
    string HeaderValueHex,
    uint? HeaderInstructionPc,
    string? HeaderInstructionPcHex,
    DreamcastPvrTaSpriteHeaderPayloadSummary HeaderPayload,
    uint ControlValue,
    string ControlValueHex,
    uint? ControlInstructionPc,
    string? ControlInstructionPcHex,
    uint? FirstPayloadInstructionPc,
    string? FirstPayloadInstructionPcHex,
    uint? LastPayloadInstructionPc,
    string? LastPayloadInstructionPcHex,
    bool EndOfStrip,
    ushort Rgb565,
    string Rgb565Hex,
    bool HasFinitePreviewCoordinates,
    bool HasRenderablePreviewArea,
    int VertexCount,
    IReadOnlyList<DreamcastPvrTaSpritePayloadWordSummary> PayloadWords,
    IReadOnlyList<DreamcastPvrTaSpriteVertexSummary> Vertices)
{
    public static DreamcastPvrTaSpriteSummary FromSprite(DreamcastPvrTaSprite sprite) =>
        new(
            sprite.Region,
            sprite.ListType,
            sprite.ListTypeName,
            sprite.HeaderValue,
            sprite.HeaderValueHex,
            sprite.HeaderInstructionPc,
            sprite.HeaderInstructionPcHex,
            DreamcastPvrTaSpriteHeaderPayloadSummary.FromPayload(sprite.HeaderPayload),
            sprite.ControlValue,
            sprite.ControlValueHex,
            sprite.ControlInstructionPc,
            sprite.ControlInstructionPcHex,
            sprite.FirstPayloadInstructionPc,
            sprite.FirstPayloadInstructionPcHex,
            sprite.LastPayloadInstructionPc,
            sprite.LastPayloadInstructionPcHex,
            sprite.EndOfStrip,
            sprite.Rgb565,
            sprite.Rgb565Hex,
            sprite.HasFinitePreviewCoordinates,
            sprite.HasRenderablePreviewArea,
            sprite.Vertices.Count,
            sprite.PayloadWords.Select(DreamcastPvrTaSpritePayloadWordSummary.FromWord).ToArray(),
            sprite.Vertices.Select(DreamcastPvrTaSpriteVertexSummary.FromVertex).ToArray());
}

public sealed record DreamcastPvrTaCommandKindSummary(string Kind, int Count);

internal sealed record PvrTaListKey(string Region, int? ListType, string? ListTypeName);

internal sealed record PvrTaSpriteSourceGroupKey(
    string PreviewStatus,
    uint? HeaderInstructionPc,
    string? HeaderInstructionPcHex,
    uint? ControlInstructionPc,
    string? ControlInstructionPcHex,
    uint? FirstPayloadInstructionPc,
    string? FirstPayloadInstructionPcHex,
    uint? LastPayloadInstructionPc,
    string? LastPayloadInstructionPcHex);

internal sealed record PvrTaSpriteShapeGroupKey(
    string PreviewStatus,
    string? ListTypeName,
    string Rgb565Hex,
    string ArgbHex,
    bool TextureEnabled,
    bool TexturePayload,
    bool Uv16Bit,
    string WidthBucket,
    string HeightBucket,
    uint? HeaderInstructionPc,
    string? HeaderInstructionPcHex,
    uint? ControlInstructionPc,
    string? ControlInstructionPcHex,
    uint? FirstPayloadInstructionPc,
    string? FirstPayloadInstructionPcHex,
    uint? LastPayloadInstructionPc,
    string? LastPayloadInstructionPcHex);

public sealed record DreamcastAudioSummary(
    int AudioRamBytes,
    ulong NonZeroBytes,
    uint Fnv1A32,
    string Fnv1A32Hex,
    IReadOnlyList<DreamcastAicaRegisterValueSummary> Registers,
    int RegisterAccessCount,
    IReadOnlyList<DreamcastAicaRegisterAccessSummary> RecentRegisterAccesses,
    IReadOnlyList<DreamcastAicaChannelSummary> Channels,
    int ActiveChannelCount,
    int CommandQueueActivityCount,
    IReadOnlyList<DreamcastAicaCommandQueueActivitySummary> RecentCommandQueueActivities,
    IReadOnlyList<DreamcastAicaCommandQueueSummary> CommandQueues,
    IReadOnlyList<DreamcastAicaRamRegionSummary> RamRegions,
    IReadOnlyList<DreamcastAicaRamAccessHotspotSummary> RamAccessHotspots,
    IReadOnlyList<DreamcastAicaRamTextMarkerSummary> TextMarkers)
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
            snapshot.Channels.Count(channel => channel.Active),
            snapshot.CommandQueueActivities.Count,
            snapshot.CommandQueueActivities.TakeLast(Math.Max(0, recentCount)).Select(DreamcastAicaCommandQueueActivitySummary.FromActivity).ToArray(),
            snapshot.CommandQueues.Select(DreamcastAicaCommandQueueSummary.FromQueue).ToArray(),
            snapshot.RamRegions.Select(DreamcastAicaRamRegionSummary.FromRegion).ToArray(),
            snapshot.RamAccessHotspots.Select(DreamcastAicaRamAccessHotspotSummary.FromHotspot).ToArray(),
            snapshot.TextMarkers.Select(DreamcastAicaRamTextMarkerSummary.FromMarker).ToArray());
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

public sealed record DreamcastAicaCommandQueueActivitySummary(
    uint QueueOffset,
    string QueueOffsetHex,
    uint Head,
    string HeadHex,
    uint Tail,
    string TailHex,
    uint NextTail,
    string NextTailHex,
    uint SizeDwords,
    uint SizeBytes,
    uint Command,
    string CommandHex,
    string CommandName,
    uint CommandId,
    string CommandIdHex,
    uint Timestamp,
    string TimestampHex,
    string Result)
{
    public static DreamcastAicaCommandQueueActivitySummary FromActivity(DreamcastAicaCommandQueueActivity activity) =>
        new(
            activity.QueueOffset,
            activity.QueueOffsetHex,
            activity.Head,
            activity.HeadHex,
            activity.Tail,
            activity.TailHex,
            activity.NextTail,
            activity.NextTailHex,
            activity.SizeDwords,
            activity.SizeBytes,
            activity.Command,
            activity.CommandHex,
            activity.CommandName,
            activity.CommandId,
            activity.CommandIdHex,
            activity.Timestamp,
            activity.TimestampHex,
            activity.Result);
}

public sealed record DreamcastAicaCommandQueueSummary(
    uint Offset,
    string OffsetHex,
    string Role,
    uint Head,
    string HeadHex,
    uint Tail,
    string TailHex,
    uint Size,
    string SizeHex,
    bool Valid,
    bool ProcessOk,
    bool Pending,
    uint Data,
    string DataHex)
{
    public static DreamcastAicaCommandQueueSummary FromQueue(DreamcastAicaCommandQueueSnapshot queue) =>
        new(
            queue.Offset,
            queue.OffsetHex,
            queue.Role,
            queue.Head,
            queue.HeadHex,
            queue.Tail,
            queue.TailHex,
            queue.Size,
            queue.SizeHex,
            queue.Valid,
            queue.ProcessOk,
            queue.Pending,
            queue.Data,
            queue.DataHex);
}

public sealed record DreamcastAicaRamRegionSummary(
    uint StartOffset,
    string StartOffsetHex,
    uint EndOffsetExclusive,
    string EndOffsetExclusiveHex,
    uint Length,
    string LengthHex,
    ulong NonZeroBytes,
    double DensityPercent,
    uint Fnv1A32,
    string Fnv1A32Hex,
    string Area)
{
    public static DreamcastAicaRamRegionSummary FromRegion(DreamcastAicaRamRegionSnapshot region) =>
        new(
            region.StartOffset,
            region.StartOffsetHex,
            region.EndOffsetExclusive,
            region.EndOffsetExclusiveHex,
            region.Length,
            region.LengthHex,
            region.NonZeroBytes,
            region.DensityPercent,
            region.Fnv1A32,
            region.Fnv1A32Hex,
            region.Area);
}

public sealed record DreamcastAicaRamAccessHotspotSummary(
    MemoryAccessKind Kind,
    uint Offset,
    string OffsetHex,
    uint Address,
    string AddressHex,
    int Size,
    ulong Count,
    uint LastValue,
    string LastValueHex,
    uint? LastPc,
    string? LastPcHex,
    string Area)
{
    public static DreamcastAicaRamAccessHotspotSummary FromHotspot(DreamcastAicaRamAccessHotspot hotspot) =>
        new(
            hotspot.Kind,
            hotspot.Offset,
            hotspot.OffsetHex,
            hotspot.Address,
            hotspot.AddressHex,
            hotspot.Size,
            hotspot.Count,
            hotspot.LastValue,
            hotspot.LastValueHex,
            hotspot.LastPc,
            hotspot.LastPcHex,
            hotspot.Area);
}

public sealed record DreamcastAicaRamTextMarkerSummary(
    uint Offset,
    string OffsetHex,
    int Length,
    string Text)
{
    public static DreamcastAicaRamTextMarkerSummary FromMarker(DreamcastAicaRamTextMarker marker) =>
        new(marker.Offset, marker.OffsetHex, marker.Length, marker.Text);
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
