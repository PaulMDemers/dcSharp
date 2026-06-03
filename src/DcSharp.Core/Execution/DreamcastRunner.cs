using DcSharp.Core.Cpu;
using DcSharp.Core.Dreamcast.Asic;
using DcSharp.Core.Dreamcast.Audio;
using DcSharp.Core.Dreamcast.Input;
using DcSharp.Core.Dreamcast.Memory;
using DcSharp.Core.Dreamcast.Timer;
using DcSharp.Core.Dreamcast.Video;
using DcSharp.Core.Loading;
using DcSharp.Core.Media;
using System.Text;

namespace DcSharp.Core.Execution;

public sealed class DreamcastRunner
{
    public DreamcastRunResult Run(ElfFile elf, DreamcastRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(elf);
        ArgumentNullException.ThrowIfNull(options);

        var memory = new DreamcastMemory(options.ControllerA, options.ControllerB, options.Controllers, options.Media, options.MemoryWriteWatch, options.MemoryReadWatch);
        var load = new DreamcastElfLoader().Load(elf, memory);
        return RunLoaded(memory, load, options);
    }

    public DreamcastRunResult RunRawBinary(ReadOnlySpan<byte> data, DreamcastRunOptions options, uint loadAddress = DreamcastRawBinaryLoader.DefaultLoadAddress, ReadOnlySpan<byte> ipBin = default, uint? entryPoint = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var memory = new DreamcastMemory(options.ControllerA, options.ControllerB, options.Controllers, options.Media, options.MemoryWriteWatch, options.MemoryReadWatch);
        var load = new DreamcastRawBinaryLoader().Load(data, memory, loadAddress, ipBin, entryPoint);
        return RunLoaded(memory, load, options);
    }

