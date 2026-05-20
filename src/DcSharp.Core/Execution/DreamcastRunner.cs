using DcSharp.Core.Cpu;
using DcSharp.Core.Dreamcast.Memory;
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

        var memory = new DreamcastMemory();
        var load = new DreamcastElfLoader().Load(elf, memory);
        FirmwareStubs.Install(memory);
        var firmwareTrap = FirmwareStubs.CreateTrapHandler();
        var cpu = new Sh4Cpu(memory, load.EntryPoint, firmwareTrap.TryHandle);
        var scheduler = new DreamcastEventScheduler(memory, options);
        var traceTail = new Queue<Sh4StepResult>();

        try
        {
            while (cpu.State.InstructionsExecuted < options.InstructionLimit)
            {
                scheduler.AdvanceBeforeInstruction(cpu.State.InstructionsExecuted);
                var step = cpu.Step();
                if (options.TraceTailLength > 0)
                {
                    traceTail.Enqueue(step);
                    while (traceTail.Count > options.TraceTailLength)
                    {
                        traceTail.Dequeue();
                    }
                }
            }

            return DreamcastRunResult.InstructionLimit(load, cpu.State, traceTail.ToArray(), memory.DeviceAccesses.ToArray(), memory.SerialOutput.ToArray());
        }
        catch (UnsupportedInstructionException ex)
        {
            var serialOutput = memory.SerialOutput.ToArray();
            if (HasKosExitBanner(serialOutput) && !IsInExecutableSegment(load, ex.Pc))
            {
                return DreamcastRunResult.ProgramExit(load, cpu.State, traceTail.ToArray(), memory.DeviceAccesses.ToArray(), serialOutput, ex.Pc, ex.Opcode, ex.Message);
            }

            return DreamcastRunResult.UnsupportedInstruction(load, cpu.State, traceTail.ToArray(), memory.DeviceAccesses.ToArray(), memory.SerialOutput.ToArray(), ex.Pc, ex.Opcode, ex.Message);
        }
        catch (MemoryMapException ex)
        {
            return DreamcastRunResult.MemoryFault(load, cpu.State, traceTail.ToArray(), memory.DeviceAccesses.ToArray(), memory.SerialOutput.ToArray(), ex.Message);
        }
        catch (DreamcastFirmwareExitException ex)
        {
            return DreamcastRunResult.FirmwareExit(load, cpu.State, traceTail.ToArray(), memory.DeviceAccesses.ToArray(), memory.SerialOutput.ToArray(), ex.Message);
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
}

public sealed record DreamcastRunOptions(ulong InstructionLimit = 1_000, int TraceTailLength = 16, ulong VBlankInterval = 200_000);

public sealed record DreamcastRunResult(
    ElfLoadResult Load,
    Sh4StateSnapshot Cpu,
    IReadOnlyList<Sh4StepResult> TraceTail,
    IReadOnlyList<MemoryAccess> DeviceAccesses,
    IReadOnlyList<byte> SerialOutput,
    DreamcastStopReason StopReason,
    string StopDetail,
    uint? StopPc,
    ushort? StopOpcode)
{
    public static DreamcastRunResult InstructionLimit(
        ElfLoadResult load,
        Sh4State state,
        IReadOnlyList<Sh4StepResult> traceTail,
        IReadOnlyList<MemoryAccess> deviceAccesses,
        IReadOnlyList<byte> serialOutput) =>
        new(load, Sh4StateSnapshot.From(state), traceTail, deviceAccesses, serialOutput, DreamcastStopReason.InstructionLimit, "Instruction limit reached", null, null);

    public static DreamcastRunResult UnsupportedInstruction(
        ElfLoadResult load,
        Sh4State state,
        IReadOnlyList<Sh4StepResult> traceTail,
        IReadOnlyList<MemoryAccess> deviceAccesses,
        IReadOnlyList<byte> serialOutput,
        uint pc,
        ushort opcode,
        string detail) =>
        new(load, Sh4StateSnapshot.From(state), traceTail, deviceAccesses, serialOutput, DreamcastStopReason.UnsupportedInstruction, detail, pc, opcode);

    public static DreamcastRunResult ProgramExit(
        ElfLoadResult load,
        Sh4State state,
        IReadOnlyList<Sh4StepResult> traceTail,
        IReadOnlyList<MemoryAccess> deviceAccesses,
        IReadOnlyList<byte> serialOutput,
        uint pc,
        ushort opcode,
        string detail) =>
        new(load, Sh4StateSnapshot.From(state), traceTail, deviceAccesses, serialOutput, DreamcastStopReason.ProgramExit, $"Program returned after KOS shutdown at 0x{pc:X8}: {detail}", pc, opcode);

    public static DreamcastRunResult MemoryFault(
        ElfLoadResult load,
        Sh4State state,
        IReadOnlyList<Sh4StepResult> traceTail,
        IReadOnlyList<MemoryAccess> deviceAccesses,
        IReadOnlyList<byte> serialOutput,
        string detail) =>
        new(load, Sh4StateSnapshot.From(state), traceTail, deviceAccesses, serialOutput, DreamcastStopReason.MemoryFault, detail, state.Pc, null);

    public static DreamcastRunResult FirmwareExit(
        ElfLoadResult load,
        Sh4State state,
        IReadOnlyList<Sh4StepResult> traceTail,
        IReadOnlyList<MemoryAccess> deviceAccesses,
        IReadOnlyList<byte> serialOutput,
        string detail) =>
        new(load, Sh4StateSnapshot.From(state), traceTail, deviceAccesses, serialOutput, DreamcastStopReason.FirmwareExit, detail, state.Pc, null);
}

public sealed record Sh4StateSnapshot(
    uint[] R,
    uint Pc,
    uint Pr,
    uint Sr,
    uint Gbr,
    uint Vbr,
    uint Fpscr,
    ulong InstructionsExecuted)
{
    public static Sh4StateSnapshot From(Sh4State state) =>
        new((uint[])state.R.Clone(), state.Pc, state.Pr, state.Sr, state.Gbr, state.Vbr, state.Fpscr, state.InstructionsExecuted);
}

public enum DreamcastStopReason
{
    InstructionLimit,
    UnsupportedInstruction,
    MemoryFault,
    FirmwareExit,
    ProgramExit
}
