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

        var memory = new DreamcastMemory(options.ControllerA, options.ControllerB, options.Controllers, options.Media);
        var load = new DreamcastElfLoader().Load(elf, memory);
        return RunLoaded(memory, load, options);
    }

    public DreamcastRunResult RunRawBinary(ReadOnlySpan<byte> data, DreamcastRunOptions options, uint loadAddress = DreamcastRawBinaryLoader.DefaultLoadAddress, ReadOnlySpan<byte> ipBin = default, uint? entryPoint = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var memory = new DreamcastMemory(options.ControllerA, options.ControllerB, options.Controllers, options.Media);
        var load = new DreamcastRawBinaryLoader().Load(data, memory, loadAddress, ipBin, entryPoint);
        return RunLoaded(memory, load, options);
    }

    private static DreamcastRunResult RunLoaded(DreamcastMemory memory, ElfLoadResult load, DreamcastRunOptions options)
    {
        FirmwareStubs.Install(memory);
        memory.ResetSystemRamWriteCounters();
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

        try
        {
            while (cpu.State.InstructionsExecuted < options.InstructionLimit)
            {
                scheduler.AdvanceBeforeInstruction(cpu.State.InstructionsExecuted);
                var deviceAccessCountBeforeStep = memory.DeviceAccesses.Count;
                var step = cpu.Step();
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
                    return DreamcastRunResult.DeviceAccessStop(load, cpu.State, memory, traceTail.ToArray(), traceLog.ToArray(), memory.DeviceAccesses.ToArray(), memory.SerialOutput.ToArray(), memory.CreateAsicSnapshot(), memory.CreateVideoSnapshot(), memory.CreateAudioSnapshot(), memory.CreateMapleSnapshot(), scheduler.CreateSnapshot(), memory.CreateGdromSnapshot(), memory.CreateTimerSnapshot(), stopAccess, stopDetail);
                }
            }

            return DreamcastRunResult.InstructionLimit(load, cpu.State, memory, traceTail.ToArray(), traceLog.ToArray(), memory.DeviceAccesses.ToArray(), memory.SerialOutput.ToArray(), memory.CreateAsicSnapshot(), memory.CreateVideoSnapshot(), memory.CreateAudioSnapshot(), memory.CreateMapleSnapshot(), scheduler.CreateSnapshot(), memory.CreateGdromSnapshot(), memory.CreateTimerSnapshot());
        }
        catch (UnsupportedInstructionException ex)
        {
            var serialOutput = memory.SerialOutput.ToArray();
            if (HasKosExitBanner(serialOutput) && !IsInExecutableSegment(load, ex.Pc))
            {
                return DreamcastRunResult.ProgramExit(load, cpu.State, memory, traceTail.ToArray(), traceLog.ToArray(), memory.DeviceAccesses.ToArray(), serialOutput, memory.CreateAsicSnapshot(), memory.CreateVideoSnapshot(), memory.CreateAudioSnapshot(), memory.CreateMapleSnapshot(), scheduler.CreateSnapshot(), memory.CreateGdromSnapshot(), memory.CreateTimerSnapshot(), ex.Pc, ex.Opcode, ex.Message);
            }

            return DreamcastRunResult.UnsupportedInstruction(load, cpu.State, memory, traceTail.ToArray(), traceLog.ToArray(), memory.DeviceAccesses.ToArray(), memory.SerialOutput.ToArray(), memory.CreateAsicSnapshot(), memory.CreateVideoSnapshot(), memory.CreateAudioSnapshot(), memory.CreateMapleSnapshot(), scheduler.CreateSnapshot(), memory.CreateGdromSnapshot(), memory.CreateTimerSnapshot(), ex.Pc, ex.Opcode, ex.Message);
        }
        catch (MemoryMapException ex)
        {
            return DreamcastRunResult.MemoryFault(load, cpu.State, memory, traceTail.ToArray(), traceLog.ToArray(), memory.DeviceAccesses.ToArray(), memory.SerialOutput.ToArray(), memory.CreateAsicSnapshot(), memory.CreateVideoSnapshot(), memory.CreateAudioSnapshot(), memory.CreateMapleSnapshot(), scheduler.CreateSnapshot(), memory.CreateGdromSnapshot(), memory.CreateTimerSnapshot(), ex.Message);
        }
        catch (DreamcastFirmwareExitException ex)
        {
            return DreamcastRunResult.FirmwareExit(load, cpu.State, memory, traceTail.ToArray(), traceLog.ToArray(), memory.DeviceAccesses.ToArray(), memory.SerialOutput.ToArray(), memory.CreateAsicSnapshot(), memory.CreateVideoSnapshot(), memory.CreateAudioSnapshot(), memory.CreateMapleSnapshot(), scheduler.CreateSnapshot(), memory.CreateGdromSnapshot(), memory.CreateTimerSnapshot(), ex.Message);
        }
    }

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
    bool SeedInitialVBlank = false);