    private static DreamcastRunResult RunLoaded(DreamcastMemory memory, ElfLoadResult load, DreamcastRunOptions options)
    {
        FirmwareStubs.Install(memory);
        memory.ResetSystemRamWriteCounters();
        memory.ResetWatchedWrites();
        memory.ResetWatchedReads();
        if (options.SeedInitialVBlank)
        {
            memory.RaiseVBlankBegin();
        }

        var firmwareTrap = FirmwareStubs.CreateTrapHandler();
        var cpu = new Sh4Cpu(memory, load.EntryPoint, firmwareTrap.TryHandle);
        cpu.State.R[15] = options.InitialStackPointer;
        cpu.State.Sr = options.InitialStatusRegister;
        var scheduler = new DreamcastEventScheduler(memory, options);
        var traceTail = new Queue<Sh4StepResult>();
        var traceLog = new List<Sh4StepResult>();
        var fpuAnomalies = new List<Sh4FpuAnomaly>();
        var fpuRegisterWrites = new List<Sh4FpuRegisterWrite>();
        var fpscrEvents = new List<Sh4FpscrEvent>();
        var fpuSnapshots = new List<Sh4FpuSnapshot>();
        var fpuMemoryTransfers = new List<Sh4FpuMemoryTransfer>();
        var pcProfile = options.PcProfile is null ? null : new Dictionary<uint, ulong>();
        var shouldSnapshotFpu = options.FpuAnomalyCapture is not null || options.FpuRegisterWatch is not null;
        var frBefore = shouldSnapshotFpu ? new uint[16] : null;
        var xfBefore = shouldSnapshotFpu ? new uint[16] : null;

        try
        {
            while (cpu.State.InstructionsExecuted < options.InstructionLimit)
            {
                scheduler.AdvanceBeforeInstruction(cpu.State.InstructionsExecuted);
                var deviceAccessCountBeforeStep = memory.DeviceAccesses.Count;
                Sh4StepResult step;
                var nextInstruction = cpu.State.InstructionsExecuted + 1;
                var shouldCaptureFpuAnomalies = ShouldCaptureFpuAnomalies(options.FpuAnomalyCapture, fpuAnomalies, nextInstruction);
                var shouldCaptureFpuWrites = ShouldCaptureFpuRegisterWrites(options.FpuRegisterWatch, fpuRegisterWrites, nextInstruction);
                var shouldCaptureFpscr = ShouldCaptureFpscrEvents(options.FpscrWatch, fpscrEvents, nextInstruction);
                var shouldCaptureFpuMemory = ShouldCaptureFpuMemoryTransfers(options.FpuMemoryWatch, fpuMemoryTransfers, nextInstruction);
                var pendingFpuSnapshot = ShouldCaptureFpuSnapshot(options.FpuSnapshotCapture, fpuSnapshots, nextInstruction, cpu.State.Pc)
                    ? CreateFpuSnapshot(nextInstruction, cpu.State, memory)
                    : null;
                var fpscrBefore = shouldCaptureFpscr ? cpu.State.Fpscr : 0;
                if (shouldCaptureFpuAnomalies || shouldCaptureFpuWrites)
                {
                    cpu.State.Fr.AsSpan().CopyTo(frBefore);
                    cpu.State.Xf.AsSpan().CopyTo(xfBefore);
                    step = cpu.Step();
                    if (shouldCaptureFpuAnomalies)
                    {
                        CaptureFpuAnomalies(options.FpuAnomalyCapture!, fpuAnomalies, step, cpu.State, frBefore, xfBefore);
                    }

                    if (shouldCaptureFpuWrites)
                    {
                        CaptureFpuRegisterWrites(options.FpuRegisterWatch!, fpuRegisterWrites, step, cpu.State, frBefore, xfBefore);
                    }
                }
                else
                {
                    step = cpu.Step();
                }

                if (shouldCaptureFpscr)
                {
                    CaptureFpscrEvent(options.FpscrWatch!, fpscrEvents, step, fpscrBefore, cpu.State.Fpscr);
                }

                if (pendingFpuSnapshot is not null)
                {
                    fpuSnapshots.Add(pendingFpuSnapshot with { Trace = step.Trace });
                }

                if (shouldCaptureFpuMemory)
                {
                    CaptureFpuMemoryTransfer(options.FpuMemoryWatch!, fpuMemoryTransfers, step, cpu.State, memory);
                }

                CapturePcProfile(options.PcProfile, pcProfile, step, nextInstruction);
                TryCaptureTraceStep(options.TraceCapture, traceLog, step);
                if (step.Trace == "sleep" || IsSideEffectFreeIdleLoop(step, memory))
                {
                    scheduler.AdvanceAfterIdle();
                }
                else
                {
                    if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C00_8EE4, 0x8C00_8F4E)
                        && cpu.TryFastForwardIpBinPatternFillLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var patternFillSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(patternFillSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C00_8348, 0x8C00_834E)
                        && cpu.TryFastForwardIpBinFramebufferCopyLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var framebufferCopySkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(framebufferCopySkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C00_84F0, 0x8C00_84FC)
                        && cpu.TryFastForwardIpBinShortDelayLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var shortDelaySkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(shortDelaySkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C12_ED90, 0x8C12_EDA0)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C12_9E20, 0x8C12_9E50)
                        && cpu.TryFastForwardDoa2VramClearLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2VramClearSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2VramClearSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C11_3312, 0x8C11_331A)
                        && cpu.TryFastForwardDoa2SystemRamClearLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2SystemRamClearSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2SystemRamClearSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C11_41FC, 0x8C11_F520)
                        && cpu.TryFastForwardDoa2InitDelayLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2InitDelaySkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2InitDelaySkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_EDAA, 0x8C10_EDBC)
                        && cpu.TryFastForwardDoa2StringScanLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2StringScanSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2StringScanSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C12_F99A, 0x8C12_F9B6)
                        && cpu.TryFastForwardDoa2CallbackTimeoutLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2CallbackTimeoutSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2CallbackTimeoutSkippedInstructions, cpu.State.InstructionsExecuted, latchVBlankEvents: false);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C13_0460, 0x8C13_0490)
                        && cpu.TryFastForwardDoa2BusyBitWaitLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2BusyBitSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2BusyBitSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (cpu.TryCompleteDoa2Slot8StubTaskCallback(step))
                    {
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C0F_AC40, 0x8C0F_AC56)
                        && cpu.TryFastForwardDoa2Fac40TrigArgumentWrapper(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2Fac40SkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2Fac40SkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardFpuRecurrence(options)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C0F_B1C0, 0x8C0F_B216)
                        && cpu.TryFastForwardDoa2TrigSetupAndRecurrenceLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2TrigSetupSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2TrigSetupSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C0F_29DE, 0x8C0F_29F4)
                        && cpu.TryFastForwardDoa2TableEntryAddressHelper(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2TableEntryAddressSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2TableEntryAddressSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C0F_3660, 0x8C0F_36CC)
                        && cpu.TryFastForwardDoa2ZeroStatusByteTableScan(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2ZeroStatusByteTableScanSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2ZeroStatusByteTableScanSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C01_3BF6, 0x8C01_3CE4)
                        && cpu.TryFastForwardDoa2ZeroRecordGroupScan(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2ZeroRecordGroupScanSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2ZeroRecordGroupScanSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardFpuRecurrence(options)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C0F_B216, 0x8C0F_B22C)
                        && cpu.TryFastForwardDoa2PostTrigHelperReturn(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2PostTrigSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2PostTrigSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardFpuRecurrence(options)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_0AC0, 0x8C10_0BB2)
                        && cpu.TryFastForwardDoa2ColorBytePackCommonPath(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2ColorBytePackSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2ColorBytePackSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_7864, 0x8C10_787C)
                        && cpu.TryFastForwardDoa2ByteFillWrapper(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2ByteFillWrapperSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2ByteFillWrapperSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C11_C7B0, 0x8C11_C7EA)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_7424, 0x8C10_74B6)
                        && cpu.TryFastForwardDoa2TableDivideSetupLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2TableDivideSetupSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2TableDivideSetupSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C11_C7EA, 0x8C11_C804)
                        && cpu.TryFastForwardDoa2PostTableVectorCopyLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2PostTableVectorCopySkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2PostTableVectorCopySkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C12_FDA0, 0x8C12_FDCC)
                        && cpu.TryFastForwardDoa2EmptyCallbackTableScan(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2EmptyCallbackTableScanSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2EmptyCallbackTableScanSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C11_B226, 0x8C11_B256)
                        && cpu.TryFastForwardDoa2FiveWordTableCopyLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2FiveWordTableCopySkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2FiveWordTableCopySkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C0F_90B0, 0x8C0F_90C8)
                        && cpu.TryFastForwardDoa2FiveWordMirrorCopyLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2FiveWordMirrorCopySkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2FiveWordMirrorCopySkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C11_B300, 0x8C11_B340)
                        && cpu.TryFastForwardDoa2EmptyStackWordScanLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2EmptyStackWordScanSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2EmptyStackWordScanSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C13_07D6, 0x8C13_09BA)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C13_06A0, 0x8C13_073A)
                        && cpu.TryFastForwardDoa2EmptyTaskHelperCallerLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2EmptyTaskHelperCallerSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2EmptyTaskHelperCallerSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C13_06A0, 0x8C13_073A)
                        && cpu.TryFastForwardDoa2EmptyTaskHelperReturn(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2EmptyTaskHelperSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2EmptyTaskHelperSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_786E, 0x8C10_7878)
                        && cpu.TryFastForwardDoa2ByteFillLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2ByteFillSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2ByteFillSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_E60A, 0x8C10_E62C)
                        && cpu.TryFastForwardDoa2UnrolledWordCopyReturn(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2WordCopySkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2WordCopySkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (options.MemoryReadWatch is null
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_4D4C, 0x8C10_4D58)
                        && cpu.TryFastForwardDoa2AicaZeroMailboxTimeoutLoop(
                            step,
                            scheduler.ClampFastForwardToExternalWake(options.InstructionLimit - cpu.State.InstructionsExecuted),
                            out var doa2AicaZeroMailboxTimeoutSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2AicaZeroMailboxTimeoutSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardFpuRecurrence(options)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_3AF0, 0x8C10_3B0E)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_E5D8, 0x8C10_E5E6)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_E61E, 0x8C10_E62C)
                        && cpu.TryFastForwardDoa2ScratchVectorCopyWrapper(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2ScratchVectorCopySkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2ScratchVectorCopySkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardFpuRecurrence(options)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_06EC, 0x8C10_077C)
                        && cpu.TryFastForwardDoa2ColorPackCommonPath(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2ColorPackSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2ColorPackSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardFpuRecurrence(options)
                        && scheduler.CanFastForwardWithoutExternalWake(132)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_077C, 0x8C10_0888)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_0D00, 0x8C10_0D32)
                        && cpu.TryFastForwardDoa2TaEmitCommonPath(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2TaEmitSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2TaEmitSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardFpuRecurrence(options)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C0E_1E08, 0x8C0E_1EB0)
                        && cpu.TryFastForwardDoa2TextGlyphSetupCommonPath(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2TextGlyphSetupSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2TextGlyphSetupSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardFpuRecurrence(options)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_0430, 0x8C10_0456)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_04A0, 0x8C10_04CC)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_0500, 0x8C10_0534)
                        && cpu.TryFastForwardDoa2RendererMode2EntryToFirstTrigCall(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2RendererMode2EntrySkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2RendererMode2EntrySkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardFpuRecurrence(options)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_0430, 0x8C10_0456)
                        && cpu.TryFastForwardDoa2RendererPrologueCommonPath(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2RendererPrologueSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2RendererPrologueSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_04A0, 0x8C10_04CC)
                        && cpu.TryFastForwardDoa2RendererMode2LookupCommonPath(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2RendererMode2LookupSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2RendererMode2LookupSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardFpuRecurrence(options)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_0500, 0x8C10_0534)
                        && cpu.TryFastForwardDoa2RendererMode2TrigSetupToFirstCall(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2RendererMode2TrigSetupSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2RendererMode2TrigSetupSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardFpuRecurrence(options)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_0536, 0x8C10_053E)
                        && cpu.TryFastForwardDoa2RendererSecondTrigCallBridge(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2RendererSecondTrigCallSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2RendererSecondTrigCallSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardFpuRecurrence(options)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_0540, 0x8C10_055A)
                        && cpu.TryFastForwardDoa2RendererPostSecondTrigBridge(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2RendererPostSecondTrigSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2RendererPostSecondTrigSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardFpuRecurrence(options)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_055C, 0x8C10_0574)
                        && cpu.TryFastForwardDoa2RendererPostCallScaleSetup(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2RendererPostCallScaleSetupSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2RendererPostCallScaleSetupSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardFpuRecurrence(options)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_05B4, 0x8C10_066E)
                        && cpu.TryFastForwardDoa2RendererModeWordSetupToColorPack(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2RendererModeWordSetupSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2RendererModeWordSetupSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_0670, 0x8C10_0672)
                        && cpu.TryFastForwardDoa2RendererColorPackReturnBridge(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2RendererColorPackReturnSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2RendererColorPackReturnSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardFpuRecurrence(options)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_0A30, 0x8C10_0A50)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_E5CC, 0x8C10_E5D6)
                        && cpu.TryFastForwardDoa2RendererInterpolationPrologueToCopyTail(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2RendererInterpolationPrologueSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2RendererInterpolationPrologueSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardFpuRecurrence(options)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_0A52, 0x8C10_0AB6)
                        && cpu.TryFastForwardDoa2RendererInterpolationSetupToLoopExit(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2RendererInterpolationSetupSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2RendererInterpolationSetupSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardFpuRecurrence(options)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_0AB6, 0x8C10_0ABC)
                        && cpu.TryFastForwardDoa2RendererInterpolationEpilogueReturn(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2RendererInterpolationEpilogueSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2RendererInterpolationEpilogueSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_751C, 0x8C10_75CC)
                        && cpu.TryFastForwardDoa2SignedRemainderHelper(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2SignedRemainderSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2SignedRemainderSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_7424, 0x8C10_74B6)
                        && cpu.TryFastForwardDoa2UnsignedDivideHelper(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2UnsignedDivideSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2UnsignedDivideSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C11_7482, 0x8C11_74AE)
                        && cpu.TryFastForwardDoa2ZeroByteClassifier(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2ZeroByteClassifierSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2ZeroByteClassifierSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C11_7500, 0x8C11_750C)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C12_4634, 0x8C12_4652)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C13_4F48, 0x8C13_4F58)
                        && cpu.TryFastForwardDoa2ListEntryAllocatorPair(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2ListEntryAllocatorSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2ListEntryAllocatorSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C11_750E, 0x8C11_7530)
                        && cpu.TryFastForwardDoa2ListEntrySetupToClassifier(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2ListEntrySetupSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2ListEntrySetupSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C11_7532, 0x8C11_753C)
                        && cpu.TryFastForwardDoa2ListEntryPostClassifierToRemainder(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2ListEntryPostClassifierSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2ListEntryPostClassifierSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C11_753E, 0x8C11_7540)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C11_759E, 0x8C11_75A8)
                        && cpu.TryFastForwardDoa2ListEntryNonzeroRemainderTail(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2ListEntryNonzeroTailSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2ListEntryNonzeroTailSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C0E_1EB2, 0x8C0E_1EF8)
                        && cpu.TryFastForwardDoa2TextAdvanceToNextGlyph(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2TextAdvanceSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2TextAdvanceSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C0F_7D2A, 0x8C0F_7D58)
                        && cpu.TryFastForwardDoa2ZeroStatusTableScan(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2ZeroStatusScanSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2ZeroStatusScanSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardFpuRecurrence(options)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C0F_B1FE, 0x8C0F_B20E)
                        && cpu.TryFastForwardDoa2FpuRecurrenceLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2FpuRecurrenceSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2FpuRecurrenceSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardFpuRecurrence(options)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_05A0, 0x8C10_05B4)
                        && cpu.TryFastForwardDoa2VectorScaleLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2VectorScaleSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2VectorScaleSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (CanFastForwardFpuRecurrence(options)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, 0x8C10_0A7A, 0x8C10_0AB6)
                        && cpu.TryFastForwardDoa2InterpolationLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var doa2InterpolationSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(doa2InterpolationSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (TryGetDelayedBranchRange(step, out var predecrementStoreBranchStartPc, out var predecrementStoreBranchEndPc)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, predecrementStoreBranchStartPc, predecrementStoreBranchEndPc)
                        && cpu.TryFastForwardPredecrementStoreDtLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var predecrementStoreSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(predecrementStoreSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (TryGetDelayedBranchRange(step, out var postincrementStoreBranchStartPc, out var postincrementStoreBranchEndPc)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, postincrementStoreBranchStartPc, postincrementStoreBranchEndPc)
                        && cpu.TryFastForwardPostincrementStoreDtLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var postincrementStoreSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(postincrementStoreSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (TryGetDelayedBranchRange(step, out var predecrementByteCopyBranchStartPc, out var predecrementByteCopyBranchEndPc)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, predecrementByteCopyBranchStartPc, predecrementByteCopyBranchEndPc)
                        && cpu.TryFastForwardPredecrementByteCopyDtLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var predecrementByteCopySkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(predecrementByteCopySkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (TryGetImmediateBranchRange(step, out var immediateBranchStartPc, out var immediateBranchEndPc)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, immediateBranchStartPc, immediateBranchEndPc)
                        && cpu.TryFastForwardImmediateDtLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var immediateDtSkippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(immediateDtSkippedInstructions, cpu.State.InstructionsExecuted);
                    }
                    else if (TryGetDelayedBranchRange(step, out var branchStartPc, out var branchEndPc)
                        && CanFastForwardTraceRange(options.TraceCapture, traceLog, branchStartPc, branchEndPc)
                        && cpu.TryFastForwardCountedIdleLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var skippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(skippedInstructions, cpu.State.InstructionsExecuted);
                    }
                }

                if (options.TraceTailLength > 0)
                {
                    traceTail.Enqueue(step);
                    while (traceTail.Count > options.TraceTailLength)
                    {
                        traceTail.Dequeue();
                    }
                }

                if (ShouldStopOnDeviceAccess(options, memory.DeviceAccesses, deviceAccessCountBeforeStep, out var stopAccess, out var stopDetail))
                {
                    return DreamcastRunResult.DeviceAccessStop(load, cpu.State, memory, traceTail.ToArray(), traceLog.ToArray(), fpuAnomalies.ToArray(), fpuRegisterWrites.ToArray(), fpscrEvents.ToArray(), fpuSnapshots.ToArray(), fpuMemoryTransfers.ToArray(), memory.DeviceAccesses.ToArray(), memory.WatchedWrites.ToArray(), memory.WatchedReads.ToArray(), memory.SerialOutput.ToArray(), memory.CreateAsicSnapshot(), memory.CreateVideoSnapshot(), memory.CreateAudioSnapshot(), memory.CreateMapleSnapshot(), scheduler.CreateSnapshot(), memory.CreateGdromSnapshot(), memory.CreateTimerSnapshot(), stopAccess, stopDetail)
                        with { PcProfile = CreatePcProfile(pcProfile, options.PcProfile) };
                }
            }

            return DreamcastRunResult.InstructionLimit(load, cpu.State, memory, traceTail.ToArray(), traceLog.ToArray(), fpuAnomalies.ToArray(), fpuRegisterWrites.ToArray(), fpscrEvents.ToArray(), fpuSnapshots.ToArray(), fpuMemoryTransfers.ToArray(), memory.DeviceAccesses.ToArray(), memory.WatchedWrites.ToArray(), memory.WatchedReads.ToArray(), memory.SerialOutput.ToArray(), memory.CreateAsicSnapshot(), memory.CreateVideoSnapshot(), memory.CreateAudioSnapshot(), memory.CreateMapleSnapshot(), scheduler.CreateSnapshot(), memory.CreateGdromSnapshot(), memory.CreateTimerSnapshot())
                with { PcProfile = CreatePcProfile(pcProfile, options.PcProfile) };
        }
        catch (UnsupportedInstructionException ex)
        {
            var serialOutput = memory.SerialOutput.ToArray();
            if (HasKosExitBanner(serialOutput) && !IsInExecutableSegment(load, ex.Pc))
            {
                return DreamcastRunResult.ProgramExit(load, cpu.State, memory, traceTail.ToArray(), traceLog.ToArray(), fpuAnomalies.ToArray(), fpuRegisterWrites.ToArray(), fpscrEvents.ToArray(), fpuSnapshots.ToArray(), fpuMemoryTransfers.ToArray(), memory.DeviceAccesses.ToArray(), memory.WatchedWrites.ToArray(), memory.WatchedReads.ToArray(), serialOutput, memory.CreateAsicSnapshot(), memory.CreateVideoSnapshot(), memory.CreateAudioSnapshot(), memory.CreateMapleSnapshot(), scheduler.CreateSnapshot(), memory.CreateGdromSnapshot(), memory.CreateTimerSnapshot(), ex.Pc, ex.Opcode, ex.Message)
                    with { PcProfile = CreatePcProfile(pcProfile, options.PcProfile) };
            }

            return DreamcastRunResult.UnsupportedInstruction(load, cpu.State, memory, traceTail.ToArray(), traceLog.ToArray(), fpuAnomalies.ToArray(), fpuRegisterWrites.ToArray(), fpscrEvents.ToArray(), fpuSnapshots.ToArray(), fpuMemoryTransfers.ToArray(), memory.DeviceAccesses.ToArray(), memory.WatchedWrites.ToArray(), memory.WatchedReads.ToArray(), memory.SerialOutput.ToArray(), memory.CreateAsicSnapshot(), memory.CreateVideoSnapshot(), memory.CreateAudioSnapshot(), memory.CreateMapleSnapshot(), scheduler.CreateSnapshot(), memory.CreateGdromSnapshot(), memory.CreateTimerSnapshot(), ex.Pc, ex.Opcode, ex.Message)
                with { PcProfile = CreatePcProfile(pcProfile, options.PcProfile) };
        }
        catch (MemoryMapException ex)
        {
            return DreamcastRunResult.MemoryFault(load, cpu.State, memory, traceTail.ToArray(), traceLog.ToArray(), fpuAnomalies.ToArray(), fpuRegisterWrites.ToArray(), fpscrEvents.ToArray(), fpuSnapshots.ToArray(), fpuMemoryTransfers.ToArray(), memory.DeviceAccesses.ToArray(), memory.WatchedWrites.ToArray(), memory.WatchedReads.ToArray(), memory.SerialOutput.ToArray(), memory.CreateAsicSnapshot(), memory.CreateVideoSnapshot(), memory.CreateAudioSnapshot(), memory.CreateMapleSnapshot(), scheduler.CreateSnapshot(), memory.CreateGdromSnapshot(), memory.CreateTimerSnapshot(), ex.Message)
                with { PcProfile = CreatePcProfile(pcProfile, options.PcProfile) };
        }
        catch (DreamcastFirmwareExitException ex)
        {
            return DreamcastRunResult.FirmwareExit(load, cpu.State, memory, traceTail.ToArray(), traceLog.ToArray(), fpuAnomalies.ToArray(), fpuRegisterWrites.ToArray(), fpscrEvents.ToArray(), fpuSnapshots.ToArray(), fpuMemoryTransfers.ToArray(), memory.DeviceAccesses.ToArray(), memory.WatchedWrites.ToArray(), memory.WatchedReads.ToArray(), memory.SerialOutput.ToArray(), memory.CreateAsicSnapshot(), memory.CreateVideoSnapshot(), memory.CreateAudioSnapshot(), memory.CreateMapleSnapshot(), scheduler.CreateSnapshot(), memory.CreateGdromSnapshot(), memory.CreateTimerSnapshot(), ex.Message)
                with { PcProfile = CreatePcProfile(pcProfile, options.PcProfile) };
        }
    }

    private static bool ShouldCaptureFpuAnomalies(DreamcastFpuAnomalyCaptureOptions? options, List<Sh4FpuAnomaly> anomalies, ulong nextInstruction) =>
        options is not null
        && anomalies.Count < options.Limit
        && (options.StartInstruction is null || nextInstruction >= options.StartInstruction)
        && (options.EndInstruction is null || nextInstruction <= options.EndInstruction);

    private static bool ShouldCaptureFpuRegisterWrites(DreamcastFpuRegisterWatchOptions? options, List<Sh4FpuRegisterWrite> writes, ulong nextInstruction) =>
        options is not null
        && writes.Count < options.Limit
        && (options.StartInstruction is null || nextInstruction >= options.StartInstruction)
        && (options.EndInstruction is null || nextInstruction <= options.EndInstruction);

    private static bool ShouldCaptureFpscrEvents(DreamcastFpscrWatchOptions? options, List<Sh4FpscrEvent> events, ulong nextInstruction) =>
        options is not null
        && events.Count < options.Limit
        && (options.StartInstruction is null || nextInstruction >= options.StartInstruction)
        && (options.EndInstruction is null || nextInstruction <= options.EndInstruction);

    private static bool ShouldCaptureFpuMemoryTransfers(DreamcastFpuMemoryWatchOptions? options, List<Sh4FpuMemoryTransfer> transfers, ulong nextInstruction) =>
        options is not null
        && transfers.Count < options.Limit
        && (options.StartInstruction is null || nextInstruction >= options.StartInstruction)
        && (options.EndInstruction is null || nextInstruction <= options.EndInstruction);

    private static void CapturePcProfile(DreamcastPcProfileOptions? options, Dictionary<uint, ulong>? profile, Sh4StepResult step, ulong nextInstruction)
    {
        if (options is null
            || profile is null
            || (options.StartInstruction is not null && nextInstruction < options.StartInstruction)
            || (options.EndInstruction is not null && nextInstruction > options.EndInstruction))
        {
            return;
        }

        profile.TryGetValue(step.Pc, out var count);
        profile[step.Pc] = count + 1;
    }

    private static IReadOnlyList<DreamcastPcProfileEntry> CreatePcProfile(Dictionary<uint, ulong>? profile, DreamcastPcProfileOptions? options)
    {
        if (profile is null || options is null || options.Limit == 0)
        {
            return [];
        }

        return profile
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key)
            .Take(options.Limit)
            .Select(entry => new DreamcastPcProfileEntry(entry.Key, entry.Value))
            .ToArray();
    }

    private static void CaptureFpuMemoryTransfer(
        DreamcastFpuMemoryWatchOptions options,
        List<Sh4FpuMemoryTransfer> transfers,
        Sh4StepResult step,
        Sh4State state,
        DreamcastMemory memory)
    {
        if (!TryDecodeFpuMemoryTransfer(step, state, memory, out var transfer)
            || !ShouldCaptureRegister(transfer.Register, options.Register, options.Registers)
            || !options.ContainsAddress(transfer.Address))
        {
            return;
        }

        transfers.Add(transfer);
    }

    private static bool TryDecodeFpuMemoryTransfer(
        Sh4StepResult step,
        Sh4State state,
        DreamcastMemory memory,
        out Sh4FpuMemoryTransfer transfer)
    {
        transfer = default!;
        var opcode = step.Opcode;
        if ((opcode >> 12) != 0xF)
        {
            return false;
        }

        var n = (opcode >> 8) & 0xF;
        var m = (opcode >> 4) & 0xF;
        var lowNibble = opcode & 0xF;
        var doubleSize = (state.Fpscr & Sh4State.FpscrSzBit) != 0;
        var byteSize = doubleSize ? 8 : 4;
        var direction = string.Empty;
        var register = 0;
        var address = 0u;

        switch (lowNibble)
        {
            case 0x6:
                direction = "load";
                register = n;
                address = state.R[0] + state.R[m];
                break;
            case 0x7:
                direction = "store";
                register = m;
                address = state.R[0] + state.R[n];
                break;
            case 0x8:
                direction = "load";
                register = n;
                address = state.R[m];
                break;
            case 0x9:
                direction = "load";
                register = n;
                address = state.R[m] - (uint)byteSize;
                break;
            case 0xA:
                direction = "store";
                register = m;
                address = state.R[n];
                break;
            case 0xB:
                direction = "store";
                register = m;
                address = state.R[n];
                break;
            default:
                return false;
        }

        transfer = new Sh4FpuMemoryTransfer(
            step.Instruction,
            step.Pc,
            step.Opcode,
            step.Trace,
            direction,
            doubleSize ? $"dr{register & ~1}" : $"fr{register}",
            address,
            memory.ReadUInt32(address),
            doubleSize ? memory.ReadUInt32(address + 4) : null,
            byteSize,
            state.Fpscr);
        return true;
    }

    private static bool ShouldCaptureFpuSnapshot(DreamcastFpuSnapshotCaptureOptions? options, List<Sh4FpuSnapshot> snapshots, ulong nextInstruction, uint pc)
    {
        if (options is null
            || snapshots.Count >= options.Limit
            || (options.StartInstruction is { } startInstruction && nextInstruction < startInstruction)
            || (options.EndInstruction is { } endInstruction && nextInstruction > endInstruction))
        {
            return false;
        }

        return options.Ranges is not { Count: > 0 } || options.Ranges.Any(range => range.Contains(pc));
    }

    private static Sh4FpuSnapshot CreateFpuSnapshot(ulong instruction, Sh4State state, DreamcastMemory memory) =>
        new(
            instruction,
            state.Pc,
            memory.ReadInstructionUInt16(state.Pc),
            string.Empty,
            state.Fr.ToArray(),
            state.Xf.ToArray(),
            state.Fpscr,
            state.Fpul,
            state.Pr,
            state.R[15]);

    private static void CaptureFpscrEvent(
        DreamcastFpscrWatchOptions options,
        List<Sh4FpscrEvent> events,
        Sh4StepResult step,
        uint oldValue,
        uint newValue)
    {
        var traceMentionsFpscr = step.Trace.Contains("fpscr", StringComparison.OrdinalIgnoreCase);
        if (oldValue == newValue && (!options.IncludeReads || !traceMentionsFpscr))
        {
            return;
        }

        events.Add(new Sh4FpscrEvent(
            step.Instruction,
            step.Pc,
            step.Opcode,
            step.Trace,
            oldValue,
            newValue,
            oldValue == newValue ? "access" : "change"));
    }

    private static void CaptureFpuAnomalies(
        DreamcastFpuAnomalyCaptureOptions options,
        List<Sh4FpuAnomaly> anomalies,
        Sh4StepResult step,
        Sh4State state,
        ReadOnlySpan<uint> frBefore,
        ReadOnlySpan<uint> xfBefore)
    {
        CaptureFpuBankAnomalies(options, anomalies, step, state, "fr", frBefore, state.Fr);
        CaptureFpuBankAnomalies(options, anomalies, step, state, "xf", xfBefore, state.Xf);
    }

    private static void CaptureFpuBankAnomalies(
        DreamcastFpuAnomalyCaptureOptions options,
        List<Sh4FpuAnomaly> anomalies,
        Sh4StepResult step,
        Sh4State state,
        string bank,
        ReadOnlySpan<uint> before,
        IReadOnlyList<uint> after)
    {
        for (var index = 0; index < after.Count && anomalies.Count < options.Limit; index++)
        {
            var oldValue = before[index];
            var newValue = after[index];
            var register = $"{bank}{index}";
            var kind = GetFpuAnomalyKind(newValue);
            if (oldValue == newValue
                || !ShouldCaptureRegister(register, options.Register)
                || !ShouldCaptureNonFiniteSingle(newValue, options.Kind)
                || (options.Distinct && ContainsFpuAnomaly(anomalies, step.Pc, register, kind)))
            {
                continue;
            }

            anomalies.Add(new Sh4FpuAnomaly(
                state.InstructionsExecuted,
                step.Pc,
                step.Opcode,
                step.Trace,
                register,
                oldValue,
                newValue,
                state.Fpscr));
        }
    }

    private static bool ContainsFpuAnomaly(List<Sh4FpuAnomaly> anomalies, uint pc, string register, string kind) =>
        anomalies.Any(anomaly =>
            anomaly.Pc == pc
            && string.Equals(anomaly.Register, register, StringComparison.OrdinalIgnoreCase)
            && string.Equals(anomaly.Kind, kind, StringComparison.Ordinal));

    private static bool ShouldCaptureRegister(string register, string? filter) =>
        ShouldCaptureRegister(register, filter, null);

    private static bool ShouldCaptureRegister(string register, string? filter, IReadOnlyList<string>? filters)
    {
        if (filters is { Count: > 0 })
        {
            return filters.Any(candidate => string.Equals(register, candidate, StringComparison.OrdinalIgnoreCase));
        }

        return filter is null || string.Equals(register, filter, StringComparison.OrdinalIgnoreCase);
    }

    private static void CaptureFpuRegisterWrites(
        DreamcastFpuRegisterWatchOptions options,
        List<Sh4FpuRegisterWrite> writes,
        Sh4StepResult step,
        Sh4State state,
        ReadOnlySpan<uint> frBefore,
        ReadOnlySpan<uint> xfBefore)
    {
        CaptureFpuRegisterBankWrites(options, writes, step, state, "fr", frBefore, state.Fr);
        CaptureFpuRegisterBankWrites(options, writes, step, state, "xf", xfBefore, state.Xf);
    }

    private static void CaptureFpuRegisterBankWrites(
        DreamcastFpuRegisterWatchOptions options,
        List<Sh4FpuRegisterWrite> writes,
        Sh4StepResult step,
        Sh4State state,
        string bank,
        ReadOnlySpan<uint> before,
        IReadOnlyList<uint> after)
    {
        for (var index = 0; index < after.Count && writes.Count < options.Limit; index++)
        {
            var oldValue = before[index];
            var newValue = after[index];
            var register = $"{bank}{index}";
            if (oldValue == newValue || !ShouldCaptureRegister(register, options.Register, options.Registers))
            {
                continue;
            }

            writes.Add(new Sh4FpuRegisterWrite(
                state.InstructionsExecuted,
                step.Pc,
                step.Opcode,
                step.Trace,
                register,
                oldValue,
                newValue,
                state.Fpscr));
        }
    }

    private static bool ShouldCaptureNonFiniteSingle(uint value, DreamcastFpuAnomalyKind kind)
    {
        if ((value & 0x7F80_0000u) != 0x7F80_0000u)
        {
            return false;
        }

        var isInfinity = (value & 0x007F_FFFFu) == 0;
        return kind switch
        {
            DreamcastFpuAnomalyKind.All => true,
            DreamcastFpuAnomalyKind.Infinity => isInfinity,
            DreamcastFpuAnomalyKind.NaN => !isInfinity,
            _ => true
        };
    }

    private static string GetFpuAnomalyKind(uint value) =>
        (value & 0x007F_FFFFu) == 0 ? "infinity" : "nan";

    private static bool TryCaptureTraceStep(DreamcastTraceCaptureOptions? traceCapture, List<Sh4StepResult> traceLog, Sh4StepResult step)
    {
        if (traceCapture is null || traceLog.Count >= traceCapture.Limit || !traceCapture.ShouldCapture(step))
        {
            return false;
        }

        traceLog.Add(step);
        return true;
    }

    private static bool CanFastForwardTraceRange(DreamcastTraceCaptureOptions? traceCapture, List<Sh4StepResult> traceLog, uint startPc, uint endPc) =>
        traceCapture is null || traceLog.Count >= traceCapture.Limit || !traceCapture.ShouldCaptureAny(startPc, endPc);

    private static bool CanFastForwardFpuRecurrence(DreamcastRunOptions options) =>
        options.FpuAnomalyCapture is null
        && options.FpuRegisterWatch is null
        && options.FpscrWatch is null
        && options.FpuSnapshotCapture is null
        && options.FpuMemoryWatch is null;

    private static bool HasKosExitBanner(IReadOnlyList<byte> serialOutput)
    {
        const string banner = "arch: exit return code";

        if (serialOutput.Count < banner.Length)
        {
            return false;
        }

        return Encoding.ASCII.GetString(serialOutput.ToArray()).Contains(banner, StringComparison.Ordinal);
    }

    private static bool IsInExecutableSegment(ElfLoadResult load, uint address) =>
        load.LoadedSegments.Any(segment => (segment.Flags & 0x1) != 0
            && (Contains(segment.VirtualAddress, segment.MemorySize, address)
                || Contains(segment.PhysicalAddress, segment.MemorySize, address)));

    private static bool Contains(uint start, uint length, uint address) =>
        address >= start && (ulong)address < (ulong)start + length;

    private static bool ShouldStopOnDeviceAccess(
        DreamcastRunOptions options,
        IReadOnlyList<MemoryAccess> deviceAccesses,
        int previousCount,
        out MemoryAccess stopAccess,
        out string detail)
    {
        stopAccess = default!;
        detail = string.Empty;
        if (!options.StopOnUnmappedAccess && string.IsNullOrWhiteSpace(options.StopOnDeviceDomain))
        {
            return false;
        }

        foreach (var access in deviceAccesses.Skip(previousCount))
        {
            if (options.StopOnUnmappedAccess && access.Kind is MemoryAccessKind.UnmappedRead or MemoryAccessKind.UnmappedWrite)
            {
                stopAccess = access;
                detail = $"Stopped on {access.Kind} at 0x{access.Address:X8}, size={access.Size}, value=0x{access.Value:X8}";
                return true;
            }

            if (!string.IsNullOrWhiteSpace(options.StopOnDeviceDomain)
                && string.Equals(DreamcastDeviceDomainClassifier.Classify(access), options.StopOnDeviceDomain, StringComparison.OrdinalIgnoreCase))
            {
                stopAccess = access;
                detail = $"Stopped on device domain '{options.StopOnDeviceDomain}' at 0x{access.Address:X8}, kind={access.Kind}, size={access.Size}, value=0x{access.Value:X8}";
                return true;
            }
        }

        return false;
    }

    internal static bool IsSideEffectFreeIdleLoop(Sh4StepResult step, DreamcastMemory memory)
    {
        if (step.Opcode == 0xAFFE)
        {
            return memory.ReadInstructionUInt16(step.Pc + 2) == 0x0009;
        }

        if ((step.Opcode & 0xFF00) is 0x8900 or 0x8B00
            && step.Trace.EndsWith(" ; taken", StringComparison.Ordinal)
            && TryGetImmediateBranchTarget(step, out var target)
            && target <= step.Pc)
        {
            for (var pc = target; pc < step.Pc; pc += 2)
            {
                if (!IsReadOnlyIdleLoopBodyInstruction(memory.ReadInstructionUInt16(pc)))
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    private static bool IsReadOnlyIdleLoopBodyInstruction(ushort opcode)
    {
        if (opcode == 0x0009)
        {
            return true;
        }

        var highNibble = opcode >> 12;
        var lowNibble = opcode & 0xF;
        if (highNibble == 0x6)
        {
            return lowNibble is 0x0 or 0x1 or 0x2 or 0x3 or 0xC or 0xD;
        }

        if (highNibble == 0x3)
        {
            return lowNibble is 0x0 or 0x2 or 0x3 or 0x6 or 0x7;
        }

        if (highNibble == 0x2)
        {
            return lowNibble == 0x8;
        }

        if (highNibble == 0x4)
        {
            return (opcode & 0x00FF) is 0x0011 or 0x0015;
        }

        if ((opcode & 0xFF00) is 0x8400 or 0x8500 or 0x8800 or 0xC800 or 0xC900)
        {
            return true;
        }

        if (highNibble == 0x0 && lowNibble is 0xC or 0xD or 0xE)
        {
            return true;
        }

        return false;
    }

    private static bool TryGetImmediateBranchTarget(Sh4StepResult step, out uint target)
    {
        target = 0;
        if ((step.Opcode & 0xFF00) is not (0x8900 or 0x8B00))
        {
            return false;
        }

        target = (uint)(step.Pc + 4 + ((sbyte)(step.Opcode & 0xFF) * 2));
        return true;
    }

    private static bool TryGetImmediateBranchRange(Sh4StepResult step, out uint startPc, out uint endPc)
    {
        startPc = 0;
        endPc = 0;
        if ((step.Opcode & 0xFF00) is not (0x8900 or 0x8B00) || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal))
        {
            return false;
        }

        var target = (uint)(step.Pc + 4 + ((sbyte)(step.Opcode & 0xFF) * 2));
        if (target >= step.Pc)
        {
            return false;
        }

        startPc = target;
        endPc = step.Pc;
        return true;
    }

    private static bool TryGetDelayedBranchRange(Sh4StepResult step, out uint startPc, out uint endPc)
    {
        startPc = 0;
        endPc = 0;
        if ((step.Opcode & 0xFF00) != 0x8F00 || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal))
        {
            return false;
        }

        var target = (uint)(step.Pc + 4 + ((sbyte)(step.Opcode & 0xFF) * 2));
        if (target >= step.Pc)
        {
            return false;
        }

        startPc = target;
        endPc = step.Pc + 2;
        return true;
    }
}

