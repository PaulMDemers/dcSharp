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

        skippedInstructions = skippedInstructionCount;
        State.Pc = State.Pr;
        State.InstructionsExecuted += skippedInstructions;
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
        State.R[14] += 32;
        State.R[1] = tablePointerAddress;
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
        State.Fpul = (uint)(int)BitConverter.UInt32BitsToSingle(State.Fr[3]);
        State.R[6] = State.Fpul;
        State.Fpul = State.R[6];
        State.Fr[3] = BitConverter.SingleToUInt32Bits(unchecked((int)State.Fpul));
        ExecuteFpuMove(0xF352, 3, 5, 0x2);
        ExecuteFpuMove(0xF69D, 6, 9, 0xD);
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

        ExecuteFpuMove(0xF58D, 5, 8, 0xD);
        State.R[3] = 1;
        ExecuteFpuMove(0xF36C, 3, 6, 0xC);
        ExecuteFpuMove(0xF351, 3, 5, 0x1);

        skippedInstructions = skippedInstructionCount;
        State.Pc = 0x8C0F_B216;
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
            || !State.T
            || State.Pc != 0x8C0F_B218)
        {
            return false;
        }

        const ulong skippedInstructionCount = 10;
        if (!IsDoa2PostTrigHelperReturn()
            || (State.Fpscr & (Sh4State.FpscrPrBit | Sh4State.FpscrSzBit)) != 0
            || maxInstructionsToSkip < skippedInstructionCount)
        {
            return false;
        }

        ExecuteFpuMove(0xF433, 4, 3, 0x3);
        ExecuteFpuMove(0xF24C, 2, 4, 0xC);
        ExecuteFpuMove(0xF272, 2, 7, 0x2);
        ExecuteFpuMove(0xF04C, 0, 4, 0xC);
        ExecuteFpuMove(0xF64E, 6, 4, 0xE);
        ExecuteFpuMove(0xF42C, 4, 2, 0xC);
        ExecuteFpuMove(0xF463, 4, 6, 0x3);
        ExecuteFpuMove(0xF04C, 0, 4, 0xC);

        skippedInstructions = skippedInstructionCount;
        State.Pc = State.Pr;
        State.InstructionsExecuted += skippedInstructions;
        delayedBranchTarget = null;
        immediateBranchTarget = null;
        return true;
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
        && memory.ReadInstructionUInt16(0x8C10_7876) == 0x7001;

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
        && memory.ReadInstructionUInt16(0x8C0F_B22A) == 0xF04C;

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
            State.Fpul = (uint)((State.Fpscr & Sh4State.FpscrPrBit) != 0
                ? (int)ReadDoubleRegister(n)
                : (int)BitConverter.UInt32BitsToSingle(State.Fr[n]));
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
                var destinationBase = n & 0xC;
                var sourceBase = m & 0xC;
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

                var rounded = ApplySingleOverflowRounding(sum, AllFiniteSingleOperands(destinationOperands, sourceOperands));
                State.Fr[destinationBase + 3] = BitConverter.SingleToUInt32Bits(rounded);
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

            case 0xE:
            {
                var addendBits = State.Fr[n];
                var factorBits = State.Fr[m];
                var accumulatorBits = State.Fr[0];
                var addend = BitConverter.UInt32BitsToSingle(State.Fr[n]);
                var factor = BitConverter.UInt32BitsToSingle(factorBits);
                var accumulator = BitConverter.UInt32BitsToSingle(accumulatorBits);
                var result = addend + (factor * accumulator);
                var rounded = ApplySingleOverflowRounding(result, float.IsFinite(addend) && float.IsFinite(factor) && float.IsFinite(accumulator));
                State.Fr[n] = BitConverter.SingleToUInt32Bits(rounded);
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
            var rounded = ApplyDoubleOverflowRounding(result, isOverflow);
            WriteDoubleRegister(n, rounded);
            RecordFpuDoubleResult(kind, left, right, result);
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
        var roundedSingle = ApplySingleOverflowRounding(resultSingle, isSingleOverflow);
        State.Fr[n] = BitConverter.SingleToUInt32Bits(roundedSingle);
        RecordFpuSingleResult(kind, leftSingle, rightSingle, resultSingle);
        return float.IsFinite(roundedSingle)
            ? isSingleOverflow && float.IsInfinity(resultSingle) ? " ; overflow-rounded" : string.Empty
            : $" ; nonfinite fr{n}old=0x{leftBitsSingle:X8},fr{m}=0x{rightBitsSingle:X8}";
    }

    private float ApplySingleOverflowRounding(float result, bool finiteOverflowInputs)
    {
        if ((State.Fpscr & Sh4State.FpscrRoundToZeroBit) == 0 || !finiteOverflowInputs || !float.IsInfinity(result))
        {
            return result;
        }

        return float.IsNegative(result) ? -float.MaxValue : float.MaxValue;
    }

    private double ApplyDoubleOverflowRounding(double result, bool finiteOverflowInputs)
    {
        if ((State.Fpscr & Sh4State.FpscrRoundToZeroBit) == 0 || !finiteOverflowInputs || !double.IsInfinity(result))
        {
            return result;
        }

        return double.IsNegative(result) ? -double.MaxValue : double.MaxValue;
    }

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

    private void RecordFpuSingleResult(FpuArithmeticKind kind, float left, float right, float result)
    {
        var cause = 0u;
        if (float.IsNaN(left) || float.IsNaN(right) || IsInvalidSingleResult(kind, left, right, result))
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
        foreach (var operandBits in operands)
        {
            var operand = BitConverter.UInt32BitsToSingle(operandBits);
            if (float.IsNaN(operand))
            {
                cause |= Sh4State.FpscrCauseInvalidBit;
            }

            finiteInputs &= float.IsFinite(operand);
        }

        if (float.IsNaN(result) && cause == 0)
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
        for (var index = 0; index < leftOperands.Length; index++)
        {
            AccumulateSingleOperandException(leftOperands[index], ref cause, ref finiteInputs);
            AccumulateSingleOperandException(rightOperands[index], ref cause, ref finiteInputs);
        }

        if (float.IsNaN(result) && cause == 0)
        {
            cause |= Sh4State.FpscrCauseInvalidBit;
        }

        if (float.IsInfinity(result) && finiteInputs)
        {
            cause |= Sh4State.FpscrCauseOverflowBit | Sh4State.FpscrCauseInexactBit;
        }

        RecordFpuExceptionCause(cause);
    }

    private static void AccumulateSingleOperandException(uint operandBits, ref uint cause, ref bool finiteInputs)
    {
        var operand = BitConverter.UInt32BitsToSingle(operandBits);
        if (float.IsNaN(operand))
        {
            cause |= Sh4State.FpscrCauseInvalidBit;
        }

        finiteInputs &= float.IsFinite(operand);
    }

    private void RecordFpuDoubleResult(FpuArithmeticKind kind, double left, double right, double result)
    {
        var cause = 0u;
        if (double.IsNaN(left) || double.IsNaN(right) || IsInvalidDoubleResult(kind, left, right, result))
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

    private void RecordFpuExceptionCause(uint cause)
    {
        State.Fpscr = (State.Fpscr & ~Sh4State.FpscrCauseMask)
            | cause
            | ((cause >> 10) & Sh4State.FpscrFlagMask);
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
        var evenRegister = register & ~1;
        var bits = BitConverter.DoubleToUInt64Bits(value);
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