public sealed record DreamcastTraceCaptureOptions(
    uint? StartPc = null,
    uint? EndPc = null,
    int Limit = 4096)
{
    public bool ShouldCapture(Sh4StepResult step)
    {
        if (Limit <= 0)
        {
            return false;
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

public sealed record DreamcastRunResult(
    ElfLoadResult Load,
    Sh4StateSnapshot Cpu,
    IReadOnlyList<Sh4StepResult> TraceTail,
    IReadOnlyList<Sh4StepResult> TraceLog,
    IReadOnlyList<MemoryAccess> DeviceAccesses,
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
        IReadOnlyList<MemoryAccess> deviceAccesses,
        IReadOnlyList<byte> serialOutput,
        DreamcastAsicSnapshot asic,
        DreamcastVideoSnapshot video,
        DreamcastAudioSnapshot audio,
        DreamcastMapleSnapshot maple,
        DreamcastSchedulerSnapshot scheduler,
        DreamcastGdromSnapshot gdrom,
        DreamcastTimerSnapshot timer) =>
        new(load, Sh4StateSnapshot.From(state, memory), traceTail, traceLog, deviceAccesses, memory.CreateSystemRamWriteSummary(), serialOutput, asic, video, audio, maple, scheduler, DreamcastStopReason.InstructionLimit, "Instruction limit reached", null, null, gdrom, timer);

    public static DreamcastRunResult UnsupportedInstruction(
        ElfLoadResult load,
        Sh4State state,
        DreamcastMemory memory,
        IReadOnlyList<Sh4StepResult> traceTail,
        IReadOnlyList<Sh4StepResult> traceLog,
        IReadOnlyList<MemoryAccess> deviceAccesses,
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
        new(load, Sh4StateSnapshot.From(state, memory), traceTail, traceLog, deviceAccesses, memory.CreateSystemRamWriteSummary(), serialOutput, asic, video, audio, maple, scheduler, DreamcastStopReason.UnsupportedInstruction, detail, pc, opcode, gdrom, timer);

    public static DreamcastRunResult ProgramExit(
        ElfLoadResult load,
        Sh4State state,
        DreamcastMemory memory,
        IReadOnlyList<Sh4StepResult> traceTail,
        IReadOnlyList<Sh4StepResult> traceLog,
        IReadOnlyList<MemoryAccess> deviceAccesses,
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
        new(load, Sh4StateSnapshot.From(state, memory), traceTail, traceLog, deviceAccesses, memory.CreateSystemRamWriteSummary(), serialOutput, asic, video, audio, maple, scheduler, DreamcastStopReason.ProgramExit, $"Program returned after KOS shutdown at 0x{pc:X8}: {detail}", pc, opcode, gdrom, timer);

    public static DreamcastRunResult MemoryFault(
        ElfLoadResult load,
        Sh4State state,
        DreamcastMemory memory,
        IReadOnlyList<Sh4StepResult> traceTail,
        IReadOnlyList<Sh4StepResult> traceLog,
        IReadOnlyList<MemoryAccess> deviceAccesses,
        IReadOnlyList<byte> serialOutput,
        DreamcastAsicSnapshot asic,
        DreamcastVideoSnapshot video,
        DreamcastAudioSnapshot audio,
        DreamcastMapleSnapshot maple,
        DreamcastSchedulerSnapshot scheduler,
        DreamcastGdromSnapshot gdrom,
        DreamcastTimerSnapshot timer,
        string detail) =>
        new(load, Sh4StateSnapshot.From(state, memory), traceTail, traceLog, deviceAccesses, memory.CreateSystemRamWriteSummary(), serialOutput, asic, video, audio, maple, scheduler, DreamcastStopReason.MemoryFault, detail, state.Pc, null, gdrom, timer);

    public static DreamcastRunResult FirmwareExit(
        ElfLoadResult load,
        Sh4State state,
        DreamcastMemory memory,
        IReadOnlyList<Sh4StepResult> traceTail,
        IReadOnlyList<Sh4StepResult> traceLog,
        IReadOnlyList<MemoryAccess> deviceAccesses,
        IReadOnlyList<byte> serialOutput,
        DreamcastAsicSnapshot asic,
        DreamcastVideoSnapshot video,
        DreamcastAudioSnapshot audio,
        DreamcastMapleSnapshot maple,
        DreamcastSchedulerSnapshot scheduler,
        DreamcastGdromSnapshot gdrom,
        DreamcastTimerSnapshot timer,
        string detail) =>
        new(load, Sh4StateSnapshot.From(state, memory), traceTail, traceLog, deviceAccesses, memory.CreateSystemRamWriteSummary(), serialOutput, asic, video, audio, maple, scheduler, DreamcastStopReason.FirmwareExit, detail, state.Pc, null, gdrom, timer);

    public static DreamcastRunResult DeviceAccessStop(
        ElfLoadResult load,
        Sh4State state,
        DreamcastMemory memory,
        IReadOnlyList<Sh4StepResult> traceTail,
        IReadOnlyList<Sh4StepResult> traceLog,
        IReadOnlyList<MemoryAccess> deviceAccesses,
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
        new(load, Sh4StateSnapshot.From(state, memory), traceTail, traceLog, deviceAccesses, memory.CreateSystemRamWriteSummary(), serialOutput, asic, video, audio, maple, scheduler, DreamcastStopReason.DeviceAccessStop, detail, state.Pc, null, gdrom, timer);
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
        const int stackWordsToCapture = 8;
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