public sealed record DreamcastRunOptions(
    ulong InstructionLimit = 1_000,
    int TraceTailLength = 16,
    ulong VBlankInterval = 200_000,
    DreamcastControllerState? ControllerA = null,
    DreamcastControllerScript? ControllerAScript = null,
    DreamcastTraceCaptureOptions? TraceCapture = null,
    DreamcastControllerState? ControllerB = null,
    IReadOnlyDictionary<byte, DreamcastControllerState>? Controllers = null,
    IReadOnlyDictionary<byte, DreamcastControllerScript>? ControllerScripts = null,
    IDreamcastMediaImage? Media = null,
    bool StopOnUnmappedAccess = false,
    string? StopOnDeviceDomain = null,
    uint InitialStackPointer = 0x8D00_0000,
    uint InitialStatusRegister = 0,
    bool SeedInitialVBlank = false,
    DreamcastMemoryWriteWatch? MemoryWriteWatch = null,
    DreamcastMemoryReadWatch? MemoryReadWatch = null,
    DreamcastFpuAnomalyCaptureOptions? FpuAnomalyCapture = null,
    DreamcastFpuRegisterWatchOptions? FpuRegisterWatch = null,
    DreamcastFpscrWatchOptions? FpscrWatch = null,
    DreamcastFpuSnapshotCaptureOptions? FpuSnapshotCapture = null,
    DreamcastFpuMemoryWatchOptions? FpuMemoryWatch = null,
    DreamcastPcProfileOptions? PcProfile = null);

