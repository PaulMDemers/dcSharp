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
        FirmwareStubs.Install(memory);
        var firmwareTrap = FirmwareStubs.CreateTrapHandler();
        var cpu = new Sh4Cpu(memory, load.EntryPoint, firmwareTrap.TryHandle);
        var scheduler = new DreamcastEventScheduler(memory, options);
        var traceTail = new Queue<Sh4StepResult>();
        var traceLog = new List<Sh4StepResult>();

        try
        {
            while (cpu.State.InstructionsExecuted < options.InstructionLimit)
            {
                scheduler.AdvanceBeforeInstruction(cpu.State.InstructionsExecuted);
                var step = cpu.Step();
                if (step.Trace == "sleep" || IsSideEffectFreeIdleLoop(step, memory))
                {
                    scheduler.AdvanceAfterIdle();
                }
                else if (options.TraceCapture is null)
                {
                    if (cpu.TryFastForwardCountedIdleLoop(step, options.InstructionLimit - cpu.State.InstructionsExecuted, out var skippedInstructions))
                    {
                        scheduler.AdvanceAfterCpuFastForward(skippedInstructions, cpu.State.InstructionsExecuted);
                    }
                }

                if (options.TraceCapture is { } traceCapture && traceLog.Count < traceCapture.Limit && traceCapture.ShouldCapture(step))
                {
                    traceLog.Add(step);
                }

                if (options.TraceTailLength > 0)
                {
                    traceTail.Enqueue(step);
                    while (traceTail.Count > options.TraceTailLength)
                    {
                        traceTail.Dequeue();
                    }
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
    IDreamcastMediaImage? Media = null);

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
}

public sealed record DreamcastRunResult(
    ElfLoadResult Load,
    Sh4StateSnapshot Cpu,
    IReadOnlyList<Sh4StepResult> TraceTail,
    IReadOnlyList<Sh4StepResult> TraceLog,
    IReadOnlyList<MemoryAccess> DeviceAccesses,
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
        new(load, Sh4StateSnapshot.From(state, memory), traceTail, traceLog, deviceAccesses, serialOutput, asic, video, audio, maple, scheduler, DreamcastStopReason.InstructionLimit, "Instruction limit reached", null, null, gdrom, timer);

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
        new(load, Sh4StateSnapshot.From(state, memory), traceTail, traceLog, deviceAccesses, serialOutput, asic, video, audio, maple, scheduler, DreamcastStopReason.UnsupportedInstruction, detail, pc, opcode, gdrom, timer);

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
        new(load, Sh4StateSnapshot.From(state, memory), traceTail, traceLog, deviceAccesses, serialOutput, asic, video, audio, maple, scheduler, DreamcastStopReason.ProgramExit, $"Program returned after KOS shutdown at 0x{pc:X8}: {detail}", pc, opcode, gdrom, timer);

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
        new(load, Sh4StateSnapshot.From(state, memory), traceTail, traceLog, deviceAccesses, serialOutput, asic, video, audio, maple, scheduler, DreamcastStopReason.MemoryFault, detail, state.Pc, null, gdrom, timer);

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
        new(load, Sh4StateSnapshot.From(state, memory), traceTail, traceLog, deviceAccesses, serialOutput, asic, video, audio, maple, scheduler, DreamcastStopReason.FirmwareExit, detail, state.Pc, null, gdrom, timer);
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
    uint Intevt = 0)
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
            events.Intevt);
    }
}

public enum DreamcastStopReason
{
    InstructionLimit,
    UnsupportedInstruction,
    MemoryFault,
    FirmwareExit,
    ProgramExit
}
