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
    public void AcceptsEnabledExternalInterruptBeforeFetchingInstruction()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x0009);
        memory.WriteUInt32(0xA05F_6930, 1u << 3);
        memory.RaiseVBlankBegin();
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Vbr = 0x8C02_0000;

        var step = cpu.Step();

        Assert.Equal(0x8C01_0000u, step.Pc);
        Assert.Equal(0, step.Opcode);
        Assert.Equal(0x8C02_0600u, cpu.State.Pc);
        Assert.Equal(0x8C01_0000u, cpu.State.Spc);
        Assert.Equal(0u, cpu.State.Ssr);
        Assert.Equal(0x0320u, memory.ReadUInt32(0xFF00_0028));
        Assert.Equal(Sh4State.SrMachineBit | Sh4State.SrRegisterBankBit | Sh4State.SrBlockBit | 0x90u, cpu.State.Sr);
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