public sealed record DreamcastPcProfileOptions(
    int Limit = 256,
    ulong? StartInstruction = null,
    ulong? EndInstruction = null);

public sealed record DreamcastFpuAnomalyCaptureOptions(
    int Limit = 4096,
    DreamcastFpuAnomalyKind Kind = DreamcastFpuAnomalyKind.All,
    ulong? StartInstruction = null,
    ulong? EndInstruction = null,
    string? Register = null,
    bool Distinct = false);

public enum DreamcastFpuAnomalyKind
{
    All,
    NaN,
    Infinity
}

public sealed record DreamcastFpuRegisterWatchOptions(
    int Limit = 4096,
    string? Register = null,
    ulong? StartInstruction = null,
    ulong? EndInstruction = null,
    IReadOnlyList<string>? Registers = null);

public sealed record DreamcastFpscrWatchOptions(
    int Limit = 4096,
    ulong? StartInstruction = null,
    ulong? EndInstruction = null,
    bool IncludeReads = true);

public sealed record DreamcastFpuSnapshotCaptureOptions(
    int Limit = 4096,
    IReadOnlyList<DreamcastTracePcRange>? Ranges = null,
    ulong? StartInstruction = null,
    ulong? EndInstruction = null);

public sealed record DreamcastFpuMemoryWatchOptions(
    int Limit = 4096,
    string? Register = null,
    ulong? StartInstruction = null,
    ulong? EndInstruction = null,
    IReadOnlyList<DreamcastMemoryAddressRange>? AddressRanges = null,
    IReadOnlyList<string>? Registers = null)
{
    public bool ContainsAddress(uint address) =>
        AddressRanges is not { Count: > 0 } || AddressRanges.Any(range => range.Overlaps(address, 1));
}

