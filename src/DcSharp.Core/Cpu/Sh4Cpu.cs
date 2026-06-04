using DcSharp.Core.Dreamcast.Memory;

namespace DcSharp.Core.Cpu;

public sealed class Sh4Cpu
{
    private readonly DreamcastMemory memory;
    private readonly Sh4TrapHandler? trapHandler;
    private uint? delayedBranchTarget;
    private uint? immediateBranchTarget;

    public Sh4Cpu(DreamcastMemory memory, uint entryPoint, Sh4TrapHandler? trapHandler = null)
    {
        this.memory = memory;
        this.trapHandler = trapHandler;
        State.Pc = entryPoint;
    }

    public Sh4State State { get; } = new();

    public Sh4StepResult Step()
    {
        var pc = State.Pc;
        memory.CurrentInstructionPc = pc;
        if (delayedBranchTarget is null && TryAcceptExternalInterrupt(pc, out var interruptTrace))
        {
            State.InstructionsExecuted++;
            return new Sh4StepResult(pc, 0, interruptTrace, State.InstructionsExecuted);
        }

        if (trapHandler?.Invoke(State, memory, out var trapTrace) == true)
        {
            State.InstructionsExecuted++;
            return new Sh4StepResult(pc, 0, trapTrace, State.InstructionsExecuted);
        }

        var opcode = memory.ReadInstructionUInt16(pc);
        var trace = Execute(pc, opcode);
        State.InstructionsExecuted++;

        return new Sh4StepResult(pc, opcode, trace, State.InstructionsExecuted);
    }

    internal bool TryFastForwardCountedIdleLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if ((State.Sr & Sh4State.SrBlockBit) == 0 && ((State.Sr >> 4) & 0xF) != 0xF)
        {
            return false;
        }

        if ((step.Opcode & 0xFF00) != 0x8F00 || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal))
        {
            return false;
        }

        var branchTarget = (uint)(step.Pc + 4 + ((sbyte)(step.Opcode & 0xFF) * 2));
        if (delayedBranchTarget != branchTarget || State.Pc != step.Pc + 2 || branchTarget >= step.Pc)
        {
            return false;
        }

        var byteDistance = step.Pc - branchTarget;
        if ((byteDistance & 1) != 0)
        {
            return false;
        }

        var dtPc = step.Pc - 2;
        var dtOpcode = memory.ReadInstructionUInt16(dtPc);
        if ((dtOpcode & 0xF0FF) != 0x4010)
        {
            return false;
        }

        for (var pc = branchTarget; pc < dtPc; pc += 2)
        {
            if (memory.ReadInstructionUInt16(pc) != 0x0009)
            {
                return false;
            }
        }

        var counterRegister = (dtOpcode >> 8) & 0xF;
        var remainingIterations = State.R[counterRegister];
        if (remainingIterations == 0)
        {
            return false;
        }

        var bodyInstructionCount = byteDistance / 2;
        if (!TryComputeSkippedInstructions(remainingIterations, bodyInstructionCount, out skippedInstructions)
            || skippedInstructions > maxInstructionsToSkip)
        {
            skippedInstructions = 0;
            return false;
        }

        var delaySlotOpcode = memory.ReadInstructionUInt16(step.Pc + 2);
        if (!TryApplyRepeatedDelaySlot(delaySlotOpcode, (ulong)remainingIterations + 1))
        {
            skippedInstructions = 0;
            return false;
        }

        State.R[counterRegister] = 0;
        State.T = true;
        State.Pc = step.Pc + 4;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardImmediateDtLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if ((step.Opcode & 0xFF00) is not (0x8900 or 0x8B00) || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal))
        {
            return false;
        }

        var branchTarget = (uint)(step.Pc + 4 + ((sbyte)(step.Opcode & 0xFF) * 2));
        if (State.Pc != branchTarget || branchTarget + 2 != step.Pc)
        {
            return false;
        }

        var dtOpcode = memory.ReadInstructionUInt16(branchTarget);
        if ((dtOpcode & 0xF0FF) != 0x4010)
        {
            return false;
        }

        var counterRegister = (dtOpcode >> 8) & 0xF;
        var remainingIterations = State.R[counterRegister];
        if (remainingIterations == 0 || maxInstructionsToSkip < 2)
        {
            return false;
        }

        var iterationsToSkip = Math.Min((ulong)remainingIterations, maxInstructionsToSkip / 2);
        if (iterationsToSkip == 0)
        {
            return false;
        }

        skippedInstructions = iterationsToSkip * 2;
        State.R[counterRegister] -= (uint)iterationsToSkip;
        if (iterationsToSkip == remainingIterations)
        {
            State.T = true;
            State.Pc = step.Pc + 2;
        }
        else
        {
            State.T = false;
            State.Pc = branchTarget;
        }

        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardIpBinPatternFillLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C00_8F4E || step.Opcode != 0x8BC9 || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal))
        {
            return false;
        }

        if (delayedBranchTarget is not null || State.Pc != 0x8C00_8EE4)
        {
            return false;
        }

        var stack = State.R[15];
        if (stack is < 0x7E00_0000 or >= 0x7E00_1000)
        {
            return false;
        }

        var column = memory.ReadUInt16(stack + 0x02);
        var row = memory.ReadUInt32(stack + 0x0C);
        var rowLimit = memory.ReadUInt32(stack + 0x04);
        var total = memory.ReadUInt32(stack + 0x10);
        var geometry = memory.ReadUInt32(stack + 0x2C);
        var width = memory.ReadUInt16(stack + 0x24);
        if (rowLimit == 0 || width == 0 || column >= width || row >= rowLimit || total >= 0x0010_0000)
        {
            return false;
        }

        var rowsRemaining = rowLimit - row;
        var firstRowCells = (uint)(width - column);
        var cellsRemaining = firstRowCells + ((rowsRemaining - 1) * (uint)width);
        if (cellsRemaining == 0)
        {
            return false;
        }

        var iterationsToSkip = (ulong)(cellsRemaining - 1);
        const ulong instructionsPerIteration = 59;
        if (iterationsToSkip > ulong.MaxValue / instructionsPerIteration)
        {
            return false;
        }

        skippedInstructions = iterationsToSkip * instructionsPerIteration;
        if (skippedInstructions == 0 || skippedInstructions > maxInstructionsToSkip)
        {
            skippedInstructions = 0;
            return false;
        }

        var finalTotal = (ulong)total + iterationsToSkip;
        if (finalTotal > uint.MaxValue)
        {
            skippedInstructions = 0;
            return false;
        }

        memory.WriteUInt16(stack + 0x02, (ushort)(width - 1));
        memory.WriteUInt32(stack + 0x0C, rowLimit - 1);
        memory.WriteUInt32(stack + 0x10, (uint)finalTotal);
        State.R[0] = (uint)width - 1;
        State.R[1] = rowLimit - 2;
        State.R[2] = rowLimit - 1;
        State.R[3] = rowLimit - 1;
        State.R[4] = (geometry & 0xFFFF) + ((uint)width - 1);
        State.T = false;
        State.Pc = 0x8C00_8F50;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardIpBinFramebufferCopyLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C00_834E || step.Opcode != 0x8BFB || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal))
        {
            return false;
        }

        if (delayedBranchTarget is not null || State.Pc != 0x8C00_8348)
        {
            return false;
        }

        var remainingIterations = State.R[7];
        if (remainingIterations == 0)
        {
            return false;
        }

        const ulong instructionsPerIteration = 4;
        var iterationsToSkip = Math.Min((ulong)remainingIterations, maxInstructionsToSkip / instructionsPerIteration);
        if (iterationsToSkip == 0)
        {
            return false;
        }

        skippedInstructions = iterationsToSkip * instructionsPerIteration;
        State.R[4] += (uint)iterationsToSkip * 4;
        State.R[1] -= (uint)iterationsToSkip * 4;
        State.R[7] -= (uint)iterationsToSkip;
        State.T = State.R[7] == 0;
        State.Pc = State.T ? 0x8C00_8350 : 0x8C00_8348;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardIpBinShortDelayLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C00_84FC || step.Opcode != 0x8BF8 || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal))
        {
            return false;
        }

        if (delayedBranchTarget is not null || State.Pc != 0x8C00_84F0)
        {
            return false;
        }

        var stack = State.R[15];
        if (stack is < 0x7E00_0000 or >= 0x7E00_1000)
        {
            return false;
        }

        var counterAddress = stack + 0x08;
        var counter = memory.ReadUInt32(counterAddress);
        var limit = (uint)memory.ReadUInt16(0x8C00_8530);
        if (limit == 0 || counter >= limit)
        {
            return false;
        }

        const ulong instructionsPerIteration = 7;
        var remainingIterations = limit - counter;
        skippedInstructions = remainingIterations * instructionsPerIteration;
        if (skippedInstructions == 0 || skippedInstructions > maxInstructionsToSkip)
        {
            skippedInstructions = 0;
            return false;
        }

        memory.WriteUInt32(counterAddress, limit);
        State.R[1] = limit;
        State.R[2] = limit;
        State.R[3] = limit;
        State.T = true;
        State.Pc = 0x8C00_84FE;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardIpBinAsicEventWaitLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C00_90A0
            || step.Opcode != 0x89FB
            || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal)
            || delayedBranchTarget is not null
            || State.Pc != 0x8C00_909A)
        {
            return false;
        }

        if (memory.ReadInstructionUInt16(0x8C00_909A) != 0xD313
            || memory.ReadInstructionUInt16(0x8C00_909C) != 0x6032
            || memory.ReadInstructionUInt16(0x8C00_909E) != 0xC808
            || memory.ReadInstructionUInt16(0x8C00_90A0) != 0x89FB
            || memory.ReadUInt32(0x8C00_90E8) != 0xA05F_6900)
        {
            return false;
        }

        var asicEvent = memory.ReadUInt32(0xA05F_6900);
        if (State.R[3] != 0xA05F_6900
            || State.R[0] != asicEvent
            || (asicEvent & 0x08) != 0
            || !State.T)
        {
            return false;
        }

        const ulong instructionsPerIteration = 4;
        var iterationsToSkip = maxInstructionsToSkip / instructionsPerIteration;
        if (iterationsToSkip == 0)
        {
            return false;
        }

        skippedInstructions = iterationsToSkip * instructionsPerIteration;
        State.R[3] = 0xA05F_6900;
        State.R[0] = asicEvent;
        State.T = true;
        State.Pc = 0x8C00_909A;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2VramClearLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C12_ED9E || step.Opcode != 0x7E04 || State.Pc != 0x8C12_ED90)
        {
            return false;
        }

        if (delayedBranchTarget is not null || immediateBranchTarget is not null)
        {
            return false;
        }

        if (State.R[11] != 0x8C12_9E20 || State.R[12] == 0 || State.R[13] >= State.R[12])
        {
            return false;
        }

        if (State.R[15] is < 0x8C00_0000 or >= 0x8D00_0000)
        {
            return false;
        }

        var remainingIterations = State.R[12] - State.R[13];
        const ulong instructionsPerIteration = 23;
        var iterationsToSkip = Math.Min((ulong)remainingIterations, maxInstructionsToSkip / instructionsPerIteration);
        if (iterationsToSkip == 0)
        {
            return false;
        }

        if (!memory.TryGetPvrVramOffset(State.R[14], checked((int)Math.Min(iterationsToSkip * 4, int.MaxValue)), out _))
        {
            return false;
        }

        var source = memory.ReadUInt32(State.R[15]);
        var destination = State.R[14];
        for (var index = 0ul; index < iterationsToSkip; index++)
        {
            memory.WriteUInt32(destination + ((uint)index * 4), source);
        }

        skippedInstructions = iterationsToSkip * instructionsPerIteration;
        var completed = iterationsToSkip == remainingIterations;
        var skippedBytes = (uint)iterationsToSkip * 4;
        State.R[0] = source;
        State.R[4] = destination + skippedBytes;
        State.R[5] = State.R[15] + 4;
        State.R[6] = 0;
        State.R[13] += (uint)iterationsToSkip;
        State.R[14] = destination + skippedBytes;
        State.Pr = 0x8C12_ED98;
        State.T = completed;
        State.Pc = completed ? 0x8C12_EDA0 : 0x8C12_ED90;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2SystemRamClearLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C11_331A || step.Opcode != 0x8BFA || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal))
        {
            return false;
        }

        if (delayedBranchTarget is not null)
        {
            return false;
        }

        if (memory.ReadInstructionUInt16(0x8C11_3312) != 0x2542
            || memory.ReadInstructionUInt16(0x8C11_3314) != 0x7504
            || memory.ReadInstructionUInt16(0x8C11_3316) != 0x6362
            || memory.ReadInstructionUInt16(0x8C11_3318) != 0x3532)
        {
            return false;
        }

        var destination = State.R[5];
        var end = State.R[3];
        if (State.R[6] == 0 || destination >= end || (destination & 3) != 0 || (end & 3) != 0)
        {
            return false;
        }

        var remainingIterations = (end - destination) / 4;
        const ulong instructionsPerIteration = 5;
        var iterationsToSkip = Math.Min((ulong)remainingIterations, maxInstructionsToSkip / instructionsPerIteration);
        if (iterationsToSkip == 0 || iterationsToSkip > int.MaxValue / 4)
        {
            return false;
        }

        if (!memory.TryGetSystemRamOffset(destination, checked((int)iterationsToSkip * 4), out _))
        {
            return false;
        }

        var value = State.R[4];
        for (var index = 0ul; index < iterationsToSkip; index++)
        {
            memory.WriteUInt32(destination + ((uint)index * 4), value);
        }

        skippedInstructions = iterationsToSkip * instructionsPerIteration;
        var skippedBytes = (uint)iterationsToSkip * 4;
        State.R[5] = destination + skippedBytes;
        State.R[3] = end;
        State.T = State.R[5] >= end;
        State.Pc = State.T ? 0x8C11_331C : 0x8C11_3312;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2InitDelayLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C11_4276 || step.Opcode != 0x8BED || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal))
        {
            return false;
        }

        if (delayedBranchTarget is not null || State.Pc != 0x8C11_4254)
        {
            return false;
        }

        if (memory.ReadInstructionUInt16(0x8C11_4254) != 0x65F2
            || memory.ReadInstructionUInt16(0x8C11_4256) != 0x56F1
            || memory.ReadInstructionUInt16(0x8C11_4272) != 0x7C01
            || memory.ReadInstructionUInt16(0x8C11_4274) != 0x3CE3
            || memory.ReadInstructionUInt16(0x8C11_4276) != 0x8BED
            || memory.ReadInstructionUInt16(0x8C11_41FE) != 0x4410
            || memory.ReadInstructionUInt16(0x8C11_4200) != 0x8BFD)
        {
            return false;
        }

        if (State.Pr != 0x8C11_4272
            || State.R[11] != 0x8C11_F518
            || State.R[13] != 0x8C11_6F94
            || State.R[14] != 0x0000_2710
            || State.R[12] >= State.R[14]
            || State.R[15] is < 0x8C00_0000 or >= 0x8D00_0000)
        {
            return false;
        }

        var remainingIterations = State.R[14] - State.R[12];
        const ulong instructionsPerIteration = 100_000;
        var iterationsToSkip = Math.Min((ulong)remainingIterations, maxInstructionsToSkip / instructionsPerIteration);
        if (iterationsToSkip == 0)
        {
            return false;
        }

        skippedInstructions = iterationsToSkip * instructionsPerIteration;
        if (skippedInstructions == 0)
        {
            return false;
        }

        var completed = iterationsToSkip == remainingIterations;
        State.R[4] = 0;
        State.R[12] += (uint)iterationsToSkip;
        State.T = completed;
        State.Pc = completed ? 0x8C11_4278 : 0x8C11_4254;
        State.InstructionsExecuted += skippedInstructions;
        memory.WriteUInt32(0x8C1C_AF88, 0);
        memory.WriteUInt32(0x8C1C_AF8C, 0);
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2StringScanLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_EDBC || step.Opcode != 0x8BF5 || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal))
        {
            return false;
        }

        if (delayedBranchTarget is not null || State.Pc != 0x8C10_EDAA)
        {
            return false;
        }

        if (memory.ReadInstructionUInt16(0x8C10_EDAA) != 0x62F2
            || memory.ReadInstructionUInt16(0x8C10_EDAC) != 0x7201
            || memory.ReadInstructionUInt16(0x8C10_EDAE) != 0x2F22
            || memory.ReadInstructionUInt16(0x8C10_EDB0) != 0x64F2
            || memory.ReadInstructionUInt16(0x8C10_EDB2) != 0x6440
            || memory.ReadInstructionUInt16(0x8C10_EDB4) != 0x2448
            || memory.ReadInstructionUInt16(0x8C10_EDB6) != 0x8902
            || memory.ReadInstructionUInt16(0x8C10_EDB8) != 0x6043
            || memory.ReadInstructionUInt16(0x8C10_EDBA) != 0x8825
            || memory.ReadInstructionUInt16(0x8C10_EDBC) != 0x8BF5)
        {
            return false;
        }

        if (State.R[15] is < 0x8C00_0000 or >= 0x8D00_0000 || !memory.TryGetSystemRamOffset(State.R[15], 4, out _))
        {
            return false;
        }

        var currentAddress = memory.ReadUInt32(State.R[15]);
        if (!memory.TryGetSystemRamOffset(currentAddress, 1, out _))
        {
            return false;
        }

        if (maxInstructionsToSkip < 7)
        {
            return false;
        }

        var maxDistance = (maxInstructionsToSkip / 10) + 1;
        uint sentinelAddress = 0;
        byte sentinel = 0;
        for (var scanDistance = 1UL; scanDistance <= maxDistance; scanDistance++)
        {
            var address = currentAddress + (uint)scanDistance;
            if (address <= currentAddress || !memory.TryGetSystemRamOffset(address, 1, out _))
            {
                break;
            }

            var value = memory.ReadByte(address);
            if (value is 0 or 0x25)
            {
                sentinelAddress = address;
                sentinel = value;
                break;
            }
        }

        if (sentinelAddress == 0)
        {
            return false;
        }

        var distance = sentinelAddress - currentAddress;
        if (distance == 0)
        {
            return false;
        }

        var nonSentinelIterations = (ulong)distance - 1;
        skippedInstructions = (nonSentinelIterations * 10) + (sentinel == 0 ? 7UL : 10UL);
        if (skippedInstructions == 0 || skippedInstructions > maxInstructionsToSkip)
        {
            skippedInstructions = 0;
            return false;
        }

        memory.WriteUInt32(State.R[15], sentinelAddress);
        State.R[2] = sentinelAddress;
        State.R[4] = sentinel;
        if (sentinel == 0x25)
        {
            State.R[0] = sentinel;
        }

        State.T = true;
        State.Pc = 0x8C10_EDBE;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2CallbackTimeoutLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C12_F9B4 || step.Opcode != 0x8BF1 || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal))
        {
            return false;
        }

        if (delayedBranchTarget is not null || State.Pc != 0x8C12_F99A)
        {
            return false;
        }

        if (!IsDoa2CallbackTimeoutLoop())
        {
            return false;
        }

        var stack = State.R[15];
        if (stack is < 0x8C00_0000 or >= 0x8D00_0000 || !memory.TryGetSystemRamOffset(stack, 8, out _))
        {
            return false;
        }

        var counterAddress = memory.ReadUInt32(stack);
        var watchedAddress = memory.ReadUInt32(stack + 4);
        if (!memory.TryGetSystemRamOffset(counterAddress, 4, out _)
            || !memory.TryGetSystemRamOffset(watchedAddress, 4, out _)
            || memory.ReadUInt32(watchedAddress) != State.R[14])
        {
            return false;
        }

        var remainingIterations = memory.ReadUInt32(counterAddress);
        if (remainingIterations == 0)
        {
            return false;
        }

        const ulong instructionsPerIteration = 32;
        var iterationsToSkip = Math.Min((ulong)remainingIterations, maxInstructionsToSkip / instructionsPerIteration);
        if (iterationsToSkip == 0)
        {
            return false;
        }

        skippedInstructions = iterationsToSkip * instructionsPerIteration;
        var timedOut = iterationsToSkip == remainingIterations;
        memory.WriteUInt32(counterAddress, remainingIterations - (uint)iterationsToSkip);
        State.R[0] = 0;
        State.R[1] = counterAddress;
        State.R[2] = timedOut ? 0 : remainingIterations - (uint)iterationsToSkip;
        State.R[3] = State.R[2];
        State.R[4] = 7;
        State.T = timedOut;
        State.Pc = timedOut ? 0x8C12_F9B6 : 0x8C12_F99A;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2BusyBitWaitLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C13_048E || step.Opcode != 0x8BE7 || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal))
        {
            return false;
        }

        if (delayedBranchTarget is not null || State.Pc != 0x8C13_0460 || maxInstructionsToSkip < 30)
        {
            return false;
        }

        if (!IsDoa2BusyBitWaitLoop())
        {
            return false;
        }

        var busyMask = memory.ReadUInt32(0x8C2F_67FC);
        var busyBit = 1u << (int)(State.R[13] & 31);
        var queueHead = memory.ReadUInt32(0x8C2F_766C);
        if (State.R[10] != 0x8C12_D2C0
            || State.R[12] != 1
            || busyBit != 1
            || (busyMask & busyBit) == 0
            || memory.ReadUInt32(0x8C2F_6808) != 0
            || memory.ReadUInt32(0x8C2F_680C) != 0
            || memory.ReadUInt32(0x8C2F_67F4) != 0
            || memory.ReadUInt32(0x8C2F_76A4) != 0
            || !IsDoa2BusyBitWorkItem(queueHead))
        {
            return false;
        }

        skippedInstructions = 30;
        memory.WriteUInt32(0x8C2F_67FC, busyMask & ~busyBit);
        memory.WriteUInt32(queueHead, 0);
        State.R[1] = 0x8C2F_67FC;
        State.R[3] = 0;
        State.R[4] = 0;
        State.T = true;
        State.Pc = 0x8C13_0490;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryCompleteDoa2Slot8StubTaskCallback(Sh4StepResult step)
    {
        if (step.Pc != 0x8C13_0728 || step.Opcode != 0x0C36)
        {
            return false;
        }

        if (memory.ReadInstructionUInt16(0x8C13_0724) != 0x9010
            || memory.ReadInstructionUInt16(0x8C13_0726) != 0xE302
            || memory.ReadInstructionUInt16(0x8C13_0728) != 0x0C36
            || memory.ReadInstructionUInt16(0x8C13_072A) != 0xD309
            || memory.ReadInstructionUInt16(0x8C13_072C) != 0x430B
            || memory.ReadInstructionUInt16(0x8C13_072E) != 0xE408
            || memory.ReadUInt32(0x8C13_0750) != 0x8C12_D2C0
            || State.Pc != 0x8C13_072A
            || unchecked(State.R[12] + State.R[0]) != 0x8C2F_67DC
            || State.R[3] != 2
            || memory.ReadUInt32(0x8C30_C780) != 0x8C0F_9F00
            || memory.ReadUInt32(0x8C30_C784) != 0x8C2F_67DC
            || memory.ReadInstructionUInt16(0x8C0F_9F00) != 0x000B
            || memory.ReadInstructionUInt16(0x8C0F_9F02) != 0x0009
            || memory.ReadUInt32(0x8C2B_6CE8) != 0x0000_01F8
            || memory.ReadUInt32(0x8C2B_6CEC) != 0x0000_0100
            || memory.ReadUInt32(0x8C2F_67D4) != 1
            || memory.ReadUInt32(0x8C2F_67D8) != 0
            || memory.ReadUInt32(0x8C2F_67DC) != 2)
        {
            return false;
        }

        memory.WriteUInt32(0x8C2B_6CEC, 0x0000_0120);
        return true;
    }

    private bool IsDoa2BusyBitWorkItem(uint queueHead)
    {
        const uint firstWorkItem = 0x8C2F_6820;
        const uint workItemStride = 0x1C4;
        const uint workItemArenaEnd = 0x8C2F_7640;

        if (queueHead < firstWorkItem
            || queueHead >= workItemArenaEnd
            || ((queueHead - firstWorkItem) % workItemStride) != 0)
        {
            return false;
        }

        return memory.ReadUInt32(queueHead) == 1
            && memory.ReadUInt32(queueHead + 0x14) == 2
            && memory.ReadUInt32(queueHead + 0x18) == 8;
    }

    internal bool TryFastForwardPredecrementStoreDtLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if ((step.Opcode & 0xFF00) != 0x8F00 || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal))
        {
            return false;
        }

        var branchTarget = (uint)(step.Pc + 4 + ((sbyte)(step.Opcode & 0xFF) * 2));
        if (delayedBranchTarget != branchTarget || State.Pc != step.Pc + 2 || branchTarget + 4 != step.Pc)
        {
            return false;
        }

        var storeOpcode = memory.ReadInstructionUInt16(branchTarget);
        var dtOpcode = memory.ReadInstructionUInt16(branchTarget + 2);
        var delaySlotOpcode = memory.ReadInstructionUInt16(step.Pc + 2);
        if ((storeOpcode & 0xF00F) != 0x2006 || (dtOpcode & 0xF0FF) != 0x4010 || delaySlotOpcode != 0x0009)
        {
            return false;
        }

        var destinationRegister = (storeOpcode >> 8) & 0xF;
        var valueRegister = (storeOpcode >> 4) & 0xF;
        var counterRegister = (dtOpcode >> 8) & 0xF;
        var remainingIterations = State.R[counterRegister];
        if (remainingIterations == 0 || maxInstructionsToSkip < 4)
        {
            return false;
        }

        const ulong instructionsPerIteration = 4;
        var iterationsToSkip = Math.Min((ulong)remainingIterations, maxInstructionsToSkip / instructionsPerIteration);
        if (iterationsToSkip == 0 || iterationsToSkip > int.MaxValue / 4)
        {
            return false;
        }

        var bytesToWrite = checked((int)iterationsToSkip * 4);
        var firstDestination = State.R[destinationRegister] - 4;
        var lastDestination = State.R[destinationRegister] - (uint)bytesToWrite;
        if (lastDestination > firstDestination || !memory.TryGetSystemRamOffset(lastDestination, bytesToWrite, out _))
        {
            return false;
        }

        var value = State.R[valueRegister];
        for (var index = 0ul; index < iterationsToSkip; index++)
        {
            memory.WriteUInt32(firstDestination - ((uint)index * 4), value);
        }

        skippedInstructions = iterationsToSkip * instructionsPerIteration;
        State.R[destinationRegister] -= (uint)bytesToWrite;
        State.R[counterRegister] -= (uint)iterationsToSkip;
        State.T = State.R[counterRegister] == 0;
        State.Pc = State.T ? step.Pc + 4 : branchTarget;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardPostincrementStoreDtLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if ((step.Opcode & 0xFF00) != 0x8F00 || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal))
        {
            return false;
        }

        var branchTarget = (uint)(step.Pc + 4 + ((sbyte)(step.Opcode & 0xFF) * 2));
        if (delayedBranchTarget != branchTarget || State.Pc != step.Pc + 2 || branchTarget + 4 != step.Pc)
        {
            return false;
        }

        var storeOpcode = memory.ReadInstructionUInt16(branchTarget);
        var dtOpcode = memory.ReadInstructionUInt16(branchTarget + 2);
        var delaySlotOpcode = memory.ReadInstructionUInt16(step.Pc + 2);
        if ((storeOpcode & 0xF00F) != 0x2002 || (dtOpcode & 0xF0FF) != 0x4010 || (delaySlotOpcode & 0xF0FF) != 0x7004)
        {
            return false;
        }

        var destinationRegister = (storeOpcode >> 8) & 0xF;
        var valueRegister = (storeOpcode >> 4) & 0xF;
        var counterRegister = (dtOpcode >> 8) & 0xF;
        var delaySlotRegister = (delaySlotOpcode >> 8) & 0xF;
        var remainingIterations = State.R[counterRegister];
        if (destinationRegister != delaySlotRegister || remainingIterations == 0 || maxInstructionsToSkip < 4)
        {
            return false;
        }

        const ulong instructionsPerIteration = 4;
        var iterationsToSkip = Math.Min((ulong)remainingIterations, maxInstructionsToSkip / instructionsPerIteration);
        if (iterationsToSkip == 0 || iterationsToSkip > int.MaxValue / 4)
        {
            return false;
        }

        var firstDestination = State.R[destinationRegister] + 4;
        var bytesToWrite = checked((int)iterationsToSkip * 4);
        if (firstDestination < State.R[destinationRegister] || !memory.TryGetSystemRamOffset(firstDestination, bytesToWrite, out _))
        {
            return false;
        }

        var value = State.R[valueRegister];
        for (var index = 0ul; index < iterationsToSkip; index++)
        {
            memory.WriteUInt32(firstDestination + ((uint)index * 4), value);
        }

        skippedInstructions = iterationsToSkip * instructionsPerIteration;
        State.R[destinationRegister] += ((uint)iterationsToSkip + 1) * 4;
        State.R[counterRegister] -= (uint)iterationsToSkip;
        State.T = State.R[counterRegister] == 0;
        State.Pc = State.T ? step.Pc + 4 : branchTarget;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardPredecrementByteCopyDtLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if ((step.Opcode & 0xFF00) != 0x8F00 || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal))
        {
            return false;
        }

        var branchTarget = (uint)(step.Pc + 4 + ((sbyte)(step.Opcode & 0xFF) * 2));
        if (delayedBranchTarget != branchTarget || State.Pc != step.Pc + 2 || branchTarget + 6 != step.Pc)
        {
            return false;
        }

        var sourceDecrementOpcode = memory.ReadInstructionUInt16(branchTarget);
        var loadOpcode = memory.ReadInstructionUInt16(branchTarget + 2);
        var dtOpcode = memory.ReadInstructionUInt16(branchTarget + 4);
        var delaySlotOpcode = memory.ReadInstructionUInt16(step.Pc + 2);
        if ((sourceDecrementOpcode & 0xF0FF) != 0x70FF
            || (loadOpcode & 0xF00F) != 0x6000
            || (dtOpcode & 0xF0FF) != 0x4010
            || (delaySlotOpcode & 0xF00F) != 0x2004)
        {
            return false;
        }

        var sourceRegister = (sourceDecrementOpcode >> 8) & 0xF;
        var loadSourceRegister = (loadOpcode >> 4) & 0xF;
        var valueRegister = (loadOpcode >> 8) & 0xF;
        var counterRegister = (dtOpcode >> 8) & 0xF;
        var destinationRegister = (delaySlotOpcode >> 8) & 0xF;
        var storeValueRegister = (delaySlotOpcode >> 4) & 0xF;
        var remainingIterations = State.R[counterRegister];
        if (sourceRegister != loadSourceRegister
            || valueRegister != storeValueRegister
            || sourceRegister == destinationRegister
            || valueRegister == sourceRegister
            || valueRegister == destinationRegister
            || valueRegister == counterRegister
            || counterRegister == sourceRegister
            || counterRegister == destinationRegister
            || remainingIterations == 0
            || remainingIterations > int.MaxValue - 1)
        {
            return false;
        }

        var instructionsToSkip = 1ul + ((ulong)remainingIterations * 5);
        if (maxInstructionsToSkip < instructionsToSkip)
        {
            return false;
        }

        var bytesToCopy = checked((int)remainingIterations + 1);
        var firstSource = State.R[sourceRegister];
        var lastSource = firstSource - remainingIterations;
        var firstDestination = State.R[destinationRegister] - 1;
        var lastDestination = State.R[destinationRegister] - (uint)bytesToCopy;
        if (lastSource > firstSource
            || lastDestination > firstDestination
            || !memory.TryGetSystemRamOffset(lastSource, bytesToCopy, out _)
            || !memory.TryGetSystemRamOffset(lastDestination, bytesToCopy, out _))
        {
            return false;
        }

        var lastValue = (byte)State.R[valueRegister];
        for (var index = 0; index < bytesToCopy; index++)
        {
            var value = index == 0
                ? (byte)State.R[valueRegister]
                : memory.ReadByte(firstSource - (uint)index);
            memory.Write(firstDestination - (uint)index, [value]);
            lastValue = value;
        }

        skippedInstructions = instructionsToSkip;
        State.R[sourceRegister] -= remainingIterations;
        State.R[destinationRegister] -= (uint)bytesToCopy;
        State.R[counterRegister] = 0;
        State.R[valueRegister] = (uint)(sbyte)lastValue;
        State.T = true;
        State.Pc = step.Pc + 4;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2ByteFillLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_7874
            || step.Opcode != 0x8FFB
            || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal)
            || delayedBranchTarget != 0x8C10_786E
            || State.Pc != 0x8C10_7876)
        {
            return false;
        }

        if (!IsDoa2ByteFillLoop()
            || State.R[7] >= State.R[6])
        {
            return false;
        }

        var remainingIterations = State.R[6] - State.R[7];
        if (remainingIterations > int.MaxValue)
        {
            return false;
        }

        skippedInstructions = 1 + ((ulong)remainingIterations * 5);
        if (skippedInstructions > maxInstructionsToSkip
            || State.R[0] > uint.MaxValue - remainingIterations
            || !memory.TryGetSystemRamOffset(State.R[0] + 1, checked((int)remainingIterations), out _))
        {
            skippedInstructions = 0;
            return false;
        }

        var address = State.R[0] + 1;
        var value = (byte)State.R[5];
        for (var index = 0u; index < remainingIterations; index++)
        {
            memory.Write(address + index, [value]);
        }

        State.R[0] = address + remainingIterations;
        State.R[7] = State.R[6];
        State.T = true;
        State.Pc = 0x8C10_7878;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2ByteFillWrapper(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_7864
            || step.Opcode != 0xE700
            || State.Pc != 0x8C10_7866
            || State.R[7] != 0)
        {
            return false;
        }

        var byteCount = State.R[6];
        if (!IsDoa2ByteFillLoop()
            || byteCount > int.MaxValue)
        {
            return false;
        }

        skippedInstructions = 6 + ((ulong)byteCount * 5);
        if (skippedInstructions > maxInstructionsToSkip)
        {
            skippedInstructions = 0;
            return false;
        }

        if (byteCount != 0)
        {
            if (State.R[4] > uint.MaxValue - (byteCount - 1)
                || !memory.TryGetSystemRamOffset(State.R[4], checked((int)byteCount), out _))
            {
                skippedInstructions = 0;
                return false;
            }

            var value = (byte)State.R[5];
            for (var index = 0u; index < byteCount; index++)
            {
                memory.Write(State.R[4] + index, [value]);
            }
        }

        State.R[0] = State.R[4];
        State.R[3] = 0;
        State.R[7] = byteCount;
        State.T = true;
        State.Pc = State.Pr;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2TableDivideSetupLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C11_C7B0
            || step.Opcode != 0x8928
            || !step.Trace.EndsWith(" ; not taken", StringComparison.Ordinal)
            || State.Pc != 0x8C11_C7B2
            || State.T)
        {
            return false;
        }

        const ulong skippedInstructionCount = 759;
        if (!IsDoa2TableDivideSetupLoop()
            || !IsDoa2UnsignedDivideHelper()
            || maxInstructionsToSkip < skippedInstructionCount
            || State.R[13] > uint.MaxValue - 0x938
            || State.R[14] > uint.MaxValue - 36
            || State.R[15] < 4)
        {
            return false;
        }

        var tableBase = State.R[13] + 0x914;
        var counterAddress = State.R[13] + 0x938;
        var destination = State.R[14] + 4;
        var savedR2StackAddress = State.R[15] - 4;
        if (!memory.TryGetSystemRamOffset(tableBase, 32, out _)
            || !memory.TryGetSystemRamOffset(counterAddress, 4, out _)
            || !memory.TryGetSystemRamOffset(destination, 32, out _)
            || !memory.TryGetSystemRamOffset(savedR2StackAddress, 4, out _))
        {
            return false;
        }

        State.R[0] = 0x938;
        State.R[4] = destination;
        State.R[5] = memory.ReadUInt32(counterAddress);
        State.R[6] = 100;
        State.R[7] = 7;
        State.R[5]--;

        for (var iteration = 0; iteration < 8; iteration++)
        {
            State.R[3] = 0x914;
            State.R[5] &= State.R[7];
            State.R[1] = State.R[5];
            State.R[1] <<= 2;
            State.R[3] += State.R[13];
            State.R[1] += State.R[3];
            State.R[3] = 0x8C10_7424;
            State.R[1] = memory.ReadUInt32(State.R[1]);
            State.R[5]--;
            State.R[1] <<= 2;
            State.R[1] <<= 2;
            State.R[1] <<= 2;
            State.T = (State.R[1] & 0x8000_0000) != 0;
            State.R[1] <<= 1;

            State.Pr = 0x8C11_C7DE;
            State.R[0] = State.R[6];
            State.T = false;
            memory.WriteUInt32(savedR2StackAddress, State.R[2]);
            State.R[2] = 0;
            State.M = false;
            State.Q = false;
            for (var index = 0; index < 32; index++)
            {
                ExecuteRotcl(1);
                ExecuteDiv1(0, 2);
            }

            ExecuteRotcl(1);
            State.R[0] = State.R[1];
            State.R[2] = memory.ReadUInt32(savedR2StackAddress);

            State.R[2] = State.R[14];
            memory.WriteUInt32(State.R[4], State.R[0]);
            State.R[2] += 36;
            State.R[4] += 4;
            State.T = State.R[4] >= State.R[2];
        }

        State.Pc = 0x8C11_C7EA;
        State.InstructionsExecuted += skippedInstructionCount;
        skippedInstructions = skippedInstructionCount;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2PostTableVectorCopyLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C11_C7EA
            || step.Opcode != 0xE400
            || State.Pc != 0x8C11_C7EC
            || State.R[4] != 0)
        {
            return false;
        }

        const ulong skippedInstructionCount = 89;
        if (!IsDoa2PostTableVectorCopyLoop()
            || maxInstructionsToSkip < skippedInstructionCount
            || State.R[13] > uint.MaxValue - 0x914
            || State.R[14] > uint.MaxValue - 64)
        {
            return false;
        }

        var source = State.R[13] + 0x8F4;
        var destination = State.R[14] + 36;
        if (!memory.TryGetSystemRamOffset(source, 32, out _)
            || !memory.TryGetSystemRamOffset(destination, 32, out _))
        {
            return false;
        }

        State.R[5] = 32;
        for (var offset = 0u; offset < 32; offset += 4)
        {
            State.R[3] = source + offset;
            State.R[2] = destination + offset;
            State.R[1] = memory.ReadUInt32(State.R[3]);
            State.R[4] = offset + 4;
            memory.WriteUInt32(State.R[2], State.R[1]);
        }

        State.T = true;
        State.Pc = 0x8C11_C804;
        State.InstructionsExecuted += skippedInstructionCount;
        skippedInstructions = skippedInstructionCount;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2EmptyCallbackTableScan(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C12_FDA0
            || step.Opcode != 0x974E
            || State.Pc != 0x8C12_FDA2
            || State.R[7] != 0xE20)
        {
            return false;
        }

        const ulong skippedInstructionCount = 70;
        const uint tableLimit = 0xE20;
        const uint tableStride = 0x1C4;
        if (!IsDoa2EmptyCallbackTableScan()
            || maxInstructionsToSkip < skippedInstructionCount)
        {
            return false;
        }

        var tableBase = memory.ReadUInt32(0x8C12_FE44);
        for (var offset = 0u; offset < tableLimit; offset += tableStride)
        {
            if (tableBase > uint.MaxValue - offset
                || !memory.TryGetSystemRamOffset(tableBase + offset, 4, out _)
                || memory.ReadUInt32(tableBase + offset) == 1)
            {
                return false;
            }
        }

        State.R[0] = 0;
        State.R[4] = tableLimit;
        State.R[5] = tableLimit;
        State.R[6] = tableStride;
        State.R[7] = tableLimit;
        State.T = true;
        State.Pc = State.Pr;
        State.InstructionsExecuted += skippedInstructionCount;
        skippedInstructions = skippedInstructionCount;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2FiveWordTableCopyLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C11_B226
            || step.Opcode != 0xE340
            || State.Pc != 0x8C11_B228
            || State.R[3] != 64
            || State.R[5] != 20
            || State.R[4] >= State.R[5]
            || (State.R[4] & 0x3) != 0)
        {
            return false;
        }

        var remainingIterations = (State.R[5] - State.R[4]) / 4;
        skippedInstructions = 23 + ((ulong)(remainingIterations - 1) * 24);
        if (!IsDoa2FiveWordTableCopyLoop()
            || skippedInstructions > maxInstructionsToSkip
            || State.R[14] > uint.MaxValue - 0x170)
        {
            skippedInstructions = 0;
            return false;
        }

        var selectorAddress = State.R[14] + 64;
        if (!memory.TryGetSystemRamOffset(selectorAddress, 4, out _))
        {
            skippedInstructions = 0;
            return false;
        }

        var selector = memory.ReadUInt32(selectorAddress);
        var sourceBaseOffset = unchecked(selector * 160);
        var sourceBase = unchecked(State.R[14] + 0x170 + State.R[12] + sourceBaseOffset);
        var destinationBase = unchecked(State.R[14] + 0xB0 + State.R[12]);
        for (var offset = State.R[4]; offset < State.R[5]; offset += 4)
        {
            if (!memory.TryGetSystemRamOffset(unchecked(sourceBase + offset), 4, out _)
                || !memory.TryGetSystemRamOffset(unchecked(destinationBase + offset), 4, out _))
            {
                skippedInstructions = 0;
                return false;
            }
        }

        while (State.R[4] < State.R[5])
        {
            State.R[3] = 64;
            State.R[1] = 0x170;
            State.R[3] += State.R[14];
            State.R[0] = 0xB0;
            State.R[3] = memory.ReadUInt32(State.R[3]);
            State.R[1] += State.R[14];
            State.R[0] += State.R[14];
            State.R[2] = State.R[3];
            State.R[3] <<= 2;
            State.R[3] += State.R[2];
            State.R[3] <<= 2;
            State.R[3] <<= 2;
            State.T = (State.R[3] & 0x8000_0000) != 0;
            State.R[3] <<= 1;
            State.R[3] += State.R[1];
            State.R[2] = State.R[3];
            State.R[2] += State.R[12];
            State.R[3] = State.R[2];
            State.R[3] += State.R[4];
            State.R[2] = memory.ReadUInt32(State.R[3]);
            State.R[0] += State.R[12];
            memory.WriteUInt32(State.R[0] + State.R[4], State.R[2]);
            State.R[4] += 4;
            State.T = State.R[4] >= State.R[5];
        }

        State.Pc = 0x8C11_B256;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2FiveWordMirrorCopyLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C0F_90B8
            || step.Opcode != 0x6366
            || State.Pc != 0x8C0F_90BA
            || State.R[4] == 0)
        {
            return false;
        }

        skippedInstructions = 5 + ((ulong)(State.R[4] - 1) * 6);
        if (!IsDoa2FiveWordMirrorCopyLoop()
            || skippedInstructions > maxInstructionsToSkip
            || State.R[4] > int.MaxValue / 4
            || State.R[5] > uint.MaxValue - ((State.R[4] * 4) - 1))
        {
            skippedInstructions = 0;
            return false;
        }

        var remainingSourceBytes = checked((int)(State.R[4] - 1) * 4);
        var destinationBytes = checked((int)State.R[4] * 4);
        if ((remainingSourceBytes != 0 && !memory.TryGetSystemRamOffset(State.R[6], remainingSourceBytes, out _))
            || !memory.TryGetSystemRamOffset(State.R[5], destinationBytes, out _))
        {
            skippedInstructions = 0;
            return false;
        }

        while (true)
        {
            State.R[4]--;
            State.T = State.R[4] == 0;
            memory.WriteUInt32(State.R[5], State.R[3]);
            State.R[5] += 4;
            if (State.T)
            {
                break;
            }

            State.R[3] = memory.ReadUInt32(State.R[6]);
            State.R[6] += 4;
        }

        State.Pc = 0x8C0F_90C4;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2EmptyStackWordScanLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C11_B33C
            || step.Opcode != 0x37D2
            || State.Pc != 0x8C11_B33E
            || State.T
            || State.R[7] >= State.R[13]
            || ((State.R[13] - State.R[7]) & 0x3) != 0)
        {
            return false;
        }

        var remainingIterations = (State.R[13] - State.R[7]) / 4;
        var remainingByteCount = remainingIterations * 4;
        skippedInstructions = ((ulong)remainingIterations * 8) + 1;
        if (!IsDoa2EmptyStackWordScanLoop()
            || skippedInstructions > maxInstructionsToSkip
            || remainingIterations > int.MaxValue / 4
            || State.R[7] > uint.MaxValue - (remainingByteCount - 1))
        {
            skippedInstructions = 0;
            return false;
        }

        for (var offset = 0u; offset < remainingByteCount; offset += 4)
        {
            var address = State.R[7] + offset;
            if (!memory.TryGetSystemRamOffset(address, 4, out _)
                || memory.ReadUInt32(address) != 0)
            {
                skippedInstructions = 0;
                return false;
            }
        }

        while (State.R[7] < State.R[13])
        {
            State.R[2] = memory.ReadUInt32(State.R[7]);
            State.T = State.R[2] == 0;
            State.R[7] += 4;
            State.R[6] += 4;
            State.R[5] += State.R[10];
            State.T = State.R[7] >= State.R[13];
        }

        State.Pc = 0x8C11_B340;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2EmptyTaskHelperReturn(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C13_06A0
            || step.Opcode != 0x2FE6
            || State.Pc != 0x8C13_06A2
            || State.R[6] != 0)
        {
            return false;
        }

        skippedInstructions = 51;
        if (!IsDoa2EmptyTaskHelperReturn()
            || skippedInstructions > maxInstructionsToSkip
            || State.R[15] < 12
            || !memory.TryGetSystemRamOffset(State.R[15] - 12, 16, out _))
        {
            skippedInstructions = 0;
            return false;
        }

        var entryOffset = State.R[7] << 2;
        var queueValueAddress = unchecked(State.R[5] + 0xE4 + entryOffset);
        var deltaAddress = unchecked(0x8C2F_65B0u + entryOffset);
        var limitAddress = unchecked(0x8C2F_66D4u + entryOffset);
        if (!memory.TryGetSystemRamOffset(queueValueAddress, 4, out _)
            || !memory.TryGetSystemRamOffset(deltaAddress, 4, out _)
            || !memory.TryGetSystemRamOffset(limitAddress, 4, out _)
            || memory.ReadUInt32(0x8C2F_6650) != 0
            || memory.ReadUInt32(queueValueAddress) != 0
            || memory.ReadUInt32(deltaAddress) != 0
            || memory.ReadUInt32(limitAddress) != 0)
        {
            skippedInstructions = 0;
            return false;
        }

        var stackPointerAfterR14Push = State.R[15];
        var savedPr = State.Pr;
        var savedR12 = State.R[12];
        var savedR13 = State.R[13];
        var savedR14 = State.R[14];
        memory.WriteUInt32(stackPointerAfterR14Push - 4, savedR13);
        memory.WriteUInt32(stackPointerAfterR14Push - 8, savedR12);
        memory.WriteUInt32(stackPointerAfterR14Push - 12, savedPr);

        State.R[0] = 0;
        State.R[1] = 0;
        State.R[2] = 0;
        State.R[3] = 0;
        State.R[4] = 0;
        State.R[6] = 0;
        State.R[7] = unchecked(State.R[5] + 0xE4);
        State.R[12] = savedR12;
        State.R[13] = savedR13;
        State.R[14] = savedR14;
        State.R[15] = stackPointerAfterR14Push + 4;
        State.T = true;
        State.Pc = savedPr;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2EmptyTaskHelperCallerLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C13_07D6
            || step.Opcode != 0x9053
            || State.Pc != 0x8C13_07D8
            || State.R[0] != 0xE4
            || State.R[13] >= 5)
        {
            return false;
        }

        var remainingEntries = 5 - State.R[13];
        skippedInstructions = (ulong)remainingEntries * 104;
        var remainingByteCount = checked((int)remainingEntries * 4);
        if (!IsDoa2EmptyTaskHelperCallerLoop()
            || !IsDoa2EmptyTaskHelperReturn()
            || skippedInstructions > maxInstructionsToSkip
            || State.R[8] != 0
            || State.R[11] != 0
            || State.R[15] < 16
            || State.R[15] > uint.MaxValue - 12
            || !memory.TryGetSystemRamOffset(State.R[15] - 16, 28, out _)
            || memory.ReadUInt32(State.R[15] + 8) != remainingEntries
            || !memory.TryGetSystemRamOffset(State.R[12], 16, out _)
            || !memory.TryGetSystemRamOffset(State.R[14] + 0x44, remainingByteCount, out _)
            || !memory.TryGetSystemRamOffset(State.R[14] + 0xE4, remainingByteCount, out _)
            || memory.ReadUInt32(0x8C2F_7640) != 0)
        {
            skippedInstructions = 0;
            return false;
        }

        var sourceBase = memory.ReadUInt32(State.R[12]);
        if (!memory.TryGetSystemRamOffset(sourceBase + (State.R[13] * 4), remainingByteCount, out _))
        {
            skippedInstructions = 0;
            return false;
        }

        var originalStackPointer = State.R[15];
        var savedR12 = State.R[12];
        var savedR14 = State.R[14];
        for (var entry = State.R[13]; entry < 5; entry++)
        {
            var entryOffset = entry * 4;
            if (memory.ReadUInt32(sourceBase + entryOffset) != 0
                || memory.ReadUInt32(savedR14 + 0xE4 + entryOffset) != 0
                || memory.ReadUInt32(0x8C2F_6650) != 0
                || memory.ReadUInt32(0x8C2F_65B0 + entryOffset) != 0
                || memory.ReadUInt32(0x8C2F_66D4 + entryOffset) != 0)
            {
                skippedInstructions = 0;
                return false;
            }
        }

        for (var entry = State.R[13]; entry < 5; entry++)
        {
            var entryOffset = entry * 4;
            memory.WriteUInt32(savedR14 + 0x44 + entryOffset, 0);
            memory.WriteUInt32(savedR14 + 0xE4 + entryOffset, 0);
        }

        memory.WriteUInt32(originalStackPointer - 16, 0x8C13_081C);
        memory.WriteUInt32(originalStackPointer - 12, savedR12);
        memory.WriteUInt32(originalStackPointer - 8, 4);
        memory.WriteUInt32(originalStackPointer - 4, savedR14);
        memory.WriteUInt32(originalStackPointer + 8, 0);
        State.R[0] = 0;
        State.R[1] = 0;
        State.R[2] = 0;
        State.R[3] = 0;
        State.R[4] = 0;
        State.R[5] = savedR14;
        State.R[6] = 0;
        State.R[7] = savedR14 + 0xE4;
        State.R[9] = 0x10;
        State.R[12] = savedR12;
        State.R[13] = 0;
        State.R[14] = savedR14;
        State.R[15] = originalStackPointer + 12;
        State.Pr = 0x8C13_081C;
        State.T = true;
        State.Pc = 0x8C13_09BC;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2UnrolledWordCopyReturn(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_E60A
            || step.Opcode != 0x5326
            || State.Pc != 0x8C10_E60C)
        {
            return false;
        }

        const ulong skippedInstructionCount = 16;
        if (!IsDoa2UnrolledWordCopyReturn()
            || maxInstructionsToSkip < skippedInstructionCount
            || State.R[1] > uint.MaxValue - 31
            || State.R[2] > uint.MaxValue - 31
            || !memory.TryGetSystemRamOffset(State.R[1], 32, out _)
            || !memory.TryGetSystemRamOffset(State.R[2], 32, out _)
            || !memory.TryGetSystemRamOffset(State.R[15], 4, out _))
        {
            return false;
        }

        ExecuteDoa2UnrolledWordCopyReturnBody();

        skippedInstructions = skippedInstructionCount;
        State.Pc = State.Pr;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    private void ExecuteDoa2UnrolledWordCopyReturnBody()
    {
        memory.WriteUInt32(State.R[1] + 28, State.R[0]);
        State.R[0] = memory.ReadUInt32(State.R[2] + 20);
        memory.WriteUInt32(State.R[1] + 24, State.R[3]);
        State.R[3] = memory.ReadUInt32(State.R[2] + 16);
        memory.WriteUInt32(State.R[1] + 20, State.R[0]);
        State.R[0] = memory.ReadUInt32(State.R[2] + 12);
        memory.WriteUInt32(State.R[1] + 16, State.R[3]);
        State.R[3] = memory.ReadUInt32(State.R[2] + 8);
        memory.WriteUInt32(State.R[1] + 12, State.R[0]);
        State.R[0] = memory.ReadUInt32(State.R[2] + 4);
        memory.WriteUInt32(State.R[1] + 8, State.R[3]);
        State.R[3] = memory.ReadUInt32(State.R[2]);
        memory.WriteUInt32(State.R[1] + 4, State.R[0]);
        memory.WriteUInt32(State.R[1], State.R[3]);
        State.R[3] = memory.ReadUInt32(State.R[15]);
        State.R[15] += 4;
    }

    internal bool TryFastForwardDoa2AicaZeroMailboxTimeoutLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_4D56
            || step.Opcode != 0x8BF9
            || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal)
            || delayedBranchTarget is not null
            || State.Pc != 0x8C10_4D4C)
        {
            return false;
        }

        if (memory.ReadInstructionUInt16(0x8C10_4D4C) != 0x6362
            || memory.ReadInstructionUInt16(0x8C10_4D4E) != 0x2338
            || memory.ReadInstructionUInt16(0x8C10_4D50) != 0x8B06
            || memory.ReadInstructionUInt16(0x8C10_4D52) != 0x7401
            || memory.ReadInstructionUInt16(0x8C10_4D54) != 0x3452
            || memory.ReadInstructionUInt16(0x8C10_4D56) != 0x8BF9
            || memory.ReadUInt32(0x8C10_4E10) != 0xA080_005C
            || memory.ReadUInt32(0x8C10_4E14) != 0x0040_0000)
        {
            return false;
        }

        if (State.R[5] != 0x0040_0000
            || State.R[6] != 0xA080_005C
            || State.R[3] != 0
            || State.R[4] >= State.R[5]
            || State.T
            || memory.ReadUInt32(State.R[6]) != 0)
        {
            return false;
        }

        var remainingIterations = State.R[5] - State.R[4];
        const ulong instructionsPerIteration = 6;
        var iterationsToSkip = Math.Min((ulong)remainingIterations, maxInstructionsToSkip / instructionsPerIteration);
        if (iterationsToSkip == 0)
        {
            return false;
        }

        skippedInstructions = iterationsToSkip * instructionsPerIteration;
        var completed = iterationsToSkip == remainingIterations;
        State.R[3] = 0;
        State.R[4] += (uint)iterationsToSkip;
        State.T = completed;
        State.Pc = completed ? 0x8C10_4D58 : 0x8C10_4D4C;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2HighRamZeroFillLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C11_3344
            || step.Opcode != 0x8BFA
            || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal)
            || delayedBranchTarget is not null
            || State.Pc != 0x8C11_333C)
        {
            return false;
        }

        if (!IsDoa2HighRamZeroFillLoop()
            || State.R[4] != 0
            || State.R[6] != 0x8C14_D4E0
            || State.R[2] != memory.ReadUInt32(State.R[6])
            || State.R[5] < memory.ReadUInt32(0x8C14_D4D4)
            || State.R[5] >= State.R[2]
            || State.T)
        {
            return false;
        }

        var remainingIterations = State.R[2] - State.R[5];
        const ulong instructionsPerIteration = 5;
        var iterationsToSkip = Math.Min((ulong)remainingIterations, maxInstructionsToSkip / instructionsPerIteration);
        if (iterationsToSkip == 0 || iterationsToSkip > int.MaxValue)
        {
            return false;
        }

        if (!memory.TryGetSystemRamOffset(State.R[5], checked((int)iterationsToSkip), out _))
        {
            return false;
        }

        var destination = State.R[5];
        for (var index = 0u; index < (uint)iterationsToSkip; index++)
        {
            memory.Write(destination + index, [0]);
        }

        skippedInstructions = iterationsToSkip * instructionsPerIteration;
        State.R[5] += (uint)iterationsToSkip;
        var completed = State.R[5] == State.R[2];
        State.T = completed;
        State.Pc = completed ? 0x8C11_3346 : 0x8C11_333C;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2CacheBlockPurgeLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C11_1EDE
            || step.Opcode != 0x8BFB
            || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal)
            || delayedBranchTarget is not null
            || State.Pc != 0x8C11_1ED8)
        {
            return false;
        }

        if (!IsDoa2CacheBlockPurgeLoop()
            || State.R[5] == 0
            || State.T)
        {
            return false;
        }

        const ulong instructionsPerIteration = 4;
        var iterationsToSkip = Math.Min((ulong)State.R[5], maxInstructionsToSkip / instructionsPerIteration);
        if (iterationsToSkip == 0
            || iterationsToSkip > uint.MaxValue / 32
            || State.R[0] > uint.MaxValue - ((uint)iterationsToSkip * 32))
        {
            return false;
        }

        skippedInstructions = iterationsToSkip * instructionsPerIteration;
        State.R[5] -= (uint)iterationsToSkip;
        State.R[0] += (uint)iterationsToSkip * 32;
        var completed = State.R[5] == 0;
        State.T = completed;
        State.Pc = completed ? 0x8C11_1EE0 : 0x8C11_1ED8;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2ScratchVectorCopyWrapper(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_3AF0
            || step.Opcode != 0xD416
            || State.Pc != 0x8C10_3AF2
            || State.R[4] != 0x8C1C_A920)
        {
            return false;
        }

        const ulong skippedInstructionCount = 27;
        if (!IsDoa2ScratchVectorCopyWrapper()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || maxInstructionsToSkip < skippedInstructionCount
            || State.R[15] < 8
            || !memory.TryGetSystemRamOffset(State.R[15] - 8, 8, out _)
            || !memory.TryGetSystemRamOffset(0x8C1C_A920, 28, out _))
        {
            return false;
        }

        var originalPr = State.Pr;
        memory.WriteUInt32(State.R[15] - 4, originalPr);
        memory.WriteUInt32(0x8C1C_A920, State.Fr[4]);
        memory.WriteUInt32(0x8C1C_A924, State.Fr[5]);
        memory.WriteUInt32(0x8C1C_A928, State.Fr[6]);
        memory.WriteUInt32(State.R[15] - 8, 0x8C10_E5D8);
        memory.WriteUInt32(0x8C1C_A938, State.Fr[6]);
        memory.WriteUInt32(0x8C1C_A934, State.Fr[5]);
        memory.WriteUInt32(0x8C1C_A930, State.Fr[4]);

        State.R[0] = State.Fr[5];
        State.R[1] = 0x8C1C_A930;
        State.R[2] = 0x8C1C_A920;
        State.R[3] = 0x8C10_E5D8;
        State.R[4] = 0x8C1C_A920;
        State.Pc = originalPr;
        State.InstructionsExecuted += skippedInstructionCount;
        skippedInstructions = skippedInstructionCount;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2TableEntryAddressHelper(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C0F_29DE
            || step.Opcode != 0x6043
            || State.Pc != 0x8C0F_29E0
            || State.R[0] != State.R[4])
        {
            return false;
        }

        const ulong skippedInstructionCount = 10;
        if (!IsDoa2TableEntryAddressHelper()
            || maxInstructionsToSkip < skippedInstructionCount)
        {
            return false;
        }

        var value = State.R[0];
        value <<= 1;
        State.R[3] = State.R[4];
        value += State.R[3];
        State.R[2] = 0x8C2A_D770;
        value <<= 2;
        value <<= 2;
        State.T = (value & 0x8000_0000) != 0;
        value <<= 1;
        State.R[0] = value + State.R[2];

        State.Pc = State.Pr;
        State.InstructionsExecuted += skippedInstructionCount;
        skippedInstructions = skippedInstructionCount;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2ZeroStatusByteTableScan(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C0F_3678
            || step.Opcode != 0x480B
            || State.Pc != 0x8C0F_367A
            || State.Pr != 0x8C0F_367C
            || delayedBranchTarget != 0x8C0F_29DE)
        {
            return false;
        }

        const uint tableBase = 0x8C2A_D770;
        const uint tableStride = 96;
        const uint tableEntryCount = 8;
        const uint progressAddress = 0x8C2A_DA94;
        var currentIndex = State.R[14];
        if (!IsDoa2ZeroStatusByteTableScan()
            || State.R[8] != 0x8C0F_29DE
            || State.R[10] != tableEntryCount
            || currentIndex >= tableEntryCount
            || !memory.TryGetSystemRamOffset(State.R[15], 32, out _)
            || !memory.TryGetSystemRamOffset(progressAddress, 1, out _))
        {
            return false;
        }

        for (var index = currentIndex; index < tableEntryCount; index++)
        {
            var address = tableBase + (index * tableStride);
            if (!memory.TryGetSystemRamOffset(address, 1, out _) || memory.ReadByte(address) != 0)
            {
                return false;
            }
        }

        var remainingEntries = tableEntryCount - currentIndex;
        var computedSkippedInstructions = remainingEntries == 1
            ? 33UL
            : 54UL + ((ulong)remainingEntries - 2) * 21UL;
        if (maxInstructionsToSkip < computedSkippedInstructions)
        {
            return false;
        }

        var localReturnValue = State.R[12];
        memory.Write(progressAddress, [(byte)tableEntryCount]);
        State.R[0] = localReturnValue;
        State.R[2] = tableBase;
        State.R[3] = progressAddress;
        State.R[4] = tableBase + ((tableEntryCount - 1) * tableStride);
        State.T = true;
        State.Pr = memory.ReadUInt32(State.R[15]);
        State.R[8] = memory.ReadUInt32(State.R[15] + 4);
        State.R[9] = memory.ReadUInt32(State.R[15] + 8);
        State.R[10] = memory.ReadUInt32(State.R[15] + 12);
        State.R[11] = memory.ReadUInt32(State.R[15] + 16);
        State.R[12] = memory.ReadUInt32(State.R[15] + 20);
        State.R[13] = memory.ReadUInt32(State.R[15] + 24);
        State.R[14] = memory.ReadUInt32(State.R[15] + 28);
        State.R[15] += 32;
        State.Pc = State.Pr;
        State.InstructionsExecuted += computedSkippedInstructions;
        skippedInstructions = computedSkippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2ZeroRecordGroupScan(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C01_3BF6
            || step.Opcode != 0x63B3
            || State.Pc != 0x8C01_3BF8
            || State.R[3] != State.R[11])
        {
            return false;
        }

        const uint baseAddress = 0x8C1E_FFB8;
        var currentIndex = State.R[11];
        if (!IsDoa2ZeroRecordGroupScan()
            || State.R[8] != 32
            || State.R[10] != baseAddress
            || currentIndex >= 32
            || (currentIndex & 3) != 0
            || !memory.TryGetSystemRamOffset(State.R[15], 32, out _))
        {
            return false;
        }

        var firstAddress = baseAddress + (currentIndex * 8);
        var byteCount = checked((int)(((32 - currentIndex - 1) * 8) + 2));
        if (!memory.TryGetSystemRamOffset(firstAddress, byteCount, out _))
        {
            return false;
        }

        for (var index = currentIndex; index < 32; index++)
        {
            if (memory.ReadUInt16(baseAddress + (index * 8)) != 0)
            {
                return false;
            }
        }

        var groupsRemaining = (32 - currentIndex) / 4;
        var computedSkippedInstructions = groupsRemaining == 1
            ? 54UL
            : 100UL + ((ulong)groupsRemaining - 2) * 46UL;
        if (maxInstructionsToSkip < computedSkippedInstructions)
        {
            return false;
        }

        State.R[2] = 0;
        State.R[3] = baseAddress + (31 * 8);
        State.T = true;
        State.Pr = memory.ReadUInt32(State.R[15]);
        State.R[8] = memory.ReadUInt32(State.R[15] + 4);
        State.R[9] = memory.ReadUInt32(State.R[15] + 8);
        State.R[10] = memory.ReadUInt32(State.R[15] + 12);
        State.R[11] = memory.ReadUInt32(State.R[15] + 16);
        State.R[12] = memory.ReadUInt32(State.R[15] + 20);
        State.R[13] = memory.ReadUInt32(State.R[15] + 24);
        State.R[14] = memory.ReadUInt32(State.R[15] + 28);
        State.R[15] += 32;
        State.Pc = State.Pr;
        State.InstructionsExecuted += computedSkippedInstructions;
        skippedInstructions = computedSkippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2ColorPackCommonPath(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_06EC
            || step.Opcode != 0x50DD
            || State.Pc != 0x8C10_06EE)
        {
            return false;
        }

        const ulong skippedInstructionCount = 50;
        var flags = State.R[0];
        var lowFlagBits = flags & 0x30;
        var highFlagBits = flags & memory.ReadUInt32(0x8C10_0900);
        if (!IsDoa2ColorPackCommonPath()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || maxInstructionsToSkip < skippedInstructionCount
            || (lowFlagBits & State.R[9]) != 0
            || (lowFlagBits & 0x20) != 0
            || highFlagBits == memory.ReadUInt32(0x8C10_0904)
            || highFlagBits == memory.ReadUInt32(0x8C10_0900)
            || State.R[13] > uint.MaxValue - 55
            || State.R[15] > uint.MaxValue - 27
            || !memory.TryGetSystemRamOffset(State.R[13] + 24, 32, out _)
            || !memory.TryGetSystemRamOffset(State.R[15], 28, out _))
        {
            return false;
        }

        State.R[0] &= 0x30;
        State.R[4] = State.R[0];
        State.R[0] = 24;
        ExecuteFpuMove(0xF7D6, 7, 13, 0x6);
        State.T = (State.R[4] & State.R[9]) == 0;
        State.R[0] = 32;
        ExecuteFpuMove(0xF4D6, 4, 13, 0x6);
        State.R[0] = 28;
        State.R[3] = 32;
        ExecuteFpuMove(0xF5D6, 5, 13, 0x6);
        State.T = (State.R[3] & State.R[4]) == 0;
        State.R[0] = 36;
        ExecuteFpuMove(0xF6D6, 6, 13, 0x6);
        ExecuteFpuMove(0xF35C, 3, 5, 0xC);
        ExecuteFpuMove(0xF5FC, 5, 15, 0xC);
        ExecuteFpuMove(0xF531, 5, 3, 0x1);
        ExecuteFpuMove(0xF36C, 3, 6, 0xC);
        ExecuteFpuMove(0xF6FC, 6, 15, 0xC);
        ExecuteFpuMove(0xF631, 6, 3, 0x1);
        State.R[0] = flags;
        State.R[3] = memory.ReadUInt32(0x8C10_0900);
        State.R[1] = memory.ReadUInt32(0x8C10_0904);
        State.R[0] &= State.R[3];
        State.T = State.R[0] == State.R[1];
        State.R[1] = memory.ReadUInt32(0x8C10_0900);
        State.T = State.R[0] == State.R[1];
        State.R[4] = State.R[15] + 24;
        State.R[5] = memory.ReadUInt32(0x8C10_090C);
        ExecuteFpuMove(0xF47A, 4, 7, 0xA);
        State.R[3] = memory.ReadUInt32(State.R[4]);
        State.R[3] &= State.R[5];
        memory.WriteUInt32(State.R[15] + 20, State.R[3]);
        ExecuteFpuMove(0xF44A, 4, 4, 0xA);
        State.R[3] = memory.ReadUInt32(State.R[4]);
        State.R[5] &= State.R[3];
        memory.WriteUInt32(State.R[15], State.R[5]);
        ExecuteFpuMove(0xF45A, 4, 5, 0xA);
        State.R[9] = memory.ReadUInt32(State.R[4]);
        ExecuteFpuMove(0xF46A, 4, 6, 0xA);
        State.R[2] = memory.ReadUInt32(State.R[4]);
        State.R[9] >>= 16;
        State.R[2] >>= 16;
        memory.WriteUInt32(State.R[15] + 12, State.R[2]);

        skippedInstructions = skippedInstructionCount;
        State.Pc = 0x8C10_077C;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2ColorBytePackCommonPath(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_0AC0
            || step.Opcode != 0xC72B
            || State.Pc != 0x8C10_0AC2)
        {
            return false;
        }

        const ulong skippedInstructionCount = 49;
        var scaleBits = memory.ReadUInt32(0x8C10_0B70);
        var maxBits = memory.ReadUInt32(0x8C10_0B74);
        if (!IsDoa2ColorBytePackCommonPath()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || maxInstructionsToSkip < skippedInstructionCount
            || scaleBits != BitConverter.SingleToUInt32Bits(255.0f)
            || maxBits != BitConverter.SingleToUInt32Bits(2147483648.0f)
            || !IsCommonColorByte(State.Fr[4], out _)
            || !IsCommonColorByte(State.Fr[5], out _)
            || !IsCommonColorByte(State.Fr[6], out _)
            || !IsCommonColorByte(State.Fr[7], out _))
        {
            return false;
        }

        ExecuteFpuMove(0xF808, 8, 0, 0x8);
        State.R[0] = 0x8C10_0B74;
        ExecuteFpuMove(0xF908, 9, 0, 0x8);
        ExecuteFpuMove(0xF482, 4, 8, 0x2);
        ExecuteFpuMove(0xF582, 5, 8, 0x2);
        ExecuteFpuMove(0xF682, 6, 8, 0x2);
        ExecuteFpuMove(0xF782, 7, 8, 0x2);
        ExecuteFpuMove(0xF495, 4, 9, 0x5);
        ExecuteFpuMove(0xF84C, 8, 4, 0xC);
        ExecuteFpuMove(0xF38C, 3, 8, 0xC);
        State.Fpul = (uint)(int)BitConverter.UInt32BitsToSingle(State.Fr[3]);
        ExecuteFpuMove(0xF595, 5, 9, 0x5);
        State.R[4] = State.Fpul;
        ExecuteFpuMove(0xF45C, 4, 5, 0xC);
        ExecuteFpuMove(0xF34C, 3, 4, 0xC);
        State.Fpul = (uint)(int)BitConverter.UInt32BitsToSingle(State.Fr[3]);
        ExecuteFpuMove(0xF695, 6, 9, 0x5);
        State.R[5] = State.Fpul;
        ExecuteFpuMove(0xF46C, 4, 6, 0xC);
        ExecuteFpuMove(0xF34C, 3, 4, 0xC);
        State.Fpul = (uint)(int)BitConverter.UInt32BitsToSingle(State.Fr[3]);
        ExecuteFpuMove(0xF795, 7, 9, 0x5);
        State.R[6] = State.Fpul;
        ExecuteFpuMove(0xF47C, 4, 7, 0xC);
        ExecuteFpuMove(0xF34C, 3, 4, 0xC);
        State.Fpul = (uint)(int)BitConverter.UInt32BitsToSingle(State.Fr[3]);
        State.R[0] = (uint)(short)memory.ReadUInt16(0x8C10_0C7E);
        State.T = State.R[4] > State.R[0];
        State.R[7] = State.Fpul;
        State.R[1] = 0;
        State.T = State.R[5] > State.R[0];
        State.T = State.R[6] > State.R[0];
        State.T = State.R[7] > State.R[0];
        State.R[4] <<= 8;
        State.R[4] |= State.R[5];
        State.R[4] <<= 8;
        State.R[4] |= State.R[6];
        State.R[4] <<= 8;
        State.R[4] |= State.R[7];
        State.R[0] = State.R[4];

        skippedInstructions = skippedInstructionCount;
        State.Pc = State.Pr;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;

        bool IsCommonColorByte(uint bits, out uint value)
        {
            var component = BitConverter.UInt32BitsToSingle(bits);
            var scaled = component * BitConverter.UInt32BitsToSingle(scaleBits);
            if (!float.IsFinite(component)
                || !float.IsFinite(scaled)
                || scaled > BitConverter.UInt32BitsToSingle(maxBits))
            {
                value = 0;
                return false;
            }

            value = (uint)(int)scaled;
            return value <= 0xFF;
        }
    }

    internal bool TryFastForwardDoa2TaEmitCommonPath(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_077C
            || step.Opcode != 0x50DC
            || State.Pc != 0x8C10_077E)
        {
            return false;
        }

        const ulong skippedInstructionCount = 132;
        var tableIndexOffset = State.R[0] << 2;
        var tablePointerAddress = memory.ReadUInt32(0x8C10_0910);
        var tableBase = memory.ReadUInt32(tablePointerAddress);
        var tableEntryAddress = tableBase + tableIndexOffset;
        var auxTable = memory.ReadUInt32(0x8C10_0924);
        var flags = memory.ReadUInt32(State.R[13] + 52);
        if (!IsDoa2TaEmitCommonPath()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || maxInstructionsToSkip < skippedInstructionCount
            || State.R[0] == 0xFFFF_FFFF
            || State.R[0] > uint.MaxValue >> 2
            || (flags & memory.ReadUInt32(0x8C10_08FC)) != 0
            || !IsCommonDoa2TaAlpha(State.Fr[14])
            || State.R[10] > uint.MaxValue - 15
            || State.R[11] > uint.MaxValue - 15
            || State.R[12] > uint.MaxValue - 19
            || State.R[13] > uint.MaxValue - 63
            || State.R[15] > uint.MaxValue - 107
            || tableBase > uint.MaxValue - tableIndexOffset
            || !memory.TryGetSystemRamOffset(State.R[10], 16, out _)
            || !memory.TryGetSystemRamOffset(State.R[11], 16, out _)
            || !memory.TryGetSystemRamOffset(State.R[12], 20, out _)
            || !memory.TryGetSystemRamOffset(State.R[13] + 48, 16, out _)
            || !memory.TryGetSystemRamOffset(State.R[15], 108, out _)
            || !memory.TryGetSystemRamOffset(tablePointerAddress, 4, out _)
            || !memory.TryGetSystemRamOffset(tableEntryAddress, 4, out _)
            || !memory.TryGetSystemRamOffset(auxTable, 8, out _))
        {
            return false;
        }

        State.T = false;
        State.R[4] = State.R[0];
        State.R[3] = tablePointerAddress;
        State.R[4] <<= 2;
        memory.WriteUInt32(State.R[15] + 16, State.R[4]);
        State.R[0] = tableBase;
        State.R[3] = memory.ReadUInt32(0x8C10_0918);
        State.R[4] = memory.ReadUInt32(State.R[0] + State.R[4]);
        State.R[14] = memory.ReadUInt32(0x8C10_091C);
        State.R[2] = State.R[4];
        State.R[2] >>= 16;
        State.R[2] >>= 8;
        State.R[1] = memory.ReadUInt32(0x8C10_0914);
        State.R[14] &= State.R[4];
        State.R[4] = auxTable;
        WriteUInt32WithPc(State.R[1], State.R[2], 0x8C10_07AC);
        WriteUInt32WithPc(State.R[3], State.R[2], 0x8C10_07AE);
        State.R[3] = memory.ReadUInt32(0x8C10_08FC);
        State.R[0] = flags;
        State.R[2] = memory.ReadUInt32(0x8C10_0920);
        State.T = (State.R[0] & State.R[3]) == 0;
        State.R[14] |= State.R[2];

        State.R[0] = memory.ReadUInt32(State.R[12]);
        State.R[1] = memory.ReadUInt32(State.R[4]);
        State.R[0] |= State.R[1];
        WriteUInt32WithPc(State.R[14], State.R[0], 0x8C10_07D6);
        State.R[3] = memory.ReadUInt32(State.R[12] + 4);
        WriteUInt32WithPc(State.R[14] + 4, State.R[3], 0x8C10_07DA);
        State.R[2] = memory.ReadUInt32(State.R[12] + 8);
        State.R[3] = memory.ReadUInt32(State.R[4] + 4);
        State.R[2] |= State.R[3];
        WriteUInt32WithPc(State.R[14] + 8, State.R[2], 0x8C10_07E2);
        State.R[3] = memory.ReadUInt32(State.R[12] + 12);
        WriteUInt32WithPc(State.R[14] + 12, State.R[3], 0x8C10_07E6);
        State.R[2] = memory.ReadUInt32(State.R[12] + 16);
        WriteUInt32WithPc(State.R[14] + 16, State.R[2], 0x8C10_07EA);

        State.Pr = 0x8C10_07F0;
        ExecuteFpuMove(0xF4EC, 4, 14, 0xC);
        State.R[0] = 0x8C10_0D3C;
        ExecuteFpuMove(0xF308, 3, 0, 0x8);
        State.R[0] = 0x8C10_0D40;
        ExecuteFpuMove(0xF108, 1, 0, 0x8);
        ExecuteFpuMove(0xF432, 4, 3, 0x2);
        ExecuteFpuMove(0xF415, 4, 1, 0x5);
        ExecuteFpuMove(0xF54C, 5, 4, 0xC);
        ExecuteFpuMove(0xF25C, 2, 5, 0xC);
        State.Fpul = (uint)(int)BitConverter.UInt32BitsToSingle(State.Fr[2]);
        State.R[5] = (uint)(short)memory.ReadUInt16(0x8C10_0D32);
        State.R[4] = State.Fpul;
        State.T = State.R[4] > State.R[5];
        State.R[0] = State.R[4];

        State.R[2] = memory.ReadUInt32(State.R[13] + 60);
        State.R[0] <<= 16;
        State.R[0] <<= 8;
        State.R[0] |= State.R[2];
        WriteUInt32WithPc(State.R[14] + 20, State.R[0], 0x8C10_07F8);
        State.R[3] = memory.ReadUInt32(0x8C10_0928);
        PrefetchWithPc(State.R[14], 0x8C10_07FA);
        State.R[14] += 32;
        State.R[4] = State.R[10];
        State.R[5] = State.R[11];
        WriteUInt32WithPc(State.R[14], State.R[3], 0x8C10_0804);
        State.R[0] = 12;
        State.R[2] = memory.ReadUInt32(State.R[5]);
        WriteUInt32WithPc(State.R[14] + 4, State.R[2], 0x8C10_080A);
        State.R[3] = memory.ReadUInt32(State.R[4]);
        WriteUInt32WithPc(State.R[14] + 8, State.R[3], 0x8C10_080E);
        WriteUInt32WithPc(State.R[14] + State.R[0], State.Fr[13], 0x8C10_0810);
        State.R[0] = 24;
        State.R[3] = memory.ReadUInt32(State.R[5] + 4);
        WriteUInt32WithPc(State.R[14] + 16, State.R[3], 0x8C10_0816);
        State.R[2] = memory.ReadUInt32(State.R[4] + 4);
        WriteUInt32WithPc(State.R[14] + 20, State.R[2], 0x8C10_081A);
        WriteUInt32WithPc(State.R[14] + State.R[0], State.Fr[13], 0x8C10_081C);
        State.R[0] = 36;
        State.R[3] = memory.ReadUInt32(State.R[5] + 8);
        WriteUInt32WithPc(State.R[14] + 28, State.R[3], 0x8C10_0822);
        State.R[2] = memory.ReadUInt32(State.R[4] + 8);
        WriteUInt32WithPc(State.R[14] + 32, State.R[2], 0x8C10_0826);
        WriteUInt32WithPc(State.R[14] + State.R[0], State.Fr[13], 0x8C10_0828);
        State.R[3] = memory.ReadUInt32(State.R[5] + 12);
        WriteUInt32WithPc(State.R[14] + 40, State.R[3], 0x8C10_082C);
        State.R[2] = memory.ReadUInt32(State.R[4] + 12);
        State.R[3] = memory.ReadUInt32(0x8C10_092C);
        WriteUInt32WithPc(State.R[14] + 44, State.R[2], 0x8C10_0832);
        State.R[8] |= State.R[3];
        WriteUInt32WithPc(State.R[14] + 48, State.R[8], 0x8C10_0836);
        State.R[2] = memory.ReadUInt32(State.R[15] + 20);
        State.R[2] |= State.R[9];
        WriteUInt32WithPc(State.R[14] + 52, State.R[2], 0x8C10_083C);
        State.R[1] = memory.ReadUInt32(State.R[15]);
        State.R[9] |= State.R[1];
        WriteUInt32WithPc(State.R[14] + 56, State.R[9], 0x8C10_0842);
        State.R[2] = memory.ReadUInt32(State.R[15] + 12);
        State.R[1] = memory.ReadUInt32(State.R[15]);
        State.R[1] |= State.R[2];
        WriteUInt32WithPc(State.R[14] + 60, State.R[1], 0x8C10_084A);
        PrefetchWithPc(State.R[14], 0x8C10_084C);
        State.R[14] += 32;
        State.R[1] = tablePointerAddress;
        PrefetchWithPc(State.R[14], 0x8C10_0850);
        State.R[14] += 32;
        State.R[4] = memory.ReadUInt32(State.R[15] + 16);
        State.R[2] = memory.ReadUInt32(State.R[1]);
        State.R[3] = memory.ReadUInt32(0x8C10_0930);
        State.R[4] += State.R[2];
        State.R[2] = memory.ReadUInt32(0x8C10_091C);
        State.R[0] = memory.ReadUInt32(State.R[4]);
        State.R[14] &= State.R[2];
        State.R[0] &= State.R[3];
        State.R[0] |= State.R[14];
        memory.WriteUInt32(State.R[4], State.R[0]);
        State.R[0] = 0;
        State.R[15] += 60;
        State.Pr = memory.ReadUInt32(State.R[15]);
        State.R[15] += 4;
        State.Fr[12] = memory.ReadUInt32(State.R[15]);
        State.R[15] += 4;
        State.Fr[13] = memory.ReadUInt32(State.R[15]);
        State.R[15] += 4;
        State.Fr[14] = memory.ReadUInt32(State.R[15]);
        State.R[15] += 4;
        State.Fr[15] = memory.ReadUInt32(State.R[15]);
        State.R[15] += 4;
        State.R[8] = memory.ReadUInt32(State.R[15]);
        State.R[15] += 4;
        State.R[9] = memory.ReadUInt32(State.R[15]);
        State.R[15] += 4;
        State.R[10] = memory.ReadUInt32(State.R[15]);
        State.R[15] += 4;
        State.R[11] = memory.ReadUInt32(State.R[15]);
        State.R[15] += 4;
        State.R[12] = memory.ReadUInt32(State.R[15]);
        State.R[15] += 4;
        State.R[13] = memory.ReadUInt32(State.R[15]);
        State.R[15] += 4;
        State.R[14] = memory.ReadUInt32(State.R[15]);
        State.R[15] += 4;

        skippedInstructions = skippedInstructionCount;
        State.Pc = State.Pr;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;

        void WriteUInt32WithPc(uint address, uint value, uint pc)
        {
            memory.CurrentInstructionPc = pc;
            memory.WriteUInt32(address, value);
        }

        void PrefetchWithPc(uint address, uint pc)
        {
            memory.CurrentInstructionPc = pc;
            memory.Prefetch(address);
        }

        bool IsCommonDoa2TaAlpha(uint bits)
        {
            var alpha = BitConverter.UInt32BitsToSingle(bits);
            var scaled = alpha * BitConverter.UInt32BitsToSingle(memory.ReadUInt32(0x8C10_0D3C));
            if (!float.IsFinite(alpha)
                || !float.IsFinite(scaled)
                || scaled > BitConverter.UInt32BitsToSingle(memory.ReadUInt32(0x8C10_0D40)))
            {
                return false;
            }

            return (uint)(int)scaled <= 0xFF;
        }
    }

    internal bool TryFastForwardDoa2TextGlyphSetupCommonPath(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C0E_1E08
            || step.Opcode != 0x64E0
            || State.Pc != 0x8C0E_1E0A)
        {
            return false;
        }

        const ulong skippedInstructionCount = 62;
        var character = State.R[4] & 0xFF;
        if (!IsDoa2TextGlyphSetupCommonPath()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || maxInstructionsToSkip < skippedInstructionCount
            || State.R[9] != 0x8C10_0430
            || unchecked((int)character) < unchecked((int)State.R[10])
            || character < 32
            || character == 0x40
            || State.R[8] > uint.MaxValue - ((character - 32) * 20)
            || State.R[13] > uint.MaxValue - 25
            || State.R[15] > uint.MaxValue - 39)
        {
            return false;
        }

        var glyphAddress = State.R[8] + ((character - 32) * 20);
        if (!memory.TryGetSystemRamOffset(State.R[14], 1, out _)
            || !memory.TryGetSystemRamOffset(glyphAddress, 18, out _)
            || !memory.TryGetSystemRamOffset(State.R[13] + 22, 4, out _)
            || !memory.TryGetSystemRamOffset(State.R[15], 40, out _))
        {
            return false;
        }

        State.R[4] = character;
        State.T = unchecked((int)State.R[4]) >= unchecked((int)State.R[10]);
        State.R[2] = (uint)(short)memory.ReadInstructionUInt16(0x8C0E_1EC2);
        State.T = unchecked((int)State.R[4]) > unchecked((int)State.R[2]);
        State.R[0] = (uint)(sbyte)memory.ReadByte(State.R[14]);
        State.R[0] &= 0xFF;
        State.T = unchecked((int)State.R[0]) == 0x40;

        State.R[12] = (uint)(sbyte)memory.ReadByte(State.R[14]);
        State.R[0] = 24;
        memory.WriteUInt32(State.R[15], State.R[11]);
        State.R[12] &= 0xFF;
        State.R[12] += unchecked((uint)-32);
        State.R[3] = State.R[12];
        State.R[12] <<= 2;
        State.R[12] += State.R[3];
        State.R[12] <<= 2;
        State.R[12] += State.R[8];

        State.Fr[3] = memory.ReadUInt32(State.R[12]);
        memory.WriteUInt32(State.R[15] + 24, State.Fr[3]);
        State.R[0] = 8;
        State.Fr[3] = memory.ReadUInt32(State.R[12] + 8);
        State.R[0] = 28;
        memory.WriteUInt32(State.R[15] + 28, State.Fr[3]);
        State.R[0] = 4;
        State.Fr[3] = memory.ReadUInt32(State.R[12] + 4);
        State.R[0] = 32;
        memory.WriteUInt32(State.R[15] + 32, State.Fr[3]);
        State.R[0] = 12;
        State.Fr[3] = memory.ReadUInt32(State.R[12] + 12);
        State.R[0] = 36;
        memory.WriteUInt32(State.R[15] + 36, State.Fr[3]);

        State.R[0] = 16;
        State.R[3] = (uint)(sbyte)memory.ReadByte(State.R[12] + State.R[0]);
        State.R[0] = 16;
        State.Fpul = State.R[3];
        State.Fr[3] = BitConverter.SingleToUInt32Bits(unchecked((int)State.Fpul));
        ExecuteFpuMove(0xF3E2, 3, 14, 0x2);
        memory.WriteUInt32(State.R[15] + State.R[0], State.Fr[3]);

        State.R[0] = 17;
        State.R[3] = (uint)(sbyte)memory.ReadByte(State.R[12] + State.R[0]);
        State.R[0] = 20;
        State.Fpul = State.R[3];
        State.Fr[3] = BitConverter.SingleToUInt32Bits(unchecked((int)State.Fpul));
        ExecuteFpuMove(0xF3F2, 3, 15, 0x2);
        memory.WriteUInt32(State.R[15] + State.R[0], State.Fr[3]);

        State.R[0] = (uint)(short)memory.ReadUInt16(State.R[13] + 22);
        State.R[3] = State.R[0];
        State.Fpul = State.R[3];
        State.R[0] = 4;
        State.Fr[3] = BitConverter.SingleToUInt32Bits(unchecked((int)State.Fpul));
        memory.WriteUInt32(State.R[15] + State.R[0], State.Fr[3]);

        State.R[0] = (uint)(short)memory.ReadUInt16(State.R[13] + 24);
        State.R[3] = State.R[0];
        State.Fpul = State.R[3];
        State.R[0] = 8;
        State.Fr[3] = BitConverter.SingleToUInt32Bits(unchecked((int)State.Fpul));
        memory.WriteUInt32(State.R[15] + State.R[0], State.Fr[3]);

        State.Pr = 0x8C0E_1EB2;
        State.R[4] = State.R[15];

        skippedInstructions = skippedInstructionCount;
        State.Pc = State.R[9];
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2TextAdvanceToNextGlyph(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C0E_1EB2
            || step.Opcode != 0xE010
            || State.Pc != 0x8C0E_1EB4)
        {
            return false;
        }

        const ulong skippedInstructionCount = 9;
        if (!IsDoa2TextAdvanceToNextGlyph()
            || maxInstructionsToSkip < skippedInstructionCount
            || State.R[12] > uint.MaxValue - 16
            || State.R[13] > uint.MaxValue - 23
            || State.R[14] == uint.MaxValue
            || !memory.TryGetSystemRamOffset(State.R[12] + 16, 1, out _)
            || !memory.TryGetSystemRamOffset(State.R[13] + 22, 2, out _)
            || !memory.TryGetSystemRamOffset(State.R[14] + 1, 1, out _))
        {
            return false;
        }

        var nextCharacter = (uint)(sbyte)memory.ReadByte(State.R[14] + 1);
        if (nextCharacter == 0)
        {
            return false;
        }

        State.R[3] = (uint)(sbyte)memory.ReadByte(State.R[12] + State.R[0]);
        State.R[0] = (uint)(short)memory.ReadUInt16(State.R[13] + 22);
        State.R[0] += State.R[3];
        memory.WriteUInt16(State.R[13] + 22, (ushort)State.R[0]);
        State.R[14]++;
        State.R[3] = nextCharacter;
        State.T = (State.R[3] & State.R[3]) == 0;

        skippedInstructions = skippedInstructionCount;
        State.Pc = 0x8C0E_1E08;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2Fac40TrigArgumentWrapper(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C0F_AC40
            || step.Opcode != 0xD338
            || State.Pc != 0x8C0F_AC42)
        {
            return false;
        }

        const ulong skippedInstructionCount = 8;
        var argument = State.R[4] & 0xFFFF;
        if (!IsDoa2Fac40TrigArgumentWrapper()
            || maxInstructionsToSkip < skippedInstructionCount
            || unchecked((int)argument) > unchecked((int)State.R[3])
            || State.R[15] < 4
            || !memory.TryGetSystemRamOffset(State.R[15] - 4, 4, out _))
        {
            return false;
        }

        ExecuteDoa2Fac40TrigArgumentWrapperBody(argument);

        skippedInstructions = skippedInstructionCount;
        State.Pc = 0x8C0F_B1C0;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    private void ExecuteDoa2Fac40TrigArgumentWrapperBody(uint argument)
    {
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.R[14]);
        State.R[14] = argument;
        State.T = unchecked((int)State.R[14]) > unchecked((int)State.R[3]);
        State.R[4] = (uint)(0x4000 - State.R[14]);
        State.R[14] = memory.ReadUInt32(State.R[15]);
        State.R[15] += 4;
    }

    internal bool TryFastForwardDoa2RendererPrologueCommonPath(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_0430
            || step.Opcode != 0x2FE6
            || State.Pc != 0x8C10_0432)
        {
            return false;
        }

        const ulong skippedInstructionCount = 19;
        var limitAddress = memory.ReadUInt32(0x8C10_0494);
        if (!IsDoa2RendererPrologueCommonPath()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || maxInstructionsToSkip < skippedInstructionCount
            || State.R[15] < 104
            || State.R[4] > uint.MaxValue - 3
            || !memory.TryGetSystemRamOffset(State.R[15] - 104, 108, out _)
            || !memory.TryGetSystemRamOffset(State.R[4], 4, out _)
            || !memory.TryGetSystemRamOffset(limitAddress, 4, out _))
        {
            return false;
        }

        var row = memory.ReadUInt32(State.R[4]);
        var limit = memory.ReadUInt32(limitAddress);
        if (unchecked((int)row) >= unchecked((int)limit))
        {
            return false;
        }

        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.R[13]);
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.R[12]);
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.R[11]);
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.R[10]);
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.R[9]);
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.R[8]);
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.Fr[15]);
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.Fr[14]);
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.Fr[13]);
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.Fr[12]);
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.Pr);
        State.R[15] += unchecked((uint)-60);

        State.R[1] = limitAddress;
        State.R[13] = State.R[4];
        State.R[2] = row;
        State.R[3] = limit;
        State.T = unchecked((int)State.R[2]) >= unchecked((int)State.R[3]);
        ExecuteFpuMove(0xFF9D, 15, 9, 0xD);

        skippedInstructions = skippedInstructionCount;
        State.Pc = 0x8C10_04A0;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2RendererMode2EntryToFirstTrigCall(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_0430
            || step.Opcode != 0x2FE6
            || State.Pc != 0x8C10_0432)
        {
            return false;
        }

        const ulong skippedInstructionCount = 67;
        var limitAddress = memory.ReadUInt32(0x8C10_0494);
        if (!IsDoa2RendererPrologueCommonPath()
            || !IsDoa2RendererMode2LookupCommonPath()
            || !IsDoa2RendererMode2TrigSetupToFirstCall()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || maxInstructionsToSkip < skippedInstructionCount
            || State.R[15] < 104
            || State.R[4] > uint.MaxValue - 51
            || !memory.TryGetSystemRamOffset(State.R[15] - 104, 108, out _)
            || !memory.TryGetSystemRamOffset(State.R[4], 52, out _)
            || !memory.TryGetSystemRamOffset(limitAddress, 4, out _))
        {
            return false;
        }

        var row = memory.ReadUInt32(State.R[4]);
        var limit = memory.ReadUInt32(limitAddress);
        if (unchecked((int)row) >= unchecked((int)limit))
        {
            return false;
        }

        var indexTablePointerAddress = memory.ReadUInt32(0x8C10_0578);
        if (!memory.TryGetSystemRamOffset(indexTablePointerAddress, 4, out _))
        {
            return false;
        }

        var indexTable = memory.ReadUInt32(indexTablePointerAddress);
        var doubledRow = row << 1;
        if (indexTable > uint.MaxValue - doubledRow
            || !memory.TryGetSystemRamOffset(indexTable + doubledRow, 2, out _))
        {
            return false;
        }

        var index = (uint)(short)memory.ReadUInt16(indexTable + doubledRow);
        if (index == 0xFFFF_FFFF)
        {
            return false;
        }

        var renderTablePointerAddress = memory.ReadUInt32(0x8C10_057C);
        if (!memory.TryGetSystemRamOffset(renderTablePointerAddress, 4, out _))
        {
            return false;
        }

        var mode = memory.ReadUInt32(State.R[4] + 48);
        if (mode != 2)
        {
            return false;
        }

        var renderBase = memory.ReadUInt32(renderTablePointerAddress);
        var renderEntry = unchecked(renderBase + (index * 32));
        if (renderEntry > uint.MaxValue - 31
            || State.R[15] > uint.MaxValue - 7
            || !memory.TryGetSystemRamOffset(renderEntry + 24, 8, out _)
            || !memory.TryGetSystemRamOffset(State.R[15] + 4, 4, out _))
        {
            return false;
        }

        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.R[13]);
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.R[12]);
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.R[11]);
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.R[10]);
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.R[9]);
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.R[8]);
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.Fr[15]);
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.Fr[14]);
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.Fr[13]);
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.Fr[12]);
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.Pr);
        State.R[15] += unchecked((uint)-60);

        State.R[1] = limitAddress;
        State.R[13] = State.R[4];
        State.R[2] = row;
        State.R[3] = limit;
        State.T = unchecked((int)State.R[2]) >= unchecked((int)State.R[3]);
        ExecuteFpuMove(0xFF9D, 15, 9, 0xD);

        State.R[3] = memory.ReadUInt32(0x8C10_0578);
        State.R[8] = row;
        State.R[0] = indexTable;
        State.T = (State.R[8] & 0x8000_0000) != 0;
        State.R[8] <<= 1;
        State.R[8] = index;
        State.R[0] = State.R[8];
        State.T = State.R[0] == 0xFFFF_FFFF;
        State.R[2] = renderTablePointerAddress;
        State.R[12] = State.R[8];
        State.R[12] <<= 2;
        State.R[0] = mode;
        State.R[3] = renderBase;
        State.R[12] <<= 2;
        State.T = (State.R[12] & 0x8000_0000) != 0;
        State.R[12] <<= 1;
        State.T = State.R[0] == 0;
        State.R[12] += State.R[3];
        State.R[4] = 0;
        State.T = State.R[0] == 2;

        State.R[0] = 44;
        ExecuteFpuMove(0xFED6, 14, 13, 0x6);
        State.R[14] = 1;
        State.R[0] = 0x8C10_0580;
        ExecuteFpuMove(0xFDFC, 13, 15, 0xC);
        ExecuteFpuMove(0xF408, 4, 0, 0x8);
        State.R[0] = 16;
        ExecuteFpuMove(0xF3D6, 3, 13, 0x6);
        State.R[0] = 24;
        ExecuteFpuMove(0xF2C6, 2, 12, 0x6);
        State.R[0] = 20;
        State.R[3] = memory.ReadUInt32(0x8C10_0584);
        ExecuteFpuMove(0xF232, 2, 3, 0x2);
        ExecuteFpuMove(0xF3D6, 3, 13, 0x6);
        State.R[0] = 28;
        ExecuteFpuMove(0xFC2C, 12, 2, 0xC);
        ExecuteFpuMove(0xFC42, 12, 4, 0x2);
        ExecuteFpuMove(0xF2C6, 2, 12, 0x6);
        State.R[0] = 4;
        ExecuteFpuMove(0xF232, 2, 3, 0x2);
        ExecuteFpuMove(0xF242, 2, 4, 0x2);
        ExecuteFpuMove(0xFF27, 15, 2, 0x7);
        State.R[0] = 12;
        ExecuteFpuMove(0xF3D6, 3, 13, 0x6);
        ExecuteFpuMove(0xFD33, 13, 3, 0x3);
        State.Pr = 0x8C10_0536;
        State.R[4] = memory.ReadUInt32(State.R[13] + 40);

        skippedInstructions = skippedInstructionCount;
        State.Pc = State.R[3];
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2RendererMode2LookupCommonPath(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_04A0
            || step.Opcode != 0xD335
            || State.Pc != 0x8C10_04A2)
        {
            return false;
        }

        const ulong skippedInstructionCount = 20;
        if (!IsDoa2RendererMode2LookupCommonPath()
            || maxInstructionsToSkip < skippedInstructionCount
            || State.R[13] > uint.MaxValue - 51
            || !memory.TryGetSystemRamOffset(State.R[13], 52, out _)
            || !memory.TryGetSystemRamOffset(State.R[3], 4, out _))
        {
            return false;
        }

        var indexTablePointerAddress = State.R[3];
        var row = memory.ReadUInt32(State.R[13]);
        var indexTable = memory.ReadUInt32(indexTablePointerAddress);
        var doubledRow = row << 1;
        if (indexTable > uint.MaxValue - doubledRow
            || !memory.TryGetSystemRamOffset(indexTable + doubledRow, 2, out _))
        {
            return false;
        }

        var indexAddress = indexTable + doubledRow;
        var index = (uint)(short)memory.ReadUInt16(indexAddress);
        if (index == 0xFFFF_FFFF)
        {
            return false;
        }

        var renderTablePointerAddress = memory.ReadUInt32(0x8C10_057C);
        if (!memory.TryGetSystemRamOffset(renderTablePointerAddress, 4, out _))
        {
            return false;
        }

        var mode = memory.ReadUInt32(State.R[13] + 48);
        if (mode != 2)
        {
            return false;
        }

        var renderBase = memory.ReadUInt32(renderTablePointerAddress);
        State.R[8] = row;
        State.R[0] = indexTable;
        State.T = (State.R[8] & 0x8000_0000) != 0;
        State.R[8] <<= 1;
        State.R[8] = index;
        State.R[0] = State.R[8];
        State.T = State.R[0] == 0xFFFF_FFFF;
        State.R[2] = renderTablePointerAddress;
        State.R[12] = State.R[8];
        State.R[12] <<= 2;
        State.R[0] = mode;
        State.R[3] = renderBase;
        State.R[12] <<= 2;
        State.T = (State.R[12] & 0x8000_0000) != 0;
        State.R[12] <<= 1;
        State.T = State.R[0] == 0;
        State.R[12] += State.R[3];
        State.R[4] = 0;
        State.T = State.R[0] == 2;

        skippedInstructions = skippedInstructionCount;
        State.Pc = 0x8C10_0500;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2RendererMode2TrigSetupToFirstCall(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_0500
            || step.Opcode != 0xE02C
            || State.Pc != 0x8C10_0502)
        {
            return false;
        }

        const ulong skippedInstructionCount = 26;
        if (!IsDoa2RendererMode2TrigSetupToFirstCall()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || maxInstructionsToSkip < skippedInstructionCount
            || State.R[13] > uint.MaxValue - 47
            || State.R[12] > uint.MaxValue - 31
            || State.R[15] > uint.MaxValue - 7
            || !memory.TryGetSystemRamOffset(State.R[13], 48, out _)
            || !memory.TryGetSystemRamOffset(State.R[12] + 24, 8, out _)
            || !memory.TryGetSystemRamOffset(State.R[15] + 4, 4, out _))
        {
            return false;
        }

        ExecuteFpuMove(0xFED6, 14, 13, 0x6);
        State.R[14] = 1;
        State.R[0] = 0x8C10_0580;
        ExecuteFpuMove(0xFDFC, 13, 15, 0xC);
        ExecuteFpuMove(0xF408, 4, 0, 0x8);
        State.R[0] = 16;
        ExecuteFpuMove(0xF3D6, 3, 13, 0x6);
        State.R[0] = 24;
        ExecuteFpuMove(0xF2C6, 2, 12, 0x6);
        State.R[0] = 20;
        State.R[3] = memory.ReadUInt32(0x8C10_0584);
        ExecuteFpuMove(0xF232, 2, 3, 0x2);
        ExecuteFpuMove(0xF3D6, 3, 13, 0x6);
        State.R[0] = 28;
        ExecuteFpuMove(0xFC2C, 12, 2, 0xC);
        ExecuteFpuMove(0xFC42, 12, 4, 0x2);
        ExecuteFpuMove(0xF2C6, 2, 12, 0x6);
        State.R[0] = 4;
        ExecuteFpuMove(0xF232, 2, 3, 0x2);
        ExecuteFpuMove(0xF242, 2, 4, 0x2);
        ExecuteFpuMove(0xFF27, 15, 2, 0x7);
        State.R[0] = 12;
        ExecuteFpuMove(0xF3D6, 3, 13, 0x6);
        ExecuteFpuMove(0xFD33, 13, 3, 0x3);
        State.Pr = 0x8C10_0536;
        State.R[4] = memory.ReadUInt32(State.R[13] + 40);

        skippedInstructions = skippedInstructionCount;
        State.Pc = State.R[3];
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2RendererSecondTrigCallBridge(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_0536
            || step.Opcode != 0xD314
            || State.Pc != 0x8C10_0538)
        {
            return false;
        }

        const ulong skippedInstructionCount = 4;
        if (!IsDoa2RendererSecondTrigCallBridge()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || maxInstructionsToSkip < skippedInstructionCount
            || State.R[15] > uint.MaxValue - 11
            || State.R[13] > uint.MaxValue - 43
            || State.R[3] != 0x8C0F_AC40
            || !memory.TryGetSystemRamOffset(State.R[15] + 8, 4, out _)
            || !memory.TryGetSystemRamOffset(State.R[13] + 40, 4, out _))
        {
            return false;
        }

        ExecuteDoa2RendererSecondTrigCallBridgeBody();

        skippedInstructions = skippedInstructionCount;
        State.Pc = State.R[3];
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2RendererTrigPairToInterpolation(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C0F_B1C0
            || step.Opcode != 0x644D
            || State.Pc != 0x8C0F_B1C2
            || State.Pr != 0x8C10_0536)
        {
            return false;
        }

        const ulong maxSkippedInstructionCount = 203;
        if (State.R[15] < 4
            || State.R[15] > uint.MaxValue - 47
            || State.R[13] > uint.MaxValue - 43
            || !memory.TryGetSystemRamOffset(State.R[15] - 4, 4, out _)
            || !memory.TryGetSystemRamOffset(State.R[15] + 4, 8, out _)
            || !memory.TryGetSystemRamOffset(State.R[15] + 8, 4, out _)
            || !memory.TryGetSystemRamOffset(State.R[13] + 40, 4, out _))
        {
            return false;
        }

        var secondTrigCallTarget = memory.ReadUInt32(0x8C10_0588);
        var fac40Limit = memory.ReadUInt32(0x8C0F_AD24);
        var secondArgument = memory.ReadUInt32(State.R[13] + 40) & 0xFFFF;
        if (!IsDoa2TrigSetupAndRecurrenceLoop()
            || !IsDoa2PostTrigHelperReturn()
            || !IsDoa2RendererSecondTrigCallBridge()
            || !IsDoa2Fac40TrigArgumentWrapper()
            || !IsDoa2RendererPostSecondTrigBridge()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || maxInstructionsToSkip < maxSkippedInstructionCount
            || secondTrigCallTarget != 0x8C0F_AC40
            || unchecked((int)secondArgument) > unchecked((int)fac40Limit)
            || !memory.TryGetSystemRamOffset(0x8C0F_B23C, 16, out _))
        {
            return false;
        }

        ExecuteDoa2TrigSetupAndRecurrenceLoopBody();
        State.T = (State.R[3] & State.R[6]) == 0;
        var firstPostSkippedInstructionCount = ExecuteDoa2PostTrigHelperReturnBodyAfterTst();

        State.R[3] = secondTrigCallTarget;
        ExecuteDoa2RendererSecondTrigCallBridgeBody();

        State.R[3] = fac40Limit;
        var argument = State.R[4] & 0xFFFF;
        ExecuteDoa2Fac40TrigArgumentWrapperBody(argument);

        State.R[4] &= 0xFFFF;
        ExecuteDoa2TrigSetupAndRecurrenceLoopBody();
        State.T = (State.R[3] & State.R[6]) == 0;
        var secondPostSkippedInstructionCount = ExecuteDoa2PostTrigHelperReturnBodyAfterTst();

        State.R[0] = 8;
        ExecuteDoa2RendererPostSecondTrigBridgeBody();

        skippedInstructions = 74
            + 1
            + firstPostSkippedInstructionCount
            + 5
            + 9
            + 1
            + 74
            + 1
            + secondPostSkippedInstructionCount
            + 14;
        State.Pc = 0x8C10_0A30;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2RendererPostSecondTrigBridge(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_0540
            || step.Opcode != 0xE008
            || State.Pc != 0x8C10_0542)
        {
            return false;
        }

        const ulong skippedInstructionCount = 13;
        if (!IsDoa2RendererPostSecondTrigBridge()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || maxInstructionsToSkip < skippedInstructionCount
            || State.R[15] > uint.MaxValue - 47
            || !memory.TryGetSystemRamOffset(State.R[15] + 4, 8, out _))
        {
            return false;
        }

        ExecuteDoa2RendererPostSecondTrigBridgeBody();

        skippedInstructions = skippedInstructionCount;
        State.Pc = 0x8C10_0A30;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    private void ExecuteDoa2RendererSecondTrigCallBridgeBody()
    {
        State.R[0] = 8;
        ExecuteFpuMove(0xFF07, 15, 0, 0x7);
        State.Pr = 0x8C10_0540;
        State.R[4] = memory.ReadUInt32(State.R[13] + 40);
    }

    private void ExecuteDoa2RendererPostSecondTrigBridgeBody()
    {
        State.R[10] = State.R[15];
        ExecuteFpuMove(0xF6F6, 6, 15, 0x6);
        State.R[0] = 4;
        State.R[11] = State.R[15];
        State.R[10] += 28;
        ExecuteFpuMove(0xF5F6, 5, 15, 0x6);
        State.R[11] += 44;
        State.R[6] = State.R[10];
        State.R[5] = State.R[11];
        ExecuteFpuMove(0xF70C, 7, 0, 0xC);
        ExecuteFpuMove(0xF4CC, 4, 12, 0xC);
        State.Pr = 0x8C10_055C;
        State.R[4] = State.R[13];
    }

    internal bool TryFastForwardDoa2RendererPostCallScaleSetup(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_055C
            || step.Opcode != 0xD20D
            || State.Pc != 0x8C10_055E)
        {
            return false;
        }

        const ulong skippedInstructionCount = 12;
        if (!IsDoa2RendererPostCallScaleSetup()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || maxInstructionsToSkip < skippedInstructionCount
            || State.R[2] != 0x8C1C_A8D8
            || !memory.TryGetSystemRamOffset(State.R[2], 4, out _)
            || !memory.TryGetSystemRamOffset(0x8C1C_A8D4, 4, out _)
            || !memory.TryGetSystemRamOffset(0x8C2F_07E0, 8, out _))
        {
            return false;
        }

        State.R[0] = 4;
        State.R[3] = memory.ReadUInt32(0x8C10_0590);
        State.R[4] = memory.ReadUInt32(0x8C10_058C);
        ExecuteFpuMove(0xF228, 2, 2, 0x8);
        ExecuteFpuMove(0xF546, 5, 4, 0x6);
        ExecuteFpuMove(0xF338, 3, 3, 0x8);
        ExecuteFpuMove(0xF448, 4, 4, 0x8);
        ExecuteFpuMove(0xF522, 5, 2, 0x2);
        State.R[4] = 0;
        ExecuteFpuMove(0xF432, 4, 3, 0x2);
        State.R[9] = 16;

        skippedInstructions = skippedInstructionCount;
        State.Pc = 0x8C10_05B0;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2RendererModeWordSetupToColorPack(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_05B4
            || step.Opcode != 0x62C2
            || State.Pc != 0x8C10_05B6)
        {
            return false;
        }

        const ulong skippedInstructionCount = 68;
        if (!IsDoa2RendererModeWordSetupToColorPack()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || maxInstructionsToSkip < skippedInstructionCount
            || State.R[12] > uint.MaxValue - 11
            || State.R[13] > uint.MaxValue - 55
            || !memory.TryGetSystemRamOffset(State.R[12], 12, out _)
            || !memory.TryGetSystemRamOffset(State.R[13] + 48, 8, out _)
            || !memory.TryGetSystemRamOffset(0x8C1C_A5D8, 4, out _)
            || !memory.TryGetSystemRamOffset(0x8C1C_A920, 12, out _))
        {
            return false;
        }

        var mode = memory.ReadUInt32(State.R[13] + 48);
        var modeFlags = memory.ReadUInt32(State.R[13] + 52);
        if ((modeFlags & 0x800) == 0
            || (modeFlags & 0x1000) != 0
            || (modeFlags & 0x1_0000) != 0
            || ((modeFlags >> 8) & 7) != 0
            || State.R[14] == 0
            || mode == 4
            || (modeFlags & 0x2000) == 0)
        {
            return false;
        }

        State.R[3] = memory.ReadUInt32(0x8C10_067C);
        State.R[2] &= State.R[3];
        State.R[3] = (uint)(short)memory.ReadUInt16(0x8C10_0674);
        memory.WriteUInt32(State.R[12], State.R[2]);
        State.R[1] = memory.ReadUInt32(State.R[12] + 4);
        State.R[2] = memory.ReadUInt32(0x8C10_0680);
        State.R[1] &= State.R[2];
        memory.WriteUInt32(State.R[12] + 4, State.R[1]);
        State.R[0] = memory.ReadUInt32(State.R[12] + 8);
        State.R[1] = memory.ReadUInt32(0x8C10_0684);
        State.R[0] &= State.R[1];
        memory.WriteUInt32(State.R[12] + 8, State.R[0]);
        State.R[0] = modeFlags;
        State.T = (State.R[0] & State.R[3]) == 0;

        State.R[3] = modeFlags;
        State.R[4] = (uint)(short)memory.ReadUInt16(0x8C10_0676);
        State.R[5] = (uint)(short)memory.ReadUInt16(0x8C10_0678);
        State.T = (State.R[3] & State.R[4]) == 0;
        State.R[2] = modeFlags;
        State.R[3] = memory.ReadUInt32(0x8C10_068C);
        State.T = (State.R[2] & State.R[3]) == 0;
        State.R[0] = modeFlags;
        State.R[3] = unchecked((uint)-8);
        State.R[4] = 7;
        State.R[0] = (uint)(unchecked((int)State.R[0]) >> 8);
        State.R[4] &= State.R[0];
        State.T = (State.R[4] & State.R[4]) == 0;
        State.R[3] = 29;
        State.R[4] = 4;
        State.R[2] = memory.ReadUInt32(State.R[12] + 4);
        State.R[4] <<= 29;
        State.T = (State.R[14] & State.R[14]) == 0;
        State.R[2] |= State.R[4];
        memory.WriteUInt32(State.R[12] + 4, State.R[2]);
        State.R[0] = mode;
        State.T = State.R[0] == 4;

        State.R[2] = memory.ReadUInt32(0x8C10_0698);
        State.R[1] = memory.ReadUInt32(0x8C10_069C);
        State.R[0] = memory.ReadUInt32(State.R[2]);
        State.R[3] = memory.ReadUInt32(State.R[12] + 8);
        State.R[0] ^= 0xFC;
        State.R[0] <<= 16;
        State.R[0] <<= 8;
        State.R[0] |= State.R[1];
        State.R[3] |= State.R[0];
        memory.WriteUInt32(State.R[12] + 8, State.R[3]);
        State.R[0] = memory.ReadUInt32(State.R[12]);
        State.R[3] = memory.ReadUInt32(0x8C10_06A0);
        State.R[0] |= State.R[3];
        memory.WriteUInt32(State.R[12], State.R[0]);

        State.R[3] = modeFlags;
        State.T = (State.R[5] & State.R[3]) == 0;
        State.R[3] = memory.ReadUInt32(0x8C10_06AC);
        State.R[2] = memory.ReadUInt32(0x8C10_06B0);
        State.R[1] = memory.ReadUInt32(0x8C10_06A8);
        ExecuteFpuMove(0xF528, 5, 2, 0x8);
        ExecuteFpuMove(0xF718, 7, 1, 0x8);
        ExecuteFpuMove(0xF638, 6, 3, 0x8);
        State.Pr = 0x8C10_0670;
        ExecuteFpuMove(0xF4EC, 4, 14, 0xC);

        skippedInstructions = skippedInstructionCount;
        State.Pc = 0x8C10_0AC0;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2RendererColorPackReturnBridge(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_0670
            || step.Opcode != 0xA03C
            || delayedBranchTarget != 0x8C10_06EC
            || State.Pc != 0x8C10_0672)
        {
            return false;
        }

        const ulong skippedInstructionCount = 1;
        if (!IsDoa2RendererColorPackReturnBridge()
            || maxInstructionsToSkip < skippedInstructionCount
            || State.R[12] > uint.MaxValue - 19
            || !memory.TryGetSystemRamOffset(State.R[12] + 16, 4, out _))
        {
            return false;
        }

        memory.WriteUInt32(State.R[12] + 16, State.R[0]);

        skippedInstructions = skippedInstructionCount;
        State.Pc = 0x8C10_06EC;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2RendererInterpolationPrologueToCopyTail(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_0A30
            || step.Opcode != 0xFFEB
            || State.Pc != 0x8C10_0A32)
        {
            return false;
        }

        const ulong skippedInstructionCount = 22;
        var sourceTableAddress = memory.ReadUInt32(0x8C10_0B68);
        var copyHelperAddress = memory.ReadUInt32(0x8C10_0B6C);
        var jumpTableAddress = memory.ReadUInt32(0x8C10_E5E4);
        if (!IsDoa2RendererInterpolationPrologueToCopyTail()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || maxInstructionsToSkip < skippedInstructionCount
            || copyHelperAddress != 0x8C10_E5CC
            || jumpTableAddress != 0x8C10_E62C
            || State.R[15] < 44
            || sourceTableAddress > uint.MaxValue - 31
            || !memory.TryGetSystemRamOffset(State.R[15] - 44, 76, out _)
            || !memory.TryGetSystemRamOffset(sourceTableAddress, 32, out _))
        {
            return false;
        }

        var copyTailAddress = memory.ReadUInt32(jumpTableAddress + 32);
        ExecuteDoa2RendererInterpolationPrologueToCopyTailBody(sourceTableAddress, copyHelperAddress, jumpTableAddress, copyTailAddress);

        skippedInstructions = skippedInstructionCount;
        State.Pc = State.R[3];
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2RendererInterpolationAggregate(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_0A30
            || step.Opcode != 0xFFEB
            || State.Pc != 0x8C10_0A32)
        {
            return false;
        }

        const ulong prologueSkippedInstructionCount = 22;
        const ulong copySkippedInstructionCount = 17;
        const ulong maxSetupSkippedInstructionCount = 136;
        const ulong epilogueSkippedInstructionCount = 4;
        const ulong maxSkippedInstructionCount = prologueSkippedInstructionCount
            + copySkippedInstructionCount
            + maxSetupSkippedInstructionCount
            + epilogueSkippedInstructionCount;
        var sourceTableAddress = memory.ReadUInt32(0x8C10_0B68);
        var copyHelperAddress = memory.ReadUInt32(0x8C10_0B6C);
        var jumpTableAddress = memory.ReadUInt32(0x8C10_E5E4);
        if (!IsDoa2RendererInterpolationPrologueToCopyTail()
            || !IsDoa2UnrolledWordCopyReturn()
            || !IsDoa2RendererInterpolationSetupToLoopExit()
            || !IsDoa2RendererInterpolationEpilogueReturn()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || maxInstructionsToSkip < maxSkippedInstructionCount
            || copyHelperAddress != 0x8C10_E5CC
            || jumpTableAddress != 0x8C10_E62C
            || State.R[15] < 44
            || sourceTableAddress > uint.MaxValue - 31
            || !memory.TryGetSystemRamOffset(State.R[15] - 44, 76, out _)
            || !memory.TryGetSystemRamOffset(sourceTableAddress, 32, out _)
            || State.R[4] > uint.MaxValue - 52
            || State.R[5] > uint.MaxValue - 15
            || State.R[6] > uint.MaxValue - 15
            || !memory.TryGetSystemRamOffset(State.R[4] + 4, 8, out _)
            || !memory.TryGetSystemRamOffset(State.R[4] + 52, 4, out _)
            || !memory.TryGetSystemRamOffset(State.R[5], 16, out _)
            || !memory.TryGetSystemRamOffset(State.R[6], 16, out _))
        {
            return false;
        }

        var copyTailAddress = memory.ReadUInt32(jumpTableAddress + 32);
        if (copyTailAddress != 0x8C10_E60A)
        {
            return false;
        }

        ExecuteDoa2RendererInterpolationPrologueToCopyTailBody(sourceTableAddress, copyHelperAddress, jumpTableAddress, copyTailAddress);
        State.R[3] = memory.ReadUInt32(State.R[2] + 24);
        ExecuteDoa2UnrolledWordCopyReturnBody();
        State.Pc = State.Pr;

        if (State.Pr != 0x8C10_0A52 || State.Pc != 0x8C10_0A52)
        {
            return false;
        }

        State.R[0] = memory.ReadUInt32(State.R[4] + 52);
        var setupSkippedInstructionCount = 1 + ExecuteDoa2RendererInterpolationSetupToLoopExitBody(ulong.MaxValue);
        if (State.Pc != 0x8C10_0AB6)
        {
            return false;
        }

        State.R[15] += 36;
        var returnAddress = memory.ReadUInt32(State.R[15]);
        if (returnAddress != 0x8C10_055C)
        {
            return false;
        }

        ExecuteDoa2RendererInterpolationEpilogueReturnBody(returnAddress);

        skippedInstructions = prologueSkippedInstructionCount
            + copySkippedInstructionCount
            + setupSkippedInstructionCount
            + epilogueSkippedInstructionCount;
        State.Pc = returnAddress;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    private void ExecuteDoa2RendererInterpolationPrologueToCopyTailBody(
        uint sourceTableAddress,
        uint copyHelperAddress,
        uint jumpTableAddress,
        uint copyTailAddress)
    {
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.Pr);
        State.R[15] += unchecked((uint)-36);
        ExecuteFpuMove(0xF95C, 9, 5, 0xC);
        ExecuteFpuMove(0xF572, 5, 7, 0x2);
        ExecuteFpuMove(0xF84C, 8, 4, 0xC);
        ExecuteFpuMove(0xF462, 4, 6, 0x2);
        ExecuteFpuMove(0xF872, 8, 7, 0x2);
        State.R[1] = State.R[15];
        ExecuteFpuMove(0xF962, 9, 6, 0x2);
        State.R[2] = sourceTableAddress;
        State.R[7] = State.R[15];
        State.R[3] = copyHelperAddress;
        State.R[7] += 4;
        State.R[1] += 4;
        State.Pr = 0x8C10_0A52;
        State.R[0] = 32;

        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.R[3]);
        State.R[3] = jumpTableAddress;
        State.R[3] = copyTailAddress;
        State.R[0] += unchecked((uint)-4);
        State.R[0] = memory.ReadUInt32(State.R[2] + State.R[0]);
    }

    internal bool TryFastForwardDoa2RendererInterpolationSetupToLoopExit(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_0A52
            || step.Opcode != 0x504D
            || State.Pc != 0x8C10_0A54)
        {
            return false;
        }

        const ulong maxSkippedInstructionCount = 117;
        if (!IsDoa2RendererInterpolationSetupToLoopExit()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || maxInstructionsToSkip < maxSkippedInstructionCount
            || State.R[0] != 0x2805
            || State.R[4] > uint.MaxValue - 52
            || State.R[5] > uint.MaxValue - 15
            || State.R[6] > uint.MaxValue - 15
            || State.R[7] > uint.MaxValue - 31
            || !memory.TryGetSystemRamOffset(State.R[4] + 4, 8, out _)
            || !memory.TryGetSystemRamOffset(State.R[4] + 52, 4, out _)
            || !memory.TryGetSystemRamOffset(State.R[5], 16, out _)
            || !memory.TryGetSystemRamOffset(State.R[6], 16, out _)
            || !memory.TryGetSystemRamOffset(State.R[7], 32, out _)
            || !memory.TryGetSystemRamOffset(State.R[15], 4, out _))
        {
            return false;
        }

        skippedInstructions = ExecuteDoa2RendererInterpolationSetupToLoopExitBody(maxSkippedInstructionCount);
        if (skippedInstructions == 0)
        {
            return false;
        }

        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    private ulong ExecuteDoa2RendererInterpolationSetupToLoopExitBody(ulong maxSkippedInstructionCount)
    {
        State.T = (State.R[0] & 0x0F) == 0;
        if (State.T)
        {
            return 0;
        }

        ulong skippedInstructions = 2;
        memory.WriteUInt32(State.R[15], State.R[0]);
        State.R[0] &= 0x03;
        State.Fpul = State.R[0];
        State.R[0] = memory.ReadUInt32(State.R[15]);
        State.T = (State.R[0] & 1) != 0;
        State.R[0] = (uint)((int)State.R[0] >> 1);
        State.Fr[3] = BitConverter.SingleToUInt32Bits(unchecked((int)State.Fpul));
        State.T = (State.R[0] & 1) != 0;
        State.R[0] = (uint)((int)State.R[0] >> 1);
        State.R[0] &= 0x03;
        State.Fpul = State.R[0];
        ExecuteFpuMove(0xF63C, 6, 3, 0xC);
        State.Fr[3] = BitConverter.SingleToUInt32Bits(unchecked((int)State.Fpul));
        ExecuteFpuMove(0xF73C, 7, 3, 0xC);
        State.R[1] = 4;
        skippedInstructions += 13;

        while (true)
        {
            ExecuteFpuMove(0xFB79, 11, 7, 0x9);
            State.R[0] = 4;
            ExecuteFpuMove(0xF38D, 3, 8, 0xD);
            ExecuteFpuMove(0xFB61, 11, 6, 0x1);
            ExecuteFpuMove(0xFA79, 10, 7, 0x9);
            ExecuteFpuMove(0xF146, 1, 4, 0x6);
            State.R[0] = 8;
            ExecuteFpuMove(0xFA71, 10, 7, 0x1);
            ExecuteFpuMove(0xFB34, 11, 3, 0x4);
            skippedInstructions += 9;

            skippedInstructions++;
            ExecuteFpuMove(0xFE46, 14, 4, 0x6);
            skippedInstructions++;
            if (!State.T)
            {
                ExecuteFpuMove(0xF24C, 2, 4, 0xC);
                ExecuteFpuMove(0xF2B2, 2, 11, 0x2);
                ExecuteFpuMove(0xF0BC, 0, 11, 0xC);
                ExecuteFpuMove(0xF18E, 1, 8, 0xE);
                ExecuteFpuMove(0xF24D, 2, 4, 0xD);
                ExecuteFpuMove(0xFE20, 14, 2, 0x0);
                skippedInstructions += 6;
            }

            ExecuteFpuMove(0xF38D, 3, 8, 0xD);
            ExecuteFpuMove(0xFA34, 10, 3, 0x4);
            skippedInstructions += 2;

            skippedInstructions++;
            if (!State.T)
            {
                ExecuteFpuMove(0xF0AC, 0, 10, 0xC);
                ExecuteFpuMove(0xFE5E, 14, 5, 0xE);
                ExecuteFpuMove(0xF19E, 1, 9, 0xE);
                skippedInstructions += 3;
            }

            State.R[1]--;
            ExecuteFpuMove(0xF51A, 5, 1, 0xA);
            ExecuteFpuMove(0xF6EA, 6, 14, 0xA);
            State.T = State.R[1] == 0;
            State.R[6] += 4;
            skippedInstructions += 5;

            skippedInstructions++;
            State.R[5] += 4;
            skippedInstructions++;
            if (State.T)
            {
                State.Pc = 0x8C10_0AB6;
                break;
            }
        }

        return skippedInstructions <= maxSkippedInstructionCount ? skippedInstructions : 0;
    }

    internal bool TryFastForwardDoa2RendererInterpolationEpilogueReturn(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_0AB6
            || step.Opcode != 0x7F24
            || State.Pc != 0x8C10_0AB8)
        {
            return false;
        }

        const ulong skippedInstructionCount = 3;
        if (!IsDoa2RendererInterpolationEpilogueReturn()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || maxInstructionsToSkip < skippedInstructionCount
            || State.R[15] > uint.MaxValue - 7
            || !memory.TryGetSystemRamOffset(State.R[15], 8, out _))
        {
            return false;
        }

        var returnAddress = memory.ReadUInt32(State.R[15]);
        if (returnAddress != 0x8C10_055C)
        {
            return false;
        }

        ExecuteDoa2RendererInterpolationEpilogueReturnBody(returnAddress);

        skippedInstructions = skippedInstructionCount;
        State.Pc = returnAddress;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    private void ExecuteDoa2RendererInterpolationEpilogueReturnBody(uint returnAddress)
    {
        State.Pr = returnAddress;
        State.R[15] += 4;
        ExecuteFpuMove(0xFEF9, 14, 15, 0x9);
    }

    internal bool TryFastForwardDoa2SignedRemainderHelper(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_751C
            || step.Opcode != 0x2008
            || State.Pc != 0x8C10_751E
            || State.T)
        {
            return false;
        }

        const ulong maxSkippedInstructionCount = 88;
        if (!IsDoa2SignedRemainderHelper()
            || maxInstructionsToSkip < maxSkippedInstructionCount
            || State.R[15] < 12
            || !memory.TryGetSystemRamOffset(State.R[15] - 12, 12, out _))
        {
            return false;
        }

        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.R[2]);
        skippedInstructions++;
        skippedInstructions++;

        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.R[3]);
        State.R[2] = 0;
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.R[4]);
        ExecuteDiv0S(2, 1);
        State.R[4] = State.T ? 1u : 0u;
        ExecuteSubc(3, 3);
        ExecuteSubc(2, 1);
        ExecuteDiv0S(0, 3);
        skippedInstructions += 8;

        for (var index = 0; index < 32; index++)
        {
            ExecuteRotcl(1);
            ExecuteDiv1(0, 3);
            skippedInstructions += 2;
        }

        ExecuteDiv0S(2, 3);
        State.R[2] = State.T ? 1u : 0u;
        State.R[2] ^= State.R[4];
        ExecuteRotcr(2);
        skippedInstructions += 4;

        skippedInstructions++;
        if (State.T)
        {
            ExecuteDiv0S(0, 3);
            State.T = (State.R[3] & 0x1) != 0;
            State.R[3] = (uint)(unchecked((int)State.R[3]) >> 1);
            ExecuteDiv1(0, 3);
            skippedInstructions += 3;
        }

        State.R[3] += State.R[4];
        State.R[0] = State.R[3];
        State.R[4] = memory.ReadUInt32(State.R[15]);
        State.R[15] += 4;
        State.R[3] = memory.ReadUInt32(State.R[15]);
        State.R[15] += 4;
        State.R[2] = memory.ReadUInt32(State.R[15]);
        State.R[15] += 4;
        skippedInstructions += 6;

        State.Pc = State.Pr;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2UnsignedDivideHelper(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_7424
            || step.Opcode != 0x2008
            || State.Pc != 0x8C10_7426
            || State.T
            || State.R[0] == 0)
        {
            return false;
        }

        const ulong skippedInstructionCount = 72;
        if (!IsDoa2UnsignedDivideHelper()
            || maxInstructionsToSkip < skippedInstructionCount
            || State.R[15] < 4
            || !memory.TryGetSystemRamOffset(State.R[15] - 4, 4, out _))
        {
            return false;
        }

        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.R[2]);
        State.R[2] = 0;
        State.M = false;
        State.Q = false;
        State.T = false;
        for (var index = 0; index < 32; index++)
        {
            ExecuteRotcl(1);
            ExecuteDiv1(0, 2);
        }

        ExecuteRotcl(1);
        State.R[0] = State.R[1];
        State.R[2] = memory.ReadUInt32(State.R[15]);
        State.R[15] += 4;
        State.Pc = State.Pr;
        State.InstructionsExecuted += skippedInstructionCount;
        skippedInstructions = skippedInstructionCount;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2ZeroByteClassifier(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C11_7482
            || step.Opcode != 0x8464
            || State.Pc != 0x8C11_7484)
        {
            return false;
        }

        const ulong skippedInstructionCount = 17;
        if (!IsDoa2ZeroByteClassifier()
            || maxInstructionsToSkip < skippedInstructionCount
            || State.Pr != 0x8C11_7532
            || State.R[6] > uint.MaxValue - 4
            || !memory.TryGetSystemRamOffset(State.R[6] + 4, 1, out _)
            || State.R[0] != 0)
        {
            return false;
        }

        State.R[1] = 0x0000_00FF;
        State.R[0] = 0;
        State.T = true;
        State.Pc = State.Pr;
        State.InstructionsExecuted += skippedInstructionCount;
        skippedInstructions = skippedInstructionCount;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2ListEntrySetupToClassifier(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C11_750E
            || step.Opcode != 0xEE34
            || State.Pc != 0x8C11_7510
            || State.R[14] != 52)
        {
            return false;
        }

        const ulong skippedInstructionCount = 17;
        if (!IsDoa2ListEntrySetupToClassifier()
            || maxInstructionsToSkip < skippedInstructionCount
            || State.R[15] < 4)
        {
            return false;
        }

        var entryTableBase = memory.ReadUInt32(0x8C11_75D0);
        var resultTableBase = memory.ReadUInt32(0x8C11_75D4);
        var product = unchecked((uint)((long)(int)State.R[14] * (int)State.R[13]));
        var entryAddress = unchecked(entryTableBase + product);
        var resultSlot = unchecked(resultTableBase + (State.R[13] << 2));
        var stackAddress = State.R[15] - 4;
        if (entryAddress > uint.MaxValue - 0x30
            || !memory.TryGetSystemRamOffset(entryAddress + 8, 4, out _)
            || !memory.TryGetSystemRamOffset(entryAddress + 0x2C, 4, out _)
            || !memory.TryGetSystemRamOffset(entryAddress + 0x30, 4, out _)
            || !memory.TryGetSystemRamOffset(stackAddress, 4, out _))
        {
            return false;
        }

        State.R[3] = entryTableBase;
        State.Macl = product;
        State.R[1] = State.R[13] << 2;
        State.R[12] = State.R[0];
        State.R[6] = State.R[11];
        State.R[5] = State.R[12];
        State.R[14] = entryAddress;
        State.R[3] = resultSlot;
        memory.WriteUInt32(entryAddress + 0x30, State.R[0]);
        State.R[2] = memory.ReadUInt32(entryAddress + 8);
        memory.WriteUInt32(entryAddress + 0x2C, State.R[2]);
        State.R[15] = stackAddress;
        memory.WriteUInt32(State.R[15], State.R[3]);
        State.Pr = 0x8C11_7532;
        State.R[4] = State.R[14];
        State.Pc = 0x8C11_7482;
        State.InstructionsExecuted += skippedInstructionCount;
        skippedInstructions = skippedInstructionCount;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2ListEntryAllocatorPair(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C11_7500
            || step.Opcode != 0xD331
            || State.Pc != 0x8C11_7502
            || State.R[3] != 0x8C12_4634)
        {
            return false;
        }

        const ulong skippedInstructionCount = 31;
        if (!IsDoa2ListEntryAllocatorPair()
            || maxInstructionsToSkip < skippedInstructionCount
            || State.R[15] < 4
            || !memory.TryGetSystemRamOffset(State.R[15] - 4, 4, out _)
            || !memory.TryGetSystemRamOffset(0x8C31_007C, 4, out _))
        {
            return false;
        }

        var index = State.R[13];
        var firstScaledBeforeFinalShift = unchecked((index * 3u) << 2);
        var firstOffset = unchecked(firstScaledBeforeFinalShift << 1);
        var secondOffset = unchecked(index * 120u);
        var secondBase = unchecked(memory.ReadUInt32(0x8C31_007C) + 0x44Cu);

        State.R[0] = unchecked(0x8C30_36BCu + firstOffset);
        State.T = (firstScaledBeforeFinalShift & 0x8000_0000u) != 0;
        State.R[2] = 0x8C12_4646;
        State.R[11] = State.R[0];
        State.Pr = 0x8C11_750E;
        State.R[4] = index;
        State.R[15] -= 4;
        memory.WriteUInt32(State.R[15], State.Pr);
        State.R[3] = 0x8C13_4F48;
        State.R[2] = 120;
        State.R[3] = 0x8C31_007C;
        State.Macl = secondOffset;
        State.R[1] = 0x0000_044C;
        State.R[0] = unchecked(secondBase + secondOffset);
        State.R[4] = secondOffset;
        State.Pr = memory.ReadUInt32(State.R[15]);
        State.R[15] += 4;
        State.Pc = State.Pr;
        State.InstructionsExecuted += skippedInstructionCount;
        skippedInstructions = skippedInstructionCount;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2ListEntryPostClassifierToRemainder(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C11_7532
            || step.Opcode != 0x62F6
            || State.Pc != 0x8C11_7534)
        {
            return false;
        }

        const ulong skippedInstructionCount = 5;
        if (!IsDoa2ListEntryPostClassifierToRemainder()
            || maxInstructionsToSkip < skippedInstructionCount
            || !memory.TryGetSystemRamOffset(State.R[2], 4, out _))
        {
            return false;
        }

        State.R[1] = State.R[13];
        State.R[3] = 0x8C10_751C;
        memory.WriteUInt32(State.R[2], State.R[0]);
        State.Pr = 0x8C11_753E;
        State.R[0] = State.R[9];
        State.Pc = 0x8C10_751C;
        State.InstructionsExecuted += skippedInstructionCount;
        skippedInstructions = skippedInstructionCount;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2ListEntryNonzeroRemainderTail(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C11_753E
            || step.Opcode != 0x2008
            || State.Pc != 0x8C11_7540
            || State.T)
        {
            return false;
        }

        const ulong skippedInstructionCount = 7;
        if (!IsDoa2ListEntryNonzeroRemainderTail()
            || maxInstructionsToSkip < skippedInstructionCount
            || State.R[12] > uint.MaxValue - 20
            || State.R[14] > uint.MaxValue - 0x24
            || !memory.TryGetSystemRamOffset(State.R[14] + 0x24, 4, out _))
        {
            return false;
        }

        State.R[3] = State.R[12];
        State.R[3] += 20;
        memory.WriteUInt32(State.R[14] + 0x24, State.R[3]);
        State.R[13]++;
        State.T = unchecked((int)State.R[13]) >= unchecked((int)State.R[10]);
        State.Pc = State.T ? 0x8C11_75AA : 0x8C11_7500;
        State.InstructionsExecuted += skippedInstructionCount;
        skippedInstructions = skippedInstructionCount;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2ZeroStatusTableScan(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C0F_7D2A
            || step.Opcode != 0x6011
            || State.Pc != 0x8C0F_7D2C
            || State.R[0] != 0)
        {
            return false;
        }

        if (!IsDoa2ZeroStatusTableScan()
            || !memory.TryGetSystemRamOffset(0x8C2A_DC04, 0x92, out _)
            || !memory.TryGetSystemRamOffset(0x8C0F_7EE4, 2, out _)
            || State.R[1] < 0x8C2A_DC04
            || State.R[1] >= 0x8C2A_DC94
            || ((State.R[1] - 0x8C2A_DC04) & 7) != 0
            || memory.ReadUInt16(State.R[1]) != 0)
        {
            return false;
        }

        var scanAddress = State.R[1];
        uint finalR0;
        uint finalR1;
        uint finalR2;
        uint finalR3;
        uint finalPc;
        bool finalT;
        ushort finalProgress;
        ulong computedSkippedInstructions = 0;
        while (true)
        {
            var nextAddress = scanAddress + 8;
            if (nextAddress > 0x8C2A_DC94)
            {
                return false;
            }

            var progress = (nextAddress - 0x8C2A_DC04) >> 3;
            if (progress > ushort.MaxValue)
            {
                return false;
            }

            computedSkippedInstructions += 14;
            if (computedSkippedInstructions > maxInstructionsToSkip)
            {
                return false;
            }

            finalProgress = (ushort)progress;
            finalR1 = nextAddress;
            finalR2 = 0x8C2A_DC94;
            finalR3 = progress;
            if (nextAddress == 0x8C2A_DC94)
            {
                finalR0 = 0x8C0F_7EE4;
                finalPc = 0x8C0F_7D5A;
                finalT = true;
                break;
            }

            computedSkippedInstructions++;
            if (computedSkippedInstructions > maxInstructionsToSkip)
            {
                return false;
            }

            var nextValue = memory.ReadUInt16(nextAddress);
            if (nextValue != 0)
            {
                finalR0 = nextValue;
                finalPc = 0x8C0F_7D2C;
                finalT = false;
                break;
            }

            scanAddress = nextAddress;
        }

        State.R[0] = finalR0;
        State.R[1] = finalR1;
        State.R[2] = finalR2;
        State.R[3] = finalR3;
        State.T = finalT;
        memory.WriteUInt16(0x8C0F_7EE4, finalProgress);
        State.Pc = finalPc;
        State.InstructionsExecuted += computedSkippedInstructions;
        skippedInstructions = computedSkippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2FpuRecurrenceLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C0F_B20A
            || step.Opcode != 0x8DF8
            || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal)
            || delayedBranchTarget != 0x8C0F_B1FE
            || State.Pc != 0x8C0F_B20C)
        {
            return false;
        }

        if (!IsDoa2FpuRecurrenceLoop()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0)
        {
            return false;
        }

        var current = unchecked((int)State.R[4]);
        var limit = unchecked((int)State.R[5]);
        if (current < limit || current < int.MinValue + 2 || limit < int.MinValue + 2)
        {
            return false;
        }

        var futureIterations = (((ulong)((long)current - limit)) / 2) + 1;
        if (futureIterations > (ulong.MaxValue - 1) / 8)
        {
            return false;
        }

        skippedInstructions = 1 + (futureIterations * 8);
        if (skippedInstructions > maxInstructionsToSkip)
        {
            skippedInstructions = 0;
            return false;
        }

        ExecuteFpuArithmetic(5, 2, FpuArithmeticKind.Divide, static (left, right) => left / right, static (left, right) => left / right);
        for (var iteration = 0ul; iteration < futureIterations; iteration++)
        {
            State.Fpul = State.R[4];
            State.R[4] -= 2;
            State.T = unchecked((int)State.R[4]) >= unchecked((int)State.R[5]);
            State.Fr[2] = BitConverter.SingleToUInt32Bits(unchecked((int)State.Fpul));
            ExecuteFpuArithmetic(2, 5, FpuArithmeticKind.Subtract, static (left, right) => left - right, static (left, right) => left - right);
            State.Fr[5] = State.Fr[6];
            ExecuteFpuArithmetic(5, 2, FpuArithmeticKind.Divide, static (left, right) => left / right, static (left, right) => left / right);
        }

        State.Pc = 0x8C0F_B20E;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2TrigSetupAndRecurrenceLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C0F_B1C0
            || step.Opcode != 0x644D
            || State.Pc != 0x8C0F_B1C2)
        {
            return false;
        }

        const ulong skippedInstructionCount = 74;
        if (!IsDoa2TrigSetupAndRecurrenceLoop()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || maxInstructionsToSkip < skippedInstructionCount
            || !memory.TryGetSystemRamOffset(0x8C0F_B23C, 16, out _))
        {
            return false;
        }

        ExecuteDoa2TrigSetupAndRecurrenceLoopBody();

        skippedInstructions = skippedInstructionCount;
        State.Pc = 0x8C0F_B216;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2TrigSetupAndPostReturn(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C0F_B1C0
            || step.Opcode != 0x644D
            || State.Pc != 0x8C0F_B1C2)
        {
            return false;
        }

        const ulong trigSkippedInstructionCount = 74;
        const ulong tstSkippedInstructionCount = 1;
        const ulong maxPostSkippedInstructionCount = 12;
        const ulong maxSkippedInstructionCount = trigSkippedInstructionCount + tstSkippedInstructionCount + maxPostSkippedInstructionCount;
        if (!IsDoa2TrigSetupAndRecurrenceLoop()
            || !IsDoa2PostTrigHelperReturn()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || maxInstructionsToSkip < maxSkippedInstructionCount
            || !memory.TryGetSystemRamOffset(0x8C0F_B23C, 16, out _))
        {
            return false;
        }

        ExecuteDoa2TrigSetupAndRecurrenceLoopBody();
        State.T = (State.R[3] & State.R[6]) == 0;
        var postSkippedInstructionCount = ExecuteDoa2PostTrigHelperReturnBodyAfterTst();

        skippedInstructions = trigSkippedInstructionCount + tstSkippedInstructionCount + postSkippedInstructionCount;
        State.Pc = State.Pr;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2PostTrigHelperReturn(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C0F_B216
            || step.Opcode != 0x2638
            || State.Pc != 0x8C0F_B218)
        {
            return false;
        }

        var branchTaken = !State.T;
        var skippedInstructionCount = branchTaken ? 12ul : 10ul;
        if (!IsDoa2PostTrigHelperReturn()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || maxInstructionsToSkip < skippedInstructionCount)
        {
            return false;
        }

        ExecuteDoa2PostTrigHelperReturnBodyAfterTst();

        skippedInstructions = skippedInstructionCount;
        State.Pc = State.Pr;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    private void ExecuteDoa2TrigSetupAndRecurrenceLoopBody()
    {
        ExecuteFpuMove(0xF79D, 7, 9, 0xD);
        State.Fpul = State.R[4];
        State.R[0] = 0x8C0F_B23C;
        ExecuteFpuMove(0xF208, 2, 0, 0x8);
        State.R[0] = 0x8C0F_B240;
        ExecuteFpuMove(0xF108, 1, 0, 0x8);
        ExecuteFpuMove(0xF770, 7, 7, 0x0);
        State.Fr[3] = BitConverter.SingleToUInt32Bits(unchecked((int)State.Fpul));
        State.R[0] = 0x8C0F_B244;
        ExecuteFpuMove(0xF508, 5, 0, 0x8);
        State.R[0] = 0x8C0F_B248;
        ExecuteFpuMove(0xF008, 0, 0, 0x8);
        State.R[4] = 11;
        State.R[5] = 3;
        ExecuteFpuMove(0xF322, 3, 2, 0x2);
        ExecuteFpuMove(0xF313, 3, 1, 0x3);
        ExecuteFpuMove(0xF43C, 4, 3, 0xC);
        ExecuteFpuMove(0xF473, 4, 7, 0x3);
        ExecuteFpuMove(0xF34C, 3, 4, 0xC);
        ExecuteFpuMove(0xF353, 3, 5, 0x3);
        ExecuteFpuMove(0xF300, 3, 0, 0x0);
        State.Fpul = ConvertSingleToFpul(State.Fr[3]);
        State.R[6] = State.Fpul;
        State.Fpul = State.R[6];
        State.Fr[3] = BitConverter.SingleToUInt32Bits(unchecked((int)State.Fpul));
        ExecuteFpuMove(0xF352, 3, 5, 0x2);
        ExecuteFpuMove(0xF58D, 5, 8, 0xD);
        ExecuteFpuMove(0xF431, 4, 3, 0x1);
        ExecuteFpuMove(0xF64C, 6, 4, 0xC);
        ExecuteFpuMove(0xF642, 6, 4, 0x2);

        while (true)
        {
            State.Fpul = State.R[4];
            State.R[4] -= 2;
            State.T = unchecked((int)State.R[4]) >= unchecked((int)State.R[5]);
            State.Fr[2] = BitConverter.SingleToUInt32Bits(unchecked((int)State.Fpul));
            ExecuteFpuMove(0xF251, 2, 5, 0x1);
            ExecuteFpuMove(0xF56C, 5, 6, 0xC);
            ExecuteFpuMove(0xF523, 5, 2, 0x3);
            if (!State.T)
            {
                break;
            }
        }

        ExecuteFpuMove(0xF69D, 6, 9, 0xD);
        State.R[3] = 1;
        ExecuteFpuMove(0xF36C, 3, 6, 0xC);
        ExecuteFpuMove(0xF351, 3, 5, 0x1);
    }

    private ulong ExecuteDoa2PostTrigHelperReturnBodyAfterTst()
    {
        var branchTaken = !State.T;
        ExecuteFpuMove(0xF433, 4, 3, 0x3);
        ExecuteFpuMove(0xF24C, 2, 4, 0xC);
        ExecuteFpuMove(0xF272, 2, 7, 0x2);
        ExecuteFpuMove(0xF04C, 0, 4, 0xC);
        ExecuteFpuMove(0xF64E, 6, 4, 0xE);
        ExecuteFpuMove(0xF42C, 4, 2, 0xC);
        ExecuteFpuMove(0xF463, 4, 6, 0x3);
        ExecuteFpuMove(0xF04C, 0, 4, 0xC);
        if (branchTaken)
        {
            ExecuteFpuMove(0xF04D, 0, 4, 0xD);
        }

        return branchTaken ? 12ul : 10ul;
    }

    internal bool TryFastForwardDoa2VectorScaleLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_05B2
            || step.Opcode != 0x8BF5
            || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal)
            || State.Pc != 0x8C10_05A0)
        {
            return false;
        }

        if (!IsDoa2VectorScaleLoop()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || (State.R[4] & 3) != 0
            || (State.R[9] & 3) != 0
            || State.R[4] >= State.R[9])
        {
            return false;
        }

        var remainingBytes = State.R[9] - State.R[4];
        var remainingIterations = remainingBytes / 4;
        const ulong instructionsPerIteration = 10;
        if (remainingIterations == 0
            || remainingIterations > int.MaxValue / 4)
        {
            return false;
        }

        skippedInstructions = remainingIterations * instructionsPerIteration;
        if (skippedInstructions > maxInstructionsToSkip)
        {
            skippedInstructions = 0;
            return false;
        }

        var firstOffset = State.R[4];
        var bytesToTouch = checked((int)remainingIterations * 4);
        if (firstOffset > uint.MaxValue - (uint)bytesToTouch
            || State.R[11] > uint.MaxValue - firstOffset
            || State.R[10] > uint.MaxValue - firstOffset
            || !memory.TryGetSystemRamOffset(State.R[11] + firstOffset, bytesToTouch, out _)
            || !memory.TryGetSystemRamOffset(State.R[10] + firstOffset, bytesToTouch, out _))
        {
            skippedInstructions = 0;
            return false;
        }

        for (var index = 0ul; index < remainingIterations; index++)
        {
            var offset = firstOffset + ((uint)index * 4);
            State.R[0] = offset;
            State.Fr[3] = memory.ReadUInt32(State.R[11] + offset);
            State.R[4] = offset + 4;
            ExecuteFpuArithmetic(3, 4, FpuArithmeticKind.Multiply, static (left, right) => left * right, static (left, right) => left * right);
            memory.WriteUInt32(State.R[11] + offset, State.Fr[3]);
            State.Fr[2] = memory.ReadUInt32(State.R[10] + offset);
            ExecuteFpuArithmetic(2, 5, FpuArithmeticKind.Multiply, static (left, right) => left * right, static (left, right) => left * right);
            memory.WriteUInt32(State.R[10] + offset, State.Fr[2]);
            State.T = State.R[4] >= State.R[9];
        }

        State.Pc = 0x8C10_05B4;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    internal bool TryFastForwardDoa2InterpolationLoop(Sh4StepResult step, ulong maxInstructionsToSkip, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        if (step.Pc != 0x8C10_0AB2
            || step.Opcode != 0x8FE2
            || !step.Trace.EndsWith(" ; taken", StringComparison.Ordinal)
            || delayedBranchTarget != 0x8C10_0A7A
            || State.Pc != 0x8C10_0AB4)
        {
            return false;
        }

        if (!IsDoa2InterpolationLoop()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0)
        {
            return false;
        }

        var remainingIterations = State.R[1];
        const ulong maxInstructionsPerIteration = 30;
        if (remainingIterations == 0
            || remainingIterations > int.MaxValue / 8)
        {
            return false;
        }

        var maxSkippedInstructions = 1 + ((ulong)remainingIterations * maxInstructionsPerIteration);
        if (maxSkippedInstructions > maxInstructionsToSkip)
        {
            return false;
        }

        var sourceBytes = checked((int)remainingIterations * 8);
        var destinationBytes = checked((int)remainingIterations * 4);
        if (State.R[5] > uint.MaxValue - 4
            || State.R[4] > uint.MaxValue - 12
            || !memory.TryGetSystemRamOffset(State.R[7], sourceBytes, out _)
            || !memory.TryGetSystemRamOffset(State.R[4] + 4, 4, out _)
            || !memory.TryGetSystemRamOffset(State.R[4] + 8, 4, out _)
            || !memory.TryGetSystemRamOffset(State.R[5] + 4, destinationBytes, out _)
            || !memory.TryGetSystemRamOffset(State.R[6], destinationBytes, out _))
        {
            return false;
        }

        State.R[5] += 4;
        skippedInstructions++;
        while (true)
        {
            ExecuteFpuMove(0xFB79, 11, 7, 0x9);
            State.R[0] = 4;
            ExecuteFpuMove(0xF38D, 3, 8, 0xD);
            ExecuteFpuMove(0xFB61, 11, 6, 0x1);
            ExecuteFpuMove(0xFA79, 10, 7, 0x9);
            ExecuteFpuMove(0xF146, 1, 4, 0x6);
            State.R[0] = 8;
            ExecuteFpuMove(0xFA71, 10, 7, 0x1);
            ExecuteFpuMove(0xFB34, 11, 3, 0x4);
            skippedInstructions += 9;

            skippedInstructions++;
            ExecuteFpuMove(0xFE46, 14, 4, 0x6);
            skippedInstructions++;
            if (!State.T)
            {
                ExecuteFpuMove(0xF24C, 2, 4, 0xC);
                ExecuteFpuMove(0xF2B2, 2, 11, 0x2);
                ExecuteFpuMove(0xF0BC, 0, 11, 0xC);
                ExecuteFpuMove(0xF18E, 1, 8, 0xE);
                ExecuteFpuMove(0xF24D, 2, 4, 0xD);
                ExecuteFpuMove(0xFE20, 14, 2, 0x0);
                skippedInstructions += 6;
            }

            ExecuteFpuMove(0xF38D, 3, 8, 0xD);
            ExecuteFpuMove(0xFA34, 10, 3, 0x4);
            skippedInstructions += 2;

            skippedInstructions++;
            if (!State.T)
            {
                ExecuteFpuMove(0xF0AC, 0, 10, 0xC);
                ExecuteFpuMove(0xFE5E, 14, 5, 0xE);
                ExecuteFpuMove(0xF19E, 1, 9, 0xE);
                skippedInstructions += 3;
            }

            State.R[1]--;
            ExecuteFpuMove(0xF51A, 5, 1, 0xA);
            ExecuteFpuMove(0xF6EA, 6, 14, 0xA);
            State.T = State.R[1] == 0;
            State.R[6] += 4;
            skippedInstructions += 5;

            skippedInstructions++;
            State.R[5] += 4;
            skippedInstructions++;
            if (State.T)
            {
                State.Pc = 0x8C10_0AB6;
                break;
            }
        }

        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
    }

    private static bool TryComputeSkippedInstructions(uint remainingIterations, uint bodyInstructionCount, out ulong skippedInstructions)
    {
        skippedInstructions = 0;
        var perIteration = (ulong)bodyInstructionCount + 2;
        if (perIteration != 0 && remainingIterations > (ulong.MaxValue - 1) / perIteration)
        {
            return false;
        }

        skippedInstructions = 1 + ((ulong)remainingIterations * perIteration);
        return true;
    }

    private bool IsDoa2CallbackTimeoutLoop() =>
        memory.ReadInstructionUInt16(0x8C12_F99A) == 0x4D0B
        && memory.ReadInstructionUInt16(0x8C12_F99C) == 0xE407
        && memory.ReadInstructionUInt16(0x8C12_F99E) == 0x63F2
        && memory.ReadInstructionUInt16(0x8C12_F9A0) == 0x6232
        && memory.ReadInstructionUInt16(0x8C12_F9A2) == 0x72FF
        && memory.ReadInstructionUInt16(0x8C12_F9A4) == 0x2322
        && memory.ReadInstructionUInt16(0x8C12_F9A6) == 0x53F1
        && memory.ReadInstructionUInt16(0x8C12_F9A8) == 0x6232
        && memory.ReadInstructionUInt16(0x8C12_F9AA) == 0x32E0
        && memory.ReadInstructionUInt16(0x8C12_F9AC) == 0x8B03
        && memory.ReadInstructionUInt16(0x8C12_F9AE) == 0x61F2
        && memory.ReadInstructionUInt16(0x8C12_F9B0) == 0x6312
        && memory.ReadInstructionUInt16(0x8C12_F9B2) == 0x2338
        && memory.ReadInstructionUInt16(0x8C12_F9B4) == 0x8BF1
        && State.R[13] == 0x8C12_D2C0
        && memory.ReadUInt32(0x8C30_C778) == 0x8C12_BE60
        && memory.ReadUInt32(0x8C30_C77C) == 0
        && memory.ReadInstructionUInt16(0x8C12_BE60) == 0x000B
        && memory.ReadInstructionUInt16(0x8C12_BE62) == 0x0009;

    private bool IsDoa2FpuRecurrenceLoop() =>
        memory.ReadInstructionUInt16(0x8C0F_B1FE) == 0x445A
        && memory.ReadInstructionUInt16(0x8C0F_B200) == 0x74FE
        && memory.ReadInstructionUInt16(0x8C0F_B202) == 0x3453
        && memory.ReadInstructionUInt16(0x8C0F_B204) == 0xF22D
        && memory.ReadInstructionUInt16(0x8C0F_B206) == 0xF251
        && memory.ReadInstructionUInt16(0x8C0F_B208) == 0xF56C
        && memory.ReadInstructionUInt16(0x8C0F_B20A) == 0x8DF8
        && memory.ReadInstructionUInt16(0x8C0F_B20C) == 0xF523;

    private bool IsDoa2ByteFillLoop() =>
        memory.ReadInstructionUInt16(0x8C10_7864) == 0xE700
        && memory.ReadInstructionUInt16(0x8C10_7866) == 0x6373
        && memory.ReadInstructionUInt16(0x8C10_7868) == 0x3362
        && memory.ReadInstructionUInt16(0x8C10_786A) == 0x8D05
        && memory.ReadInstructionUInt16(0x8C10_786C) == 0x6043
        && memory.ReadInstructionUInt16(0x8C10_786E) == 0x7701
        && memory.ReadInstructionUInt16(0x8C10_7870) == 0x2050
        && memory.ReadInstructionUInt16(0x8C10_7872) == 0x3762
        && memory.ReadInstructionUInt16(0x8C10_7874) == 0x8FFB
        && memory.ReadInstructionUInt16(0x8C10_7876) == 0x7001
        && memory.ReadInstructionUInt16(0x8C10_7878) == 0x000B
        && memory.ReadInstructionUInt16(0x8C10_787A) == 0x6043;

    private bool IsDoa2HighRamZeroFillLoop() =>
        memory.ReadInstructionUInt16(0x8C11_333C) == 0x2540
        && memory.ReadInstructionUInt16(0x8C11_333E) == 0x7501
        && memory.ReadInstructionUInt16(0x8C11_3340) == 0x6262
        && memory.ReadInstructionUInt16(0x8C11_3342) == 0x3522
        && memory.ReadInstructionUInt16(0x8C11_3344) == 0x8BFA
        && memory.ReadUInt32(0x8C11_343C) == 0x8C14_D4E0
        && memory.ReadUInt32(0x8C11_3440) == 0x8C14_D4D4;

    private bool IsDoa2CacheBlockPurgeLoop() =>
        memory.ReadInstructionUInt16(0x8C11_1ED8) == 0x00A3
        && memory.ReadInstructionUInt16(0x8C11_1EDA) == 0x4510
        && memory.ReadInstructionUInt16(0x8C11_1EDC) == 0x7020
        && memory.ReadInstructionUInt16(0x8C11_1EDE) == 0x8BFB;

    private bool IsDoa2TableDivideSetupLoop() =>
        memory.ReadInstructionUInt16(0x8C11_C7B0) == 0x8928
        && memory.ReadInstructionUInt16(0x8C11_C7B2) == 0x9079
        && memory.ReadInstructionUInt16(0x8C11_C7B4) == 0x64E3
        && memory.ReadInstructionUInt16(0x8C11_C7B6) == 0x7404
        && memory.ReadInstructionUInt16(0x8C11_C7B8) == 0x05DE
        && memory.ReadInstructionUInt16(0x8C11_C7BA) == 0xE664
        && memory.ReadInstructionUInt16(0x8C11_C7BC) == 0xE707
        && memory.ReadInstructionUInt16(0x8C11_C7BE) == 0x75FF
        && memory.ReadInstructionUInt16(0x8C11_C7C0) == 0x9373
        && memory.ReadInstructionUInt16(0x8C11_C7C2) == 0x2579
        && memory.ReadInstructionUInt16(0x8C11_C7C4) == 0x6153
        && memory.ReadInstructionUInt16(0x8C11_C7C6) == 0x4108
        && memory.ReadInstructionUInt16(0x8C11_C7C8) == 0x33DC
        && memory.ReadInstructionUInt16(0x8C11_C7CA) == 0x313C
        && memory.ReadInstructionUInt16(0x8C11_C7CC) == 0xD33D
        && memory.ReadInstructionUInt16(0x8C11_C7CE) == 0x6112
        && memory.ReadInstructionUInt16(0x8C11_C7D0) == 0x75FF
        && memory.ReadInstructionUInt16(0x8C11_C7D2) == 0x4108
        && memory.ReadInstructionUInt16(0x8C11_C7D4) == 0x4108
        && memory.ReadInstructionUInt16(0x8C11_C7D6) == 0x4108
        && memory.ReadInstructionUInt16(0x8C11_C7D8) == 0x4100
        && memory.ReadInstructionUInt16(0x8C11_C7DA) == 0x430B
        && memory.ReadInstructionUInt16(0x8C11_C7DC) == 0x6063
        && memory.ReadInstructionUInt16(0x8C11_C7DE) == 0x62E3
        && memory.ReadInstructionUInt16(0x8C11_C7E0) == 0x2402
        && memory.ReadInstructionUInt16(0x8C11_C7E2) == 0x7224
        && memory.ReadInstructionUInt16(0x8C11_C7E4) == 0x7404
        && memory.ReadInstructionUInt16(0x8C11_C7E6) == 0x3422
        && memory.ReadInstructionUInt16(0x8C11_C7E8) == 0x8BEA
        && memory.ReadInstructionUInt16(0x8C11_C8A8) == 0x0938
        && memory.ReadInstructionUInt16(0x8C11_C8AA) == 0x0914
        && memory.ReadUInt32(0x8C11_C8C4) == 0x8C10_7424;

    private bool IsDoa2PostTableVectorCopyLoop() =>
        memory.ReadInstructionUInt16(0x8C11_C7EA) == 0xE400
        && memory.ReadInstructionUInt16(0x8C11_C7EC) == 0xE520
        && memory.ReadInstructionUInt16(0x8C11_C7EE) == 0x935D
        && memory.ReadInstructionUInt16(0x8C11_C7F0) == 0x62E3
        && memory.ReadInstructionUInt16(0x8C11_C7F2) == 0x7224
        && memory.ReadInstructionUInt16(0x8C11_C7F4) == 0x33DC
        && memory.ReadInstructionUInt16(0x8C11_C7F6) == 0x334C
        && memory.ReadInstructionUInt16(0x8C11_C7F8) == 0x324C
        && memory.ReadInstructionUInt16(0x8C11_C7FA) == 0x6132
        && memory.ReadInstructionUInt16(0x8C11_C7FC) == 0x7404
        && memory.ReadInstructionUInt16(0x8C11_C7FE) == 0x3452
        && memory.ReadInstructionUInt16(0x8C11_C800) == 0x8FF5
        && memory.ReadInstructionUInt16(0x8C11_C802) == 0x2212
        && memory.ReadInstructionUInt16(0x8C11_C8AC) == 0x08F4;

    private bool IsDoa2EmptyCallbackTableScan() =>
        memory.ReadInstructionUInt16(0x8C12_FDA0) == 0x974E
        && memory.ReadInstructionUInt16(0x8C12_FDA2) == 0xE400
        && memory.ReadInstructionUInt16(0x8C12_FDA4) == 0x964D
        && memory.ReadInstructionUInt16(0x8C12_FDA6) == 0x6543
        && memory.ReadInstructionUInt16(0x8C12_FDA8) == 0xD026
        && memory.ReadInstructionUInt16(0x8C12_FDAA) == 0x004E
        && memory.ReadInstructionUInt16(0x8C12_FDAC) == 0x8801
        && memory.ReadInstructionUInt16(0x8C12_FDAE) == 0x8B06
        && memory.ReadInstructionUInt16(0x8C12_FDBE) == 0x346C
        && memory.ReadInstructionUInt16(0x8C12_FDC0) == 0x3472
        && memory.ReadInstructionUInt16(0x8C12_FDC2) == 0x8FF1
        && memory.ReadInstructionUInt16(0x8C12_FDC4) == 0x356C
        && memory.ReadInstructionUInt16(0x8C12_FDC6) == 0xE000
        && memory.ReadInstructionUInt16(0x8C12_FDC8) == 0x000B
        && memory.ReadInstructionUInt16(0x8C12_FDCA) == 0x0009
        && memory.ReadInstructionUInt16(0x8C12_FE40) == 0x0E20
        && memory.ReadInstructionUInt16(0x8C12_FE42) == 0x01C4
        && memory.ReadUInt32(0x8C12_FE44) == 0x8C2F_6820;

    private bool IsDoa2FiveWordTableCopyLoop() =>
        memory.ReadInstructionUInt16(0x8C11_B226) == 0xE340
        && memory.ReadInstructionUInt16(0x8C11_B228) == 0x914B
        && memory.ReadInstructionUInt16(0x8C11_B22A) == 0x33EC
        && memory.ReadInstructionUInt16(0x8C11_B22C) == 0x9048
        && memory.ReadInstructionUInt16(0x8C11_B22E) == 0x6332
        && memory.ReadInstructionUInt16(0x8C11_B230) == 0x31EC
        && memory.ReadInstructionUInt16(0x8C11_B232) == 0x30EC
        && memory.ReadInstructionUInt16(0x8C11_B234) == 0x6233
        && memory.ReadInstructionUInt16(0x8C11_B236) == 0x4308
        && memory.ReadInstructionUInt16(0x8C11_B238) == 0x332C
        && memory.ReadInstructionUInt16(0x8C11_B23A) == 0x4308
        && memory.ReadInstructionUInt16(0x8C11_B23C) == 0x4308
        && memory.ReadInstructionUInt16(0x8C11_B23E) == 0x4300
        && memory.ReadInstructionUInt16(0x8C11_B240) == 0x331C
        && memory.ReadInstructionUInt16(0x8C11_B242) == 0x6233
        && memory.ReadInstructionUInt16(0x8C11_B244) == 0x32CC
        && memory.ReadInstructionUInt16(0x8C11_B246) == 0x6323
        && memory.ReadInstructionUInt16(0x8C11_B248) == 0x334C
        && memory.ReadInstructionUInt16(0x8C11_B24A) == 0x6232
        && memory.ReadInstructionUInt16(0x8C11_B24C) == 0x30CC
        && memory.ReadInstructionUInt16(0x8C11_B24E) == 0x0426
        && memory.ReadInstructionUInt16(0x8C11_B250) == 0x7404
        && memory.ReadInstructionUInt16(0x8C11_B252) == 0x3452
        && memory.ReadInstructionUInt16(0x8C11_B254) == 0x8BE7
        && memory.ReadInstructionUInt16(0x8C11_B2C0) == 0x00B0
        && memory.ReadInstructionUInt16(0x8C11_B2C2) == 0x0170;

    private bool IsDoa2FiveWordMirrorCopyLoop() =>
        memory.ReadInstructionUInt16(0x8C0F_90B0) == 0xD333
        && memory.ReadInstructionUInt16(0x8C0F_90B2) == 0xE405
        && memory.ReadInstructionUInt16(0x8C0F_90B4) == 0xD533
        && memory.ReadInstructionUInt16(0x8C0F_90B6) == 0x6632
        && memory.ReadInstructionUInt16(0x8C0F_90B8) == 0x6366
        && memory.ReadInstructionUInt16(0x8C0F_90BA) == 0x74FF
        && memory.ReadInstructionUInt16(0x8C0F_90BC) == 0x2448
        && memory.ReadInstructionUInt16(0x8C0F_90BE) == 0x2532
        && memory.ReadInstructionUInt16(0x8C0F_90C0) == 0x8FFA
        && memory.ReadInstructionUInt16(0x8C0F_90C2) == 0x7504
        && memory.ReadInstructionUInt16(0x8C0F_90C4) == 0x000B
        && memory.ReadInstructionUInt16(0x8C0F_90C6) == 0x0009
        && memory.ReadUInt32(0x8C0F_9180) == 0x8C2B_6F34
        && memory.ReadUInt32(0x8C0F_9184) == 0x8C2B_6F68;

    private bool IsDoa2EmptyStackWordScanLoop() =>
        memory.ReadInstructionUInt16(0x8C11_B300) == 0x6272
        && memory.ReadInstructionUInt16(0x8C11_B302) == 0x2228
        && memory.ReadInstructionUInt16(0x8C11_B304) == 0x8917
        && memory.ReadInstructionUInt16(0x8C11_B336) == 0x7704
        && memory.ReadInstructionUInt16(0x8C11_B338) == 0x7604
        && memory.ReadInstructionUInt16(0x8C11_B33A) == 0x35AC
        && memory.ReadInstructionUInt16(0x8C11_B33C) == 0x37D2
        && memory.ReadInstructionUInt16(0x8C11_B33E) == 0x8BDF;

    private bool IsDoa2EmptyTaskHelperReturn() =>
        memory.ReadInstructionUInt16(0x8C13_06A0) == 0x2FE6
        && memory.ReadInstructionUInt16(0x8C13_06A2) == 0x6363
        && memory.ReadInstructionUInt16(0x8C13_06A4) == 0x2FD6
        && memory.ReadInstructionUInt16(0x8C13_06A6) == 0x6D63
        && memory.ReadInstructionUInt16(0x8C13_06A8) == 0x2FC6
        && memory.ReadInstructionUInt16(0x8C13_06AA) == 0x4D08
        && memory.ReadInstructionUInt16(0x8C13_06AC) == 0xDC27
        && memory.ReadInstructionUInt16(0x8C13_06AE) == 0x3D3C
        && memory.ReadInstructionUInt16(0x8C13_06B0) == 0x9046
        && memory.ReadInstructionUInt16(0x8C13_06B2) == 0x4D08
        && memory.ReadInstructionUInt16(0x8C13_06B4) == 0x4F22
        && memory.ReadInstructionUInt16(0x8C13_06B6) == 0x02CE
        && memory.ReadInstructionUInt16(0x8C13_06B8) == 0x6E73
        && memory.ReadInstructionUInt16(0x8C13_06BA) == 0x2228
        && memory.ReadInstructionUInt16(0x8C13_06BC) == 0x8D09
        && memory.ReadInstructionUInt16(0x8C13_06BE) == 0x4E08
        && memory.ReadInstructionUInt16(0x8C13_06D2) == 0xE400
        && memory.ReadInstructionUInt16(0x8C13_06D4) == 0x9736
        && memory.ReadInstructionUInt16(0x8C13_06D6) == 0x6243
        && memory.ReadInstructionUInt16(0x8C13_06D8) == 0x3262
        && memory.ReadInstructionUInt16(0x8C13_06DA) == 0x6143
        && memory.ReadInstructionUInt16(0x8C13_06DC) == 0x8D07
        && memory.ReadInstructionUInt16(0x8C13_06DE) == 0x375C
        && memory.ReadInstructionUInt16(0x8C13_06EE) == 0x9328
        && memory.ReadInstructionUInt16(0x8C13_06F0) == 0x9028
        && memory.ReadInstructionUInt16(0x8C13_06F2) == 0x33CC
        && memory.ReadInstructionUInt16(0x8C13_06F4) == 0x305C
        && memory.ReadInstructionUInt16(0x8C13_06F6) == 0x30DC
        && memory.ReadInstructionUInt16(0x8C13_06F8) == 0x33EC
        && memory.ReadInstructionUInt16(0x8C13_06FA) == 0x06EE
        && memory.ReadInstructionUInt16(0x8C13_06FC) == 0x6232
        && memory.ReadInstructionUInt16(0x8C13_06FE) == 0x364C
        && memory.ReadInstructionUInt16(0x8C13_0700) == 0x3248
        && memory.ReadInstructionUInt16(0x8C13_0702) == 0x6423
        && memory.ReadInstructionUInt16(0x8C13_0704) == 0x901F
        && memory.ReadInstructionUInt16(0x8C13_0706) == 0x30CC
        && memory.ReadInstructionUInt16(0x8C13_0708) == 0x30DC
        && memory.ReadInstructionUInt16(0x8C13_070A) == 0x03EE
        && memory.ReadInstructionUInt16(0x8C13_070C) == 0x3636
        && memory.ReadInstructionUInt16(0x8C13_070E) == 0x8B03
        && memory.ReadInstructionUInt16(0x8C13_0718) == 0x9014
        && memory.ReadInstructionUInt16(0x8C13_071A) == 0x305C
        && memory.ReadInstructionUInt16(0x8C13_071C) == 0x30DC
        && memory.ReadInstructionUInt16(0x8C13_071E) == 0x03EE
        && memory.ReadInstructionUInt16(0x8C13_0720) == 0x3432
        && memory.ReadInstructionUInt16(0x8C13_0722) == 0x8905
        && memory.ReadInstructionUInt16(0x8C13_0730) == 0x4F26
        && memory.ReadInstructionUInt16(0x8C13_0732) == 0xE000
        && memory.ReadInstructionUInt16(0x8C13_0734) == 0x6CF6
        && memory.ReadInstructionUInt16(0x8C13_0736) == 0x6DF6
        && memory.ReadInstructionUInt16(0x8C13_0738) == 0x000B
        && memory.ReadInstructionUInt16(0x8C13_073A) == 0x6EF6
        && memory.ReadInstructionUInt16(0x8C13_0740) == 0x07D0
        && memory.ReadInstructionUInt16(0x8C13_0742) == 0x0730
        && memory.ReadInstructionUInt16(0x8C13_0744) == 0x00E4
        && memory.ReadInstructionUInt16(0x8C13_0746) == 0x0854
        && memory.ReadUInt32(0x8C13_074C) == 0x8C2F_5E80;

    private bool IsDoa2EmptyTaskHelperCallerLoop() =>
        memory.ReadInstructionUInt16(0x8C13_07D6) == 0x9053
        && memory.ReadInstructionUInt16(0x8C13_07D8) == 0x69D3
        && memory.ReadInstructionUInt16(0x8C13_07DA) == 0x4908
        && memory.ReadInstructionUInt16(0x8C13_07DC) == 0x62E3
        && memory.ReadInstructionUInt16(0x8C13_07DE) == 0x30EC
        && memory.ReadInstructionUInt16(0x8C13_07E0) == 0x308C
        && memory.ReadInstructionUInt16(0x8C13_07E2) == 0x309C
        && memory.ReadInstructionUInt16(0x8C13_07E4) == 0x2F06
        && memory.ReadInstructionUInt16(0x8C13_07E6) == 0x7244
        && memory.ReadInstructionUInt16(0x8C13_07E8) == 0x51C2
        && memory.ReadInstructionUInt16(0x8C13_07EA) == 0x328C
        && memory.ReadInstructionUInt16(0x8C13_07EC) == 0x60C2
        && memory.ReadInstructionUInt16(0x8C13_07EE) == 0x329C
        && memory.ReadInstructionUInt16(0x8C13_07F0) == 0x5113
        && memory.ReadInstructionUInt16(0x8C13_07F2) == 0x039E
        && memory.ReadInstructionUInt16(0x8C13_07F4) == 0x50C3
        && memory.ReadInstructionUInt16(0x8C13_07F6) == 0x4108
        && memory.ReadInstructionUInt16(0x8C13_07F8) == 0x001E
        && memory.ReadInstructionUInt16(0x8C13_07FA) == 0x308C
        && memory.ReadInstructionUInt16(0x8C13_07FC) == 0x019E
        && memory.ReadInstructionUInt16(0x8C13_07FE) == 0x2212
        && memory.ReadInstructionUInt16(0x8C13_0800) == 0x3318
        && memory.ReadInstructionUInt16(0x8C13_0802) == 0x62F6
        && memory.ReadInstructionUInt16(0x8C13_0804) == 0x2232
        && memory.ReadInstructionUInt16(0x8C13_0806) == 0xD221
        && memory.ReadInstructionUInt16(0x8C13_0808) == 0x6122
        && memory.ReadInstructionUInt16(0x8C13_080A) == 0x2118
        && memory.ReadInstructionUInt16(0x8C13_080C) == 0x8901
        && memory.ReadInstructionUInt16(0x8C13_0812) == 0x65E3
        && memory.ReadInstructionUInt16(0x8C13_0814) == 0x66B3
        && memory.ReadInstructionUInt16(0x8C13_0816) == 0x67D3
        && memory.ReadInstructionUInt16(0x8C13_0818) == 0xBF42
        && memory.ReadInstructionUInt16(0x8C13_081A) == 0x64C3
        && memory.ReadInstructionUInt16(0x8C13_081C) == 0x9030
        && memory.ReadInstructionUInt16(0x8C13_081E) == 0x30EC
        && memory.ReadInstructionUInt16(0x8C13_0820) == 0x308C
        && memory.ReadInstructionUInt16(0x8C13_0822) == 0x039E
        && memory.ReadInstructionUInt16(0x8C13_0824) == 0x2338
        && memory.ReadInstructionUInt16(0x8C13_0826) == 0x8B01
        && memory.ReadInstructionUInt16(0x8C13_0828) == 0xA0BA
        && memory.ReadInstructionUInt16(0x8C13_082A) == 0x0009
        && memory.ReadInstructionUInt16(0x8C13_0880) == 0x00E4
        && memory.ReadUInt32(0x8C13_088C) == 0x8C2F_7640
        && memory.ReadInstructionUInt16(0x8C13_09A0) == 0xE305
        && memory.ReadInstructionUInt16(0x8C13_09A2) == 0x7D01
        && memory.ReadInstructionUInt16(0x8C13_09A4) == 0x3D32
        && memory.ReadInstructionUInt16(0x8C13_09A6) == 0x8B00
        && memory.ReadInstructionUInt16(0x8C13_09A8) == 0xED00
        && memory.ReadInstructionUInt16(0x8C13_09AA) == 0x53F2
        && memory.ReadInstructionUInt16(0x8C13_09AC) == 0x73FF
        && memory.ReadInstructionUInt16(0x8C13_09AE) == 0x2338
        && memory.ReadInstructionUInt16(0x8C13_09B0) == 0x8D02
        && memory.ReadInstructionUInt16(0x8C13_09B2) == 0x1F32
        && memory.ReadInstructionUInt16(0x8C13_09B4) == 0xAF0F
        && memory.ReadInstructionUInt16(0x8C13_09B6) == 0x0009
        && memory.ReadInstructionUInt16(0x8C13_09B8) == 0xE000
        && memory.ReadInstructionUInt16(0x8C13_09BA) == 0x7F0C;

    private bool IsDoa2UnrolledWordCopyReturn() =>
        memory.ReadInstructionUInt16(0x8C10_E60A) == 0x5326
        && memory.ReadInstructionUInt16(0x8C10_E60C) == 0x1107
        && memory.ReadInstructionUInt16(0x8C10_E60E) == 0x5025
        && memory.ReadInstructionUInt16(0x8C10_E610) == 0x1136
        && memory.ReadInstructionUInt16(0x8C10_E612) == 0x5324
        && memory.ReadInstructionUInt16(0x8C10_E614) == 0x1105
        && memory.ReadInstructionUInt16(0x8C10_E616) == 0x5023
        && memory.ReadInstructionUInt16(0x8C10_E618) == 0x1134
        && memory.ReadInstructionUInt16(0x8C10_E61A) == 0x5322
        && memory.ReadInstructionUInt16(0x8C10_E61C) == 0x1103
        && memory.ReadInstructionUInt16(0x8C10_E61E) == 0x5021
        && memory.ReadInstructionUInt16(0x8C10_E620) == 0x1132
        && memory.ReadInstructionUInt16(0x8C10_E622) == 0x6322
        && memory.ReadInstructionUInt16(0x8C10_E624) == 0x1101
        && memory.ReadInstructionUInt16(0x8C10_E626) == 0x2132
        && memory.ReadInstructionUInt16(0x8C10_E628) == 0x000B
        && memory.ReadInstructionUInt16(0x8C10_E62A) == 0x63F6;

    private bool IsDoa2ScratchVectorCopyWrapper() =>
        memory.ReadInstructionUInt16(0x8C10_3AF0) == 0xD416
        && memory.ReadInstructionUInt16(0x8C10_3AF2) == 0xE004
        && memory.ReadInstructionUInt16(0x8C10_3AF4) == 0x4F22
        && memory.ReadInstructionUInt16(0x8C10_3AF6) == 0xD116
        && memory.ReadInstructionUInt16(0x8C10_3AF8) == 0x6243
        && memory.ReadInstructionUInt16(0x8C10_3AFA) == 0xD316
        && memory.ReadInstructionUInt16(0x8C10_3AFC) == 0xF44A
        && memory.ReadInstructionUInt16(0x8C10_3AFE) == 0xF457
        && memory.ReadInstructionUInt16(0x8C10_3B00) == 0xE008
        && memory.ReadInstructionUInt16(0x8C10_3B02) == 0xF467
        && memory.ReadInstructionUInt16(0x8C10_3B04) == 0x430B
        && memory.ReadInstructionUInt16(0x8C10_3B06) == 0xE00C
        && memory.ReadInstructionUInt16(0x8C10_3B08) == 0x4F26
        && memory.ReadInstructionUInt16(0x8C10_3B0A) == 0x000B
        && memory.ReadInstructionUInt16(0x8C10_3B0C) == 0x0009
        && memory.ReadUInt32(0x8C10_3B4C) == 0x8C1C_A920
        && memory.ReadUInt32(0x8C10_3B50) == 0x8C1C_A930
        && memory.ReadUInt32(0x8C10_3B54) == 0x8C10_E5D8
        && memory.ReadInstructionUInt16(0x8C10_E5D8) == 0x2F36
        && memory.ReadInstructionUInt16(0x8C10_E5DA) == 0xD302
        && memory.ReadInstructionUInt16(0x8C10_E5DC) == 0x033E
        && memory.ReadInstructionUInt16(0x8C10_E5DE) == 0x70FC
        && memory.ReadInstructionUInt16(0x8C10_E5E0) == 0x432B
        && memory.ReadInstructionUInt16(0x8C10_E5E2) == 0x032E
        && memory.ReadUInt32(0x8C10_E5E4) == 0x8C10_E62C
        && memory.ReadUInt32(0x8C10_E638) == 0x8C10_E61E
        && memory.ReadInstructionUInt16(0x8C10_E61E) == 0x5021
        && memory.ReadInstructionUInt16(0x8C10_E620) == 0x1132
        && memory.ReadInstructionUInt16(0x8C10_E622) == 0x6322
        && memory.ReadInstructionUInt16(0x8C10_E624) == 0x1101
        && memory.ReadInstructionUInt16(0x8C10_E626) == 0x2132
        && memory.ReadInstructionUInt16(0x8C10_E628) == 0x000B
        && memory.ReadInstructionUInt16(0x8C10_E62A) == 0x63F6;

    private bool IsDoa2TableEntryAddressHelper() =>
        memory.ReadInstructionUInt16(0x8C0F_29DE) == 0x6043
        && memory.ReadInstructionUInt16(0x8C0F_29E0) == 0x0009
        && memory.ReadInstructionUInt16(0x8C0F_29E2) == 0x4000
        && memory.ReadInstructionUInt16(0x8C0F_29E4) == 0x6343
        && memory.ReadInstructionUInt16(0x8C0F_29E6) == 0x303C
        && memory.ReadInstructionUInt16(0x8C0F_29E8) == 0xD23A
        && memory.ReadInstructionUInt16(0x8C0F_29EA) == 0x4008
        && memory.ReadInstructionUInt16(0x8C0F_29EC) == 0x4008
        && memory.ReadInstructionUInt16(0x8C0F_29EE) == 0x4000
        && memory.ReadInstructionUInt16(0x8C0F_29F0) == 0x000B
        && memory.ReadInstructionUInt16(0x8C0F_29F2) == 0x302C
        && memory.ReadUInt32(0x8C0F_2AD4) == 0x8C2A_D770;

    private bool IsDoa2ZeroStatusByteTableScan() =>
        memory.ReadInstructionUInt16(0x8C0F_3660) == 0x2FC6
        && memory.ReadInstructionUInt16(0x8C0F_3662) == 0x6C43
        && memory.ReadInstructionUInt16(0x8C0F_3664) == 0x2FB6
        && memory.ReadInstructionUInt16(0x8C0F_3666) == 0xEB01
        && memory.ReadInstructionUInt16(0x8C0F_3668) == 0x2FA6
        && memory.ReadInstructionUInt16(0x8C0F_366A) == 0xEA08
        && memory.ReadInstructionUInt16(0x8C0F_366C) == 0x2F96
        && memory.ReadInstructionUInt16(0x8C0F_366E) == 0x2F86
        && memory.ReadInstructionUInt16(0x8C0F_3670) == 0x4F22
        && memory.ReadInstructionUInt16(0x8C0F_3672) == 0xD822
        && memory.ReadInstructionUInt16(0x8C0F_3674) == 0xD922
        && memory.ReadInstructionUInt16(0x8C0F_3676) == 0xDD23
        && memory.ReadInstructionUInt16(0x8C0F_3678) == 0x480B
        && memory.ReadInstructionUInt16(0x8C0F_367A) == 0x64E3
        && memory.ReadInstructionUInt16(0x8C0F_367C) == 0x6403
        && memory.ReadInstructionUInt16(0x8C0F_367E) == 0x6040
        && memory.ReadInstructionUInt16(0x8C0F_3680) == 0x600C
        && memory.ReadInstructionUInt16(0x8C0F_3682) == 0x8801
        && memory.ReadInstructionUInt16(0x8C0F_3684) == 0x8B12
        && memory.ReadInstructionUInt16(0x8C0F_36AC) == 0x7E01
        && memory.ReadInstructionUInt16(0x8C0F_36AE) == 0x3EA3
        && memory.ReadInstructionUInt16(0x8C0F_36B0) == 0x8BE2
        && memory.ReadInstructionUInt16(0x8C0F_36B2) == 0xD315
        && memory.ReadInstructionUInt16(0x8C0F_36B4) == 0x23E0
        && memory.ReadInstructionUInt16(0x8C0F_36B6) == 0x60C3
        && memory.ReadInstructionUInt16(0x8C0F_36B8) == 0x0009
        && memory.ReadInstructionUInt16(0x8C0F_36BA) == 0x4F26
        && memory.ReadInstructionUInt16(0x8C0F_36BC) == 0x68F6
        && memory.ReadInstructionUInt16(0x8C0F_36BE) == 0x69F6
        && memory.ReadInstructionUInt16(0x8C0F_36C0) == 0x6AF6
        && memory.ReadInstructionUInt16(0x8C0F_36C2) == 0x6BF6
        && memory.ReadInstructionUInt16(0x8C0F_36C4) == 0x6CF6
        && memory.ReadInstructionUInt16(0x8C0F_36C6) == 0x6DF6
        && memory.ReadInstructionUInt16(0x8C0F_36C8) == 0x000B
        && memory.ReadInstructionUInt16(0x8C0F_36CA) == 0x6EF6
        && memory.ReadUInt32(0x8C0F_36FC) == 0x8C0F_29DE
        && memory.ReadUInt32(0x8C0F_3700) == 0x8CE7_E1D0
        && memory.ReadUInt32(0x8C0F_3704) == 0x8C1C_938C
        && memory.ReadUInt32(0x8C0F_3708) == 0x8C2A_DA94
        && IsDoa2TableEntryAddressHelper();

    private bool IsDoa2ZeroRecordGroupScan() =>
        memory.ReadInstructionUInt16(0x8C01_3BF6) == 0x63B3
        && memory.ReadInstructionUInt16(0x8C01_3BF8) == 0x4308
        && memory.ReadInstructionUInt16(0x8C01_3BFA) == 0x4300
        && memory.ReadInstructionUInt16(0x8C01_3BFC) == 0x33AC
        && memory.ReadInstructionUInt16(0x8C01_3BFE) == 0x6231
        && memory.ReadInstructionUInt16(0x8C01_3C00) == 0x2228
        && memory.ReadInstructionUInt16(0x8C01_3C02) == 0x890F
        && memory.ReadInstructionUInt16(0x8C01_3C24) == 0x7B01
        && memory.ReadInstructionUInt16(0x8C01_3C26) == 0x63B3
        && memory.ReadInstructionUInt16(0x8C01_3C28) == 0x4308
        && memory.ReadInstructionUInt16(0x8C01_3C2A) == 0x4300
        && memory.ReadInstructionUInt16(0x8C01_3C2C) == 0x33AC
        && memory.ReadInstructionUInt16(0x8C01_3C2E) == 0x6231
        && memory.ReadInstructionUInt16(0x8C01_3C30) == 0x7C08
        && memory.ReadInstructionUInt16(0x8C01_3C32) == 0x7E08
        && memory.ReadInstructionUInt16(0x8C01_3C34) == 0x2228
        && memory.ReadInstructionUInt16(0x8C01_3C36) == 0x8D10
        && memory.ReadInstructionUInt16(0x8C01_3C38) == 0x7D08
        && memory.ReadInstructionUInt16(0x8C01_3C5A) == 0x7B01
        && memory.ReadInstructionUInt16(0x8C01_3C5C) == 0x63B3
        && memory.ReadInstructionUInt16(0x8C01_3C5E) == 0x4308
        && memory.ReadInstructionUInt16(0x8C01_3C60) == 0x4300
        && memory.ReadInstructionUInt16(0x8C01_3C62) == 0x33AC
        && memory.ReadInstructionUInt16(0x8C01_3C64) == 0x6231
        && memory.ReadInstructionUInt16(0x8C01_3C66) == 0x7C08
        && memory.ReadInstructionUInt16(0x8C01_3C68) == 0x7E08
        && memory.ReadInstructionUInt16(0x8C01_3C6A) == 0x2228
        && memory.ReadInstructionUInt16(0x8C01_3C6C) == 0x8D10
        && memory.ReadInstructionUInt16(0x8C01_3C6E) == 0x7D08
        && memory.ReadInstructionUInt16(0x8C01_3C90) == 0x7B01
        && memory.ReadInstructionUInt16(0x8C01_3C92) == 0x63B3
        && memory.ReadInstructionUInt16(0x8C01_3C94) == 0x4308
        && memory.ReadInstructionUInt16(0x8C01_3C96) == 0x4300
        && memory.ReadInstructionUInt16(0x8C01_3C98) == 0x33AC
        && memory.ReadInstructionUInt16(0x8C01_3C9A) == 0x6231
        && memory.ReadInstructionUInt16(0x8C01_3C9C) == 0x7C08
        && memory.ReadInstructionUInt16(0x8C01_3C9E) == 0x7E08
        && memory.ReadInstructionUInt16(0x8C01_3CA0) == 0x2228
        && memory.ReadInstructionUInt16(0x8C01_3CA2) == 0x8D10
        && memory.ReadInstructionUInt16(0x8C01_3CA4) == 0x7D08
        && memory.ReadInstructionUInt16(0x8C01_3CC6) == 0x7B01
        && memory.ReadInstructionUInt16(0x8C01_3CC8) == 0x3B82
        && memory.ReadInstructionUInt16(0x8C01_3CCA) == 0x7C08
        && memory.ReadInstructionUInt16(0x8C01_3CCC) == 0x7E08
        && memory.ReadInstructionUInt16(0x8C01_3CCE) == 0x8F92
        && memory.ReadInstructionUInt16(0x8C01_3CD0) == 0x7D08
        && memory.ReadInstructionUInt16(0x8C01_3CD2) == 0x4F26
        && memory.ReadInstructionUInt16(0x8C01_3CD4) == 0x68F6
        && memory.ReadInstructionUInt16(0x8C01_3CD6) == 0x69F6
        && memory.ReadInstructionUInt16(0x8C01_3CD8) == 0x6AF6
        && memory.ReadInstructionUInt16(0x8C01_3CDA) == 0x6BF6
        && memory.ReadInstructionUInt16(0x8C01_3CDC) == 0x6CF6
        && memory.ReadInstructionUInt16(0x8C01_3CDE) == 0x6DF6
        && memory.ReadInstructionUInt16(0x8C01_3CE0) == 0x000B
        && memory.ReadInstructionUInt16(0x8C01_3CE2) == 0x6EF6
        && memory.ReadUInt32(0x8C01_3CF8) == 0x8C1E_FFB8;

    private bool IsDoa2ColorPackCommonPath() =>
        memory.ReadInstructionUInt16(0x8C10_06EC) == 0x50DD
        && memory.ReadInstructionUInt16(0x8C10_06EE) == 0xC930
        && memory.ReadInstructionUInt16(0x8C10_06F0) == 0x6403
        && memory.ReadInstructionUInt16(0x8C10_06F2) == 0xE018
        && memory.ReadInstructionUInt16(0x8C10_06F4) == 0xF7D6
        && memory.ReadInstructionUInt16(0x8C10_06F6) == 0x2948
        && memory.ReadInstructionUInt16(0x8C10_06F8) == 0xE020
        && memory.ReadInstructionUInt16(0x8C10_06FA) == 0x8D06
        && memory.ReadInstructionUInt16(0x8C10_06FC) == 0xF4D6
        && memory.ReadInstructionUInt16(0x8C10_070A) == 0xE01C
        && memory.ReadInstructionUInt16(0x8C10_070C) == 0xE320
        && memory.ReadInstructionUInt16(0x8C10_070E) == 0xF5D6
        && memory.ReadInstructionUInt16(0x8C10_0710) == 0x2438
        && memory.ReadInstructionUInt16(0x8C10_0712) == 0xE024
        && memory.ReadInstructionUInt16(0x8C10_0714) == 0x8F06
        && memory.ReadInstructionUInt16(0x8C10_0716) == 0xF6D6
        && memory.ReadInstructionUInt16(0x8C10_0718) == 0xF35C
        && memory.ReadInstructionUInt16(0x8C10_071A) == 0xF5FC
        && memory.ReadInstructionUInt16(0x8C10_071C) == 0xF531
        && memory.ReadInstructionUInt16(0x8C10_071E) == 0xF36C
        && memory.ReadInstructionUInt16(0x8C10_0720) == 0xF6FC
        && memory.ReadInstructionUInt16(0x8C10_0722) == 0xF631
        && memory.ReadInstructionUInt16(0x8C10_0724) == 0x50DD
        && memory.ReadInstructionUInt16(0x8C10_0726) == 0xD376
        && memory.ReadInstructionUInt16(0x8C10_0728) == 0xD176
        && memory.ReadInstructionUInt16(0x8C10_072A) == 0x2039
        && memory.ReadInstructionUInt16(0x8C10_072C) == 0x3010
        && memory.ReadInstructionUInt16(0x8C10_072E) == 0x8907
        && memory.ReadInstructionUInt16(0x8C10_0730) == 0xD173
        && memory.ReadInstructionUInt16(0x8C10_0732) == 0x3010
        && memory.ReadInstructionUInt16(0x8C10_0734) == 0x890C
        && memory.ReadInstructionUInt16(0x8C10_0736) == 0xA00F
        && memory.ReadInstructionUInt16(0x8C10_0738) == 0x0009
        && memory.ReadInstructionUInt16(0x8C10_0758) == 0x64F3
        && memory.ReadInstructionUInt16(0x8C10_075A) == 0x7418
        && memory.ReadInstructionUInt16(0x8C10_075C) == 0xD56B
        && memory.ReadInstructionUInt16(0x8C10_075E) == 0xF47A
        && memory.ReadInstructionUInt16(0x8C10_0760) == 0x6342
        && memory.ReadInstructionUInt16(0x8C10_0762) == 0x2359
        && memory.ReadInstructionUInt16(0x8C10_0764) == 0x1F35
        && memory.ReadInstructionUInt16(0x8C10_0766) == 0xF44A
        && memory.ReadInstructionUInt16(0x8C10_0768) == 0x6342
        && memory.ReadInstructionUInt16(0x8C10_076A) == 0x2539
        && memory.ReadInstructionUInt16(0x8C10_076C) == 0x2F52
        && memory.ReadInstructionUInt16(0x8C10_076E) == 0xF45A
        && memory.ReadInstructionUInt16(0x8C10_0770) == 0x6942
        && memory.ReadInstructionUInt16(0x8C10_0772) == 0xF46A
        && memory.ReadInstructionUInt16(0x8C10_0774) == 0x6242
        && memory.ReadInstructionUInt16(0x8C10_0776) == 0x4929
        && memory.ReadInstructionUInt16(0x8C10_0778) == 0x4229
        && memory.ReadInstructionUInt16(0x8C10_077A) == 0x1F23;

    private bool IsDoa2ColorBytePackCommonPath() =>
        memory.ReadInstructionUInt16(0x8C10_0AC0) == 0xC72B
        && memory.ReadInstructionUInt16(0x8C10_0AC2) == 0xF808
        && memory.ReadInstructionUInt16(0x8C10_0AC4) == 0xC72B
        && memory.ReadInstructionUInt16(0x8C10_0AC6) == 0xF908
        && memory.ReadInstructionUInt16(0x8C10_0AC8) == 0xF482
        && memory.ReadInstructionUInt16(0x8C10_0ACA) == 0xF582
        && memory.ReadInstructionUInt16(0x8C10_0ACC) == 0xF682
        && memory.ReadInstructionUInt16(0x8C10_0ACE) == 0xF782
        && memory.ReadInstructionUInt16(0x8C10_0AD0) == 0xF495
        && memory.ReadInstructionUInt16(0x8C10_0AD2) == 0x8F0D
        && memory.ReadInstructionUInt16(0x8C10_0AD4) == 0xF84C
        && memory.ReadInstructionUInt16(0x8C10_0AF0) == 0xF38C
        && memory.ReadInstructionUInt16(0x8C10_0AF2) == 0xF33D
        && memory.ReadInstructionUInt16(0x8C10_0AF4) == 0xF595
        && memory.ReadInstructionUInt16(0x8C10_0AF6) == 0x045A
        && memory.ReadInstructionUInt16(0x8C10_0AF8) == 0x8F0A
        && memory.ReadInstructionUInt16(0x8C10_0AFA) == 0xF45C
        && memory.ReadInstructionUInt16(0x8C10_0B10) == 0xF34C
        && memory.ReadInstructionUInt16(0x8C10_0B12) == 0xF33D
        && memory.ReadInstructionUInt16(0x8C10_0B14) == 0xF695
        && memory.ReadInstructionUInt16(0x8C10_0B16) == 0x055A
        && memory.ReadInstructionUInt16(0x8C10_0B18) == 0x8F0A
        && memory.ReadInstructionUInt16(0x8C10_0B1A) == 0xF46C
        && memory.ReadInstructionUInt16(0x8C10_0B30) == 0xF34C
        && memory.ReadInstructionUInt16(0x8C10_0B32) == 0xF33D
        && memory.ReadInstructionUInt16(0x8C10_0B34) == 0xF795
        && memory.ReadInstructionUInt16(0x8C10_0B36) == 0x065A
        && memory.ReadInstructionUInt16(0x8C10_0B38) == 0x8F22
        && memory.ReadInstructionUInt16(0x8C10_0B3A) == 0xF47C
        && memory.ReadInstructionUInt16(0x8C10_0B80) == 0xF34C
        && memory.ReadInstructionUInt16(0x8C10_0B82) == 0xF33D
        && memory.ReadInstructionUInt16(0x8C10_0B84) == 0x907B
        && memory.ReadInstructionUInt16(0x8C10_0B86) == 0x3406
        && memory.ReadInstructionUInt16(0x8C10_0B88) == 0x075A
        && memory.ReadInstructionUInt16(0x8C10_0B8A) == 0x8F01
        && memory.ReadInstructionUInt16(0x8C10_0B8C) == 0xE100
        && memory.ReadInstructionUInt16(0x8C10_0B90) == 0x3506
        && memory.ReadInstructionUInt16(0x8C10_0B92) == 0x8B00
        && memory.ReadInstructionUInt16(0x8C10_0B96) == 0x3606
        && memory.ReadInstructionUInt16(0x8C10_0B98) == 0x8B00
        && memory.ReadInstructionUInt16(0x8C10_0B9C) == 0x3706
        && memory.ReadInstructionUInt16(0x8C10_0B9E) == 0x8F01
        && memory.ReadInstructionUInt16(0x8C10_0BA0) == 0x4418
        && memory.ReadInstructionUInt16(0x8C10_0BA4) == 0x245B
        && memory.ReadInstructionUInt16(0x8C10_0BA6) == 0x4418
        && memory.ReadInstructionUInt16(0x8C10_0BA8) == 0x246B
        && memory.ReadInstructionUInt16(0x8C10_0BAA) == 0x4418
        && memory.ReadInstructionUInt16(0x8C10_0BAC) == 0x247B
        && memory.ReadInstructionUInt16(0x8C10_0BAE) == 0x000B
        && memory.ReadInstructionUInt16(0x8C10_0BB0) == 0x6043;

    private bool IsDoa2TaEmitCommonPath() =>
        memory.ReadInstructionUInt16(0x8C10_077C) == 0x50DC
        && memory.ReadInstructionUInt16(0x8C10_077E) == 0x88FF
        && memory.ReadInstructionUInt16(0x8C10_0780) == 0x8F07
        && memory.ReadInstructionUInt16(0x8C10_0782) == 0x6403
        && memory.ReadInstructionUInt16(0x8C10_0792) == 0xD35F
        && memory.ReadInstructionUInt16(0x8C10_0794) == 0x4408
        && memory.ReadInstructionUInt16(0x8C10_0796) == 0x1F44
        && memory.ReadInstructionUInt16(0x8C10_0798) == 0x6032
        && memory.ReadInstructionUInt16(0x8C10_079A) == 0xD35F
        && memory.ReadInstructionUInt16(0x8C10_079C) == 0x044E
        && memory.ReadInstructionUInt16(0x8C10_079E) == 0xDE5F
        && memory.ReadInstructionUInt16(0x8C10_07A0) == 0x6243
        && memory.ReadInstructionUInt16(0x8C10_07A2) == 0x4229
        && memory.ReadInstructionUInt16(0x8C10_07A4) == 0x4219
        && memory.ReadInstructionUInt16(0x8C10_07A6) == 0xD15B
        && memory.ReadInstructionUInt16(0x8C10_07A8) == 0x2E49
        && memory.ReadInstructionUInt16(0x8C10_07AA) == 0xD45E
        && memory.ReadInstructionUInt16(0x8C10_07AC) == 0x2122
        && memory.ReadInstructionUInt16(0x8C10_07AE) == 0x2322
        && memory.ReadInstructionUInt16(0x8C10_07B0) == 0xD352
        && memory.ReadInstructionUInt16(0x8C10_07B2) == 0x50DD
        && memory.ReadInstructionUInt16(0x8C10_07B4) == 0xD25A
        && memory.ReadInstructionUInt16(0x8C10_07B6) == 0x2038
        && memory.ReadInstructionUInt16(0x8C10_07B8) == 0x8D0A
        && memory.ReadInstructionUInt16(0x8C10_07BA) == 0x2E2B
        && memory.ReadInstructionUInt16(0x8C10_07D0) == 0x60C2
        && memory.ReadInstructionUInt16(0x8C10_07D2) == 0x6142
        && memory.ReadInstructionUInt16(0x8C10_07D4) == 0x201B
        && memory.ReadInstructionUInt16(0x8C10_07D6) == 0x2E02
        && memory.ReadInstructionUInt16(0x8C10_07D8) == 0x53C1
        && memory.ReadInstructionUInt16(0x8C10_07DA) == 0x1E31
        && memory.ReadInstructionUInt16(0x8C10_07DC) == 0x52C2
        && memory.ReadInstructionUInt16(0x8C10_07DE) == 0x5341
        && memory.ReadInstructionUInt16(0x8C10_07E0) == 0x223B
        && memory.ReadInstructionUInt16(0x8C10_07E2) == 0x1E22
        && memory.ReadInstructionUInt16(0x8C10_07E4) == 0x53C3
        && memory.ReadInstructionUInt16(0x8C10_07E6) == 0x1E33
        && memory.ReadInstructionUInt16(0x8C10_07E8) == 0x52C4
        && memory.ReadInstructionUInt16(0x8C10_07EA) == 0x1E24
        && memory.ReadInstructionUInt16(0x8C10_07EC) == 0xB288
        && memory.ReadInstructionUInt16(0x8C10_07EE) == 0xF4EC
        && memory.ReadInstructionUInt16(0x8C10_07F0) == 0x52DF
        && memory.ReadInstructionUInt16(0x8C10_07F2) == 0x4028
        && memory.ReadInstructionUInt16(0x8C10_07F4) == 0x4018
        && memory.ReadInstructionUInt16(0x8C10_07F6) == 0x202B
        && memory.ReadInstructionUInt16(0x8C10_07F8) == 0x1E05
        && memory.ReadInstructionUInt16(0x8C10_07FA) == 0x0E83
        && memory.ReadInstructionUInt16(0x8C10_07FC) == 0xD34A
        && memory.ReadInstructionUInt16(0x8C10_07FE) == 0x7E20
        && memory.ReadInstructionUInt16(0x8C10_0800) == 0x64A3
        && memory.ReadInstructionUInt16(0x8C10_0802) == 0x65B3
        && memory.ReadInstructionUInt16(0x8C10_0804) == 0x2E32
        && memory.ReadInstructionUInt16(0x8C10_0806) == 0xE00C
        && memory.ReadInstructionUInt16(0x8C10_0808) == 0x6252
        && memory.ReadInstructionUInt16(0x8C10_080A) == 0x1E21
        && memory.ReadInstructionUInt16(0x8C10_080C) == 0x6342
        && memory.ReadInstructionUInt16(0x8C10_080E) == 0x1E32
        && memory.ReadInstructionUInt16(0x8C10_0810) == 0xFED7
        && memory.ReadInstructionUInt16(0x8C10_0812) == 0xE018
        && memory.ReadInstructionUInt16(0x8C10_0814) == 0x5351
        && memory.ReadInstructionUInt16(0x8C10_0816) == 0x1E34
        && memory.ReadInstructionUInt16(0x8C10_0818) == 0x5241
        && memory.ReadInstructionUInt16(0x8C10_081A) == 0x1E25
        && memory.ReadInstructionUInt16(0x8C10_081C) == 0xFED7
        && memory.ReadInstructionUInt16(0x8C10_081E) == 0xE024
        && memory.ReadInstructionUInt16(0x8C10_0820) == 0x5352
        && memory.ReadInstructionUInt16(0x8C10_0822) == 0x1E37
        && memory.ReadInstructionUInt16(0x8C10_0824) == 0x5242
        && memory.ReadInstructionUInt16(0x8C10_0826) == 0x1E28
        && memory.ReadInstructionUInt16(0x8C10_0828) == 0xFED7
        && memory.ReadInstructionUInt16(0x8C10_082A) == 0x5353
        && memory.ReadInstructionUInt16(0x8C10_082C) == 0x1E3A
        && memory.ReadInstructionUInt16(0x8C10_082E) == 0x5243
        && memory.ReadInstructionUInt16(0x8C10_0830) == 0xD33E
        && memory.ReadInstructionUInt16(0x8C10_0832) == 0x1E2B
        && memory.ReadInstructionUInt16(0x8C10_0834) == 0x283B
        && memory.ReadInstructionUInt16(0x8C10_0836) == 0x1E8C
        && memory.ReadInstructionUInt16(0x8C10_0838) == 0x52F5
        && memory.ReadInstructionUInt16(0x8C10_083A) == 0x229B
        && memory.ReadInstructionUInt16(0x8C10_083C) == 0x1E2D
        && memory.ReadInstructionUInt16(0x8C10_083E) == 0x61F2
        && memory.ReadInstructionUInt16(0x8C10_0840) == 0x291B
        && memory.ReadInstructionUInt16(0x8C10_0842) == 0x1E9E
        && memory.ReadInstructionUInt16(0x8C10_0844) == 0x52F3
        && memory.ReadInstructionUInt16(0x8C10_0846) == 0x61F2
        && memory.ReadInstructionUInt16(0x8C10_0848) == 0x212B
        && memory.ReadInstructionUInt16(0x8C10_084A) == 0x1E1F
        && memory.ReadInstructionUInt16(0x8C10_084C) == 0x0E83
        && memory.ReadInstructionUInt16(0x8C10_084E) == 0x7E20
        && memory.ReadInstructionUInt16(0x8C10_0850) == 0x0E83
        && memory.ReadInstructionUInt16(0x8C10_0852) == 0xD12F
        && memory.ReadInstructionUInt16(0x8C10_0854) == 0x7E20
        && memory.ReadInstructionUInt16(0x8C10_0856) == 0x54F4
        && memory.ReadInstructionUInt16(0x8C10_0858) == 0x6212
        && memory.ReadInstructionUInt16(0x8C10_085A) == 0xD335
        && memory.ReadInstructionUInt16(0x8C10_085C) == 0x342C
        && memory.ReadInstructionUInt16(0x8C10_085E) == 0xD22F
        && memory.ReadInstructionUInt16(0x8C10_0860) == 0x6042
        && memory.ReadInstructionUInt16(0x8C10_0862) == 0x2E29
        && memory.ReadInstructionUInt16(0x8C10_0864) == 0x2039
        && memory.ReadInstructionUInt16(0x8C10_0866) == 0x20EB
        && memory.ReadInstructionUInt16(0x8C10_0868) == 0x2402
        && memory.ReadInstructionUInt16(0x8C10_086A) == 0xE000
        && memory.ReadInstructionUInt16(0x8C10_086C) == 0x7F3C
        && memory.ReadInstructionUInt16(0x8C10_086E) == 0x4F26
        && memory.ReadInstructionUInt16(0x8C10_0870) == 0xFCF9
        && memory.ReadInstructionUInt16(0x8C10_0872) == 0xFDF9
        && memory.ReadInstructionUInt16(0x8C10_0874) == 0xFEF9
        && memory.ReadInstructionUInt16(0x8C10_0876) == 0xFFF9
        && memory.ReadInstructionUInt16(0x8C10_0878) == 0x68F6
        && memory.ReadInstructionUInt16(0x8C10_087A) == 0x69F6
        && memory.ReadInstructionUInt16(0x8C10_087C) == 0x6AF6
        && memory.ReadInstructionUInt16(0x8C10_087E) == 0x6BF6
        && memory.ReadInstructionUInt16(0x8C10_0880) == 0x6CF6
        && memory.ReadInstructionUInt16(0x8C10_0882) == 0x6DF6
        && memory.ReadInstructionUInt16(0x8C10_0884) == 0x000B
        && memory.ReadInstructionUInt16(0x8C10_0886) == 0x6EF6
        && memory.ReadInstructionUInt16(0x8C10_0D00) == 0xC70E
        && memory.ReadInstructionUInt16(0x8C10_0D02) == 0xF308
        && memory.ReadInstructionUInt16(0x8C10_0D04) == 0xC70E
        && memory.ReadInstructionUInt16(0x8C10_0D06) == 0xF108
        && memory.ReadInstructionUInt16(0x8C10_0D08) == 0xF432
        && memory.ReadInstructionUInt16(0x8C10_0D0A) == 0xF415
        && memory.ReadInstructionUInt16(0x8C10_0D0C) == 0x8F08
        && memory.ReadInstructionUInt16(0x8C10_0D0E) == 0xF54C
        && memory.ReadInstructionUInt16(0x8C10_0D20) == 0xF25C
        && memory.ReadInstructionUInt16(0x8C10_0D22) == 0xF23D
        && memory.ReadInstructionUInt16(0x8C10_0D24) == 0x9505
        && memory.ReadInstructionUInt16(0x8C10_0D26) == 0x045A
        && memory.ReadInstructionUInt16(0x8C10_0D28) == 0x3456
        && memory.ReadInstructionUInt16(0x8C10_0D2A) == 0x8B00
        && memory.ReadInstructionUInt16(0x8C10_0D2E) == 0x000B
        && memory.ReadInstructionUInt16(0x8C10_0D30) == 0x6043;

    private bool IsDoa2TextGlyphSetupCommonPath() =>
        memory.ReadInstructionUInt16(0x8C0E_1E08) == 0x64E0
        && memory.ReadInstructionUInt16(0x8C0E_1E0A) == 0x644C
        && memory.ReadInstructionUInt16(0x8C0E_1E0C) == 0x34A3
        && memory.ReadInstructionUInt16(0x8C0E_1E0E) == 0x8B65
        && memory.ReadInstructionUInt16(0x8C0E_1E10) == 0x9257
        && memory.ReadInstructionUInt16(0x8C0E_1E12) == 0x3427
        && memory.ReadInstructionUInt16(0x8C0E_1E14) == 0x8962
        && memory.ReadInstructionUInt16(0x8C0E_1E16) == 0x60E0
        && memory.ReadInstructionUInt16(0x8C0E_1E18) == 0x600C
        && memory.ReadInstructionUInt16(0x8C0E_1E1A) == 0x8840
        && memory.ReadInstructionUInt16(0x8C0E_1E1C) == 0x8B15
        && memory.ReadInstructionUInt16(0x8C0E_1E4A) == 0x6CE0
        && memory.ReadInstructionUInt16(0x8C0E_1E4C) == 0xE018
        && memory.ReadInstructionUInt16(0x8C0E_1E4E) == 0x2FB2
        && memory.ReadInstructionUInt16(0x8C0E_1E50) == 0x6CCC
        && memory.ReadInstructionUInt16(0x8C0E_1E52) == 0x7CE0
        && memory.ReadInstructionUInt16(0x8C0E_1E54) == 0x63C3
        && memory.ReadInstructionUInt16(0x8C0E_1E56) == 0x4C08
        && memory.ReadInstructionUInt16(0x8C0E_1E58) == 0x3C3C
        && memory.ReadInstructionUInt16(0x8C0E_1E5A) == 0x4C08
        && memory.ReadInstructionUInt16(0x8C0E_1E5C) == 0x3C8C
        && memory.ReadInstructionUInt16(0x8C0E_1E5E) == 0xF3C8
        && memory.ReadInstructionUInt16(0x8C0E_1E60) == 0xFF37
        && memory.ReadInstructionUInt16(0x8C0E_1E62) == 0xE008
        && memory.ReadInstructionUInt16(0x8C0E_1E64) == 0xF3C6
        && memory.ReadInstructionUInt16(0x8C0E_1E66) == 0xE01C
        && memory.ReadInstructionUInt16(0x8C0E_1E68) == 0xFF37
        && memory.ReadInstructionUInt16(0x8C0E_1E6A) == 0xE004
        && memory.ReadInstructionUInt16(0x8C0E_1E6C) == 0xF3C6
        && memory.ReadInstructionUInt16(0x8C0E_1E6E) == 0xE020
        && memory.ReadInstructionUInt16(0x8C0E_1E70) == 0xFF37
        && memory.ReadInstructionUInt16(0x8C0E_1E72) == 0xE00C
        && memory.ReadInstructionUInt16(0x8C0E_1E74) == 0xF3C6
        && memory.ReadInstructionUInt16(0x8C0E_1E76) == 0xE024
        && memory.ReadInstructionUInt16(0x8C0E_1E78) == 0xFF37
        && memory.ReadInstructionUInt16(0x8C0E_1E7A) == 0xE010
        && memory.ReadInstructionUInt16(0x8C0E_1E7C) == 0x03CC
        && memory.ReadInstructionUInt16(0x8C0E_1E7E) == 0xE010
        && memory.ReadInstructionUInt16(0x8C0E_1E80) == 0x435A
        && memory.ReadInstructionUInt16(0x8C0E_1E82) == 0xF32D
        && memory.ReadInstructionUInt16(0x8C0E_1E84) == 0xF3E2
        && memory.ReadInstructionUInt16(0x8C0E_1E86) == 0xFF37
        && memory.ReadInstructionUInt16(0x8C0E_1E88) == 0xE011
        && memory.ReadInstructionUInt16(0x8C0E_1E8A) == 0x03CC
        && memory.ReadInstructionUInt16(0x8C0E_1E8C) == 0xE014
        && memory.ReadInstructionUInt16(0x8C0E_1E8E) == 0x435A
        && memory.ReadInstructionUInt16(0x8C0E_1E90) == 0xF32D
        && memory.ReadInstructionUInt16(0x8C0E_1E92) == 0xF3F2
        && memory.ReadInstructionUInt16(0x8C0E_1E94) == 0xFF37
        && memory.ReadInstructionUInt16(0x8C0E_1E96) == 0x85DB
        && memory.ReadInstructionUInt16(0x8C0E_1E98) == 0x6303
        && memory.ReadInstructionUInt16(0x8C0E_1E9A) == 0x435A
        && memory.ReadInstructionUInt16(0x8C0E_1E9C) == 0xE004
        && memory.ReadInstructionUInt16(0x8C0E_1E9E) == 0xF32D
        && memory.ReadInstructionUInt16(0x8C0E_1EA0) == 0xFF37
        && memory.ReadInstructionUInt16(0x8C0E_1EA2) == 0x85DC
        && memory.ReadInstructionUInt16(0x8C0E_1EA4) == 0x6303
        && memory.ReadInstructionUInt16(0x8C0E_1EA6) == 0x435A
        && memory.ReadInstructionUInt16(0x8C0E_1EA8) == 0xE008
        && memory.ReadInstructionUInt16(0x8C0E_1EAA) == 0xF32D
        && memory.ReadInstructionUInt16(0x8C0E_1EAC) == 0xFF37
        && memory.ReadInstructionUInt16(0x8C0E_1EAE) == 0x490B
        && memory.ReadInstructionUInt16(0x8C0E_1EB0) == 0x64F3
        && memory.ReadInstructionUInt16(0x8C0E_1EC2) == 0x00FF;

    private bool IsDoa2TextAdvanceToNextGlyph() =>
        memory.ReadInstructionUInt16(0x8C0E_1EB2) == 0xE010
        && memory.ReadInstructionUInt16(0x8C0E_1EB4) == 0x03CC
        && memory.ReadInstructionUInt16(0x8C0E_1EB6) == 0x85DB
        && memory.ReadInstructionUInt16(0x8C0E_1EB8) == 0x303C
        && memory.ReadInstructionUInt16(0x8C0E_1EBA) == 0xA01A
        && memory.ReadInstructionUInt16(0x8C0E_1EBC) == 0x81DB
        && memory.ReadInstructionUInt16(0x8C0E_1EF2) == 0x7E01
        && memory.ReadInstructionUInt16(0x8C0E_1EF4) == 0x63E0
        && memory.ReadInstructionUInt16(0x8C0E_1EF6) == 0x2338
        && memory.ReadInstructionUInt16(0x8C0E_1EF8) == 0x8B86;

    private bool IsDoa2Fac40TrigArgumentWrapper() =>
        memory.ReadInstructionUInt16(0x8C0F_AC40) == 0xD338
        && memory.ReadInstructionUInt16(0x8C0F_AC42) == 0x2FE6
        && memory.ReadInstructionUInt16(0x8C0F_AC44) == 0x6E4D
        && memory.ReadInstructionUInt16(0x8C0F_AC46) == 0x3E37
        && memory.ReadInstructionUInt16(0x8C0F_AC48) == 0x8B02
        && memory.ReadInstructionUInt16(0x8C0F_AC50) == 0x9466
        && memory.ReadInstructionUInt16(0x8C0F_AC52) == 0x34E8
        && memory.ReadInstructionUInt16(0x8C0F_AC54) == 0xA2B4
        && memory.ReadInstructionUInt16(0x8C0F_AC56) == 0x6EF6
        && memory.ReadInstructionUInt16(0x8C0F_AD20) == 0x4000
        && memory.ReadUInt32(0x8C0F_AD24) == 0x0000_8000;

    private bool IsDoa2RendererPrologueCommonPath() =>
        memory.ReadInstructionUInt16(0x8C10_0430) == 0x2FE6
        && memory.ReadInstructionUInt16(0x8C10_0432) == 0x2FD6
        && memory.ReadInstructionUInt16(0x8C10_0434) == 0x2FC6
        && memory.ReadInstructionUInt16(0x8C10_0436) == 0x2FB6
        && memory.ReadInstructionUInt16(0x8C10_0438) == 0x2FA6
        && memory.ReadInstructionUInt16(0x8C10_043A) == 0x2F96
        && memory.ReadInstructionUInt16(0x8C10_043C) == 0x2F86
        && memory.ReadInstructionUInt16(0x8C10_043E) == 0xFFFB
        && memory.ReadInstructionUInt16(0x8C10_0440) == 0xFFEB
        && memory.ReadInstructionUInt16(0x8C10_0442) == 0xFFDB
        && memory.ReadInstructionUInt16(0x8C10_0444) == 0xFFCB
        && memory.ReadInstructionUInt16(0x8C10_0446) == 0x4F22
        && memory.ReadInstructionUInt16(0x8C10_0448) == 0x7FC4
        && memory.ReadInstructionUInt16(0x8C10_044A) == 0xD112
        && memory.ReadInstructionUInt16(0x8C10_044C) == 0x6D43
        && memory.ReadInstructionUInt16(0x8C10_044E) == 0x62D2
        && memory.ReadInstructionUInt16(0x8C10_0450) == 0x6312
        && memory.ReadInstructionUInt16(0x8C10_0452) == 0x3233
        && memory.ReadInstructionUInt16(0x8C10_0454) == 0x8F24
        && memory.ReadInstructionUInt16(0x8C10_0456) == 0xFF9D
        && memory.ReadUInt32(0x8C10_0494) == 0x8C2F_07D0;

    private bool IsDoa2RendererMode2LookupCommonPath() =>
        memory.ReadInstructionUInt16(0x8C10_04A0) == 0xD335
        && memory.ReadInstructionUInt16(0x8C10_04A2) == 0x68D2
        && memory.ReadInstructionUInt16(0x8C10_04A4) == 0x6032
        && memory.ReadInstructionUInt16(0x8C10_04A6) == 0x4800
        && memory.ReadInstructionUInt16(0x8C10_04A8) == 0x088D
        && memory.ReadInstructionUInt16(0x8C10_04AA) == 0x6083
        && memory.ReadInstructionUInt16(0x8C10_04AC) == 0x88FF
        && memory.ReadInstructionUInt16(0x8C10_04AE) == 0x8B01
        && memory.ReadInstructionUInt16(0x8C10_04B4) == 0xD231
        && memory.ReadInstructionUInt16(0x8C10_04B6) == 0x6C83
        && memory.ReadInstructionUInt16(0x8C10_04B8) == 0x4C08
        && memory.ReadInstructionUInt16(0x8C10_04BA) == 0x50DC
        && memory.ReadInstructionUInt16(0x8C10_04BC) == 0x6322
        && memory.ReadInstructionUInt16(0x8C10_04BE) == 0x4C08
        && memory.ReadInstructionUInt16(0x8C10_04C0) == 0x4C00
        && memory.ReadInstructionUInt16(0x8C10_04C2) == 0x8800
        && memory.ReadInstructionUInt16(0x8C10_04C4) == 0x3C3C
        && memory.ReadInstructionUInt16(0x8C10_04C6) == 0x8D13
        && memory.ReadInstructionUInt16(0x8C10_04C8) == 0xE400
        && memory.ReadInstructionUInt16(0x8C10_04CA) == 0x8802
        && memory.ReadInstructionUInt16(0x8C10_04CC) == 0x8918
        && memory.ReadUInt32(0x8C10_0578) == 0x8C2F_07CC
        && memory.ReadUInt32(0x8C10_057C) == 0x8C2F_07DC;

    private bool IsDoa2RendererMode2TrigSetupToFirstCall() =>
        memory.ReadInstructionUInt16(0x8C10_0500) == 0xE02C
        && memory.ReadInstructionUInt16(0x8C10_0502) == 0xFED6
        && memory.ReadInstructionUInt16(0x8C10_0504) == 0xEE01
        && memory.ReadInstructionUInt16(0x8C10_0506) == 0xC71E
        && memory.ReadInstructionUInt16(0x8C10_0508) == 0xFDFC
        && memory.ReadInstructionUInt16(0x8C10_050A) == 0xF408
        && memory.ReadInstructionUInt16(0x8C10_050C) == 0xE010
        && memory.ReadInstructionUInt16(0x8C10_050E) == 0xF3D6
        && memory.ReadInstructionUInt16(0x8C10_0510) == 0xE018
        && memory.ReadInstructionUInt16(0x8C10_0512) == 0xF2C6
        && memory.ReadInstructionUInt16(0x8C10_0514) == 0xE014
        && memory.ReadInstructionUInt16(0x8C10_0516) == 0xD31B
        && memory.ReadInstructionUInt16(0x8C10_0518) == 0xF232
        && memory.ReadInstructionUInt16(0x8C10_051A) == 0xF3D6
        && memory.ReadInstructionUInt16(0x8C10_051C) == 0xE01C
        && memory.ReadInstructionUInt16(0x8C10_051E) == 0xFC2C
        && memory.ReadInstructionUInt16(0x8C10_0520) == 0xFC42
        && memory.ReadInstructionUInt16(0x8C10_0522) == 0xF2C6
        && memory.ReadInstructionUInt16(0x8C10_0524) == 0xE004
        && memory.ReadInstructionUInt16(0x8C10_0526) == 0xF232
        && memory.ReadInstructionUInt16(0x8C10_0528) == 0xF242
        && memory.ReadInstructionUInt16(0x8C10_052A) == 0xFF27
        && memory.ReadInstructionUInt16(0x8C10_052C) == 0xE00C
        && memory.ReadInstructionUInt16(0x8C10_052E) == 0xF3D6
        && memory.ReadInstructionUInt16(0x8C10_0530) == 0xFD33
        && memory.ReadInstructionUInt16(0x8C10_0532) == 0x430B
        && memory.ReadInstructionUInt16(0x8C10_0534) == 0x54DA
        && memory.ReadUInt32(0x8C10_0580) == 0x3F00_0000
        && memory.ReadUInt32(0x8C10_0584) == 0x8C0F_B1C0;

    private bool IsDoa2RendererSecondTrigCallBridge() =>
        memory.ReadInstructionUInt16(0x8C10_0536) == 0xD314
        && memory.ReadInstructionUInt16(0x8C10_0538) == 0xE008
        && memory.ReadInstructionUInt16(0x8C10_053A) == 0xFF07
        && memory.ReadInstructionUInt16(0x8C10_053C) == 0x430B
        && memory.ReadInstructionUInt16(0x8C10_053E) == 0x54DA
        && memory.ReadUInt32(0x8C10_0588) == 0x8C0F_AC40;

    private bool IsDoa2RendererPostSecondTrigBridge() =>
        memory.ReadInstructionUInt16(0x8C10_0540) == 0xE008
        && memory.ReadInstructionUInt16(0x8C10_0542) == 0x6AF3
        && memory.ReadInstructionUInt16(0x8C10_0544) == 0xF6F6
        && memory.ReadInstructionUInt16(0x8C10_0546) == 0xE004
        && memory.ReadInstructionUInt16(0x8C10_0548) == 0x6BF3
        && memory.ReadInstructionUInt16(0x8C10_054A) == 0x7A1C
        && memory.ReadInstructionUInt16(0x8C10_054C) == 0xF5F6
        && memory.ReadInstructionUInt16(0x8C10_054E) == 0x7B2C
        && memory.ReadInstructionUInt16(0x8C10_0550) == 0x66A3
        && memory.ReadInstructionUInt16(0x8C10_0552) == 0x65B3
        && memory.ReadInstructionUInt16(0x8C10_0554) == 0xF70C
        && memory.ReadInstructionUInt16(0x8C10_0556) == 0xF4CC
        && memory.ReadInstructionUInt16(0x8C10_0558) == 0xB26A
        && memory.ReadInstructionUInt16(0x8C10_055A) == 0x64D3;

    private bool IsDoa2RendererPostCallScaleSetup() =>
        memory.ReadInstructionUInt16(0x8C10_055C) == 0xD20D
        && memory.ReadInstructionUInt16(0x8C10_055E) == 0xE004
        && memory.ReadInstructionUInt16(0x8C10_0560) == 0xD30B
        && memory.ReadInstructionUInt16(0x8C10_0562) == 0xD40A
        && memory.ReadInstructionUInt16(0x8C10_0564) == 0xF228
        && memory.ReadInstructionUInt16(0x8C10_0566) == 0xF546
        && memory.ReadInstructionUInt16(0x8C10_0568) == 0xF338
        && memory.ReadInstructionUInt16(0x8C10_056A) == 0xF448
        && memory.ReadInstructionUInt16(0x8C10_056C) == 0xF522
        && memory.ReadInstructionUInt16(0x8C10_056E) == 0xE400
        && memory.ReadInstructionUInt16(0x8C10_0570) == 0xF432
        && memory.ReadInstructionUInt16(0x8C10_0572) == 0xA01D
        && memory.ReadInstructionUInt16(0x8C10_0574) == 0xE910
        && memory.ReadUInt32(0x8C10_058C) == 0x8C2F_07E0
        && memory.ReadUInt32(0x8C10_0590) == 0x8C1C_A8D4
        && memory.ReadUInt32(0x8C10_0594) == 0x8C1C_A8D8;

    private bool IsDoa2RendererModeWordSetupToColorPack() =>
        memory.ReadInstructionUInt16(0x8C10_05B4) == 0x62C2
        && memory.ReadInstructionUInt16(0x8C10_05B6) == 0xD331
        && memory.ReadInstructionUInt16(0x8C10_05B8) == 0x2239
        && memory.ReadInstructionUInt16(0x8C10_05BA) == 0x935B
        && memory.ReadInstructionUInt16(0x8C10_05BC) == 0x2C22
        && memory.ReadInstructionUInt16(0x8C10_05BE) == 0x51C1
        && memory.ReadInstructionUInt16(0x8C10_05C0) == 0xD22F
        && memory.ReadInstructionUInt16(0x8C10_05C2) == 0x2129
        && memory.ReadInstructionUInt16(0x8C10_05C4) == 0x1C11
        && memory.ReadInstructionUInt16(0x8C10_05C6) == 0x50C2
        && memory.ReadInstructionUInt16(0x8C10_05C8) == 0xD12E
        && memory.ReadInstructionUInt16(0x8C10_05CA) == 0x2019
        && memory.ReadInstructionUInt16(0x8C10_05CC) == 0x1C02
        && memory.ReadInstructionUInt16(0x8C10_05CE) == 0x50DD
        && memory.ReadInstructionUInt16(0x8C10_05D0) == 0x2038
        && memory.ReadInstructionUInt16(0x8C10_05D2) == 0x8B03
        && memory.ReadInstructionUInt16(0x8C10_05DC) == 0x53DD
        && memory.ReadInstructionUInt16(0x8C10_05DE) == 0x944A
        && memory.ReadInstructionUInt16(0x8C10_05E0) == 0x954A
        && memory.ReadInstructionUInt16(0x8C10_05E2) == 0x2348
        && memory.ReadInstructionUInt16(0x8C10_05E4) == 0x8902
        && memory.ReadInstructionUInt16(0x8C10_05EC) == 0x52DD
        && memory.ReadInstructionUInt16(0x8C10_05EE) == 0xD327
        && memory.ReadInstructionUInt16(0x8C10_05F0) == 0x2238
        && memory.ReadInstructionUInt16(0x8C10_05F2) == 0x8902
        && memory.ReadInstructionUInt16(0x8C10_05FA) == 0x50DD
        && memory.ReadInstructionUInt16(0x8C10_05FC) == 0xE3F8
        && memory.ReadInstructionUInt16(0x8C10_05FE) == 0xE407
        && memory.ReadInstructionUInt16(0x8C10_0600) == 0x403C
        && memory.ReadInstructionUInt16(0x8C10_0602) == 0x2409
        && memory.ReadInstructionUInt16(0x8C10_0604) == 0x2448
        && memory.ReadInstructionUInt16(0x8C10_0606) == 0x8F01
        && memory.ReadInstructionUInt16(0x8C10_0608) == 0xE31D
        && memory.ReadInstructionUInt16(0x8C10_060A) == 0xE404
        && memory.ReadInstructionUInt16(0x8C10_060C) == 0x52C1
        && memory.ReadInstructionUInt16(0x8C10_060E) == 0x443C
        && memory.ReadInstructionUInt16(0x8C10_0610) == 0x2EE8
        && memory.ReadInstructionUInt16(0x8C10_0612) == 0x224B
        && memory.ReadInstructionUInt16(0x8C10_0614) == 0x8D1C
        && memory.ReadInstructionUInt16(0x8C10_0616) == 0x1C21
        && memory.ReadInstructionUInt16(0x8C10_0618) == 0x50DC
        && memory.ReadInstructionUInt16(0x8C10_061A) == 0x8804
        && memory.ReadInstructionUInt16(0x8C10_061C) == 0x8B08
        && memory.ReadInstructionUInt16(0x8C10_0630) == 0xD219
        && memory.ReadInstructionUInt16(0x8C10_0632) == 0xD11A
        && memory.ReadInstructionUInt16(0x8C10_0634) == 0x6022
        && memory.ReadInstructionUInt16(0x8C10_0636) == 0x53C2
        && memory.ReadInstructionUInt16(0x8C10_0638) == 0xCAFC
        && memory.ReadInstructionUInt16(0x8C10_063A) == 0x4028
        && memory.ReadInstructionUInt16(0x8C10_063C) == 0x4018
        && memory.ReadInstructionUInt16(0x8C10_063E) == 0x201B
        && memory.ReadInstructionUInt16(0x8C10_0640) == 0x230B
        && memory.ReadInstructionUInt16(0x8C10_0642) == 0x1C32
        && memory.ReadInstructionUInt16(0x8C10_0644) == 0x60C2
        && memory.ReadInstructionUInt16(0x8C10_0646) == 0xD316
        && memory.ReadInstructionUInt16(0x8C10_0648) == 0x203B
        && memory.ReadInstructionUInt16(0x8C10_064A) == 0xA006
        && memory.ReadInstructionUInt16(0x8C10_064C) == 0x2C02
        && memory.ReadInstructionUInt16(0x8C10_065A) == 0x53DD
        && memory.ReadInstructionUInt16(0x8C10_065C) == 0x2538
        && memory.ReadInstructionUInt16(0x8C10_065E) == 0x892F
        && memory.ReadInstructionUInt16(0x8C10_0660) == 0xD312
        && memory.ReadInstructionUInt16(0x8C10_0662) == 0xD213
        && memory.ReadInstructionUInt16(0x8C10_0664) == 0xD110
        && memory.ReadInstructionUInt16(0x8C10_0666) == 0xF528
        && memory.ReadInstructionUInt16(0x8C10_0668) == 0xF718
        && memory.ReadInstructionUInt16(0x8C10_066A) == 0xF638
        && memory.ReadInstructionUInt16(0x8C10_066C) == 0xB228
        && memory.ReadInstructionUInt16(0x8C10_066E) == 0xF4EC
        && memory.ReadUInt16(0x8C10_0674) == 0x0800
        && memory.ReadUInt16(0x8C10_0676) == 0x1000
        && memory.ReadUInt16(0x8C10_0678) == 0x2000
        && memory.ReadUInt32(0x8C10_067C) == 0xF8FC_FFFF
        && memory.ReadUInt32(0x8C10_0680) == 0x1FFF_FFFF
        && memory.ReadUInt32(0x8C10_0684) == 0x0327_8FFF
        && memory.ReadUInt32(0x8C10_068C) == 0x0001_0000
        && memory.ReadUInt32(0x8C10_0698) == 0x8C1C_A5D8
        && memory.ReadUInt32(0x8C10_069C) == 0x0010_0000
        && memory.ReadUInt32(0x8C10_06A0) == 0x0200_0000
        && memory.ReadUInt32(0x8C10_06A8) == 0x8C1C_A928
        && memory.ReadUInt32(0x8C10_06AC) == 0x8C1C_A924
        && memory.ReadUInt32(0x8C10_06B0) == 0x8C1C_A920;

    private bool IsDoa2RendererColorPackReturnBridge() =>
        memory.ReadInstructionUInt16(0x8C10_0670) == 0xA03C
        && memory.ReadInstructionUInt16(0x8C10_0672) == 0x1C04;

    private bool IsDoa2RendererInterpolationPrologueToCopyTail() =>
        memory.ReadInstructionUInt16(0x8C10_0A30) == 0xFFEB
        && memory.ReadInstructionUInt16(0x8C10_0A32) == 0x4F22
        && memory.ReadInstructionUInt16(0x8C10_0A34) == 0x7FDC
        && memory.ReadInstructionUInt16(0x8C10_0A36) == 0xF95C
        && memory.ReadInstructionUInt16(0x8C10_0A38) == 0xF572
        && memory.ReadInstructionUInt16(0x8C10_0A3A) == 0xF84C
        && memory.ReadInstructionUInt16(0x8C10_0A3C) == 0xF462
        && memory.ReadInstructionUInt16(0x8C10_0A3E) == 0xF872
        && memory.ReadInstructionUInt16(0x8C10_0A40) == 0x61F3
        && memory.ReadInstructionUInt16(0x8C10_0A42) == 0xF962
        && memory.ReadInstructionUInt16(0x8C10_0A44) == 0xD248
        && memory.ReadInstructionUInt16(0x8C10_0A46) == 0x67F3
        && memory.ReadInstructionUInt16(0x8C10_0A48) == 0xD348
        && memory.ReadInstructionUInt16(0x8C10_0A4A) == 0x7704
        && memory.ReadInstructionUInt16(0x8C10_0A4C) == 0x7104
        && memory.ReadInstructionUInt16(0x8C10_0A4E) == 0x430B
        && memory.ReadInstructionUInt16(0x8C10_0A50) == 0xE020
        && memory.ReadInstructionUInt16(0x8C10_E5CC) == 0x2F36
        && memory.ReadInstructionUInt16(0x8C10_E5CE) == 0xD305
        && memory.ReadInstructionUInt16(0x8C10_E5D0) == 0x033E
        && memory.ReadInstructionUInt16(0x8C10_E5D2) == 0x70FC
        && memory.ReadInstructionUInt16(0x8C10_E5D4) == 0x432B
        && memory.ReadInstructionUInt16(0x8C10_E5D6) == 0x002E
        && memory.ReadUInt32(0x8C10_0B68) == 0x8C14_8EBC
        && memory.ReadUInt32(0x8C10_0B6C) == 0x8C10_E5CC
        && memory.ReadUInt32(0x8C10_E5E4) == 0x8C10_E62C
        && memory.ReadUInt32(0x8C10_E64C) == 0x8C10_E60A;

    private bool IsDoa2RendererInterpolationSetupToLoopExit() =>
        memory.ReadInstructionUInt16(0x8C10_0A52) == 0x504D
        && memory.ReadInstructionUInt16(0x8C10_0A54) == 0xC80F
        && memory.ReadInstructionUInt16(0x8C10_0A56) == 0x8B03
        && memory.ReadInstructionUInt16(0x8C10_0A60) == 0x2F02
        && memory.ReadInstructionUInt16(0x8C10_0A62) == 0xC903
        && memory.ReadInstructionUInt16(0x8C10_0A64) == 0x405A
        && memory.ReadInstructionUInt16(0x8C10_0A66) == 0x60F2
        && memory.ReadInstructionUInt16(0x8C10_0A68) == 0x4021
        && memory.ReadInstructionUInt16(0x8C10_0A6A) == 0xF32D
        && memory.ReadInstructionUInt16(0x8C10_0A6C) == 0x4021
        && memory.ReadInstructionUInt16(0x8C10_0A6E) == 0xC903
        && memory.ReadInstructionUInt16(0x8C10_0A70) == 0x405A
        && memory.ReadInstructionUInt16(0x8C10_0A72) == 0xF63C
        && memory.ReadInstructionUInt16(0x8C10_0A74) == 0xF32D
        && memory.ReadInstructionUInt16(0x8C10_0A76) == 0xF73C
        && memory.ReadInstructionUInt16(0x8C10_0A78) == 0xE104
        && IsDoa2InterpolationLoop();

    private bool IsDoa2RendererInterpolationEpilogueReturn() =>
        memory.ReadInstructionUInt16(0x8C10_0AB6) == 0x7F24
        && memory.ReadInstructionUInt16(0x8C10_0AB8) == 0x4F26
        && memory.ReadInstructionUInt16(0x8C10_0ABA) == 0x000B
        && memory.ReadInstructionUInt16(0x8C10_0ABC) == 0xFEF9;

    private bool IsDoa2SignedRemainderHelper()
    {
        if (memory.ReadInstructionUInt16(0x8C10_751C) != 0x2008
            || memory.ReadInstructionUInt16(0x8C10_751E) != 0x2F26
            || memory.ReadInstructionUInt16(0x8C10_7520) != 0x8955
            || memory.ReadInstructionUInt16(0x8C10_7522) != 0x2F36
            || memory.ReadInstructionUInt16(0x8C10_7524) != 0xE200
            || memory.ReadInstructionUInt16(0x8C10_7526) != 0x2F46
            || memory.ReadInstructionUInt16(0x8C10_7528) != 0x2127
            || memory.ReadInstructionUInt16(0x8C10_752A) != 0x0429
            || memory.ReadInstructionUInt16(0x8C10_752C) != 0x333A
            || memory.ReadInstructionUInt16(0x8C10_752E) != 0x312A
            || memory.ReadInstructionUInt16(0x8C10_7530) != 0x2307)
        {
            return false;
        }

        for (var address = 0x8C10_7532u; address <= 0x8C10_75B0; address += 4)
        {
            if (memory.ReadInstructionUInt16(address) != 0x4124
                || memory.ReadInstructionUInt16(address + 2) != 0x3304)
            {
                return false;
            }
        }

        return memory.ReadInstructionUInt16(0x8C10_75B2) == 0x2327
            && memory.ReadInstructionUInt16(0x8C10_75B4) == 0x0229
            && memory.ReadInstructionUInt16(0x8C10_75B6) == 0x224A
            && memory.ReadInstructionUInt16(0x8C10_75B8) == 0x4225
            && memory.ReadInstructionUInt16(0x8C10_75BA) == 0x8B02
            && memory.ReadInstructionUInt16(0x8C10_75BC) == 0x2307
            && memory.ReadInstructionUInt16(0x8C10_75BE) == 0x4321
            && memory.ReadInstructionUInt16(0x8C10_75C0) == 0x3304
            && memory.ReadInstructionUInt16(0x8C10_75C2) == 0x334C
            && memory.ReadInstructionUInt16(0x8C10_75C4) == 0x6033
            && memory.ReadInstructionUInt16(0x8C10_75C6) == 0x64F6
            && memory.ReadInstructionUInt16(0x8C10_75C8) == 0x63F6
            && memory.ReadInstructionUInt16(0x8C10_75CA) == 0x000B
            && memory.ReadInstructionUInt16(0x8C10_75CC) == 0x62F6;
    }

    private bool IsDoa2UnsignedDivideHelper()
    {
        if (memory.ReadInstructionUInt16(0x8C10_7424) != 0x2008
            || memory.ReadInstructionUInt16(0x8C10_7426) != 0x2F26
            || memory.ReadInstructionUInt16(0x8C10_7428) != 0x8945
            || memory.ReadInstructionUInt16(0x8C10_742A) != 0xE200
            || memory.ReadInstructionUInt16(0x8C10_742C) != 0x0019)
        {
            return false;
        }

        for (var address = 0x8C10_742Eu; address <= 0x8C10_74AC; address += 4)
        {
            if (memory.ReadInstructionUInt16(address) != 0x4124
                || memory.ReadInstructionUInt16(address + 2) != 0x3204)
            {
                return false;
            }
        }

        return memory.ReadInstructionUInt16(0x8C10_74AE) == 0x4124
            && memory.ReadInstructionUInt16(0x8C10_74B0) == 0x6013
            && memory.ReadInstructionUInt16(0x8C10_74B2) == 0x000B
            && memory.ReadInstructionUInt16(0x8C10_74B4) == 0x62F6;
    }

    private bool IsDoa2ZeroByteClassifier() =>
        memory.ReadInstructionUInt16(0x8C11_7482) == 0x8464
        && memory.ReadInstructionUInt16(0x8C11_7484) == 0x911E
        && memory.ReadInstructionUInt16(0x8C11_7486) == 0x600C
        && memory.ReadInstructionUInt16(0x8C11_7488) == 0x3010
        && memory.ReadInstructionUInt16(0x8C11_748A) == 0x890C
        && memory.ReadInstructionUInt16(0x8C11_748C) == 0x911B
        && memory.ReadInstructionUInt16(0x8C11_748E) == 0x3010
        && memory.ReadInstructionUInt16(0x8C11_7490) == 0x890C
        && memory.ReadInstructionUInt16(0x8C11_7492) == 0x8808
        && memory.ReadInstructionUInt16(0x8C11_7494) == 0x890A
        && memory.ReadInstructionUInt16(0x8C11_7496) == 0x8805
        && memory.ReadInstructionUInt16(0x8C11_7498) == 0x8908
        && memory.ReadInstructionUInt16(0x8C11_749A) == 0x8806
        && memory.ReadInstructionUInt16(0x8C11_749C) == 0x8906
        && memory.ReadInstructionUInt16(0x8C11_749E) == 0x8800
        && memory.ReadInstructionUInt16(0x8C11_74A0) == 0x8904
        && memory.ReadInstructionUInt16(0x8C11_74AC) == 0x000B
        && memory.ReadInstructionUInt16(0x8C11_74AE) == 0xE000
        && memory.ReadUInt16(0x8C11_74C4) == 0x00FC
        && memory.ReadUInt16(0x8C11_74C6) == 0x00FF;

    private bool IsDoa2ListEntrySetupToClassifier() =>
        memory.ReadInstructionUInt16(0x8C11_750E) == 0xEE34
        && memory.ReadInstructionUInt16(0x8C11_7510) == 0xD32F
        && memory.ReadInstructionUInt16(0x8C11_7512) == 0x0DE7
        && memory.ReadInstructionUInt16(0x8C11_7514) == 0x61D3
        && memory.ReadInstructionUInt16(0x8C11_7516) == 0x4108
        && memory.ReadInstructionUInt16(0x8C11_7518) == 0x6C03
        && memory.ReadInstructionUInt16(0x8C11_751A) == 0x66B3
        && memory.ReadInstructionUInt16(0x8C11_751C) == 0x65C3
        && memory.ReadInstructionUInt16(0x8C11_751E) == 0x0E1A
        && memory.ReadInstructionUInt16(0x8C11_7520) == 0x3E3C
        && memory.ReadInstructionUInt16(0x8C11_7522) == 0xD32C
        && memory.ReadInstructionUInt16(0x8C11_7524) == 0x1E0C
        && memory.ReadInstructionUInt16(0x8C11_7526) == 0x52E2
        && memory.ReadInstructionUInt16(0x8C11_7528) == 0x331C
        && memory.ReadInstructionUInt16(0x8C11_752A) == 0x1E2B
        && memory.ReadInstructionUInt16(0x8C11_752C) == 0x2F36
        && memory.ReadInstructionUInt16(0x8C11_752E) == 0xBFA8
        && memory.ReadInstructionUInt16(0x8C11_7530) == 0x64E3
        && memory.ReadUInt32(0x8C11_75D0) == 0x8C2F_B814
        && memory.ReadUInt32(0x8C11_75D4) == 0x8C2F_BCF4;

    private bool IsDoa2ListEntryAllocatorPair() =>
        memory.ReadInstructionUInt16(0x8C11_7500) == 0xD331
        && memory.ReadInstructionUInt16(0x8C11_7502) == 0x430B
        && memory.ReadInstructionUInt16(0x8C11_7504) == 0x64D3
        && memory.ReadInstructionUInt16(0x8C11_7506) == 0xD231
        && memory.ReadInstructionUInt16(0x8C11_7508) == 0x6B03
        && memory.ReadInstructionUInt16(0x8C11_750A) == 0x420B
        && memory.ReadInstructionUInt16(0x8C11_750C) == 0x64D3
        && memory.ReadUInt32(0x8C11_75C8) == 0x8C12_4634
        && memory.ReadUInt32(0x8C11_75CC) == 0x8C12_4646
        && memory.ReadInstructionUInt16(0x8C12_4634) == 0x6043
        && memory.ReadInstructionUInt16(0x8C12_4636) == 0x4000
        && memory.ReadInstructionUInt16(0x8C12_4638) == 0x6343
        && memory.ReadInstructionUInt16(0x8C12_463A) == 0x303C
        && memory.ReadInstructionUInt16(0x8C12_463C) == 0xD212
        && memory.ReadInstructionUInt16(0x8C12_463E) == 0x4008
        && memory.ReadInstructionUInt16(0x8C12_4640) == 0x4000
        && memory.ReadInstructionUInt16(0x8C12_4642) == 0x000B
        && memory.ReadInstructionUInt16(0x8C12_4644) == 0x302C
        && memory.ReadUInt32(0x8C12_4688) == 0x8C30_36BC
        && memory.ReadInstructionUInt16(0x8C12_4646) == 0x4F22
        && memory.ReadInstructionUInt16(0x8C12_4648) == 0xD311
        && memory.ReadInstructionUInt16(0x8C12_464A) == 0x430B
        && memory.ReadInstructionUInt16(0x8C12_464C) == 0x0009
        && memory.ReadInstructionUInt16(0x8C12_464E) == 0x4F26
        && memory.ReadInstructionUInt16(0x8C12_4650) == 0x000B
        && memory.ReadInstructionUInt16(0x8C12_4652) == 0x0009
        && memory.ReadUInt32(0x8C12_4690) == 0x8C13_4F48
        && memory.ReadInstructionUInt16(0x8C13_4F48) == 0xE278
        && memory.ReadInstructionUInt16(0x8C13_4F4A) == 0xD30D
        && memory.ReadInstructionUInt16(0x8C13_4F4C) == 0x0427
        && memory.ReadInstructionUInt16(0x8C13_4F4E) == 0x9110
        && memory.ReadInstructionUInt16(0x8C13_4F50) == 0x6032
        && memory.ReadInstructionUInt16(0x8C13_4F52) == 0x041A
        && memory.ReadInstructionUInt16(0x8C13_4F54) == 0x301C
        && memory.ReadInstructionUInt16(0x8C13_4F56) == 0x000B
        && memory.ReadInstructionUInt16(0x8C13_4F58) == 0x304C
        && memory.ReadUInt16(0x8C13_4F72) == 0x044C
        && memory.ReadUInt32(0x8C13_4F80) == 0x8C31_007C;

    private bool IsDoa2ListEntryPostClassifierToRemainder() =>
        memory.ReadInstructionUInt16(0x8C11_7532) == 0x62F6
        && memory.ReadInstructionUInt16(0x8C11_7534) == 0x61D3
        && memory.ReadInstructionUInt16(0x8C11_7536) == 0xD328
        && memory.ReadInstructionUInt16(0x8C11_7538) == 0x2202
        && memory.ReadInstructionUInt16(0x8C11_753A) == 0x430B
        && memory.ReadInstructionUInt16(0x8C11_753C) == 0x6093
        && memory.ReadUInt32(0x8C11_75D8) == 0x8C10_751C;

    private bool IsDoa2ListEntryNonzeroRemainderTail() =>
        memory.ReadInstructionUInt16(0x8C11_753E) == 0x2008
        && memory.ReadInstructionUInt16(0x8C11_7540) == 0x8B2D
        && memory.ReadInstructionUInt16(0x8C11_759E) == 0x63C3
        && memory.ReadInstructionUInt16(0x8C11_75A0) == 0x7314
        && memory.ReadInstructionUInt16(0x8C11_75A2) == 0x1E39
        && memory.ReadInstructionUInt16(0x8C11_75A4) == 0x7D01
        && memory.ReadInstructionUInt16(0x8C11_75A6) == 0x3DA3
        && memory.ReadInstructionUInt16(0x8C11_75A8) == 0x8BAA;

    private bool IsDoa2ZeroStatusTableScan() =>
        memory.ReadInstructionUInt16(0x8C0F_7D2A) == 0x6011
        && memory.ReadInstructionUInt16(0x8C0F_7D2C) == 0x8800
        && memory.ReadInstructionUInt16(0x8C0F_7D2E) == 0x8908
        && memory.ReadInstructionUInt16(0x8C0F_7D42) == 0x7108
        && memory.ReadInstructionUInt16(0x8C0F_7D44) == 0x6313
        && memory.ReadInstructionUInt16(0x8C0F_7D46) == 0xD06A
        && memory.ReadInstructionUInt16(0x8C0F_7D48) == 0xD268
        && memory.ReadInstructionUInt16(0x8C0F_7D4A) == 0x3328
        && memory.ReadInstructionUInt16(0x8C0F_7D4C) == 0x6203
        && memory.ReadInstructionUInt16(0x8C0F_7D4E) == 0xD004
        && memory.ReadInstructionUInt16(0x8C0F_7D50) == 0x4301
        && memory.ReadInstructionUInt16(0x8C0F_7D52) == 0x3120
        && memory.ReadInstructionUInt16(0x8C0F_7D54) == 0x4309
        && memory.ReadInstructionUInt16(0x8C0F_7D56) == 0x8FE8
        && memory.ReadInstructionUInt16(0x8C0F_7D58) == 0x2031
        && memory.ReadUInt32(0x8C0F_7D60) == 0x8C0F_7EE4
        && memory.ReadUInt32(0x8C0F_7EEC) == 0x8C2A_DC04
        && memory.ReadUInt32(0x8C0F_7EF0) == 0x8C2A_DC94;

    private bool IsDoa2TrigSetupAndRecurrenceLoop() =>
        memory.ReadInstructionUInt16(0x8C0F_B1C0) == 0x644D
        && memory.ReadInstructionUInt16(0x8C0F_B1C2) == 0xF79D
        && memory.ReadInstructionUInt16(0x8C0F_B1C4) == 0x445A
        && memory.ReadInstructionUInt16(0x8C0F_B1C6) == 0xC71D
        && memory.ReadInstructionUInt16(0x8C0F_B1C8) == 0xF208
        && memory.ReadInstructionUInt16(0x8C0F_B1CA) == 0xC71D
        && memory.ReadInstructionUInt16(0x8C0F_B1CC) == 0xF108
        && memory.ReadInstructionUInt16(0x8C0F_B1CE) == 0xF770
        && memory.ReadInstructionUInt16(0x8C0F_B1D0) == 0xF32D
        && memory.ReadInstructionUInt16(0x8C0F_B1D2) == 0xC71C
        && memory.ReadInstructionUInt16(0x8C0F_B1D4) == 0xF508
        && memory.ReadInstructionUInt16(0x8C0F_B1D6) == 0xC71C
        && memory.ReadInstructionUInt16(0x8C0F_B1D8) == 0xF008
        && memory.ReadInstructionUInt16(0x8C0F_B1DA) == 0xE40B
        && memory.ReadInstructionUInt16(0x8C0F_B1DC) == 0xE503
        && memory.ReadInstructionUInt16(0x8C0F_B1DE) == 0xF322
        && memory.ReadInstructionUInt16(0x8C0F_B1E0) == 0xF313
        && memory.ReadInstructionUInt16(0x8C0F_B1E2) == 0xF43C
        && memory.ReadInstructionUInt16(0x8C0F_B1E4) == 0xF473
        && memory.ReadInstructionUInt16(0x8C0F_B1E6) == 0xF34C
        && memory.ReadInstructionUInt16(0x8C0F_B1E8) == 0xF353
        && memory.ReadInstructionUInt16(0x8C0F_B1EA) == 0xF300
        && memory.ReadInstructionUInt16(0x8C0F_B1EC) == 0xF33D
        && memory.ReadInstructionUInt16(0x8C0F_B1EE) == 0x065A
        && memory.ReadInstructionUInt16(0x8C0F_B1F0) == 0x465A
        && memory.ReadInstructionUInt16(0x8C0F_B1F2) == 0xF32D
        && memory.ReadInstructionUInt16(0x8C0F_B1F4) == 0xF352
        && memory.ReadInstructionUInt16(0x8C0F_B1F6) == 0xF58D
        && memory.ReadInstructionUInt16(0x8C0F_B1F8) == 0xF431
        && memory.ReadInstructionUInt16(0x8C0F_B1FA) == 0xF64C
        && memory.ReadInstructionUInt16(0x8C0F_B1FC) == 0xF642
        && IsDoa2FpuRecurrenceLoop()
        && memory.ReadInstructionUInt16(0x8C0F_B20E) == 0xF69D
        && memory.ReadInstructionUInt16(0x8C0F_B210) == 0xE301
        && memory.ReadInstructionUInt16(0x8C0F_B212) == 0xF36C
        && memory.ReadInstructionUInt16(0x8C0F_B214) == 0xF351;

    private bool IsDoa2PostTrigHelperReturn() =>
        memory.ReadInstructionUInt16(0x8C0F_B216) == 0x2638
        && memory.ReadInstructionUInt16(0x8C0F_B218) == 0xF433
        && memory.ReadInstructionUInt16(0x8C0F_B21A) == 0xF24C
        && memory.ReadInstructionUInt16(0x8C0F_B21C) == 0xF272
        && memory.ReadInstructionUInt16(0x8C0F_B21E) == 0xF04C
        && memory.ReadInstructionUInt16(0x8C0F_B220) == 0xF64E
        && memory.ReadInstructionUInt16(0x8C0F_B222) == 0xF42C
        && memory.ReadInstructionUInt16(0x8C0F_B224) == 0x8F14
        && memory.ReadInstructionUInt16(0x8C0F_B226) == 0xF463
        && memory.ReadInstructionUInt16(0x8C0F_B228) == 0x000B
        && memory.ReadInstructionUInt16(0x8C0F_B22A) == 0xF04C
        && memory.ReadInstructionUInt16(0x8C0F_B250) == 0xF04C
        && memory.ReadInstructionUInt16(0x8C0F_B252) == 0xF04D
        && memory.ReadInstructionUInt16(0x8C0F_B254) == 0x000B
        && memory.ReadInstructionUInt16(0x8C0F_B256) == 0x0009;

    private bool IsDoa2VectorScaleLoop() =>
        memory.ReadInstructionUInt16(0x8C10_05A0) == 0x6043
        && memory.ReadInstructionUInt16(0x8C10_05A2) == 0xF3B6
        && memory.ReadInstructionUInt16(0x8C10_05A4) == 0x7404
        && memory.ReadInstructionUInt16(0x8C10_05A6) == 0xF342
        && memory.ReadInstructionUInt16(0x8C10_05A8) == 0xFB37
        && memory.ReadInstructionUInt16(0x8C10_05AA) == 0xF2A6
        && memory.ReadInstructionUInt16(0x8C10_05AC) == 0xF252
        && memory.ReadInstructionUInt16(0x8C10_05AE) == 0xFA27
        && memory.ReadInstructionUInt16(0x8C10_05B0) == 0x3492
        && memory.ReadInstructionUInt16(0x8C10_05B2) == 0x8BF5;

    private bool IsDoa2InterpolationLoop() =>
        memory.ReadInstructionUInt16(0x8C10_0A7A) == 0xFB79
        && memory.ReadInstructionUInt16(0x8C10_0A7C) == 0xE004
        && memory.ReadInstructionUInt16(0x8C10_0A7E) == 0xF38D
        && memory.ReadInstructionUInt16(0x8C10_0A80) == 0xFB61
        && memory.ReadInstructionUInt16(0x8C10_0A82) == 0xFA79
        && memory.ReadInstructionUInt16(0x8C10_0A84) == 0xF146
        && memory.ReadInstructionUInt16(0x8C10_0A86) == 0xE008
        && memory.ReadInstructionUInt16(0x8C10_0A88) == 0xFA71
        && memory.ReadInstructionUInt16(0x8C10_0A8A) == 0xFB34
        && memory.ReadInstructionUInt16(0x8C10_0A8C) == 0x8D06
        && memory.ReadInstructionUInt16(0x8C10_0A8E) == 0xFE46
        && memory.ReadInstructionUInt16(0x8C10_0A90) == 0xF24C
        && memory.ReadInstructionUInt16(0x8C10_0A92) == 0xF2B2
        && memory.ReadInstructionUInt16(0x8C10_0A94) == 0xF0BC
        && memory.ReadInstructionUInt16(0x8C10_0A96) == 0xF18E
        && memory.ReadInstructionUInt16(0x8C10_0A98) == 0xF24D
        && memory.ReadInstructionUInt16(0x8C10_0A9A) == 0xFE20
        && memory.ReadInstructionUInt16(0x8C10_0A9C) == 0xF38D
        && memory.ReadInstructionUInt16(0x8C10_0A9E) == 0xFA34
        && memory.ReadInstructionUInt16(0x8C10_0AA0) == 0x8902
        && memory.ReadInstructionUInt16(0x8C10_0AA2) == 0xF0AC
        && memory.ReadInstructionUInt16(0x8C10_0AA4) == 0xFE5E
        && memory.ReadInstructionUInt16(0x8C10_0AA6) == 0xF19E
        && memory.ReadInstructionUInt16(0x8C10_0AA8) == 0x71FF
        && memory.ReadInstructionUInt16(0x8C10_0AAA) == 0xF51A
        && memory.ReadInstructionUInt16(0x8C10_0AAC) == 0xF6EA
        && memory.ReadInstructionUInt16(0x8C10_0AAE) == 0x2118
        && memory.ReadInstructionUInt16(0x8C10_0AB0) == 0x7604
        && memory.ReadInstructionUInt16(0x8C10_0AB2) == 0x8FE2
        && memory.ReadInstructionUInt16(0x8C10_0AB4) == 0x7504;

    private bool IsDoa2BusyBitWaitLoop() =>
        memory.ReadInstructionUInt16(0x8C13_0460) == 0x4A0B
        && memory.ReadInstructionUInt16(0x8C13_0462) == 0xE407
        && memory.ReadInstructionUInt16(0x8C13_0464) == 0xD22F
        && memory.ReadInstructionUInt16(0x8C13_0466) == 0x6422
        && memory.ReadInstructionUInt16(0x8C13_0468) == 0x2448
        && memory.ReadInstructionUInt16(0x8C13_046A) == 0x890A
        && memory.ReadInstructionUInt16(0x8C13_0482) == 0xD12A
        && memory.ReadInstructionUInt16(0x8C13_0484) == 0x63DB
        && memory.ReadInstructionUInt16(0x8C13_0486) == 0x6412
        && memory.ReadInstructionUInt16(0x8C13_0488) == 0x443D
        && memory.ReadInstructionUInt16(0x8C13_048A) == 0x24C9
        && memory.ReadInstructionUInt16(0x8C13_048C) == 0x2448
        && memory.ReadInstructionUInt16(0x8C13_048E) == 0x8BE7
        && memory.ReadUInt32(0x8C13_0524) == 0x8C2F_6808
        && memory.ReadUInt32(0x8C13_0528) == 0x8C2F_6814
        && memory.ReadUInt32(0x8C13_052C) == 0x8C2F_67FC
        && memory.ReadUInt32(0x8C30_C778) == 0x8C12_BE60
        && memory.ReadUInt32(0x8C30_C77C) == 0
        && memory.ReadInstructionUInt16(0x8C12_BE60) == 0x000B
        && memory.ReadInstructionUInt16(0x8C12_BE62) == 0x0009;

    private bool TryApplyRepeatedDelaySlot(ushort opcode, ulong executions)
    {
        if (opcode == 0x0009)
        {
            return true;
        }

        if ((opcode & 0xF000) == 0x7000)
        {
            var register = (opcode >> 8) & 0xF;
            var delta = unchecked((uint)(int)(sbyte)(opcode & 0xFF));
            State.R[register] = unchecked(State.R[register] + (uint)((ulong)delta * executions));
            return true;
        }

        if ((opcode & 0xF00F) == 0x2006)
        {
            var destinationRegister = (opcode >> 8) & 0xF;
            var sourceRegister = (opcode >> 4) & 0xF;
            for (ulong index = 0; index < executions; index++)
            {
                State.R[destinationRegister] -= 4;
                memory.WriteUInt32(State.R[destinationRegister], State.R[sourceRegister]);
            }

            return true;
        }

        return false;
    }

    private bool TryAcceptExternalInterrupt(uint pc, out string trace)
    {
        trace = string.Empty;
        if (!memory.TryGetPendingExternalInterrupt(out var eventCode, out var level))
        {
            return false;
        }

        var interruptMask = (int)((State.Sr >> 4) & 0xF);
        var blockBitSet = (State.Sr & Sh4State.SrBlockBit) != 0;
        if (blockBitSet || level <= interruptMask)
        {
            return false;
        }

        State.Spc = pc;
        State.Ssr = State.Sr;
        memory.WriteUInt32(0xFF00_0028, eventCode);
        State.Sr = (State.Sr & ~0xF0u) | Sh4State.SrMachineBit | Sh4State.SrRegisterBankBit | Sh4State.SrBlockBit | ((uint)level << 4);
        var target = State.Vbr + 0x600;
        if (State.Vbr == 0 && memory.TryGetBiosInterruptHandler(level, out var vectorAddress, out var handlerAddress))
        {
            State.Pc = handlerAddress;
            State.SaveBiosInterruptPr(State.Pr, eventCode);
            State.Pr = DreamcastMemory.BiosInterruptReturnHleStub;
            State.R[4] = eventCode;
            trace = $"interrupt event=0x{eventCode:X4}, level={level}, target=0x{State.Pc:X8}, bios-vector=0x{vectorAddress:X8}";
            return true;
        }

        State.Pc = target;
        trace = $"interrupt event=0x{eventCode:X4}, level={level}, target=0x{State.Pc:X8}";
        return true;
    }

    private string Execute(uint pc, ushort opcode)
    {
        var nextPc = pc + 2;
        var branchTargetAfterSlot = delayedBranchTarget;
        var isDelaySlot = branchTargetAfterSlot is not null;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        var trace = ExecuteInstruction(pc, opcode, nextPc, isDelaySlot);

        State.Pc = immediateBranchTarget ?? branchTargetAfterSlot ?? nextPc;
        return trace;
    }

    private string ExecuteInstruction(uint pc, ushort opcode, uint nextPc, bool isDelaySlot)
    {
        var highNibble = opcode >> 12;
        var n = (opcode >> 8) & 0xF;
        var m = (opcode >> 4) & 0xF;
        var lowNibble = opcode & 0xF;

        if (isDelaySlot && (opcode == 0xFFFD || IsIllegalInDelaySlot(opcode)))
        {
            const uint eventCode = 0x0000_01A0;
            EnterGeneralException(pc - 2, eventCode);
            return $"slot illegal instruction ; expevt=0x{eventCode:X8}, target=0x{immediateBranchTarget:X8}";
        }

        if (opcode == 0xFFFD)
        {
            const uint eventCode = 0x0000_0180;
            EnterGeneralException(pc, eventCode);
            return $"general illegal instruction ; expevt=0x{eventCode:X8}, target=0x{immediateBranchTarget:X8}";
        }

        if (opcode == 0x0009)
        {
            return "nop";
        }

        if (opcode == 0x001B)
        {
            return "sleep";
        }

        if (opcode == 0x0019)
        {
            State.M = false;
            State.Q = false;
            State.T = false;
            return "div0u";
        }

        if (opcode == 0x0008)
        {
            State.T = false;
            return "clrt";
        }

        if (opcode == 0x0018)
        {
            State.T = true;
            return "sett";
        }

        if (opcode == 0x002B)
        {
            delayedBranchTarget = State.Spc;
            State.Sr = State.Ssr;
            State.RestoreBiosInterruptPr();
            return $"rte ; target=0x{State.Spc:X8}, sr=0x{State.Sr:X8}";
        }

        if ((opcode & 0xF0FF) == 0x0083)
        {
            memory.Prefetch(State.R[n]);
            return $"pref @r{n}";
        }

        if ((opcode & 0xF0FF) == 0x0093)
        {
            return $"ocbi @r{n}";
        }

        if ((opcode & 0xF0FF) == 0x00A3)
        {
            return $"ocbp @r{n}";
        }

        if ((opcode & 0xF0FF) == 0x00B3)
        {
            return $"ocbwb @r{n}";
        }

        if ((opcode & 0xF0FF) == 0x0029)
        {
            State.R[n] = State.T ? 1u : 0u;
            return $"movt r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if ((opcode & 0xF000) == 0xD000)
        {
            var address = ((pc + 4) & 0xFFFF_FFFCu) + ((uint)(opcode & 0xFF) * 4);
            State.R[n] = memory.ReadUInt32(address);
            return $"mov.l @(0x{opcode & 0xFF:X2},pc),r{n} ; [0x{address:X8}]=0x{State.R[n]:X8}";
        }

        if ((opcode & 0xF000) == 0x9000)
        {
            var address = pc + 4 + ((uint)(opcode & 0xFF) * 2);
            State.R[n] = (uint)(short)memory.ReadUInt16(address);
            return $"mov.w @(0x{opcode & 0xFF:X2},pc),r{n} ; [0x{address:X8}]=0x{State.R[n]:X8}";
        }

        if ((opcode & 0xF000) == 0xE000)
        {
            State.R[n] = (uint)(sbyte)(opcode & 0xFF);
            return $"mov #{(sbyte)(opcode & 0xFF)},r{n}";
        }

        if ((opcode & 0xFF00) == 0xC700)
        {
            var address = ((pc + 4) & 0xFFFF_FFFCu) + ((uint)(opcode & 0xFF) * 4);
            State.R[0] = address;
            return $"mova @(0x{opcode & 0xFF:X2},pc),r0 ; r0=0x{State.R[0]:X8}";
        }

        if ((opcode & 0xFF00) == 0xC000)
        {
            var address = State.Gbr + (uint)(opcode & 0xFF);
            memory.Write(address, [(byte)State.R[0]]);
            return $"mov.b r0,@(0x{opcode & 0xFF:X2},gbr) ; [0x{address:X8}]=0x{State.R[0] & 0xFF:X2}";
        }

        if ((opcode & 0xFF00) == 0xC100)
        {
            var address = State.Gbr + ((uint)(opcode & 0xFF) * 2);
            memory.WriteUInt16(address, (ushort)State.R[0]);
            return $"mov.w r0,@(0x{opcode & 0xFF:X2},gbr) ; [0x{address:X8}]=0x{State.R[0] & 0xFFFF:X4}";
        }

        if ((opcode & 0xFF00) == 0xC200)
        {
            var address = State.Gbr + ((uint)(opcode & 0xFF) * 4);
            memory.WriteUInt32(address, State.R[0]);
            return $"mov.l r0,@(0x{opcode & 0xFF:X2},gbr) ; [0x{address:X8}]=0x{State.R[0]:X8}";
        }

        if ((opcode & 0xFF00) == 0xC400)
        {
            var address = State.Gbr + (uint)(opcode & 0xFF);
            State.R[0] = (uint)(sbyte)memory.ReadByte(address);
            return $"mov.b @(0x{opcode & 0xFF:X2},gbr),r0 ; [0x{address:X8}]=0x{State.R[0]:X8}";
        }

        if ((opcode & 0xFF00) == 0xC500)
        {
            var address = State.Gbr + ((uint)(opcode & 0xFF) * 2);
            State.R[0] = (uint)(short)memory.ReadUInt16(address);
            return $"mov.w @(0x{opcode & 0xFF:X2},gbr),r0 ; [0x{address:X8}]=0x{State.R[0]:X8}";
        }

        if ((opcode & 0xFF00) == 0xC600)
        {
            var address = State.Gbr + ((uint)(opcode & 0xFF) * 4);
            State.R[0] = memory.ReadUInt32(address);
            return $"mov.l @(0x{opcode & 0xFF:X2},gbr),r0 ; [0x{address:X8}]=0x{State.R[0]:X8}";
        }

        if (highNibble == 0x2 && lowNibble == 0x0)
        {
            memory.Write(State.R[n], [(byte)State.R[m]]);
            return $"mov.b r{m},@r{n} ; [0x{State.R[n]:X8}]=0x{State.R[m] & 0xFF:X2}";
        }

        if (highNibble == 0x2 && lowNibble == 0x2)
        {
            memory.WriteUInt32(State.R[n], State.R[m]);
            return $"mov.l r{m},@r{n} ; [0x{State.R[n]:X8}]=0x{State.R[m]:X8}";
        }

        if (highNibble == 0x2 && lowNibble == 0x1)
        {
            memory.WriteUInt16(State.R[n], (ushort)State.R[m]);
            return $"mov.w r{m},@r{n} ; [0x{State.R[n]:X8}]=0x{State.R[m] & 0xFFFF:X4}";
        }

        if (highNibble == 0x2 && lowNibble == 0x4)
        {
            State.R[n] -= 1;
            memory.Write(State.R[n], [(byte)State.R[m]]);
            return $"mov.b r{m},@-r{n} ; [0x{State.R[n]:X8}]=0x{State.R[m] & 0xFF:X2}";
        }

        if (highNibble == 0x2 && lowNibble == 0x6)
        {
            State.R[n] -= 4;
            memory.WriteUInt32(State.R[n], State.R[m]);
            return $"mov.l r{m},@-r{n} ; [0x{State.R[n]:X8}]=0x{State.R[m]:X8}";
        }

        if (highNibble == 0x2 && lowNibble == 0x7)
        {
            State.M = (State.R[m] & 0x8000_0000) != 0;
            State.Q = (State.R[n] & 0x8000_0000) != 0;
            State.T = State.M != State.Q;
            return $"div0s r{m},r{n} ; q={(State.Q ? 1 : 0)}, m={(State.M ? 1 : 0)}, t={(State.T ? 1 : 0)}";
        }

        if (highNibble == 0x2 && lowNibble == 0x8)
        {
            State.T = (State.R[n] & State.R[m]) == 0;
            return $"tst r{m},r{n} ; t={(State.T ? 1 : 0)}";
        }

        if (highNibble == 0x2 && lowNibble == 0x9)
        {
            State.R[n] &= State.R[m];
            return $"and r{m},r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x2 && lowNibble == 0xA)
        {
            State.R[n] ^= State.R[m];
            return $"xor r{m},r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x2 && lowNibble == 0xB)
        {
            State.R[n] |= State.R[m];
            return $"or r{m},r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x2 && lowNibble == 0xC)
        {
            var xor = State.R[n] ^ State.R[m];
            State.T = (xor & 0x0000_00FF) == 0
                || (xor & 0x0000_FF00) == 0
                || (xor & 0x00FF_0000) == 0
                || (xor & 0xFF00_0000) == 0;
            return $"cmp/str r{m},r{n} ; t={(State.T ? 1 : 0)}";
        }

        if (highNibble == 0x2 && lowNibble == 0xD)
        {
            State.R[n] = (State.R[n] >> 16) | (State.R[m] << 16);
            return $"xtrct r{m},r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x2 && lowNibble == 0xE)
        {
            State.Macl = (State.R[n] & 0xFFFF) * (State.R[m] & 0xFFFF);
            return $"mulu.w r{m},r{n} ; macl=0x{State.Macl:X8}";
        }

        if (highNibble == 0x2 && lowNibble == 0xF)
        {
            State.Macl = (uint)((short)State.R[n] * (short)State.R[m]);
            return $"muls.w r{m},r{n} ; macl=0x{State.Macl:X8}";
        }

        if ((opcode & 0xFF00) == 0xCB00)
        {
            State.R[0] |= (uint)(opcode & 0xFF);
            return $"or #0x{opcode & 0xFF:X2},r0 ; r0=0x{State.R[0]:X8}";
        }

        if ((opcode & 0xFF00) == 0xC900)
        {
            State.R[0] &= (uint)(opcode & 0xFF);
            return $"and #0x{opcode & 0xFF:X2},r0 ; r0=0x{State.R[0]:X8}";
        }

        if ((opcode & 0xFF00) == 0xCA00)
        {
            State.R[0] ^= (uint)(opcode & 0xFF);
            return $"xor #0x{opcode & 0xFF:X2},r0 ; r0=0x{State.R[0]:X8}";
        }

        if ((opcode & 0xFF00) == 0xCC00)
        {
            var address = State.R[0] + State.Gbr;
            var value = memory.ReadByte(address);
            State.T = (value & (opcode & 0xFF)) == 0;
            return $"tst.b #0x{opcode & 0xFF:X2},@(r0,gbr) ; [0x{address:X8}]=0x{value:X2}, t={(State.T ? 1 : 0)}";
        }

        if ((opcode & 0xFF00) == 0xCD00)
        {
            var address = State.R[0] + State.Gbr;
            var value = (byte)(memory.ReadByte(address) & (opcode & 0xFF));
            memory.Write(address, [value]);
            return $"and.b #0x{opcode & 0xFF:X2},@(r0,gbr) ; [0x{address:X8}]=0x{value:X2}";
        }

        if ((opcode & 0xFF00) == 0xCE00)
        {
            var address = State.R[0] + State.Gbr;
            var value = (byte)(memory.ReadByte(address) ^ (opcode & 0xFF));
            memory.Write(address, [value]);
            return $"xor.b #0x{opcode & 0xFF:X2},@(r0,gbr) ; [0x{address:X8}]=0x{value:X2}";
        }

        if ((opcode & 0xFF00) == 0xCF00)
        {
            var address = State.R[0] + State.Gbr;
            var value = (byte)(memory.ReadByte(address) | (opcode & 0xFF));
            memory.Write(address, [value]);
            return $"or.b #0x{opcode & 0xFF:X2},@(r0,gbr) ; [0x{address:X8}]=0x{value:X2}";
        }

        if (highNibble == 0x3 && lowNibble == 0x0)
        {
            State.T = State.R[n] == State.R[m];
            return $"cmp/eq r{m},r{n} ; t={(State.T ? 1 : 0)}";
        }

        if (highNibble == 0x3 && lowNibble == 0x2)
        {
            State.T = State.R[n] >= State.R[m];
            return $"cmp/hs r{m},r{n} ; t={(State.T ? 1 : 0)}";
        }

        if (highNibble == 0x3 && lowNibble == 0x3)
        {
            State.T = unchecked((int)State.R[n]) >= unchecked((int)State.R[m]);
            return $"cmp/ge r{m},r{n} ; t={(State.T ? 1 : 0)}";
        }

        if (highNibble == 0x3 && lowNibble == 0x6)
        {
            State.T = State.R[n] > State.R[m];
            return $"cmp/hi r{m},r{n} ; t={(State.T ? 1 : 0)}";
        }

        if (highNibble == 0x3 && lowNibble == 0x7)
        {
            State.T = unchecked((int)State.R[n]) > unchecked((int)State.R[m]);
            return $"cmp/gt r{m},r{n} ; t={(State.T ? 1 : 0)}";
        }

        if (highNibble == 0x3 && lowNibble == 0x8)
        {
            State.R[n] -= State.R[m];
            return $"sub r{m},r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x3 && lowNibble == 0xA)
        {
            var original = State.R[n];
            var subtrahend = State.R[m] + (State.T ? 1u : 0u);
            State.R[n] -= subtrahend;
            State.T = original < State.R[m] || (State.T && original == State.R[m]);
            return $"subc r{m},r{n} ; r{n}=0x{State.R[n]:X8}, t={(State.T ? 1 : 0)}";
        }

        if (highNibble == 0x3 && lowNibble == 0x5)
        {
            var product = (ulong)State.R[n] * State.R[m];
            State.Macl = (uint)product;
            State.Mach = (uint)(product >> 32);
            return $"dmulu.l r{m},r{n} ; mach=0x{State.Mach:X8}, macl=0x{State.Macl:X8}";
        }

        if (highNibble == 0x3 && lowNibble == 0xE)
        {
            var result = (ulong)State.R[n] + State.R[m] + (State.T ? 1u : 0u);
            State.R[n] = (uint)result;
            State.T = result > uint.MaxValue;
            return $"addc r{m},r{n} ; r{n}=0x{State.R[n]:X8}, t={(State.T ? 1 : 0)}";
        }

        if (highNibble == 0x3 && lowNibble == 0x4)
        {
            ExecuteDiv1(m, n);
            return $"div1 r{m},r{n} ; r{n}=0x{State.R[n]:X8}, q={(State.Q ? 1 : 0)}, m={(State.M ? 1 : 0)}, t={(State.T ? 1 : 0)}";
        }

        if (highNibble == 0x3 && lowNibble == 0xC)
        {
            State.R[n] += State.R[m];
            return $"add r{m},r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x000B)
        {
            State.Pr = pc + 4;
            delayedBranchTarget = State.R[n];
            return $"jsr @r{n} ; target=0x{State.R[n]:X8}, pr=0x{State.Pr:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x002B)
        {
            delayedBranchTarget = State.R[n];
            return $"jmp @r{n} ; target=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x0 && (opcode & 0x00FF) == 0x0023)
        {
            delayedBranchTarget = pc + 4 + State.R[n];
            return $"braf r{n} ; target=0x{delayedBranchTarget:X8}";
        }

        if (highNibble == 0x0 && (opcode & 0x00FF) == 0x00C3)
        {
            memory.WriteUInt32(State.R[n], State.R[0]);
            return $"movca.l r0,@r{n} ; [0x{State.R[n]:X8}]=0x{State.R[0]:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0028)
        {
            State.R[n] <<= 16;
            return $"shll16 r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0000)
        {
            State.T = (State.R[n] & 0x8000_0000) != 0;
            State.R[n] <<= 1;
            return $"shll r{n} ; r{n}=0x{State.R[n]:X8}, t={(State.T ? 1 : 0)}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0008)
        {
            State.R[n] <<= 2;
            return $"shll2 r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0011)
        {
            State.T = unchecked((int)State.R[n]) >= 0;
            return $"cmp/pz r{n} ; t={(State.T ? 1 : 0)}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0010)
        {
            State.R[n]--;
            State.T = State.R[n] == 0;
            return $"dt r{n} ; r{n}=0x{State.R[n]:X8}, t={(State.T ? 1 : 0)}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0015)
        {
            State.T = unchecked((int)State.R[n]) > 0;
            return $"cmp/pl r{n} ; t={(State.T ? 1 : 0)}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0019)
        {
            State.R[n] >>= 8;
            return $"shlr8 r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0009)
        {
            State.R[n] >>= 2;
            return $"shlr2 r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0029)
        {
            State.R[n] >>= 16;
            return $"shlr16 r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0001)
        {
            State.T = (State.R[n] & 0x1) != 0;
            State.R[n] >>= 1;
            return $"shlr r{n} ; r{n}=0x{State.R[n]:X8}, t={(State.T ? 1 : 0)}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0021)
        {
            State.T = (State.R[n] & 0x1) != 0;
            State.R[n] = (uint)(unchecked((int)State.R[n]) >> 1);
            return $"shar r{n} ; r{n}=0x{State.R[n]:X8}, t={(State.T ? 1 : 0)}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0018)
        {
            State.R[n] <<= 8;
            return $"shll8 r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0024)
        {
            var oldT = State.T;
            State.T = (State.R[n] & 0x8000_0000) != 0;
            State.R[n] = (State.R[n] << 1) | (oldT ? 1u : 0u);
            return $"rotcl r{n} ; r{n}=0x{State.R[n]:X8}, t={(State.T ? 1 : 0)}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0025)
        {
            var oldT = State.T;
            State.T = (State.R[n] & 0x1) != 0;
            State.R[n] = (State.R[n] >> 1) | (oldT ? 0x8000_0000u : 0u);
            return $"rotcr r{n} ; r{n}=0x{State.R[n]:X8}, t={(State.T ? 1 : 0)}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x001B)
        {
            var value = memory.ReadByte(State.R[n]);
            State.T = value == 0;
            memory.Write(State.R[n], [(byte)(value | 0x80)]);
            return $"tas.b @r{n} ; [0x{State.R[n]:X8}]=0x{value | 0x80:X2}, t={(State.T ? 1 : 0)}";
        }

        if (highNibble == 0x4 && lowNibble == 0xD)
        {
            var shift = (int)State.R[m];
            State.R[n] = shift >= 0
                ? State.R[n] << (shift & 0x1F)
                : State.R[n] >> ((-shift) & 0x1F);
            return $"shld r{m},r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x4 && lowNibble == 0xC)
        {
            var shift = (int)State.R[m];
            State.R[n] = shift >= 0
                ? State.R[n] << (shift & 0x1F)
                : (uint)(unchecked((int)State.R[n]) >> ((-shift) & 0x1F));
            return $"shad r{m},r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0022)
        {
            State.R[n] -= 4;
            memory.WriteUInt32(State.R[n], State.Pr);
            return $"sts.l pr,@-r{n} ; [0x{State.R[n]:X8}]=0x{State.Pr:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0002)
        {
            State.R[n] -= 4;
            memory.WriteUInt32(State.R[n], State.Mach);
            return $"sts.l mach,@-r{n} ; [0x{State.R[n]:X8}]=0x{State.Mach:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0012)
        {
            State.R[n] -= 4;
            memory.WriteUInt32(State.R[n], State.Macl);
            return $"sts.l macl,@-r{n} ; [0x{State.R[n]:X8}]=0x{State.Macl:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0052)
        {
            State.R[n] -= 4;
            memory.WriteUInt32(State.R[n], State.Fpul);
            return $"sts.l fpul,@-r{n} ; [0x{State.R[n]:X8}]=0x{State.Fpul:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0026)
        {
            State.Pr = memory.ReadUInt32(State.R[n]);
            State.R[n] += 4;
            return $"lds.l @r{n}+,pr ; pr=0x{State.Pr:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0006)
        {
            State.Mach = memory.ReadUInt32(State.R[n]);
            State.R[n] += 4;
            return $"lds.l @r{n}+,mach ; mach=0x{State.Mach:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0016)
        {
            State.Macl = memory.ReadUInt32(State.R[n]);
            State.R[n] += 4;
            return $"lds.l @r{n}+,macl ; macl=0x{State.Macl:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0056)
        {
            State.Fpul = memory.ReadUInt32(State.R[n]);
            State.R[n] += 4;
            return $"lds.l @r{n}+,fpul ; fpul=0x{State.Fpul:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0066)
        {
            State.Fpscr = memory.ReadUInt32(State.R[n]);
            State.R[n] += 4;
            return $"lds.l @r{n}+,fpscr ; fpscr=0x{State.Fpscr:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x006A)
        {
            State.Fpscr = State.R[n];
            return $"lds r{n},fpscr ; fpscr=0x{State.Fpscr:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x005A)
        {
            State.Fpul = State.R[n];
            return $"lds r{n},fpul ; fpul=0x{State.Fpul:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0062)
        {
            State.R[n] -= 4;
            memory.WriteUInt32(State.R[n], State.Fpscr);
            return $"sts.l fpscr,@-r{n} ; [0x{State.R[n]:X8}]=0x{State.Fpscr:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0003)
        {
            State.R[n] -= 4;
            memory.WriteUInt32(State.R[n], State.Sr);
            return $"stc.l sr,@-r{n} ; [0x{State.R[n]:X8}]=0x{State.Sr:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0013)
        {
            State.R[n] -= 4;
            memory.WriteUInt32(State.R[n], State.Gbr);
            return $"stc.l gbr,@-r{n} ; [0x{State.R[n]:X8}]=0x{State.Gbr:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0023)
        {
            State.R[n] -= 4;
            memory.WriteUInt32(State.R[n], State.Vbr);
            return $"stc.l vbr,@-r{n} ; [0x{State.R[n]:X8}]=0x{State.Vbr:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0033)
        {
            State.R[n] -= 4;
            memory.WriteUInt32(State.R[n], State.Ssr);
            return $"stc.l ssr,@-r{n} ; [0x{State.R[n]:X8}]=0x{State.Ssr:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0043)
        {
            State.R[n] -= 4;
            memory.WriteUInt32(State.R[n], State.Spc);
            return $"stc.l spc,@-r{n} ; [0x{State.R[n]:X8}]=0x{State.Spc:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0017)
        {
            State.Gbr = memory.ReadUInt32(State.R[n]);
            State.R[n] += 4;
            return $"ldc.l @r{n}+,gbr ; gbr=0x{State.Gbr:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0037)
        {
            State.Ssr = memory.ReadUInt32(State.R[n]);
            State.R[n] += 4;
            return $"ldc.l @r{n}+,ssr ; ssr=0x{State.Ssr:X8}";
        }

        if (highNibble == 0x4 && (opcode & 0x00FF) == 0x0047)
        {
            State.Spc = memory.ReadUInt32(State.R[n]);
            State.R[n] += 4;
            return $"ldc.l @r{n}+,spc ; spc=0x{State.Spc:X8}";
        }

        if (highNibble == 0x4 && lowNibble == 0x7 && m >= 8)
        {
            var bankIndex = m - 8;
            State.RBank[bankIndex] = memory.ReadUInt32(State.R[n]);
            State.R[n] += 4;
            return $"ldc.l @r{n}+,r{bankIndex}_bank ; r{bankIndex}_bank=0x{State.RBank[bankIndex]:X8}";
        }

        if (highNibble == 0x4 && lowNibble == 0x3 && m >= 8)
        {
            var bankIndex = m - 8;
            State.R[n] -= 4;
            memory.WriteUInt32(State.R[n], State.RBank[bankIndex]);
            return $"stc.l r{bankIndex}_bank,@-r{n} ; [0x{State.R[n]:X8}]=0x{State.RBank[bankIndex]:X8}";
        }

        if (opcode == 0x000B)
        {
            delayedBranchTarget = State.Pr;
            return $"rts ; target=0x{State.Pr:X8}";
        }

        if (highNibble == 0x5)
        {
            var displacement = (uint)(opcode & 0xF) * 4;
            var address = State.R[m] + displacement;
            State.R[n] = memory.ReadUInt32(address);
            return $"mov.l @(0x{opcode & 0xF:X},r{m}),r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if ((opcode & 0xF0FF) == 0x0002)
        {
            State.R[n] = State.Sr;
            return $"stc sr,r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if ((opcode & 0xF0FF) == 0x0022)
        {
            State.R[n] = State.Vbr;
            return $"stc vbr,r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if ((opcode & 0xF0FF) == 0x0012)
        {
            State.R[n] = State.Gbr;
            return $"stc gbr,r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if ((opcode & 0xF0FF) == 0x006A)
        {
            State.R[n] = State.Fpscr;
            return $"sts fpscr,r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if ((opcode & 0xF0FF) == 0x005A)
        {
            State.R[n] = State.Fpul;
            return $"sts fpul,r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if ((opcode & 0xF0FF) == 0x000A)
        {
            State.R[n] = State.Mach;
            return $"sts mach,r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if ((opcode & 0xF0FF) == 0x001A)
        {
            State.R[n] = State.Macl;
            return $"sts macl,r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if ((opcode & 0xF0FF) == 0x002A)
        {
            State.R[n] = State.Pr;
            return $"sts pr,r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if ((opcode & 0xF0FF) == 0x402A)
        {
            State.Pr = State.R[n];
            return $"lds r{n},pr ; pr=0x{State.Pr:X8}";
        }

        if ((opcode & 0xF0FF) == 0x400E)
        {
            State.Sr = State.R[n];
            return $"ldc r{n},sr ; sr=0x{State.Sr:X8}";
        }

        if ((opcode & 0xF0FF) == 0x402E)
        {
            State.Vbr = State.R[n];
            return $"ldc r{n},vbr ; vbr=0x{State.Vbr:X8}";
        }

        if ((opcode & 0xF0FF) == 0x401E)
        {
            State.Gbr = State.R[n];
            return $"ldc r{n},gbr ; gbr=0x{State.Gbr:X8}";
        }

        if (highNibble == 0x6 && lowNibble == 0x0)
        {
            State.R[n] = (uint)(sbyte)memory.ReadByte(State.R[m]);
            return $"mov.b @r{m},r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x6 && lowNibble == 0x2)
        {
            State.R[n] = memory.ReadUInt32(State.R[m]);
            return $"mov.l @r{m},r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x6 && lowNibble == 0x3)
        {
            State.R[n] = State.R[m];
            return $"mov r{m},r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x6 && lowNibble == 0x1)
        {
            State.R[n] = (uint)(short)memory.ReadUInt16(State.R[m]);
            return $"mov.w @r{m},r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x6 && lowNibble == 0x6)
        {
            State.R[n] = memory.ReadUInt32(State.R[m]);
            State.R[m] += 4;
            return $"mov.l @r{m}+,r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x6 && lowNibble == 0x4)
        {
            State.R[n] = (uint)(sbyte)memory.ReadByte(State.R[m]);
            State.R[m] += 1;
            return $"mov.b @r{m}+,r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x6 && lowNibble == 0x5)
        {
            State.R[n] = (uint)(short)memory.ReadUInt16(State.R[m]);
            State.R[m] += 2;
            return $"mov.w @r{m}+,r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x6 && lowNibble == 0x7)
        {
            State.R[n] = ~State.R[m];
            return $"not r{m},r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x6 && lowNibble == 0x8)
        {
            State.R[n] = ((State.R[m] & 0xFF) << 8) | ((State.R[m] >> 8) & 0xFF) | (State.R[m] & 0xFFFF_0000);
            return $"swap.b r{m},r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x6 && lowNibble == 0x9)
        {
            State.R[n] = (State.R[m] << 16) | (State.R[m] >> 16);
            return $"swap.w r{m},r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x6 && lowNibble == 0xB)
        {
            State.R[n] = (uint)(0 - State.R[m]);
            return $"neg r{m},r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x6 && lowNibble == 0xA)
        {
            var temp = 0u - State.R[m];
            var oldT = State.T ? 1u : 0u;
            State.R[n] = temp - oldT;
            State.T = 0 < temp || temp < State.R[n];
            return $"negc r{m},r{n} ; r{n}=0x{State.R[n]:X8}, t={(State.T ? 1 : 0)}";
        }

        if (highNibble == 0x6 && lowNibble == 0xD)
        {
            State.R[n] = State.R[m] & 0xFFFF;
            return $"extu.w r{m},r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x6 && lowNibble == 0xE)
        {
            State.R[n] = (uint)(sbyte)State.R[m];
            return $"exts.b r{m},r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x6 && lowNibble == 0xF)
        {
            State.R[n] = (uint)(short)State.R[m];
            return $"exts.w r{m},r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x6 && lowNibble == 0xC)
        {
            State.R[n] = State.R[m] & 0xFF;
            return $"extu.b r{m},r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x7)
        {
            State.R[n] = (uint)(State.R[n] + (sbyte)(opcode & 0xFF));
            return $"add #{(sbyte)(opcode & 0xFF)},r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x1)
        {
            var displacement = (uint)(opcode & 0xF) * 4;
            var address = State.R[n] + displacement;
            memory.WriteUInt32(address, State.R[m]);
            return $"mov.l r{m},@(0x{opcode & 0xF:X},r{n}) ; [0x{address:X8}]=0x{State.R[m]:X8}";
        }

        if (highNibble == 0x0 && lowNibble == 0x4)
        {
            var address = State.R[0] + State.R[n];
            memory.Write(address, [(byte)State.R[m]]);
            return $"mov.b r{m},@(r0,r{n}) ; [0x{address:X8}]=0x{State.R[m] & 0xFF:X2}";
        }

        if (highNibble == 0x0 && lowNibble == 0x5)
        {
            var address = State.R[0] + State.R[n];
            memory.WriteUInt16(address, (ushort)State.R[m]);
            return $"mov.w r{m},@(r0,r{n}) ; [0x{address:X8}]=0x{State.R[m] & 0xFFFF:X4}";
        }

        if (highNibble == 0x0 && lowNibble == 0x6)
        {
            var address = State.R[0] + State.R[n];
            memory.WriteUInt32(address, State.R[m]);
            return $"mov.l r{m},@(r0,r{n}) ; [0x{address:X8}]=0x{State.R[m]:X8}";
        }

        if (highNibble == 0x0 && lowNibble == 0xC)
        {
            var address = State.R[0] + State.R[m];
            State.R[n] = (uint)(sbyte)memory.ReadByte(address);
            return $"mov.b @(r0,r{m}),r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x0 && lowNibble == 0xD)
        {
            var address = State.R[0] + State.R[m];
            State.R[n] = (uint)(short)memory.ReadUInt16(address);
            return $"mov.w @(r0,r{m}),r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x0 && lowNibble == 0xE)
        {
            var address = State.R[0] + State.R[m];
            State.R[n] = memory.ReadUInt32(address);
            return $"mov.l @(r0,r{m}),r{n} ; r{n}=0x{State.R[n]:X8}";
        }

        if (highNibble == 0x0 && lowNibble == 0x7)
        {
            State.Macl = (uint)(unchecked((int)State.R[n]) * unchecked((int)State.R[m]));
            return $"mul.l r{m},r{n} ; macl=0x{State.Macl:X8}";
        }

        if ((opcode & 0xFF00) == 0x8400)
        {
            var address = State.R[m] + (uint)(opcode & 0xF);
            State.R[0] = (uint)(sbyte)memory.ReadByte(address);
            return $"mov.b @(0x{opcode & 0xF:X},r{m}),r0 ; [0x{address:X8}]=0x{State.R[0]:X8}";
        }

        if ((opcode & 0xFF00) == 0x8500)
        {
            var address = State.R[m] + ((uint)(opcode & 0xF) * 2);
            State.R[0] = (uint)(short)memory.ReadUInt16(address);
            return $"mov.w @(0x{opcode & 0xF:X},r{m}),r0 ; [0x{address:X8}]=0x{State.R[0]:X8}";
        }

        if ((opcode & 0xFF00) == 0x8000)
        {
            var address = State.R[m] + (uint)(opcode & 0xF);
            memory.Write(address, [(byte)State.R[0]]);
            return $"mov.b r0,@(0x{opcode & 0xF:X},r{m}) ; [0x{address:X8}]=0x{State.R[0] & 0xFF:X2}";
        }

        if ((opcode & 0xFF00) == 0x8100)
        {
            var address = State.R[m] + ((uint)(opcode & 0xF) * 2);
            memory.WriteUInt16(address, (ushort)State.R[0]);
            return $"mov.w r0,@(0x{opcode & 0xF:X},r{m}) ; [0x{address:X8}]=0x{State.R[0] & 0xFFFF:X4}";
        }

        if ((opcode & 0xFF00) == 0x8900)
        {
            if (State.T)
            {
                immediateBranchTarget = (uint)(pc + 4 + ((sbyte)(opcode & 0xFF) * 2));
                return $"bt 0x{immediateBranchTarget:X8} ; taken";
            }

            return "bt ; not taken";
        }

        if ((opcode & 0xFF00) == 0x8800)
        {
            State.T = State.R[0] == (uint)(sbyte)(opcode & 0xFF);
            return $"cmp/eq #{(sbyte)(opcode & 0xFF)},r0 ; t={(State.T ? 1 : 0)}";
        }

        if ((opcode & 0xFF00) == 0x8B00)
        {
            if (!State.T)
            {
                immediateBranchTarget = (uint)(pc + 4 + ((sbyte)(opcode & 0xFF) * 2));
                return $"bf 0x{immediateBranchTarget:X8} ; taken";
            }

            return "bf ; not taken";
        }

        if ((opcode & 0xFF00) == 0x8F00)
        {
            if (!State.T)
            {
                delayedBranchTarget = (uint)(pc + 4 + ((sbyte)(opcode & 0xFF) * 2));
                return $"bf/s 0x{delayedBranchTarget:X8} ; taken";
            }

            return "bf/s ; not taken";
        }

        if ((opcode & 0xFF00) == 0x8D00)
        {
            if (State.T)
            {
                delayedBranchTarget = (uint)(pc + 4 + ((sbyte)(opcode & 0xFF) * 2));
                return $"bt/s 0x{delayedBranchTarget:X8} ; taken";
            }

            return "bt/s ; not taken";
        }

        if ((opcode & 0xFF00) == 0xC800)
        {
            State.T = (State.R[0] & (uint)(opcode & 0xFF)) == 0;
            return $"tst #0x{opcode & 0xFF:X2},r0 ; t={(State.T ? 1 : 0)}";
        }

        if ((opcode & 0xFF00) == 0xC300)
        {
            var immediate = (uint)(opcode & 0xFF);
            State.Spc = nextPc;
            State.Ssr = State.Sr;
            memory.WriteUInt32(0xFF00_0020, immediate << 2);
            memory.WriteUInt32(0xFF00_0024, 0x0000_0160);
            State.Sr |= Sh4State.SrMachineBit | Sh4State.SrRegisterBankBit | Sh4State.SrBlockBit;
            immediateBranchTarget = State.Vbr + 0x100;
            return $"trapa #0x{immediate:X2} ; tra=0x{immediate << 2:X8}, target=0x{immediateBranchTarget:X8}";
        }

        if ((opcode & 0xF000) == 0xA000)
        {
            var displacement = SignExtend12(opcode & 0x0FFF) * 2;
            delayedBranchTarget = (uint)(pc + 4 + displacement);
            return $"bra 0x{delayedBranchTarget:X8}";
        }

        if ((opcode & 0xF000) == 0xB000)
        {
            var displacement = SignExtend12(opcode & 0x0FFF) * 2;
            State.Pr = pc + 4;
            delayedBranchTarget = (uint)(pc + 4 + displacement);
            return $"bsr 0x{delayedBranchTarget:X8} ; pr=0x{State.Pr:X8}";
        }

        if (opcode == 0xFBFD)
        {
            State.Fpscr ^= Sh4State.FpscrFrBit;
            return $"frchg ; fpscr=0x{State.Fpscr:X8}";
        }

        if (opcode == 0xF3FD)
        {
            State.Fpscr ^= Sh4State.FpscrSzBit;
            return $"fschg ; fpscr=0x{State.Fpscr:X8}";
        }

        if ((opcode & 0xF0FF) == 0xF05D)
        {
            if ((State.Fpscr & Sh4State.FpscrPrBit) != 0)
            {
                State.Fr[n & ~1] &= 0x7FFF_FFFF;
            }
            else
            {
                State.Fr[n] &= 0x7FFF_FFFF;
            }

            return $"fabs fr{n} ; fr{n}=0x{State.Fr[n]:X8}";
        }

        if ((opcode & 0xF0FF) == 0xF02D)
        {
            var value = unchecked((int)State.Fpul);
            if ((State.Fpscr & Sh4State.FpscrPrBit) != 0)
            {
                WriteDoubleRegister(n, value);
            }
            else
            {
                State.Fr[n] = BitConverter.SingleToUInt32Bits(value);
            }

            return $"float fpul,fr{n} ; fr{n}=0x{State.Fr[n]:X8}";
        }

        if ((opcode & 0xF0FF) == 0xF03D)
        {
            State.Fpul = (State.Fpscr & Sh4State.FpscrPrBit) != 0
                ? ConvertDoubleToFpul(ReadDoubleRegisterBits(n))
                : ConvertSingleToFpul(State.Fr[n]);
            return $"ftrc fr{n},fpul ; fpul=0x{State.Fpul:X8}";
        }

        if (highNibble == 0xF)
        {
            return ExecuteFpuMove(opcode, n, m, lowNibble);
        }

        throw new UnsupportedInstructionException(pc, opcode);
    }

    private void EnterGeneralException(uint savedPc, uint eventCode)
    {
        State.Spc = savedPc;
        State.Ssr = State.Sr;
        memory.WriteUInt32(0xFF00_0024, eventCode);
        State.Sr |= Sh4State.SrMachineBit | Sh4State.SrRegisterBankBit | Sh4State.SrBlockBit;
        immediateBranchTarget = State.Vbr + 0x100;
    }

    private static bool IsIllegalInDelaySlot(ushort opcode)
    {
        if (opcode is 0x000B or 0x002B)
        {
            return true;
        }

        if ((opcode & 0xFF00) is 0x8900 or 0x8B00 or 0x8D00 or 0x8F00 or 0xC300)
        {
            return true;
        }

        if ((opcode & 0xF000) is 0xA000 or 0xB000)
        {
            return true;
        }

        if ((opcode & 0xF0FF) is 0x400B or 0x402B or 0x400E)
        {
            return true;
        }

        if ((opcode & 0xF0FF) == 0x0007)
        {
            return true;
        }

        return (opcode & 0xF0FF) is 0x0003 or 0x0023;
    }

    private static int SignExtend12(int value) => (value & 0x800) != 0 ? value | unchecked((int)0xFFFF_F000) : value;

    private void ExecuteDiv0S(int m, int n)
    {
        State.M = (State.R[m] & 0x8000_0000) != 0;
        State.Q = (State.R[n] & 0x8000_0000) != 0;
        State.T = State.M != State.Q;
    }

    private void ExecuteDiv1(int m, int n)
    {
        var oldQ = State.Q;
        State.Q = (State.R[n] & 0x8000_0000) != 0;
        State.R[n] = (State.R[n] << 1) | (State.T ? 1u : 0u);

        if (!oldQ)
        {
            if (!State.M)
            {
                var before = State.R[n];
                State.R[n] -= State.R[m];
                var borrow = State.R[n] > before;
                State.Q = !State.Q ? borrow : !borrow;
            }
            else
            {
                var before = State.R[n];
                State.R[n] += State.R[m];
                var carry = State.R[n] < before;
                State.Q = !State.Q ? !carry : carry;
            }
        }
        else
        {
            if (!State.M)
            {
                var before = State.R[n];
                State.R[n] += State.R[m];
                var carry = State.R[n] < before;
                State.Q = !State.Q ? carry : !carry;
            }
            else
            {
                var before = State.R[n];
                State.R[n] -= State.R[m];
                var borrow = State.R[n] > before;
                State.Q = !State.Q ? !borrow : borrow;
            }
        }

        State.T = State.Q == State.M;
    }

    private void ExecuteSubc(int m, int n)
    {
        var original = State.R[n];
        var subtrahend = State.R[m] + (State.T ? 1u : 0u);
        State.R[n] -= subtrahend;
        State.T = original < State.R[m] || (State.T && original == State.R[m]);
    }

    private void ExecuteRotcl(int n)
    {
        var oldT = State.T;
        State.T = (State.R[n] & 0x8000_0000) != 0;
        State.R[n] = (State.R[n] << 1) | (oldT ? 1u : 0u);
    }

    private void ExecuteRotcr(int n)
    {
        var oldT = State.T;
        State.T = (State.R[n] & 0x1) != 0;
        State.R[n] = (State.R[n] >> 1) | (oldT ? 0x8000_0000u : 0u);
    }

    private string ExecuteFpuMove(ushort opcode, int n, int m, int lowNibble)
    {
        var doubleSize = (State.Fpscr & Sh4State.FpscrSzBit) != 0;

        switch (lowNibble)
        {
            case 0x0:
            {
                var detail = ExecuteFpuArithmetic(n, m, FpuArithmeticKind.Add, static (left, right) => left + right, static (left, right) => left + right);
                return $"fadd fr{m},fr{n} ; fr{n}=0x{State.Fr[n]:X8}{detail}";
            }

            case 0x1:
            {
                var detail = ExecuteFpuArithmetic(n, m, FpuArithmeticKind.Subtract, static (left, right) => left - right, static (left, right) => left - right);
                return $"fsub fr{m},fr{n} ; fr{n}=0x{State.Fr[n]:X8}{detail}";
            }

            case 0x2:
            {
                var detail = ExecuteFpuArithmetic(n, m, FpuArithmeticKind.Multiply, static (left, right) => left * right, static (left, right) => left * right);
                return $"fmul fr{m},fr{n} ; fr{n}=0x{State.Fr[n]:X8}{detail}";
            }

            case 0x3:
            {
                var detail = ExecuteFpuArithmetic(n, m, FpuArithmeticKind.Divide, static (left, right) => left / right, static (left, right) => left / right);
                return $"fdiv fr{m},fr{n} ; fr{n}=0x{State.Fr[n]:X8}{detail}";
            }

            case 0x4:
                State.T = CompareFpu(n, m, static (left, right) => left == right, static (left, right) => left == right);
                return $"fcmp/eq fr{m},fr{n} ; t={(State.T ? 1 : 0)}";

            case 0x5:
                State.T = CompareFpu(n, m, static (left, right) => left > right, static (left, right) => left > right);
                return $"fcmp/gt fr{m},fr{n} ; t={(State.T ? 1 : 0)}";

            case 0x6:
            {
                var address = State.R[0] + State.R[m];
                LoadFpuRegister(n, address, doubleSize);
                return $"{FmovMnemonic(doubleSize)} @(r0,r{m}),fr{n} ; fr{n}=0x{State.Fr[n]:X8}";
            }

            case 0x7:
            {
                var address = State.R[0] + State.R[n];
                StoreFpuRegister(m, address, doubleSize);
                return $"{FmovMnemonic(doubleSize)} fr{m},@(r0,r{n}) ; [0x{address:X8}]=0x{State.Fr[m]:X8}";
            }

            case 0x8:
                LoadFpuRegister(n, State.R[m], doubleSize);
                return $"{FmovMnemonic(doubleSize)} @r{m},fr{n} ; fr{n}=0x{State.Fr[n]:X8}";

            case 0x9:
                LoadFpuRegister(n, State.R[m], doubleSize);
                State.R[m] += doubleSize ? 8u : 4u;
                return $"{FmovMnemonic(doubleSize)} @r{m}+,fr{n} ; fr{n}=0x{State.Fr[n]:X8}";

            case 0xA:
                StoreFpuRegister(m, State.R[n], doubleSize);
                return $"{FmovMnemonic(doubleSize)} fr{m},@r{n} ; [0x{State.R[n]:X8}]=0x{State.Fr[m]:X8}";

            case 0xB:
                State.R[n] -= doubleSize ? 8u : 4u;
                StoreFpuRegister(m, State.R[n], doubleSize);
                return $"{FmovMnemonic(doubleSize)} fr{m},@-r{n} ; [0x{State.R[n]:X8}]=0x{State.Fr[m]:X8}";

            case 0xC:
                State.Fr[n] = State.Fr[m];
                if (doubleSize)
                {
                    State.Fr[(n + 1) & 0xF] = State.Fr[(m + 1) & 0xF];
                }

                return $"{FmovMnemonic(doubleSize)} fr{m},fr{n} ; fr{n}=0x{State.Fr[n]:X8}";

            case 0xD:
            {
                if ((opcode & 0xF0FF) == 0xF00D)
                {
                    State.Fr[n] = State.Fpul;
                    return $"fsts fpul,fr{n} ; fr{n}=0x{State.Fr[n]:X8}";
                }

                if ((opcode & 0xF0FF) == 0xF01D)
                {
                    State.Fpul = State.Fr[n];
                    return $"flds fr{n},fpul ; fpul=0x{State.Fpul:X8}";
                }

                if ((opcode & 0xF0FF) == 0xF04D)
                {
                    State.Fr[n] ^= 0x8000_0000;
                    return $"fneg fr{n} ; fr{n}=0x{State.Fr[n]:X8}";
                }

                if ((opcode & 0xF0FF) == 0xF06D)
                {
                    var operandBits = State.Fr[n];
                    var operand = BitConverter.UInt32BitsToSingle(operandBits);
                    var result = MathF.Sqrt(operand);
                    State.Fr[n] = SingleResultToBits(result);
                    RecordFpuSingleResult([operandBits], result);
                    return float.IsFinite(result)
                        ? $"fsqrt fr{n} ; fr{n}=0x{State.Fr[n]:X8}"
                        : $"fsqrt fr{n} ; fr{n}=0x{State.Fr[n]:X8} ; nonfinite fr{n}old=0x{operandBits:X8}";
                }

                if ((opcode & 0xF0FF) == 0xF08D)
                {
                    State.Fr[n] = 0;
                    return $"fldi0 fr{n} ; fr{n}=0x{State.Fr[n]:X8}";
                }

                if ((opcode & 0xF0FF) == 0xF09D)
                {
                    State.Fr[n] = BitConverter.SingleToUInt32Bits(1.0f);
                    return $"fldi1 fr{n} ; fr{n}=0x{State.Fr[n]:X8}";
                }

                if ((opcode & 0xF0FF) == 0xF0ED)
                {
                    return ExecuteFipr(opcode);
                }

                throw new UnsupportedInstructionException(State.Pc, opcode);
            }

            case 0xE:
            {
                var addendBits = State.Fr[n];
                var factorBits = State.Fr[m];
                var accumulatorBits = State.Fr[0];
                var addend = BitConverter.UInt32BitsToSingle(State.Fr[n]);
                var factor = BitConverter.UInt32BitsToSingle(factorBits);
                var accumulator = BitConverter.UInt32BitsToSingle(accumulatorBits);
                var result = addend + (factor * accumulator);
                var rounded = ApplySingleResultRounding(result, float.IsFinite(addend) && float.IsFinite(factor) && float.IsFinite(accumulator));
                State.Fr[n] = SingleResultToBits(rounded);
                RecordFpuSingleResult([addendBits, factorBits, accumulatorBits], result);
                var detail = float.IsFinite(rounded)
                    ? float.IsInfinity(result) ? " ; overflow-rounded" : string.Empty
                    : $" ; nonfinite fr{n}old=0x{addendBits:X8},fr{m}=0x{factorBits:X8},fr0=0x{accumulatorBits:X8}";
                return $"fmac fr0,fr{m},fr{n} ; fr{n}=0x{State.Fr[n]:X8}{detail}";
            }

            default:
                throw new UnsupportedInstructionException(State.Pc, opcode);
        }
    }

    private string ExecuteFipr(ushort opcode)
    {
        var destinationBase = ((opcode >> 8) & 0xC);
        var sourceBase = ((opcode >> 6) & 0xC);
        Span<uint> destinationOperands = stackalloc uint[4];
        Span<uint> sourceOperands = stackalloc uint[4];
        var sum = 0.0f;
        for (var index = 0; index < 4; index++)
        {
            destinationOperands[index] = State.Fr[destinationBase + index];
            sourceOperands[index] = State.Fr[sourceBase + index];
            sum += BitConverter.UInt32BitsToSingle(destinationOperands[index])
                * BitConverter.UInt32BitsToSingle(sourceOperands[index]);
        }

        var rounded = ApplySingleResultRounding(sum, AllFiniteSingleOperands(destinationOperands, sourceOperands));
        State.Fr[destinationBase + 3] = SingleResultToBits(rounded);
        RecordFpuSingleResult(destinationOperands, sourceOperands, sum, forceInexact: true);
        var trace = $"fipr fv{sourceBase},fv{destinationBase} ; fr{destinationBase + 3}=0x{State.Fr[destinationBase + 3]:X8}";
        if (float.IsFinite(rounded))
        {
            return float.IsInfinity(sum) ? $"{trace} ; overflow-rounded" : trace;
        }

        return float.IsFinite(sum)
            ? trace
            : $"{trace} ; fv{destinationBase}={FormatFpuVector(destinationOperands)}, fv{sourceBase}={FormatFpuVector(sourceOperands)}";
    }

    private string ExecuteFpuArithmetic(
        int n,
        int m,
        FpuArithmeticKind kind,
        Func<float, float, float> singleOperation,
        Func<double, double, double> doubleOperation)
    {
        if ((State.Fpscr & Sh4State.FpscrPrBit) != 0)
        {
            var left = ReadDoubleRegister(n);
            var right = ReadDoubleRegister(m);
            var result = doubleOperation(left, right);
            var leftBits = ReadDoubleRegisterBits(n);
            var rightBits = ReadDoubleRegisterBits(m);
            var isOverflow = double.IsInfinity(result)
                && double.IsFinite(left)
                && double.IsFinite(right)
                && (kind != FpuArithmeticKind.Divide || right != 0.0);
            var rounded = ApplyDoubleResultRounding(result, isOverflow);
            WriteDoubleRegisterBits(n, DoubleResultToBits(rounded));
            RecordFpuDoubleResult(kind, leftBits, rightBits, left, right, result);
            return double.IsFinite(rounded)
                ? isOverflow && double.IsInfinity(result) ? " ; overflow-rounded" : string.Empty
                : $" ; nonfinite dr{n & ~1}old=0x{leftBits:X16},dr{m & ~1}=0x{rightBits:X16}";
        }

        var leftBitsSingle = State.Fr[n];
        var rightBitsSingle = State.Fr[m];
        var leftSingle = BitConverter.UInt32BitsToSingle(leftBitsSingle);
        var rightSingle = BitConverter.UInt32BitsToSingle(rightBitsSingle);
        var resultSingle = singleOperation(leftSingle, rightSingle);
        var isSingleOverflow = float.IsInfinity(resultSingle)
            && float.IsFinite(leftSingle)
            && float.IsFinite(rightSingle)
            && (kind != FpuArithmeticKind.Divide || rightSingle != 0.0f);
        var roundedSingle = ApplySingleResultRounding(resultSingle, isSingleOverflow);
        State.Fr[n] = SingleResultToBits(roundedSingle);
        RecordFpuSingleResult(kind, leftBitsSingle, rightBitsSingle, leftSingle, rightSingle, resultSingle);
        return float.IsFinite(roundedSingle)
            ? isSingleOverflow && float.IsInfinity(resultSingle) ? " ; overflow-rounded" : string.Empty
            : $" ; nonfinite fr{n}old=0x{leftBitsSingle:X8},fr{m}=0x{rightBitsSingle:X8}";
    }

    private float ApplySingleResultRounding(float result, bool finiteOverflowInputs)
    {
        if ((State.Fpscr & Sh4State.FpscrRoundToZeroBit) == 0 || !finiteOverflowInputs || !float.IsInfinity(result))
        {
            return result;
        }

        return float.IsNegative(result) ? -float.MaxValue : float.MaxValue;
    }

    private static uint SingleResultToBits(float result) =>
        float.IsNaN(result) ? Sh4State.DefaultSingleQNaN : BitConverter.SingleToUInt32Bits(result);

    private double ApplyDoubleResultRounding(double result, bool finiteOverflowInputs)
    {
        if ((State.Fpscr & Sh4State.FpscrRoundToZeroBit) == 0 || !finiteOverflowInputs || !double.IsInfinity(result))
        {
            return result;
        }

        return double.IsNegative(result) ? -double.MaxValue : double.MaxValue;
    }

    private static ulong DoubleResultToBits(double result) =>
        double.IsNaN(result) ? Sh4State.DefaultDoubleQNaN : BitConverter.DoubleToUInt64Bits(result);

    private static bool AllFiniteSingleOperands(ReadOnlySpan<uint> leftOperands, ReadOnlySpan<uint> rightOperands)
    {
        for (var index = 0; index < leftOperands.Length; index++)
        {
            if (!float.IsFinite(BitConverter.UInt32BitsToSingle(leftOperands[index]))
                || !float.IsFinite(BitConverter.UInt32BitsToSingle(rightOperands[index])))
            {
                return false;
            }
        }

        return true;
    }

    private void RecordFpuSingleResult(FpuArithmeticKind kind, uint leftBits, uint rightBits, float left, float right, float result)
    {
        var cause = 0u;
        if (IsSingleSignalingNaN(leftBits) || IsSingleSignalingNaN(rightBits) || IsInvalidSingleResult(kind, left, right, result))
        {
            cause |= Sh4State.FpscrCauseInvalidBit;
        }

        if (kind == FpuArithmeticKind.Divide && left != 0.0f && float.IsFinite(left) && right == 0.0f)
        {
            cause |= Sh4State.FpscrCauseDivisionByZeroBit;
        }

        if (float.IsInfinity(result) && float.IsFinite(left) && float.IsFinite(right) && (kind != FpuArithmeticKind.Divide || right != 0.0f))
        {
            cause |= Sh4State.FpscrCauseOverflowBit | Sh4State.FpscrCauseInexactBit;
        }

        RecordFpuExceptionCause(cause);
    }

    private void RecordFpuSingleResult(ReadOnlySpan<uint> operands, float result)
    {
        var cause = 0u;
        var finiteInputs = true;
        var hasNaNInput = false;
        foreach (var operandBits in operands)
        {
            var operand = BitConverter.UInt32BitsToSingle(operandBits);
            hasNaNInput |= IsSingleNaN(operandBits);
            if (IsSingleSignalingNaN(operandBits))
            {
                cause |= Sh4State.FpscrCauseInvalidBit;
            }

            finiteInputs &= float.IsFinite(operand);
        }

        if (float.IsNaN(result) && cause == 0 && !hasNaNInput)
        {
            cause |= Sh4State.FpscrCauseInvalidBit;
        }

        if (float.IsInfinity(result) && finiteInputs)
        {
            cause |= Sh4State.FpscrCauseOverflowBit | Sh4State.FpscrCauseInexactBit;
        }

        RecordFpuExceptionCause(cause);
    }

    private void RecordFpuSingleResult(
        ReadOnlySpan<uint> leftOperands,
        ReadOnlySpan<uint> rightOperands,
        float result,
        bool forceInexact = false)
    {
        var cause = forceInexact ? Sh4State.FpscrCauseInexactBit : 0u;
        var finiteInputs = true;
        var hasNaNInput = false;
        for (var index = 0; index < leftOperands.Length; index++)
        {
            AccumulateSingleOperandException(leftOperands[index], ref cause, ref finiteInputs, ref hasNaNInput);
            AccumulateSingleOperandException(rightOperands[index], ref cause, ref finiteInputs, ref hasNaNInput);
        }

        if (float.IsNaN(result) && cause == 0 && !hasNaNInput)
        {
            cause |= Sh4State.FpscrCauseInvalidBit;
        }

        if (float.IsInfinity(result) && finiteInputs)
        {
            cause |= Sh4State.FpscrCauseOverflowBit | Sh4State.FpscrCauseInexactBit;
        }

        RecordFpuExceptionCause(cause);
    }

    private static void AccumulateSingleOperandException(uint operandBits, ref uint cause, ref bool finiteInputs, ref bool hasNaNInput)
    {
        var operand = BitConverter.UInt32BitsToSingle(operandBits);
        hasNaNInput |= IsSingleNaN(operandBits);
        if (IsSingleSignalingNaN(operandBits))
        {
            cause |= Sh4State.FpscrCauseInvalidBit;
        }

        finiteInputs &= float.IsFinite(operand);
    }

    private void RecordFpuDoubleResult(FpuArithmeticKind kind, ulong leftBits, ulong rightBits, double left, double right, double result)
    {
        var cause = 0u;
        if (IsDoubleSignalingNaN(leftBits) || IsDoubleSignalingNaN(rightBits) || IsInvalidDoubleResult(kind, left, right, result))
        {
            cause |= Sh4State.FpscrCauseInvalidBit;
        }

        if (kind == FpuArithmeticKind.Divide && left != 0.0 && double.IsFinite(left) && right == 0.0)
        {
            cause |= Sh4State.FpscrCauseDivisionByZeroBit;
        }

        if (double.IsInfinity(result) && double.IsFinite(left) && double.IsFinite(right) && (kind != FpuArithmeticKind.Divide || right != 0.0))
        {
            cause |= Sh4State.FpscrCauseOverflowBit | Sh4State.FpscrCauseInexactBit;
        }

        RecordFpuExceptionCause(cause);
    }

    private static bool IsInvalidSingleResult(FpuArithmeticKind kind, float left, float right, float result) =>
        float.IsNaN(result)
        && kind switch
        {
            FpuArithmeticKind.Add => float.IsInfinity(left) && float.IsInfinity(right) && MathF.Sign(left) != MathF.Sign(right),
            FpuArithmeticKind.Subtract => float.IsInfinity(left) && float.IsInfinity(right) && MathF.Sign(left) == MathF.Sign(right),
            FpuArithmeticKind.Multiply => (left == 0.0f && float.IsInfinity(right)) || (right == 0.0f && float.IsInfinity(left)),
            FpuArithmeticKind.Divide => left == 0.0f && right == 0.0f,
            _ => true
        };

    private static bool IsInvalidDoubleResult(FpuArithmeticKind kind, double left, double right, double result) =>
        double.IsNaN(result)
        && kind switch
        {
            FpuArithmeticKind.Add => double.IsInfinity(left) && double.IsInfinity(right) && Math.Sign(left) != Math.Sign(right),
            FpuArithmeticKind.Subtract => double.IsInfinity(left) && double.IsInfinity(right) && Math.Sign(left) == Math.Sign(right),
            FpuArithmeticKind.Multiply => (left == 0.0 && double.IsInfinity(right)) || (right == 0.0 && double.IsInfinity(left)),
            FpuArithmeticKind.Divide => left == 0.0 && right == 0.0,
            _ => true
        };

    private static bool IsSingleNaN(uint bits) =>
        (bits & 0x7F80_0000u) == 0x7F80_0000u && (bits & 0x007F_FFFFu) != 0;

    private static bool IsSingleSignalingNaN(uint bits) =>
        IsSingleNaN(bits) && (bits & 0x0040_0000u) != 0;

    private static bool IsDoubleNaN(ulong bits) =>
        (bits & 0x7FF0_0000_0000_0000ul) == 0x7FF0_0000_0000_0000ul && (bits & 0x000F_FFFF_FFFF_FFFFul) != 0;

    private static bool IsDoubleSignalingNaN(ulong bits) =>
        IsDoubleNaN(bits) && (bits & 0x0008_0000_0000_0000ul) != 0;

    private void RecordFpuExceptionCause(uint cause)
    {
        State.Fpscr = (State.Fpscr & ~Sh4State.FpscrCauseMask)
            | cause
            | ((cause >> 10) & Sh4State.FpscrFlagMask);
    }

    private uint ConvertSingleToFpul(uint bits)
    {
        var value = BitConverter.UInt32BitsToSingle(bits);
        var cause = 0u;
        int result;
        if (float.IsNaN(value))
        {
            cause = Sh4State.FpscrCauseInvalidBit;
            result = int.MinValue;
        }
        else if (value >= 2147483648.0f)
        {
            cause = Sh4State.FpscrCauseInvalidBit;
            result = int.MaxValue;
        }
        else if (value < int.MinValue)
        {
            cause = Sh4State.FpscrCauseInvalidBit;
            result = int.MinValue;
        }
        else
        {
            result = (int)value;
        }

        RecordFpuExceptionCause(cause);
        return unchecked((uint)result);
    }

    private uint ConvertDoubleToFpul(ulong bits)
    {
        var value = BitConverter.UInt64BitsToDouble(bits);
        var cause = 0u;
        int result;
        if (double.IsNaN(value))
        {
            cause = Sh4State.FpscrCauseInvalidBit;
            result = int.MinValue;
        }
        else if (value > int.MaxValue)
        {
            cause = Sh4State.FpscrCauseInvalidBit;
            result = int.MaxValue;
        }
        else if (value < int.MinValue)
        {
            cause = Sh4State.FpscrCauseInvalidBit;
            result = int.MinValue;
        }
        else
        {
            result = (int)value;
        }

        RecordFpuExceptionCause(cause);
        return unchecked((uint)result);
    }

    private bool CompareFpu(
        int n,
        int m,
        Func<float, float, bool> singleComparison,
        Func<double, double, bool> doubleComparison)
    {
        if ((State.Fpscr & Sh4State.FpscrPrBit) != 0)
        {
            return doubleComparison(ReadDoubleRegister(n), ReadDoubleRegister(m));
        }

        return singleComparison(
            BitConverter.UInt32BitsToSingle(State.Fr[n]),
            BitConverter.UInt32BitsToSingle(State.Fr[m]));
    }

    private void LoadFpuRegister(int register, uint address, bool doubleSize)
    {
        State.Fr[register] = memory.ReadUInt32(address);
        if (doubleSize)
        {
            State.Fr[(register + 1) & 0xF] = memory.ReadUInt32(address + 4);
        }
    }

    private void StoreFpuRegister(int register, uint address, bool doubleSize)
    {
        memory.WriteUInt32(address, State.Fr[register]);
        if (doubleSize)
        {
            memory.WriteUInt32(address + 4, State.Fr[(register + 1) & 0xF]);
        }
    }

    private static string FmovMnemonic(bool doubleSize) => doubleSize ? "fmov.d" : "fmov.s";

    private static string FormatFpuVector(ReadOnlySpan<uint> values) =>
        $"[0x{values[0]:X8},0x{values[1]:X8},0x{values[2]:X8},0x{values[3]:X8}]";

    private double ReadDoubleRegister(int register)
    {
        return BitConverter.UInt64BitsToDouble(ReadDoubleRegisterBits(register));
    }

    private ulong ReadDoubleRegisterBits(int register)
    {
        var evenRegister = register & ~1;
        return ((ulong)State.Fr[evenRegister] << 32) | State.Fr[evenRegister + 1];
    }

    private void WriteDoubleRegister(int register, double value)
    {
        WriteDoubleRegisterBits(register, BitConverter.DoubleToUInt64Bits(value));
    }

    private void WriteDoubleRegisterBits(int register, ulong bits)
    {
        var evenRegister = register & ~1;
        State.Fr[evenRegister] = (uint)(bits >> 32);
        State.Fr[evenRegister + 1] = (uint)bits;
    }

    private enum FpuArithmeticKind
    {
        Add,
        Subtract,
        Multiply,
        Divide
    }
}

public sealed class Sh4State
{
    public const uint DefaultSingleQNaN = 0x7FBF_FFFF;
    public const ulong DefaultDoubleQNaN = 0x7FF7_FFFF_FFFF_FFFF;
    public const uint FpscrRoundToZeroBit = 1u;
    public const uint FpscrSzBit = 1u << 20;
    public const uint FpscrFrBit = 1u << 21;
    public const uint FpscrPrBit = 1u << 19;
    public const uint FpscrDnBit = 1u << 18;
    public const uint FpscrCauseInexactBit = 1u << 12;
    public const uint FpscrCauseUnderflowBit = 1u << 13;
    public const uint FpscrCauseOverflowBit = 1u << 14;
    public const uint FpscrCauseDivisionByZeroBit = 1u << 15;
    public const uint FpscrCauseInvalidBit = 1u << 16;
    public const uint FpscrCauseMask = FpscrCauseInvalidBit
        | FpscrCauseDivisionByZeroBit
        | FpscrCauseOverflowBit
        | FpscrCauseUnderflowBit
        | FpscrCauseInexactBit;
    public const uint FpscrFlagInexactBit = 1u << 2;
    public const uint FpscrFlagUnderflowBit = 1u << 3;
    public const uint FpscrFlagOverflowBit = 1u << 4;
    public const uint FpscrFlagDivisionByZeroBit = 1u << 5;
    public const uint FpscrFlagInvalidBit = 1u << 6;
    public const uint FpscrFlagMask = FpscrFlagInvalidBit
        | FpscrFlagDivisionByZeroBit
        | FpscrFlagOverflowBit
        | FpscrFlagUnderflowBit
        | FpscrFlagInexactBit;
    public const uint FpscrEnableMask = FpscrFlagMask << 5;
    public const uint SrBlockBit = 1u << 28;
    public const uint SrRegisterBankBit = 1u << 29;
    public const uint SrMachineBit = 1u << 30;

    private uint sr;
    private uint fpscr = 0x0004_0001;
    private uint biosInterruptPr;
    private uint biosInterruptEventCode;
    private bool hasBiosInterruptPr;

    public uint[] R { get; } = new uint[16];
    public uint[] RBank { get; } = new uint[8];
    public uint[] Fr { get; } = new uint[16];
    public uint[] Xf { get; } = new uint[16];
    public uint Pc { get; set; }
    public uint Pr { get; set; }
    public uint Sr
    {
        get => sr;
        set
        {
            if (((sr ^ value) & SrRegisterBankBit) != 0)
            {
                for (var i = 0; i < RBank.Length; i++)
                {
                    (R[i], RBank[i]) = (RBank[i], R[i]);
                }
            }

            sr = value;
        }
    }
    public uint Ssr { get; set; }
    public uint Spc { get; set; }
    public uint Gbr { get; set; }
    public uint Vbr { get; set; }
    public uint Fpscr
    {
        get => fpscr;
        set
        {
            if (((fpscr ^ value) & FpscrFrBit) != 0)
            {
                SwapFloatingPointRegisterBanks();
            }

            fpscr = value;
        }
    }
    public uint Fpul { get; set; }
    public uint Mach { get; set; }
    public uint Macl { get; set; }
    public bool M
    {
        get => (Sr & 0x200) != 0;
        set => Sr = value ? Sr | 0x200u : Sr & ~0x200u;
    }

    public bool Q
    {
        get => (Sr & 0x100) != 0;
        set => Sr = value ? Sr | 0x100u : Sr & ~0x100u;
    }

    public bool T
    {
        get => (Sr & 1) != 0;
        set => Sr = value ? Sr | 1u : Sr & ~1u;
    }

    public ulong InstructionsExecuted { get; set; }

    private void SwapFloatingPointRegisterBanks()
    {
        for (var index = 0; index < Fr.Length; index++)
        {
            (Fr[index], Xf[index]) = (Xf[index], Fr[index]);
        }
    }

    internal void SaveBiosInterruptPr(uint value, uint eventCode = 0)
    {
        biosInterruptPr = value;
        biosInterruptEventCode = eventCode;
        hasBiosInterruptPr = true;
    }

    internal bool RestoreBiosInterruptPr() => RestoreBiosInterruptPr(out _);

    internal bool RestoreBiosInterruptPr(out uint eventCode)
    {
        eventCode = 0;
        if (!hasBiosInterruptPr)
        {
            return false;
        }

        Pr = biosInterruptPr;
        eventCode = biosInterruptEventCode;
        biosInterruptPr = 0;
        biosInterruptEventCode = 0;
        hasBiosInterruptPr = false;
        return true;
    }
}

public sealed record Sh4StepResult(uint Pc, ushort Opcode, string Trace, ulong Instruction = 0);

public delegate bool Sh4TrapHandler(Sh4State state, DreamcastMemory memory, out string trace);

public sealed class UnsupportedInstructionException(uint pc, ushort opcode)
    : InvalidOperationException($"Unsupported SH-4 opcode 0x{opcode:X4} at 0x{pc:X8}")
{
    public uint Pc { get; } = pc;
    public ushort Opcode { get; } = opcode;
}
