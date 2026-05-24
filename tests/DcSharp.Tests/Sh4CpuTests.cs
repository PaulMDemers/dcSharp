using DcSharp.Core.Cpu;
using DcSharp.Core.Dreamcast.Memory;

namespace DcSharp.Tests;

public class Sh4CpuTests
{
    [Fact]
    public void ExecutesPcRelativeMovLong()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xD000);
        memory.WriteUInt32(0x8C01_0004, 0x1234_5678);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);

        cpu.Step();

        Assert.Equal(0x1234_5678u, cpu.State.R[0]);
        Assert.Equal(0x8C01_0002u, cpu.State.Pc);
    }

    [Fact]
    public void AppliesJumpAfterDelaySlot()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x402B);
        WriteInstruction(memory, 0x8C01_0002, 0x0009);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[0] = 0x8C01_0020;

        cpu.Step();
        Assert.Equal(0x8C01_0002u, cpu.State.Pc);

        cpu.Step();
        Assert.Equal(0x8C01_0020u, cpu.State.Pc);
    }

    [Fact]
    public void BranchesToSubroutineAfterDelaySlot()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xB001);
        WriteInstruction(memory, 0x8C01_0002, 0x0009);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);

        cpu.Step();
        Assert.Equal(0x8C01_0004u, cpu.State.Pr);
        Assert.Equal(0x8C01_0002u, cpu.State.Pc);

        cpu.Step();
        Assert.Equal(0x8C01_0006u, cpu.State.Pc);
    }

    [Fact]
    public void ExecutesSleepAsIdleInstruction()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x001B);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);

        var step = cpu.Step();

        Assert.Equal("sleep", step.Trace);
        Assert.Equal(0x8C01_0002u, cpu.State.Pc);
        Assert.Equal(1u, cpu.State.InstructionsExecuted);
    }

    [Theory]
    [InlineData(0x0483, "pref @r4")]
    [InlineData(0x0493, "ocbi @r4")]
    [InlineData(0x04A3, "ocbp @r4")]
    [InlineData(0x04B3, "ocbwb @r4")]
    public void ExecutesCacheMaintenanceAsNoOp(ushort opcode, string expectedTrace)
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, opcode);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[4] = 0x8C02_0000;

        var step = cpu.Step();

        Assert.Equal(expectedTrace, step.Trace);
        Assert.Equal(0x8C01_0002u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsMaskedCountedIdleLoop()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x0009); // nop
        WriteInstruction(memory, 0x8C01_0002, 0x4110); // dt r1
        WriteInstruction(memory, 0x8C01_0004, 0x8FFC); // bf/s 0x8C010000
        WriteInstruction(memory, 0x8C01_0006, 0x72FF); // add #-1,r2
        WriteInstruction(memory, 0x8C01_0008, 0x0009); // fallthrough
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Sr = 0xF0;
        cpu.State.R[1] = 3;
        cpu.State.R[2] = 10;

        cpu.Step();
        cpu.Step();
        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardCountedIdleLoop(branch, 100, out var skippedInstructions));
        Assert.Equal(9UL, skippedInstructions);
        Assert.Equal(0u, cpu.State.R[1]);
        Assert.Equal(7u, cpu.State.R[2]);
        Assert.True(cpu.State.T);
        Assert.Equal(0x8C01_0008u, cpu.State.Pc);
        Assert.Equal(12UL, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void DoesNotFastForwardCountedIdleLoopWhenInterruptsAreUnmasked()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x0009);
        WriteInstruction(memory, 0x8C01_0002, 0x4110);
        WriteInstruction(memory, 0x8C01_0004, 0x8FFC);
        WriteInstruction(memory, 0x8C01_0006, 0x0009);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[1] = 3;

        cpu.Step();
        cpu.Step();
        var branch = cpu.Step();

        Assert.False(cpu.TryFastForwardCountedIdleLoop(branch, 100, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C01_0006u, cpu.State.Pc);
        Assert.Equal(3UL, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void ExecutesDisplacementWordStoreFromR0()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x8137);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[0] = 0xA55A;
        cpu.State.R[3] = 0x8C02_0000;

        cpu.Step();

        Assert.Equal(0xA55A, memory.ReadUInt16(0x8C02_000E));
        Assert.Equal(0x8C01_0002u, cpu.State.Pc);
    }

    [Fact]
    public void ExecutesDisplacementByteStoreFromR0()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x8025);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[0] = 0xA5;
        cpu.State.R[2] = 0x8C02_0000;

        cpu.Step();

        Assert.Equal(0xA5, memory.ReadByte(0x8C02_0005));
    }

    [Fact]
    public void ExecutesSignedGreaterOrEqualCompare()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x3213);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[1] = 0;
        cpu.State.R[2] = 0xFFFF_FFFF;

        cpu.Step();

        Assert.False(cpu.State.T);
    }

    [Fact]
    public void ExecutesUnsignedWordMultiply()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x212E);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[1] = 0xFFFF;
        cpu.State.R[2] = 2;

        cpu.Step();

        Assert.Equal(0x0001_FFFEu, cpu.State.Macl);
    }

    [Fact]
    public void ExecutesSignedWordExtension()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x611F);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[1] = 0x8000;

        cpu.Step();

        Assert.Equal(0xFFFF_8000u, cpu.State.R[1]);
    }

    [Fact]
    public void ExecutesIndexedWordLoad()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x011D);
        memory.WriteUInt16(0x8C02_0002, 0xFF80);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[0] = 0x8C02_0000;
        cpu.State.R[1] = 2;

        cpu.Step();

        Assert.Equal(0xFFFF_FF80u, cpu.State.R[1]);
    }

    [Fact]
    public void ExecutesPostIncrementWordLoad()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x6255);
        memory.WriteUInt16(0x8C02_0004, 0xFF80);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[5] = 0x8C02_0004;

        cpu.Step();

        Assert.Equal(0xFFFF_FF80u, cpu.State.R[2]);
        Assert.Equal(0x8C02_0006u, cpu.State.R[5]);
        Assert.Equal(0x8C01_0002u, cpu.State.Pc);
    }

    [Fact]
    public void StoresFloatingPointRegisterToMemory()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF1AA);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[1] = 0x8C02_0000;
        cpu.State.Fr[10] = 0x3F80_0000;

        cpu.Step();

        Assert.Equal(0x3F80_0000u, memory.ReadUInt32(0x8C02_0000));
    }

    [Fact]
    public void DoubleSizeFloatingPointPredecrementStoresRegisterPair()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF4EB);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fpscr = Sh4State.FpscrSzBit;
        cpu.State.R[4] = 0x8C02_0010;
        cpu.State.Fr[14] = 0x1111_1111;
        cpu.State.Fr[15] = 0x2222_2222;

        cpu.Step();

        Assert.Equal(0x8C02_0008u, cpu.State.R[4]);
        Assert.Equal(0x1111_1111u, memory.ReadUInt32(0x8C02_0008));
        Assert.Equal(0x2222_2222u, memory.ReadUInt32(0x8C02_000C));
    }

    [Fact]
    public void ExecutesSinglePrecisionFloatingPointMultiply()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF012);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fr[0] = BitConverter.SingleToUInt32Bits(3.0f);
        cpu.State.Fr[1] = BitConverter.SingleToUInt32Bits(2.0f);

        cpu.Step();

        Assert.Equal(BitConverter.SingleToUInt32Bits(6.0f), cpu.State.Fr[0]);
    }

    [Fact]
    public void ExecutesDoublePrecisionFloatingPointDivide()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF403);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fpscr = Sh4State.FpscrPrBit;
        WriteDouble(cpu, 0, 2.0);
        WriteDouble(cpu, 4, 8.0);

        cpu.Step();

        Assert.Equal(4.0, ReadDouble(cpu, 4));
    }

    [Fact]
    public void ExecutesFloatingPointAbsoluteValue()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF25D);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fr[2] = 0xBF80_0000;

        cpu.Step();

        Assert.Equal(0x3F80_0000u, cpu.State.Fr[2]);
    }

    [Fact]
    public void ExecutesFloatingPointGreaterThanCompare()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF245);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fr[2] = BitConverter.SingleToUInt32Bits(5.0f);
        cpu.State.Fr[4] = BitConverter.SingleToUInt32Bits(4.0f);

        cpu.Step();

        Assert.True(cpu.State.T);
    }

    [Fact]
    public void ConvertsFpulToDoublePrecisionFloatingPoint()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF42D);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fpscr = Sh4State.FpscrPrBit;
        cpu.State.Fpul = 42;

        cpu.Step();

        Assert.Equal(42.0, ReadDouble(cpu, 4));
    }

    [Fact]
    public void ExecutesLogicalShiftRightSixteen()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x4629);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[6] = 0x89AB_CDEF;

        cpu.Step();

        Assert.Equal(0x0000_89ABu, cpu.State.R[6]);
    }

    [Fact]
    public void ExecutesArithmeticShiftRight()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x4821);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[8] = 0x8000_0001;

        cpu.Step();

        Assert.Equal(0xC000_0000u, cpu.State.R[8]);
        Assert.True(cpu.State.T);
    }

    [Fact]
    public void ExecutesLogicalShiftLeft()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x4700);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[7] = 0x8000_0001;

        cpu.Step();

        Assert.Equal(0x0000_0002u, cpu.State.R[7]);
        Assert.True(cpu.State.T);
    }

    [Fact]
    public void ExecutesSetT()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x0018);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);

        cpu.Step();

        Assert.True(cpu.State.T);
    }

    [Fact]
    public void ExecutesPrefetchAsNoOp()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x0083);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[0] = 0xE080_0000;

        cpu.Step();

        Assert.Equal(0x8C01_0002u, cpu.State.Pc);
    }

    [Fact]
    public void StoresFpscrToPredecrementedRegister()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x4462);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[4] = 0x8C02_0004;
        cpu.State.Fpscr = 0x0004_0001;

        cpu.Step();

        Assert.Equal(0x8C02_0000u, cpu.State.R[4]);
        Assert.Equal(0x0004_0001u, memory.ReadUInt32(0x8C02_0000));
    }

    [Fact]
    public void ExecutesMoveCacheLineAsLongStore()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x03C3);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[0] = 0xCAFE_BABEu;
        cpu.State.R[3] = 0x8C02_0000;

        cpu.Step();

        Assert.Equal(0xCAFE_BABEu, memory.ReadUInt32(0x8C02_0000));
    }

    [Fact]
    public void ExecutesFloatingPointModeToggles()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xFBFD);
        WriteInstruction(memory, 0x8C01_0002, 0xF3FD);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);

        cpu.Step();
        cpu.Step();

        Assert.Equal(Sh4State.FpscrFrBit | Sh4State.FpscrSzBit | 0x0004_0001u, cpu.State.Fpscr);
    }

    [Fact]
    public void StoresControlRegisterToPredecrementedRegister()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x4423);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[4] = 0x8C02_0004;
        cpu.State.Vbr = 0x8C00_0000;

        cpu.Step();

        Assert.Equal(0x8C02_0000u, cpu.State.R[4]);
        Assert.Equal(0x8C00_0000u, memory.ReadUInt32(0x8C02_0000));
    }

    [Fact]
    public void StoresSavedStatusRegisterToPredecrementedRegister()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x4033);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[0] = 0x8C02_0004;
        cpu.State.Ssr = 0x4000_0001;

        cpu.Step();

        Assert.Equal(0x8C02_0000u, cpu.State.R[0]);
        Assert.Equal(0x4000_0001u, memory.ReadUInt32(0x8C02_0000));
    }

    [Fact]
    public void StoresProcedureRegisterToGeneralRegister()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x402A);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Pr = 0x8C04_0000;

        cpu.Step();

        Assert.Equal(0x8C04_0000u, cpu.State.R[0]);
    }

    [Fact]
    public void StoresFloatingPointStatusRegisterToGeneralRegister()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x016A);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fpscr = 0x0004_0001;

        cpu.Step();

        Assert.Equal(0x0004_0001u, cpu.State.R[1]);
    }

    [Fact]
    public void LoadsSavedProgramCounterFromPostIncrementedRegister()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x4147);
        memory.WriteUInt32(0x8C02_0000, 0x8C03_0000);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[1] = 0x8C02_0000;

        cpu.Step();

        Assert.Equal(0x8C03_0000u, cpu.State.Spc);
        Assert.Equal(0x8C02_0004u, cpu.State.R[1]);
    }

    [Fact]
    public void ReturnFromExceptionBranchesAfterDelaySlot()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x002B);
        WriteInstruction(memory, 0x8C01_0002, 0x0009);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Spc = 0x8C02_0000;
        cpu.State.Ssr = 0x4000_00F0;

        cpu.Step();
        Assert.Equal(0x8C01_0002u, cpu.State.Pc);

        cpu.Step();
        Assert.Equal(0x8C02_0000u, cpu.State.Pc);
        Assert.Equal(0x4000_00F0u, cpu.State.Sr);
    }

    [Fact]
    public void TrapInstructionEntersGeneralExceptionHandler()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xC33C);
        memory.WriteUInt32(0xFF00_0028, 0x0320);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Vbr = 0x8C02_0000;
        cpu.State.Sr = 0x0000_00F1;

        var step = cpu.Step();

        Assert.Equal(0x8C01_0000u, step.Pc);
        Assert.Equal(0xC33C, step.Opcode);
        Assert.Equal("trapa #0x3C ; tra=0x000000F0, target=0x8C020100", step.Trace);
        Assert.Equal(0x8C02_0100u, cpu.State.Pc);
        Assert.Equal(0x8C01_0002u, cpu.State.Spc);
        Assert.Equal(0x0000_00F1u, cpu.State.Ssr);
        Assert.Equal(0x0000_00F0u, memory.ReadUInt32(0xFF00_0020));
        Assert.Equal(0x0000_0160u, memory.ReadUInt32(0xFF00_0024));
        Assert.Equal(0x0000_0320u, memory.ReadUInt32(0xFF00_0028));
        Assert.Equal(Sh4State.SrMachineBit | Sh4State.SrRegisterBankBit | Sh4State.SrBlockBit | 0xF1u, cpu.State.Sr);
    }

    [Fact]
    public void ReturnFromTrapExceptionResumesAfterTrapInstruction()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xC305);
        WriteInstruction(memory, 0x8C01_0002, 0x0009);
        WriteInstruction(memory, 0x8C02_0100, 0x002B);
        WriteInstruction(memory, 0x8C02_0102, 0x0009);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Vbr = 0x8C02_0000;
        cpu.State.Sr = 0x0000_00A1;

        cpu.Step();
        Assert.Equal(0x8C02_0100u, cpu.State.Pc);
        Assert.Equal(Sh4State.SrMachineBit | Sh4State.SrRegisterBankBit | Sh4State.SrBlockBit | 0xA1u, cpu.State.Sr);

        cpu.Step();
        Assert.Equal(0x8C02_0102u, cpu.State.Pc);
        Assert.Equal(0x0000_00A1u, cpu.State.Sr);

        cpu.Step();
        Assert.Equal(0x8C01_0002u, cpu.State.Pc);
        Assert.Equal(0x0000_00A1u, cpu.State.Sr);
    }

    [Fact]
    public void DefinedUndefinedInstructionEntersGeneralIllegalInstructionException()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xFFFD);
        memory.WriteUInt32(0xFF00_0020, 0x0000_00A8);
        memory.WriteUInt32(0xFF00_0028, 0x0000_0320);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Vbr = 0x8C02_0000;
        cpu.State.Sr = 0x0000_00B1;

        var step = cpu.Step();

        Assert.Equal(0x8C01_0000u, step.Pc);
        Assert.Equal(0xFFFD, step.Opcode);
        Assert.Equal("general illegal instruction ; expevt=0x00000180, target=0x8C020100", step.Trace);
        Assert.Equal(0x8C02_0100u, cpu.State.Pc);
        Assert.Equal(0x8C01_0000u, cpu.State.Spc);
        Assert.Equal(0x0000_00B1u, cpu.State.Ssr);
        Assert.Equal(0x0000_00A8u, memory.ReadUInt32(0xFF00_0020));
        Assert.Equal(0x0000_0180u, memory.ReadUInt32(0xFF00_0024));
        Assert.Equal(0x0000_0320u, memory.ReadUInt32(0xFF00_0028));
        Assert.Equal(Sh4State.SrMachineBit | Sh4State.SrRegisterBankBit | Sh4State.SrBlockBit | 0xB1u, cpu.State.Sr);
    }

    [Fact]
    public void DefinedUndefinedInstructionInDelaySlotEntersSlotIllegalInstructionException()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xA00E);
        WriteInstruction(memory, 0x8C01_0002, 0xFFFD);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Vbr = 0x8C02_0000;
        cpu.State.Sr = 0x0000_0031;

        cpu.Step();
        var delaySlotStep = cpu.Step();

        Assert.Equal(0x8C01_0002u, delaySlotStep.Pc);
        Assert.Equal(0xFFFD, delaySlotStep.Opcode);
        Assert.Equal("slot illegal instruction ; expevt=0x000001A0, target=0x8C020100", delaySlotStep.Trace);
        Assert.Equal(0x8C02_0100u, cpu.State.Pc);
        Assert.Equal(0x8C01_0000u, cpu.State.Spc);
        Assert.Equal(0x0000_0031u, cpu.State.Ssr);
        Assert.Equal(0x0000_01A0u, memory.ReadUInt32(0xFF00_0024));
        Assert.Equal(Sh4State.SrMachineBit | Sh4State.SrRegisterBankBit | Sh4State.SrBlockBit | 0x31u, cpu.State.Sr);
    }

    [Fact]
    public void AcceptsEnabledExternalInterruptBeforeFetchingInstruction()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x0009);
        memory.WriteUInt32(0xFF00_0020, 0x0000_00F0);
        memory.WriteUInt32(0xFF00_0024, 0x0000_0160);
        RaiseVBlankIrq9(memory);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Vbr = 0x8C02_0000;

        var step = cpu.Step();

        Assert.Equal(0x8C01_0000u, step.Pc);
        Assert.Equal(0, step.Opcode);
        Assert.Equal(0x8C02_0600u, cpu.State.Pc);
        Assert.Equal(0x8C01_0000u, cpu.State.Spc);
        Assert.Equal(0u, cpu.State.Ssr);
        Assert.Equal(0x0000_00F0u, memory.ReadUInt32(0xFF00_0020));
        Assert.Equal(0x0000_0160u, memory.ReadUInt32(0xFF00_0024));
        Assert.Equal(0x0320u, memory.ReadUInt32(0xFF00_0028));
        Assert.Equal(Sh4State.SrMachineBit | Sh4State.SrRegisterBankBit | Sh4State.SrBlockBit | 0x90u, cpu.State.Sr);
        Assert.Equal("interrupt event=0x0320, level=9, target=0x8C020600", step.Trace);
    }

    [Fact]
    public void DoesNotAcceptExternalInterruptWhenBlockBitIsSet()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x0009);
        RaiseVBlankIrq9(memory);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Vbr = 0x8C02_0000;
        cpu.State.Sr = Sh4State.SrBlockBit;

        var step = cpu.Step();

        Assert.Equal(0x8C01_0000u, step.Pc);
        Assert.Equal(0x0009, step.Opcode);
        Assert.Equal("nop", step.Trace);
        Assert.Equal(0x8C01_0002u, cpu.State.Pc);
        Assert.Equal(0u, cpu.State.Spc);
        Assert.Equal(0u, cpu.State.Ssr);
        Assert.Equal(0u, memory.ReadUInt32(0xFF00_0028));
        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0320u, eventCode);
        Assert.Equal(9, level);
    }

    [Fact]
    public void DoesNotAcceptExternalInterruptAtOrBelowInterruptMask()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x0009);
        RaiseVBlankIrq9(memory);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Vbr = 0x8C02_0000;
        cpu.State.Sr = 9u << 4;

        var step = cpu.Step();

        Assert.Equal(0x0009, step.Opcode);
        Assert.Equal("nop", step.Trace);
        Assert.Equal(0x8C01_0002u, cpu.State.Pc);
        Assert.Equal(0u, memory.ReadUInt32(0xFF00_0028));
        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0320u, eventCode);
        Assert.Equal(9, level);
    }

    [Fact]
    public void DefersExternalInterruptUntilAfterBranchDelaySlot()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x402B);
        WriteInstruction(memory, 0x8C01_0002, 0x0009);
        WriteInstruction(memory, 0x8C01_0020, 0x0009);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[0] = 0x8C01_0020;
        cpu.State.Vbr = 0x8C02_0000;

        cpu.Step();
        RaiseVBlankIrq9(memory);
        var delaySlotStep = cpu.Step();

        Assert.Equal(0x8C01_0002u, delaySlotStep.Pc);
        Assert.Equal(0x0009, delaySlotStep.Opcode);
        Assert.Equal("nop", delaySlotStep.Trace);
        Assert.Equal(0x8C01_0020u, cpu.State.Pc);
        Assert.Equal(0u, memory.ReadUInt32(0xFF00_0028));

        var interruptStep = cpu.Step();

        Assert.Equal(0x8C01_0020u, interruptStep.Pc);
        Assert.Equal(0, interruptStep.Opcode);
        Assert.Equal(0x8C02_0600u, cpu.State.Pc);
        Assert.Equal(0x8C01_0020u, cpu.State.Spc);
        Assert.Equal("interrupt event=0x0320, level=9, target=0x8C020600", interruptStep.Trace);
    }

    [Fact]
    public void ReturnFromExceptionDefersRestoredInterruptUntilAfterDelaySlot()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x002B);
        WriteInstruction(memory, 0x8C01_0002, 0x0009);
        WriteInstruction(memory, 0x8C01_0020, 0x0009);
        RaiseVBlankIrq9(memory);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Spc = 0x8C01_0020;
        cpu.State.Ssr = 0;
        cpu.State.Sr = Sh4State.SrBlockBit;
        cpu.State.Vbr = 0x8C02_0000;

        cpu.Step();
        Assert.Equal(0x8C01_0002u, cpu.State.Pc);
        Assert.Equal(0u, cpu.State.Sr);

        var delaySlotStep = cpu.Step();

        Assert.Equal(0x8C01_0002u, delaySlotStep.Pc);
        Assert.Equal(0x0009, delaySlotStep.Opcode);
        Assert.Equal(0x8C01_0020u, cpu.State.Pc);
        Assert.Equal(0u, memory.ReadUInt32(0xFF00_0028));

        var interruptStep = cpu.Step();

        Assert.Equal(0x8C01_0020u, interruptStep.Pc);
        Assert.Equal(0, interruptStep.Opcode);
        Assert.Equal(0x8C02_0600u, cpu.State.Pc);
        Assert.Equal(0x8C01_0020u, cpu.State.Spc);
    }

    [Fact]
    public void ChangingStatusRegisterBankBitSwapsLowRegisters()
    {
        var state = new Sh4State();
        state.R[4] = 0x1111_1111;
        state.RBank[4] = 0x2222_2222;

        state.Sr = Sh4State.SrRegisterBankBit;
        Assert.Equal(0x2222_2222u, state.R[4]);
        Assert.Equal(0x1111_1111u, state.RBank[4]);

        state.Sr = 0;
        Assert.Equal(0x1111_1111u, state.R[4]);
        Assert.Equal(0x2222_2222u, state.RBank[4]);
    }

    [Fact]
    public void StoresBankedRegisterToPredecrementedRegister()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x40F3);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[0] = 0x8C02_0004;
        cpu.State.RBank[7] = 0x4000_0001;

        cpu.Step();

        Assert.Equal(0x8C02_0000u, cpu.State.R[0]);
        Assert.Equal(0x4000_0001u, memory.ReadUInt32(0x8C02_0000));
    }

    [Fact]
    public void TrapHandlerCanReturnWithoutFetchingInstruction()
    {
        var memory = new DreamcastMemory();
        var cpu = new Sh4Cpu(memory, 0x8C00_00D0, (Sh4State state, DreamcastMemory _, out string trace) =>
        {
            state.R[0] = 0x1234;
            state.Pc = state.Pr;
            trace = "test trap";
            return true;
        });
        cpu.State.Pr = 0x8C01_0000;

        var step = cpu.Step();

        Assert.Equal(0x8C00_00D0u, step.Pc);
        Assert.Equal(0, step.Opcode);
        Assert.Equal("test trap", step.Trace);
        Assert.Equal(0x1234u, cpu.State.R[0]);
        Assert.Equal(0x8C01_0000u, cpu.State.Pc);
        Assert.Equal(1u, cpu.State.InstructionsExecuted);
    }

    private static void WriteInstruction(DreamcastMemory memory, uint address, ushort opcode)
    {
        memory.Write(address, [(byte)opcode, (byte)(opcode >> 8)]);
    }

    private static void RaiseVBlankIrq9(DreamcastMemory memory)
    {
        memory.WriteUInt32(0xA05F_6930, 1u << 3);
        memory.RaiseVBlankBegin();
    }

    private static double ReadDouble(Sh4Cpu cpu, int register)
    {
        var bits = ((ulong)cpu.State.Fr[register] << 32) | cpu.State.Fr[register + 1];
        return BitConverter.UInt64BitsToDouble(bits);
    }

    private static void WriteDouble(Sh4Cpu cpu, int register, double value)
    {
        var bits = BitConverter.DoubleToUInt64Bits(value);
        cpu.State.Fr[register] = (uint)(bits >> 32);
        cpu.State.Fr[register + 1] = (uint)bits;
    }
}