public sealed record DreamcastTraceCaptureOptions(
    uint? StartPc = null,
    uint? EndPc = null,
    int Limit = 4096,
    IReadOnlyList<DreamcastTracePcRange>? Ranges = null,
    ulong? StartInstruction = null,
    ulong? EndInstruction = null)
{
    public bool ShouldCapture(Sh4StepResult step)
    {
        if (Limit <= 0)
        {
            return false;
        }

        if (StartInstruction is { } startInstruction && step.Instruction < startInstruction)
        {
            return false;
        }

        if (EndInstruction is { } endInstruction && step.Instruction > endInstruction)
        {
            return false;
        }

        if (Ranges is { Count: > 0 })
        {
            return Ranges.Any(range => range.Contains(step.Pc));
        }

        if (StartPc is { } startPc && step.Pc < startPc)
        {
            return false;
        }

        if (EndPc is { } endPc && step.Pc > endPc)
        {
            return false;
        }

        return true;
    }

    public bool ShouldCaptureAny(uint startPc, uint endPc)
    {
        if (Limit <= 0)
        {
            return false;
        }

        if (startPc > endPc)
        {
            (startPc, endPc) = (endPc, startPc);
        }

        if (Ranges is { Count: > 0 })
        {
            return Ranges.Any(range => range.Overlaps(startPc, endPc));
        }

        if (StartPc is { } startFilter && endPc < startFilter)
        {
            return false;
        }

        if (EndPc is { } endFilter && startPc > endFilter)
        {
            return false;
        }

        return true;
    }
}

