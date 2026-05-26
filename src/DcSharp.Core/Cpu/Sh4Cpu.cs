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
        if (delayedBranchTarget is null && TryAcceptExternalInterrupt(pc, out var interruptTrace))
        {
            State.InstructionsExecuted++;
            return new Sh4StepResult(pc, 0, interruptTrace);
        }

        if (trapHandler?.Invoke(State, memory, out var trapTrace) == true)
        {
            State.InstructionsExecuted++;
            return new Sh4StepResult(pc, 0, trapTrace);
        }

        var opcode = memory.ReadInstructionUInt16(pc);
        var trace = Execute(pc, opcode);
        State.InstructionsExecuted++;

        return new Sh4StepResult(pc, opcode, trace);
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
        skippedInstructions = remainingIterations * instructionsPerIteration;
        if (skippedInstructions == 0 || skippedInstructions > maxInstructionsToSkip)
        {
            skippedInstructions = 0;
            return false;
        }

        State.R[4] += remainingIterations * 4;
        State.R[1] -= remainingIterations * 4;
        State.R[7] = 0;
        State.T = true;
        State.Pc = 0x8C00_8350;
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
        State.Pc = State.Vbr + 0x600;
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

        if ((opcode & 0xF0FF) == 0x402A)
        {
            State.R[n] = State.Pr;
            return $"sts pr,r{n} ; r{n}=0x{State.R[n]:X8}";
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
                ExecuteFpuArithmetic(n, m, static (left, right) => left + right, static (left, right) => left + right);
                return $"fadd fr{m},fr{n} ; fr{n}=0x{State.Fr[n]:X8}";

            case 0x1:
                ExecuteFpuArithmetic(n, m, static (left, right) => left - right, static (left, right) => left - right);
                return $"fsub fr{m},fr{n} ; fr{n}=0x{State.Fr[n]:X8}";

            case 0x2:
                ExecuteFpuArithmetic(n, m, static (left, right) => left * right, static (left, right) => left * right);
                return $"fmul fr{m},fr{n} ; fr{n}=0x{State.Fr[n]:X8}";

            case 0x3:
                ExecuteFpuArithmetic(n, m, static (left, right) => left / right, static (left, right) => left / right);
                return $"fdiv fr{m},fr{n} ; fr{n}=0x{State.Fr[n]:X8}";

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

            default:
                throw new UnsupportedInstructionException(State.Pc, opcode);
        }
    }

    private void ExecuteFpuArithmetic(
        int n,
        int m,
        Func<float, float, float> singleOperation,
        Func<double, double, double> doubleOperation)
    {
        if ((State.Fpscr & Sh4State.FpscrPrBit) != 0)
        {
            WriteDoubleRegister(n, doubleOperation(ReadDoubleRegister(n), ReadDoubleRegister(m)));
            return;
        }

        var left = BitConverter.UInt32BitsToSingle(State.Fr[n]);
        var right = BitConverter.UInt32BitsToSingle(State.Fr[m]);
        State.Fr[n] = BitConverter.SingleToUInt32Bits(singleOperation(left, right));
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

    private double ReadDoubleRegister(int register)
    {
        var evenRegister = register & ~1;
        var bits = ((ulong)State.Fr[evenRegister] << 32) | State.Fr[evenRegister + 1];
        return BitConverter.UInt64BitsToDouble(bits);
    }

    private void WriteDoubleRegister(int register, double value)
    {
        var evenRegister = register & ~1;
        var bits = BitConverter.DoubleToUInt64Bits(value);
        State.Fr[evenRegister] = (uint)(bits >> 32);
        State.Fr[evenRegister + 1] = (uint)bits;
    }
}

public sealed class Sh4State
{
    public const uint FpscrSzBit = 1u << 20;
    public const uint FpscrFrBit = 1u << 21;
    public const uint FpscrPrBit = 1u << 19;
    public const uint SrBlockBit = 1u << 28;
    public const uint SrRegisterBankBit = 1u << 29;
    public const uint SrMachineBit = 1u << 30;

    private uint sr;

    public uint[] R { get; } = new uint[16];
    public uint[] RBank { get; } = new uint[8];
    public uint[] Fr { get; } = new uint[16];
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
    public uint Fpscr { get; set; } = 0x0004_0001;
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
}

public sealed record Sh4StepResult(uint Pc, ushort Opcode, string Trace);

public delegate bool Sh4TrapHandler(Sh4State state, DreamcastMemory memory, out string trace);

public sealed class UnsupportedInstructionException(uint pc, ushort opcode)
    : InvalidOperationException($"Unsupported SH-4 opcode 0x{opcode:X4} at 0x{pc:X8}")
{
    public uint Pc { get; } = pc;
    public ushort Opcode { get; } = opcode;
}