public sealed record DreamcastTracePcRange(uint StartPc, uint EndPc)
{
    public bool Contains(uint pc) =>
        pc >= Math.Min(StartPc, EndPc) && pc <= Math.Max(StartPc, EndPc);

    public bool Overlaps(uint startPc, uint endPc)
    {
        var start = Math.Min(StartPc, EndPc);
        var end = Math.Max(StartPc, EndPc);
        return startPc <= end && endPc >= start;
    }
}

public sealed record DreamcastRunResult(
    ElfLoadResult Load,
    Sh4StateSnapshot Cpu,
    IReadOnlyList<Sh4StepResult> TraceTail,
    IReadOnlyList<Sh4StepResult> TraceLog,
    IReadOnlyList<Sh4FpuAnomaly> FpuAnomalies,
    IReadOnlyList<Sh4FpuRegisterWrite> FpuRegisterWrites,
    IReadOnlyList<Sh4FpscrEvent> FpscrEvents,
    IReadOnlyList<MemoryAccess> DeviceAccesses,
    IReadOnlyList<MemoryAccess> WatchedMemoryWrites,
    IReadOnlyList<MemoryAccess> WatchedMemoryReads,
    IReadOnlyList<DreamcastMemoryRegionWriteSummary> MemoryRegionWrites,
    IReadOnlyList<byte> SerialOutput,
    DreamcastAsicSnapshot Asic,
    DreamcastVideoSnapshot Video,
    DreamcastAudioSnapshot Audio,
    DreamcastMapleSnapshot Maple,
    DreamcastSchedulerSnapshot Scheduler,
    DreamcastStopReason StopReason,
    string StopDetail,
    uint? StopPc,
    ushort? StopOpcode,
    DreamcastGdromSnapshot? Gdrom = null,
    DreamcastTimerSnapshot? Timer = null)
{
    public static DreamcastRunResult InstructionLimit(
        ElfLoadResult load,
        Sh4State state,
        DreamcastMemory memory,
        IReadOnlyList<Sh4StepResult> traceTail,
        IReadOnlyList<Sh4StepResult> traceLog,
        IReadOnlyList<Sh4FpuAnomaly> fpuAnomalies,
        IReadOnlyList<Sh4FpuRegisterWrite> fpuRegisterWrites,
        IReadOnlyList<Sh4FpscrEvent> fpscrEvents,
        IReadOnlyList<Sh4FpuSnapshot> fpuSnapshots,
        IReadOnlyList<Sh4FpuMemoryTransfer> fpuMemoryTransfers,
        IReadOnlyList<MemoryAccess> deviceAccesses,
        IReadOnlyList<MemoryAccess> watchedMemoryWrites,
        IReadOnlyList<MemoryAccess> watchedMemoryReads,
        IReadOnlyList<byte> serialOutput,
        DreamcastAsicSnapshot asic,
        DreamcastVideoSnapshot video,
        DreamcastAudioSnapshot audio,
        DreamcastMapleSnapshot maple,
        DreamcastSchedulerSnapshot scheduler,
        DreamcastGdromSnapshot gdrom,
        DreamcastTimerSnapshot timer) =>
        new(load, Sh4StateSnapshot.From(state, memory), traceTail, traceLog, fpuAnomalies, fpuRegisterWrites, fpscrEvents, deviceAccesses, watchedMemoryWrites, watchedMemoryReads, memory.CreateSystemRamWriteSummary(), serialOutput, asic, video, audio, maple, scheduler, DreamcastStopReason.InstructionLimit, "Instruction limit reached", null, null, gdrom, timer)
        {
            FpuSnapshots = fpuSnapshots,
            FpuMemoryTransfers = fpuMemoryTransfers
        };

    public static DreamcastRunResult UnsupportedInstruction(
        ElfLoadResult load,
        Sh4State state,
        DreamcastMemory memory,
        IReadOnlyList<Sh4StepResult> traceTail,
        IReadOnlyList<Sh4StepResult> traceLog,
        IReadOnlyList<Sh4FpuAnomaly> fpuAnomalies,
        IReadOnlyList<Sh4FpuRegisterWrite> fpuRegisterWrites,
        IReadOnlyList<Sh4FpscrEvent> fpscrEvents,
        IReadOnlyList<Sh4FpuSnapshot> fpuSnapshots,
        IReadOnlyList<Sh4FpuMemoryTransfer> fpuMemoryTransfers,
        IReadOnlyList<MemoryAccess> deviceAccesses,
        IReadOnlyList<MemoryAccess> watchedMemoryWrites,
        IReadOnlyList<MemoryAccess> watchedMemoryReads,
        IReadOnlyList<byte> serialOutput,
        DreamcastAsicSnapshot asic,
        DreamcastVideoSnapshot video,
        DreamcastAudioSnapshot audio,
        DreamcastMapleSnapshot maple,
        DreamcastSchedulerSnapshot scheduler,
        DreamcastGdromSnapshot gdrom,
        DreamcastTimerSnapshot timer,
        uint pc,
        ushort opcode,
        string detail) =>
        new(load, Sh4StateSnapshot.From(state, memory), traceTail, traceLog, fpuAnomalies, fpuRegisterWrites, fpscrEvents, deviceAccesses, watchedMemoryWrites, watchedMemoryReads, memory.CreateSystemRamWriteSummary(), serialOutput, asic, video, audio, maple, scheduler, DreamcastStopReason.UnsupportedInstruction, detail, pc, opcode, gdrom, timer)
        {
            FpuSnapshots = fpuSnapshots,
            FpuMemoryTransfers = fpuMemoryTransfers
        };

    public static DreamcastRunResult ProgramExit(
        ElfLoadResult load,
        Sh4State state,
        DreamcastMemory memory,
        IReadOnlyList<Sh4StepResult> traceTail,
        IReadOnlyList<Sh4StepResult> traceLog,
        IReadOnlyList<Sh4FpuAnomaly> fpuAnomalies,
        IReadOnlyList<Sh4FpuRegisterWrite> fpuRegisterWrites,
        IReadOnlyList<Sh4FpscrEvent> fpscrEvents,
        IReadOnlyList<Sh4FpuSnapshot> fpuSnapshots,
        IReadOnlyList<Sh4FpuMemoryTransfer> fpuMemoryTransfers,
        IReadOnlyList<MemoryAccess> deviceAccesses,
        IReadOnlyList<MemoryAccess> watchedMemoryWrites,
        IReadOnlyList<MemoryAccess> watchedMemoryReads,
        IReadOnlyList<byte> serialOutput,
        DreamcastAsicSnapshot asic,
        DreamcastVideoSnapshot video,
        DreamcastAudioSnapshot audio,
        DreamcastMapleSnapshot maple,
        DreamcastSchedulerSnapshot scheduler,
        DreamcastGdromSnapshot gdrom,
        DreamcastTimerSnapshot timer,
        uint pc,
        ushort opcode,
        string detail) =>
        new(load, Sh4StateSnapshot.From(state, memory), traceTail, traceLog, fpuAnomalies, fpuRegisterWrites, fpscrEvents, deviceAccesses, watchedMemoryWrites, watchedMemoryReads, memory.CreateSystemRamWriteSummary(), serialOutput, asic, video, audio, maple, scheduler, DreamcastStopReason.ProgramExit, $"Program returned after KOS shutdown at 0x{pc:X8}: {detail}", pc, opcode, gdrom, timer)
        {
            FpuSnapshots = fpuSnapshots,
            FpuMemoryTransfers = fpuMemoryTransfers
        };

    public static DreamcastRunResult MemoryFault(
        ElfLoadResult load,
        Sh4State state,
        DreamcastMemory memory,
        IReadOnlyList<Sh4StepResult> traceTail,
        IReadOnlyList<Sh4StepResult> traceLog,
        IReadOnlyList<Sh4FpuAnomaly> fpuAnomalies,
        IReadOnlyList<Sh4FpuRegisterWrite> fpuRegisterWrites,
        IReadOnlyList<Sh4FpscrEvent> fpscrEvents,
        IReadOnlyList<Sh4FpuSnapshot> fpuSnapshots,
        IReadOnlyList<Sh4FpuMemoryTransfer> fpuMemoryTransfers,
        IReadOnlyList<MemoryAccess> deviceAccesses,
        IReadOnlyList<MemoryAccess> watchedMemoryWrites,
        IReadOnlyList<MemoryAccess> watchedMemoryReads,
        IReadOnlyList<byte> serialOutput,
        DreamcastAsicSnapshot asic,
        DreamcastVideoSnapshot video,
        DreamcastAudioSnapshot audio,
        DreamcastMapleSnapshot maple,
        DreamcastSchedulerSnapshot scheduler,
        DreamcastGdromSnapshot gdrom,
        DreamcastTimerSnapshot timer,
        string detail) =>
        new(load, Sh4StateSnapshot.From(state, memory), traceTail, traceLog, fpuAnomalies, fpuRegisterWrites, fpscrEvents, deviceAccesses, watchedMemoryWrites, watchedMemoryReads, memory.CreateSystemRamWriteSummary(), serialOutput, asic, video, audio, maple, scheduler, DreamcastStopReason.MemoryFault, detail, state.Pc, null, gdrom, timer)
        {
            FpuSnapshots = fpuSnapshots,
            FpuMemoryTransfers = fpuMemoryTransfers
        };

    public static DreamcastRunResult FirmwareExit(
        ElfLoadResult load,
        Sh4State state,
        DreamcastMemory memory,
        IReadOnlyList<Sh4StepResult> traceTail,
        IReadOnlyList<Sh4StepResult> traceLog,
        IReadOnlyList<Sh4FpuAnomaly> fpuAnomalies,
        IReadOnlyList<Sh4FpuRegisterWrite> fpuRegisterWrites,
        IReadOnlyList<Sh4FpscrEvent> fpscrEvents,
        IReadOnlyList<Sh4FpuSnapshot> fpuSnapshots,
        IReadOnlyList<Sh4FpuMemoryTransfer> fpuMemoryTransfers,
        IReadOnlyList<MemoryAccess> deviceAccesses,
        IReadOnlyList<MemoryAccess> watchedMemoryWrites,
        IReadOnlyList<MemoryAccess> watchedMemoryReads,
        IReadOnlyList<byte> serialOutput,
        DreamcastAsicSnapshot asic,
        DreamcastVideoSnapshot video,
        DreamcastAudioSnapshot audio,
        DreamcastMapleSnapshot maple,
        DreamcastSchedulerSnapshot scheduler,
        DreamcastGdromSnapshot gdrom,
        DreamcastTimerSnapshot timer,
        string detail) =>
        new(load, Sh4StateSnapshot.From(state, memory), traceTail, traceLog, fpuAnomalies, fpuRegisterWrites, fpscrEvents, deviceAccesses, watchedMemoryWrites, watchedMemoryReads, memory.CreateSystemRamWriteSummary(), serialOutput, asic, video, audio, maple, scheduler, DreamcastStopReason.FirmwareExit, detail, state.Pc, null, gdrom, timer)
        {
            FpuSnapshots = fpuSnapshots,
            FpuMemoryTransfers = fpuMemoryTransfers
        };

    public static DreamcastRunResult DeviceAccessStop(
        ElfLoadResult load,
        Sh4State state,
        DreamcastMemory memory,
        IReadOnlyList<Sh4StepResult> traceTail,
        IReadOnlyList<Sh4StepResult> traceLog,
        IReadOnlyList<Sh4FpuAnomaly> fpuAnomalies,
        IReadOnlyList<Sh4FpuRegisterWrite> fpuRegisterWrites,
        IReadOnlyList<Sh4FpscrEvent> fpscrEvents,
        IReadOnlyList<Sh4FpuSnapshot> fpuSnapshots,
        IReadOnlyList<Sh4FpuMemoryTransfer> fpuMemoryTransfers,
        IReadOnlyList<MemoryAccess> deviceAccesses,
        IReadOnlyList<MemoryAccess> watchedMemoryWrites,
        IReadOnlyList<MemoryAccess> watchedMemoryReads,
        IReadOnlyList<byte> serialOutput,
        DreamcastAsicSnapshot asic,
        DreamcastVideoSnapshot video,
        DreamcastAudioSnapshot audio,
        DreamcastMapleSnapshot maple,
        DreamcastSchedulerSnapshot scheduler,
        DreamcastGdromSnapshot gdrom,
        DreamcastTimerSnapshot timer,
        MemoryAccess access,
        string detail) =>
        new(load, Sh4StateSnapshot.From(state, memory), traceTail, traceLog, fpuAnomalies, fpuRegisterWrites, fpscrEvents, deviceAccesses, watchedMemoryWrites, watchedMemoryReads, memory.CreateSystemRamWriteSummary(), serialOutput, asic, video, audio, maple, scheduler, DreamcastStopReason.DeviceAccessStop, detail, state.Pc, null, gdrom, timer)
        {
            FpuSnapshots = fpuSnapshots,
            FpuMemoryTransfers = fpuMemoryTransfers
        };

    public IReadOnlyList<Sh4FpuSnapshot> FpuSnapshots { get; init; } = [];
    public IReadOnlyList<Sh4FpuMemoryTransfer> FpuMemoryTransfers { get; init; } = [];
    public IReadOnlyList<DreamcastPcProfileEntry> PcProfile { get; init; } = [];
}

public sealed record DreamcastPcProfileEntry(uint Pc, ulong Count)
{
    public string PcHex => $"0x{Pc:X8}";
}

public sealed record Sh4FpuAnomaly(
    ulong Instruction,
    uint Pc,
    ushort Opcode,
    string Trace,
    string Register,
    uint OldValue,
    uint NewValue,
    uint Fpscr)
{
    public string PcHex => $"0x{Pc:X8}";
    public string OpcodeHex => $"0x{Opcode:X4}";
    public string OldValueHex => $"0x{OldValue:X8}";
    public string NewValueHex => $"0x{NewValue:X8}";
    public string FpscrHex => $"0x{Fpscr:X8}";
    public string Kind => (NewValue & 0x007F_FFFFu) == 0 ? "infinity" : "nan";
}

public sealed record Sh4FpuRegisterWrite(
    ulong Instruction,
    uint Pc,
    ushort Opcode,
    string Trace,
    string Register,
    uint OldValue,
    uint NewValue,
    uint Fpscr)
{
    public string PcHex => $"0x{Pc:X8}";
    public string OpcodeHex => $"0x{Opcode:X4}";
    public string OldValueHex => $"0x{OldValue:X8}";
    public string NewValueHex => $"0x{NewValue:X8}";
    public string FpscrHex => $"0x{Fpscr:X8}";
}

public sealed record Sh4FpscrEvent(
    ulong Instruction,
    uint Pc,
    ushort Opcode,
    string Trace,
    uint OldValue,
    uint NewValue,
    string Kind)
{
    public string PcHex => $"0x{Pc:X8}";
    public string OpcodeHex => $"0x{Opcode:X4}";
    public string OldValueHex => $"0x{OldValue:X8}";
    public string NewValueHex => $"0x{NewValue:X8}";
}

public sealed record Sh4FpuSnapshot(
    ulong Instruction,
    uint Pc,
    ushort Opcode,
    string Trace,
    uint[] Fr,
    uint[] Xf,
    uint Fpscr,
    uint Fpul,
    uint Pr,
    uint R15)
{
    public string PcHex => $"0x{Pc:X8}";
    public string OpcodeHex => $"0x{Opcode:X4}";
    public string FpscrHex => $"0x{Fpscr:X8}";
    public string FpulHex => $"0x{Fpul:X8}";
    public string PrHex => $"0x{Pr:X8}";
    public string R15Hex => $"0x{R15:X8}";
}

public sealed record Sh4FpuMemoryTransfer(
    ulong Instruction,
    uint Pc,
    ushort Opcode,
    string Trace,
    string Direction,
    string Register,
    uint Address,
    uint Value,
    uint? ValueHigh,
    int Size,
    uint Fpscr)
{
    public string PcHex => $"0x{Pc:X8}";
    public string OpcodeHex => $"0x{Opcode:X4}";
    public string AddressHex => $"0x{Address:X8}";
    public string ValueHex => $"0x{Value:X8}";
    public string? ValueHighHex => ValueHigh is { } high ? $"0x{high:X8}" : null;
    public string FpscrHex => $"0x{Fpscr:X8}";
}

public sealed record Sh4StateSnapshot(
    uint[] R,
    uint Pc,
    uint Pr,
    uint Sr,
    uint Gbr,
    uint Vbr,
    uint Fpscr,
    ulong InstructionsExecuted,
    uint Spc = 0,
    uint Ssr = 0,
    uint Tra = 0,
    uint Expevt = 0,
    uint Intevt = 0,
    IReadOnlyList<Sh4StackWord>? StackWords = null)
{
    public static Sh4StateSnapshot From(Sh4State state, DreamcastMemory? memory = null)
    {
        var events = memory?.CreateSh4EventRegistersSnapshot() ?? new DreamcastSh4EventRegistersSnapshot(0, 0, 0);
        return new(
            (uint[])state.R.Clone(),
            state.Pc,
            state.Pr,
            state.Sr,
            state.Gbr,
            state.Vbr,
            state.Fpscr,
            state.InstructionsExecuted,
            state.Spc,
            state.Ssr,
            events.Tra,
            events.Expevt,
            events.Intevt,
            CaptureStackWords(state, memory));
    }

    private static IReadOnlyList<Sh4StackWord> CaptureStackWords(Sh4State state, DreamcastMemory? memory)
    {
        const int stackWordsToCapture = 10;
        if (memory is null)
        {
            return [];
        }

        var words = new List<Sh4StackWord>(stackWordsToCapture);
        var stackPointer = state.R[15];
        for (var index = 0; index < stackWordsToCapture; index++)
        {
            var address = stackPointer + ((uint)index * 4);
            if (!memory.TryPeekUInt32(address, out var value))
            {
                break;
            }

            words.Add(new Sh4StackWord(address, $"0x{address:X8}", value, $"0x{value:X8}"));
        }

        return words;
    }
}

public sealed record Sh4StackWord(uint Address, string AddressHex, uint Value, string ValueHex);

public enum DreamcastStopReason
{
    InstructionLimit,
    UnsupportedInstruction,
    MemoryFault,
    FirmwareExit,
    ProgramExit,
    DeviceAccessStop
}
