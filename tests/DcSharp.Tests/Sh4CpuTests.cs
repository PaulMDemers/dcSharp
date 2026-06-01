using DcSharp.Core.Cpu;
using DcSharp.Core.Dreamcast.Memory;
using DcSharp.Core.Execution;

namespace DcSharp.Tests;

public class Sh4CpuTests
{
    [Fact]
    public void DecodesFloatingPointStatusRegisterFields()
    {
        var summary = Sh4FpscrSummary.FromValue(0x0004_5015);

        Assert.Equal("0x00045015", summary.ValueHex);
        Assert.Equal("zero", summary.RoundingMode);
        Assert.Equal("O/I", summary.Flags);
        Assert.Equal("none", summary.Enables);
        Assert.Equal("O/I", summary.Causes);
        Assert.Equal("DN", summary.Controls);
        Assert.True(summary.DenormalAsZero);
        Assert.False(summary.DoublePrecision);
        Assert.False(summary.DoubleTransferSize);
        Assert.False(summary.RegisterBank);
        Assert.Equal("rm=zero, flags=O/I, enables=none, causes=O/I, controls=DN", summary.Display);
    }

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
    public void FastForwardsMaskedCountedMemoryClearLoop()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x4110); // dt r1
        WriteInstruction(memory, 0x8C01_0002, 0x8FFD); // bf/s 0x8C010000
        WriteInstruction(memory, 0x8C01_0004, 0x2466); // mov.l r6,@-r4
        WriteInstruction(memory, 0x8C01_0006, 0x0009); // fallthrough
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Sr = 0xF0;
        cpu.State.R[1] = 3;
        cpu.State.R[4] = 0x8C02_000C;
        cpu.State.R[6] = 0xAABB_CCDD;

        cpu.Step();
        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardCountedIdleLoop(branch, 100, out var skippedInstructions));
        Assert.Equal(7UL, skippedInstructions);
        Assert.Equal(0u, cpu.State.R[1]);
        Assert.Equal(0x8C02_0000u, cpu.State.R[4]);
        Assert.True(cpu.State.T);
        Assert.Equal(0x8C01_0006u, cpu.State.Pc);
        Assert.Equal(9UL, cpu.State.InstructionsExecuted);
        Assert.Equal(0xAABB_CCDDu, memory.ReadUInt32(0x8C02_0000));
        Assert.Equal(0xAABB_CCDDu, memory.ReadUInt32(0x8C02_0004));
        Assert.Equal(0xAABB_CCDDu, memory.ReadUInt32(0x8C02_0008));
    }

    [Fact]
    public void FastForwardsImmediateDtLoop()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x4410); // dt r4
        WriteInstruction(memory, 0x8C01_0002, 0x8BFD); // bf 0x8C010000
        WriteInstruction(memory, 0x8C01_0004, 0x0009); // fallthrough
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[4] = 4;

        cpu.Step();
        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardImmediateDtLoop(branch, 100, out var skippedInstructions));
        Assert.Equal(6UL, skippedInstructions);
        Assert.Equal(0u, cpu.State.R[4]);
        Assert.True(cpu.State.T);
        Assert.Equal(0x8C01_0004u, cpu.State.Pc);
        Assert.Equal(8UL, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void FastForwardsIpBinPatternFillLoop()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C00_8F4E, 0x8BC9); // bf 0x8C008EE4
        WriteInstruction(memory, 0x8C00_8F50, 0x0009);
        var cpu = new Sh4Cpu(memory, 0x8C00_8F4E);
        cpu.State.Sr = 0xF0;
        cpu.State.R[15] = 0x7E00_0F70;
        memory.WriteUInt16(0x7E00_0F72, 1);
        memory.WriteUInt16(0x7E00_0F94, 4);
        memory.WriteUInt32(0x7E00_0F7C, 2);
        memory.WriteUInt32(0x7E00_0F74, 5);
        memory.WriteUInt32(0x7E00_0F80, 9);
        memory.WriteUInt32(0x7E00_0F9C, 0x0000_0020);

        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardIpBinPatternFillLoop(branch, 1_000, out var skippedInstructions));
        Assert.Equal(10UL * 59, skippedInstructions);
        Assert.Equal(3, memory.ReadUInt16(0x7E00_0F72));
        Assert.Equal(4u, memory.ReadUInt32(0x7E00_0F7C));
        Assert.Equal(19u, memory.ReadUInt32(0x7E00_0F80));
        Assert.Equal(0x8C00_8F50u, cpu.State.Pc);
        Assert.Equal(1UL + skippedInstructions, cpu.State.InstructionsExecuted);
        Assert.False(cpu.State.T);
    }

    [Fact]
    public void FastForwardsIpBinFramebufferCopyLoop()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C00_834E, 0x8BFB); // bf 0x8C008348
        WriteInstruction(memory, 0x8C00_8350, 0x0009);
        var cpu = new Sh4Cpu(memory, 0x8C00_834E);
        cpu.State.R[1] = 0xA500_0010;
        cpu.State.R[4] = 0x8C10_0000;
        cpu.State.R[7] = 3;

        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardIpBinFramebufferCopyLoop(branch, 100, out var skippedInstructions));
        Assert.Equal(12UL, skippedInstructions);
        Assert.Equal(0xA500_0004u, cpu.State.R[1]);
        Assert.Equal(0x8C10_000Cu, cpu.State.R[4]);
        Assert.Equal(0u, cpu.State.R[7]);
        Assert.True(cpu.State.T);
        Assert.Equal(0x8C00_8350u, cpu.State.Pc);
        Assert.Equal(1UL + skippedInstructions, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void PartiallyFastForwardsIpBinFramebufferCopyLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C00_834E, 0x8BFB);
        var cpu = new Sh4Cpu(memory, 0x8C00_834E);
        cpu.State.R[1] = 0xA500_0010;
        cpu.State.R[4] = 0x8C10_0000;
        cpu.State.R[7] = 5;

        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardIpBinFramebufferCopyLoop(branch, 8, out var skippedInstructions));
        Assert.Equal(8UL, skippedInstructions);
        Assert.Equal(0xA500_0008u, cpu.State.R[1]);
        Assert.Equal(0x8C10_0008u, cpu.State.R[4]);
        Assert.Equal(3u, cpu.State.R[7]);
        Assert.False(cpu.State.T);
        Assert.Equal(0x8C00_8348u, cpu.State.Pc);
        Assert.Equal(1UL + skippedInstructions, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void FastForwardsIpBinShortDelayLoop()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C00_84FC, 0x8BF8); // bf 0x8C0084F0
        WriteInstruction(memory, 0x8C00_84FE, 0x0009);
        memory.WriteUInt16(0x8C00_8530, 0x2710);
        var cpu = new Sh4Cpu(memory, 0x8C00_84FC);
        cpu.State.R[15] = 0x7E00_0FD0;
        memory.WriteUInt32(0x7E00_0FD8, 0x270E);

        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardIpBinShortDelayLoop(branch, 100, out var skippedInstructions));
        Assert.Equal(14UL, skippedInstructions);
        Assert.Equal(0x2710u, memory.ReadUInt32(0x7E00_0FD8));
        Assert.Equal(0x2710u, cpu.State.R[1]);
        Assert.Equal(0x2710u, cpu.State.R[2]);
        Assert.Equal(0x2710u, cpu.State.R[3]);
        Assert.True(cpu.State.T);
        Assert.Equal(0x8C00_84FEu, cpu.State.Pc);
        Assert.Equal(1UL + skippedInstructions, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void FastForwardsDoa2VramClearLoop()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C12_ED9C, 0x8FF8); // bf/s 0x8C12ED90
        WriteInstruction(memory, 0x8C12_ED9E, 0x7E04); // add #4,r14
        memory.WriteUInt32(0x8CFF_FF80, 0xA5A5_5A5A);
        var cpu = new Sh4Cpu(memory, 0x8C12_ED9C);
        cpu.State.R[11] = 0x8C12_9E20;
        cpu.State.R[12] = 5;
        cpu.State.R[13] = 2;
        cpu.State.R[14] = 0xA500_0000;
        cpu.State.R[15] = 0x8CFF_FF80;
        cpu.State.T = false;

        cpu.Step();
        var delaySlot = cpu.Step();

        Assert.True(cpu.TryFastForwardDoa2VramClearLoop(delaySlot, 100, out var skippedInstructions));
        Assert.Equal(69UL, skippedInstructions);
        Assert.Equal(0xA5A5_5A5Au, memory.ReadUInt32(0xA500_0004));
        Assert.Equal(0xA5A5_5A5Au, memory.ReadUInt32(0xA500_0008));
        Assert.Equal(0xA5A5_5A5Au, memory.ReadUInt32(0xA500_000C));
        Assert.Equal(5u, cpu.State.R[13]);
        Assert.Equal(0xA500_0010u, cpu.State.R[14]);
        Assert.Equal(0xA500_0010u, cpu.State.R[4]);
        Assert.Equal(0x8CFF_FF84u, cpu.State.R[5]);
        Assert.Equal(0u, cpu.State.R[6]);
        Assert.Equal(0x8C12_EDA0u, cpu.State.Pc);
        Assert.Equal(71UL, cpu.State.InstructionsExecuted);
        Assert.True(cpu.State.T);
    }

    [Fact]
    public void PartiallyFastForwardsDoa2VramClearLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C12_ED9C, 0x8FF8);
        WriteInstruction(memory, 0x8C12_ED9E, 0x7E04);
        memory.WriteUInt32(0x8CFF_FF80, 0x1122_3344);
        var cpu = new Sh4Cpu(memory, 0x8C12_ED9C);
        cpu.State.R[11] = 0x8C12_9E20;
        cpu.State.R[12] = 6;
        cpu.State.R[13] = 1;
        cpu.State.R[14] = 0xA500_0000;
        cpu.State.R[15] = 0x8CFF_FF80;
        cpu.State.T = false;

        cpu.Step();
        var delaySlot = cpu.Step();

        Assert.True(cpu.TryFastForwardDoa2VramClearLoop(delaySlot, 46, out var skippedInstructions));
        Assert.Equal(46UL, skippedInstructions);
        Assert.Equal(0x1122_3344u, memory.ReadUInt32(0xA500_0004));
        Assert.Equal(0x1122_3344u, memory.ReadUInt32(0xA500_0008));
        Assert.Equal(3u, cpu.State.R[13]);
        Assert.Equal(0xA500_000Cu, cpu.State.R[14]);
        Assert.Equal(0xA500_000Cu, cpu.State.R[4]);
        Assert.False(cpu.State.T);
        Assert.Equal(0x8C12_ED90u, cpu.State.Pc);
        Assert.Equal(48UL, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void FastForwardsDoa2SystemRamClearLoop()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C11_3312, 0x2542); // mov.l r4,@r5
        WriteInstruction(memory, 0x8C11_3314, 0x7504); // add #4,r5
        WriteInstruction(memory, 0x8C11_3316, 0x6362); // mov.l @r6,r3
        WriteInstruction(memory, 0x8C11_3318, 0x3532); // cmp/hs r3,r5
        WriteInstruction(memory, 0x8C11_331A, 0x8BFA); // bf 0x8C113312
        WriteInstruction(memory, 0x8C11_331C, 0x0009);
        memory.WriteUInt32(0x8C14_8850, 0x8C20_0010);
        var cpu = new Sh4Cpu(memory, 0x8C11_331A);
        cpu.State.R[3] = 0x8C20_0010;
        cpu.State.R[4] = 0;
        cpu.State.R[5] = 0x8C20_0004;
        cpu.State.R[6] = 0x8C14_8850;
        cpu.State.T = false;

        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardDoa2SystemRamClearLoop(branch, 100, out var skippedInstructions));
        Assert.Equal(15UL, skippedInstructions);
        Assert.Equal(0u, memory.ReadUInt32(0x8C20_0004));
        Assert.Equal(0u, memory.ReadUInt32(0x8C20_0008));
        Assert.Equal(0u, memory.ReadUInt32(0x8C20_000C));
        Assert.Equal(0x8C20_0010u, cpu.State.R[5]);
        Assert.Equal(0x8C20_0010u, cpu.State.R[3]);
        Assert.True(cpu.State.T);
        Assert.Equal(0x8C11_331Cu, cpu.State.Pc);
        Assert.Equal(1UL + skippedInstructions, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void PartiallyFastForwardsDoa2SystemRamClearLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C11_3312, 0x2542);
        WriteInstruction(memory, 0x8C11_3314, 0x7504);
        WriteInstruction(memory, 0x8C11_3316, 0x6362);
        WriteInstruction(memory, 0x8C11_3318, 0x3532);
        WriteInstruction(memory, 0x8C11_331A, 0x8BFA);
        memory.WriteUInt32(0x8C14_8850, 0x8C20_0018);
        var cpu = new Sh4Cpu(memory, 0x8C11_331A);
        cpu.State.R[3] = 0x8C20_0018;
        cpu.State.R[4] = 0xAABB_CCDD;
        cpu.State.R[5] = 0x8C20_0004;
        cpu.State.R[6] = 0x8C14_8850;
        cpu.State.T = false;

        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardDoa2SystemRamClearLoop(branch, 10, out var skippedInstructions));
        Assert.Equal(10UL, skippedInstructions);
        Assert.Equal(0xAABB_CCDDu, memory.ReadUInt32(0x8C20_0004));
        Assert.Equal(0xAABB_CCDDu, memory.ReadUInt32(0x8C20_0008));
        Assert.Equal(0x8C20_000Cu, cpu.State.R[5]);
        Assert.False(cpu.State.T);
        Assert.Equal(0x8C11_3312u, cpu.State.Pc);
        Assert.Equal(1UL + skippedInstructions, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void FastForwardsDoa2InitDelayLoop()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C11_41FE, 0x4410); // dt r4
        WriteInstruction(memory, 0x8C11_4200, 0x8BFD); // bf 0x8C1141FE
        WriteInstruction(memory, 0x8C11_4254, 0x65F2); // mov.l @r15,r5
        WriteInstruction(memory, 0x8C11_4256, 0x56F1); // mov.l @(0x1,r15),r6
        WriteInstruction(memory, 0x8C11_4272, 0x7C01); // add #1,r12
        WriteInstruction(memory, 0x8C11_4274, 0x3CE3); // cmp/ge r14,r12
        WriteInstruction(memory, 0x8C11_4276, 0x8BED); // bf 0x8C114254
        WriteInstruction(memory, 0x8C11_4278, 0x0009);
        memory.WriteUInt32(0x8C1C_AF88, 2);
        memory.WriteUInt32(0x8C1C_AF8C, 1);
        var cpu = new Sh4Cpu(memory, 0x8C11_4276);
        cpu.State.Pr = 0x8C11_4272;
        cpu.State.R[11] = 0x8C11_F518;
        cpu.State.R[12] = 0x0000_0138;
        cpu.State.R[13] = 0x8C11_6F94;
        cpu.State.R[14] = 0x0000_2710;
        cpu.State.R[15] = 0x8CFF_FF28;
        cpu.State.T = false;

        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardDoa2InitDelayLoop(branch, 4_000_000_000, out var skippedInstructions));
        Assert.Equal(968_800_000UL, skippedInstructions);
        Assert.Equal(0u, cpu.State.R[4]);
        Assert.Equal(0x2710u, cpu.State.R[12]);
        Assert.Equal(0u, memory.ReadUInt32(0x8C1C_AF88));
        Assert.Equal(0u, memory.ReadUInt32(0x8C1C_AF8C));
        Assert.True(cpu.State.T);
        Assert.Equal(0x8C11_4278u, cpu.State.Pc);
        Assert.Equal(1UL + skippedInstructions, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void PartiallyFastForwardsDoa2InitDelayLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C11_41FE, 0x4410);
        WriteInstruction(memory, 0x8C11_4200, 0x8BFD);
        WriteInstruction(memory, 0x8C11_4254, 0x65F2);
        WriteInstruction(memory, 0x8C11_4256, 0x56F1);
        WriteInstruction(memory, 0x8C11_4272, 0x7C01);
        WriteInstruction(memory, 0x8C11_4274, 0x3CE3);
        WriteInstruction(memory, 0x8C11_4276, 0x8BED);
        var cpu = new Sh4Cpu(memory, 0x8C11_4276);
        cpu.State.Pr = 0x8C11_4272;
        cpu.State.R[11] = 0x8C11_F518;
        cpu.State.R[12] = 0x0000_0138;
        cpu.State.R[13] = 0x8C11_6F94;
        cpu.State.R[14] = 0x0000_2710;
        cpu.State.R[15] = 0x8CFF_FF28;
        cpu.State.T = false;

        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardDoa2InitDelayLoop(branch, 200_000, out var skippedInstructions));
        Assert.Equal(200_000UL, skippedInstructions);
        Assert.Equal(0u, cpu.State.R[4]);
        Assert.Equal(0x013Au, cpu.State.R[12]);
        Assert.False(cpu.State.T);
        Assert.Equal(0x8C11_4254u, cpu.State.Pc);
        Assert.Equal(1UL + skippedInstructions, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void FastForwardsDoa2StringScanLoop()
    {
        var memory = new DreamcastMemory();
        WriteDoa2StringScanLoop(memory);
        memory.Write(0x8C20_0000, [(byte)'A', (byte)'D', (byte)'X', (byte)'%', (byte)'S']);
        var cpu = new Sh4Cpu(memory, 0x8C10_EDBC);
        cpu.State.R[0] = 'A';
        cpu.State.R[4] = 'A';
        cpu.State.R[15] = 0x8CFF_FDD4;
        cpu.State.T = false;
        memory.WriteUInt32(cpu.State.R[15], 0x8C20_0000);

        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardDoa2StringScanLoop(branch, 100, out var skippedInstructions));
        Assert.Equal(30UL, skippedInstructions);
        Assert.Equal(0x8C20_0003u, memory.ReadUInt32(cpu.State.R[15]));
        Assert.Equal(0x8C20_0003u, cpu.State.R[2]);
        Assert.Equal(0x25u, cpu.State.R[4]);
        Assert.Equal(0x25u, cpu.State.R[0]);
        Assert.True(cpu.State.T);
        Assert.Equal(0x8C10_EDBEu, cpu.State.Pc);
        Assert.Equal(1UL + skippedInstructions, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void FastForwardsDoa2StringScanLoopToNullTerminator()
    {
        var memory = new DreamcastMemory();
        WriteDoa2StringScanLoop(memory);
        memory.Write(0x8C20_0000, [(byte)'A', (byte)'D', (byte)'X', 0, (byte)'S']);
        var cpu = new Sh4Cpu(memory, 0x8C10_EDBC);
        cpu.State.R[0] = 'A';
        cpu.State.R[4] = 'A';
        cpu.State.R[15] = 0x8CFF_FDD4;
        cpu.State.T = false;
        memory.WriteUInt32(cpu.State.R[15], 0x8C20_0000);

        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardDoa2StringScanLoop(branch, 100, out var skippedInstructions));
        Assert.Equal(27UL, skippedInstructions);
        Assert.Equal(0x8C20_0003u, memory.ReadUInt32(cpu.State.R[15]));
        Assert.Equal(0x8C20_0003u, cpu.State.R[2]);
        Assert.Equal(0u, cpu.State.R[4]);
        Assert.Equal((uint)'A', cpu.State.R[0]);
        Assert.True(cpu.State.T);
        Assert.Equal(0x8C10_EDBEu, cpu.State.Pc);
        Assert.Equal(1UL + skippedInstructions, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void FastForwardsDoa2CallbackTimeoutLoop()
    {
        var memory = new DreamcastMemory();
        WriteDoa2CallbackTimeoutLoop(memory);
        WriteInstruction(memory, 0x8C12_BE60, 0x000B);
        WriteInstruction(memory, 0x8C12_BE62, 0x0009);
        memory.WriteUInt32(0x8C30_C778, 0x8C12_BE60);
        memory.WriteUInt32(0x8C30_C77C, 0);
        memory.WriteUInt32(0x8CFF_FF80, 0x8CFF_FFBC);
        memory.WriteUInt32(0x8CFF_FF84, 0x8C2F_67C0);
        memory.WriteUInt32(0x8CFF_FFBC, 3);
        memory.WriteUInt32(0x8C2F_67C0, 0);
        var cpu = new Sh4Cpu(memory, 0x8C12_F9B4);
        cpu.State.R[13] = 0x8C12_D2C0;
        cpu.State.R[14] = 0;
        cpu.State.R[15] = 0x8CFF_FF80;
        cpu.State.T = false;

        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardDoa2CallbackTimeoutLoop(branch, 100, out var skippedInstructions));
        Assert.Equal(96UL, skippedInstructions);
        Assert.Equal(0u, memory.ReadUInt32(0x8CFF_FFBC));
        Assert.Equal(0u, cpu.State.R[0]);
        Assert.Equal(0x8CFF_FFBCu, cpu.State.R[1]);
        Assert.Equal(0u, cpu.State.R[2]);
        Assert.Equal(0u, cpu.State.R[3]);
        Assert.Equal(7u, cpu.State.R[4]);
        Assert.True(cpu.State.T);
        Assert.Equal(0x8C12_F9B6u, cpu.State.Pc);
        Assert.Equal(97UL, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void PartiallyFastForwardsDoa2CallbackTimeoutLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2CallbackTimeoutLoop(memory);
        WriteInstruction(memory, 0x8C12_BE60, 0x000B);
        WriteInstruction(memory, 0x8C12_BE62, 0x0009);
        memory.WriteUInt32(0x8C30_C778, 0x8C12_BE60);
        memory.WriteUInt32(0x8C30_C77C, 0);
        memory.WriteUInt32(0x8CFF_FF80, 0x8CFF_FFBC);
        memory.WriteUInt32(0x8CFF_FF84, 0x8C2F_67C0);
        memory.WriteUInt32(0x8CFF_FFBC, 5);
        memory.WriteUInt32(0x8C2F_67C0, 0);
        var cpu = new Sh4Cpu(memory, 0x8C12_F9B4);
        cpu.State.R[13] = 0x8C12_D2C0;
        cpu.State.R[14] = 0;
        cpu.State.R[15] = 0x8CFF_FF80;
        cpu.State.T = false;

        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardDoa2CallbackTimeoutLoop(branch, 64, out var skippedInstructions));
        Assert.Equal(64UL, skippedInstructions);
        Assert.Equal(3u, memory.ReadUInt32(0x8CFF_FFBC));
        Assert.Equal(3u, cpu.State.R[2]);
        Assert.Equal(3u, cpu.State.R[3]);
        Assert.False(cpu.State.T);
        Assert.Equal(0x8C12_F99Au, cpu.State.Pc);
        Assert.Equal(65UL, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void FastForwardsDoa2BusyBitWaitLoop()
    {
        var memory = new DreamcastMemory();
        WriteDoa2BusyBitWaitLoop(memory);
        WriteInstruction(memory, 0x8C12_BE60, 0x000B);
        WriteInstruction(memory, 0x8C12_BE62, 0x0009);
        memory.WriteUInt32(0x8C30_C778, 0x8C12_BE60);
        memory.WriteUInt32(0x8C30_C77C, 0);
        memory.WriteUInt32(0x8C2F_67F4, 0);
        memory.WriteUInt32(0x8C2F_67FC, 1);
        memory.WriteUInt32(0x8C2F_6808, 0);
        memory.WriteUInt32(0x8C2F_680C, 0);
        memory.WriteUInt32(0x8C2F_6820, 1);
        memory.WriteUInt32(0x8C2F_6834, 2);
        memory.WriteUInt32(0x8C2F_6838, 8);
        memory.WriteUInt32(0x8C2F_766C, 0x8C2F_6820);
        memory.WriteUInt32(0x8C2F_76A4, 0);
        var cpu = new Sh4Cpu(memory, 0x8C13_048E);
        cpu.State.R[10] = 0x8C12_D2C0;
        cpu.State.R[12] = 1;
        cpu.State.R[13] = 0;
        cpu.State.T = false;

        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardDoa2BusyBitWaitLoop(branch, 100, out var skippedInstructions));
        Assert.Equal(30UL, skippedInstructions);
        Assert.Equal(0u, memory.ReadUInt32(0x8C2F_67FC));
        Assert.Equal(0u, memory.ReadUInt32(0x8C2F_6820));
        Assert.Equal(0x8C2F_67FCu, cpu.State.R[1]);
        Assert.Equal(0u, cpu.State.R[3]);
        Assert.Equal(0u, cpu.State.R[4]);
        Assert.True(cpu.State.T);
        Assert.Equal(0x8C13_0490u, cpu.State.Pc);
        Assert.Equal(31UL, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void FastForwardsDoa2BusyBitWaitLoopForSecondWorkItem()
    {
        var memory = new DreamcastMemory();
        WriteDoa2BusyBitWaitLoop(memory);
        WriteInstruction(memory, 0x8C12_BE60, 0x000B);
        WriteInstruction(memory, 0x8C12_BE62, 0x0009);
        memory.WriteUInt32(0x8C30_C778, 0x8C12_BE60);
        memory.WriteUInt32(0x8C30_C77C, 0);
        memory.WriteUInt32(0x8C2F_67F4, 0);
        memory.WriteUInt32(0x8C2F_67FC, 1);
        memory.WriteUInt32(0x8C2F_6808, 0);
        memory.WriteUInt32(0x8C2F_680C, 0);
        memory.WriteUInt32(0x8C2F_69E4, 1);
        memory.WriteUInt32(0x8C2F_69F8, 2);
        memory.WriteUInt32(0x8C2F_69FC, 8);
        memory.WriteUInt32(0x8C2F_766C, 0x8C2F_69E4);
        memory.WriteUInt32(0x8C2F_76A4, 0);
        var cpu = new Sh4Cpu(memory, 0x8C13_048E);
        cpu.State.R[10] = 0x8C12_D2C0;
        cpu.State.R[12] = 1;
        cpu.State.R[13] = 0;
        cpu.State.T = false;

        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardDoa2BusyBitWaitLoop(branch, 100, out var skippedInstructions));
        Assert.Equal(30UL, skippedInstructions);
        Assert.Equal(0u, memory.ReadUInt32(0x8C2F_67FC));
        Assert.Equal(0u, memory.ReadUInt32(0x8C2F_69E4));
        Assert.Equal(0x8C2F_67FCu, cpu.State.R[1]);
        Assert.Equal(0u, cpu.State.R[3]);
        Assert.Equal(0u, cpu.State.R[4]);
        Assert.True(cpu.State.T);
        Assert.Equal(0x8C13_0490u, cpu.State.Pc);
        Assert.Equal(31UL, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void FastForwardsDoa2BusyBitWaitLoopForLaterWorkItem()
    {
        var memory = new DreamcastMemory();
        WriteDoa2BusyBitWaitLoop(memory);
        WriteInstruction(memory, 0x8C12_BE60, 0x000B);
        WriteInstruction(memory, 0x8C12_BE62, 0x0009);
        memory.WriteUInt32(0x8C30_C778, 0x8C12_BE60);
        memory.WriteUInt32(0x8C30_C77C, 0);
        memory.WriteUInt32(0x8C2F_67F4, 0);
        memory.WriteUInt32(0x8C2F_67FC, 1);
        memory.WriteUInt32(0x8C2F_6808, 0);
        memory.WriteUInt32(0x8C2F_680C, 0);
        memory.WriteUInt32(0x8C2F_6D6C, 1);
        memory.WriteUInt32(0x8C2F_6D80, 2);
        memory.WriteUInt32(0x8C2F_6D84, 8);
        memory.WriteUInt32(0x8C2F_766C, 0x8C2F_6D6C);
        memory.WriteUInt32(0x8C2F_76A4, 0);
        var cpu = new Sh4Cpu(memory, 0x8C13_048E);
        cpu.State.R[10] = 0x8C12_D2C0;
        cpu.State.R[12] = 1;
        cpu.State.R[13] = 0;
        cpu.State.T = false;

        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardDoa2BusyBitWaitLoop(branch, 100, out var skippedInstructions));
        Assert.Equal(30UL, skippedInstructions);
        Assert.Equal(0u, memory.ReadUInt32(0x8C2F_67FC));
        Assert.Equal(0u, memory.ReadUInt32(0x8C2F_6D6C));
        Assert.Equal(0x8C13_0490u, cpu.State.Pc);
        Assert.Equal(31UL, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void CompletesDoa2Slot8StubTaskCallback()
    {
        var memory = new DreamcastMemory();
        WriteDoa2Slot8TaskCallback(memory);
        WriteInstruction(memory, 0x8C0F_9F00, 0x000B);
        WriteInstruction(memory, 0x8C0F_9F02, 0x0009);
        memory.WriteUInt32(0x8C30_C780, 0x8C0F_9F00);
        memory.WriteUInt32(0x8C30_C784, 0x8C2F_67DC);
        memory.WriteUInt32(0x8C2B_6CE8, 0x0000_01F8);
        memory.WriteUInt32(0x8C2B_6CEC, 0x0000_0100);
        memory.WriteUInt32(0x8C2F_67D4, 1);
        memory.WriteUInt32(0x8C2F_67D8, 0);
        memory.WriteUInt32(0x8C2F_67DC, 0);
        var cpu = new Sh4Cpu(memory, 0x8C13_0728);
        cpu.State.R[0] = 0x67DC;
        cpu.State.R[3] = 2;
        cpu.State.R[12] = 0x8C2F_0000;

        var store = cpu.Step();

        Assert.True(cpu.TryCompleteDoa2Slot8StubTaskCallback(store));
        Assert.Equal(0x0000_0120u, memory.ReadUInt32(0x8C2B_6CEC));
        Assert.Equal(0u, memory.ReadUInt32(0x8C2F_67D8));
        Assert.Equal(2u, memory.ReadUInt32(0x8C2F_67DC));
        Assert.Equal(0x8C13_072Au, cpu.State.Pc);
        Assert.Equal(1UL, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void DoesNotCompleteDoa2Slot8CallbackWhenHandlerIsNotStub()
    {
        var memory = new DreamcastMemory();
        WriteDoa2Slot8TaskCallback(memory);
        WriteInstruction(memory, 0x8C0F_9F00, 0x4F22);
        WriteInstruction(memory, 0x8C0F_9F02, 0x0009);
        memory.WriteUInt32(0x8C30_C780, 0x8C0F_9F00);
        memory.WriteUInt32(0x8C30_C784, 0x8C2F_67DC);
        memory.WriteUInt32(0x8C2B_6CE8, 0x0000_01F8);
        memory.WriteUInt32(0x8C2B_6CEC, 0x0000_0100);
        memory.WriteUInt32(0x8C2F_67D4, 1);
        memory.WriteUInt32(0x8C2F_67D8, 0);
        memory.WriteUInt32(0x8C2F_67DC, 0);
        var cpu = new Sh4Cpu(memory, 0x8C13_0728);
        cpu.State.R[0] = 0x67DC;
        cpu.State.R[3] = 2;
        cpu.State.R[12] = 0x8C2F_0000;

        var store = cpu.Step();

        Assert.False(cpu.TryCompleteDoa2Slot8StubTaskCallback(store));
        Assert.Equal(0x0000_0100u, memory.ReadUInt32(0x8C2B_6CEC));
        Assert.Equal(0u, memory.ReadUInt32(0x8C2F_67D8));
        Assert.Equal(2u, memory.ReadUInt32(0x8C2F_67DC));
    }

    [Fact]
    public void FastForwardsPredecrementStoreDtLoop()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C0F_7D0C, 0x2126); // mov.l r2,@-r1
        WriteInstruction(memory, 0x8C0F_7D0E, 0x4010); // dt r0
        WriteInstruction(memory, 0x8C0F_7D10, 0x8FFC); // bf/s 0x8C0F7D0C
        WriteInstruction(memory, 0x8C0F_7D12, 0x0009); // nop
        var cpu = new Sh4Cpu(memory, 0x8C0F_7D10);
        cpu.State.R[0] = 4;
        cpu.State.R[1] = 0x8C20_0010;
        cpu.State.R[2] = 0xAABB_CCDD;
        cpu.State.T = false;

        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardPredecrementStoreDtLoop(branch, 100, out var skippedInstructions));
        Assert.Equal(16UL, skippedInstructions);
        Assert.Equal(0u, cpu.State.R[0]);
        Assert.Equal(0x8C20_0000u, cpu.State.R[1]);
        Assert.Equal(0xAABB_CCDDu, memory.ReadUInt32(0x8C20_0000));
        Assert.Equal(0xAABB_CCDDu, memory.ReadUInt32(0x8C20_0004));
        Assert.Equal(0xAABB_CCDDu, memory.ReadUInt32(0x8C20_0008));
        Assert.Equal(0xAABB_CCDDu, memory.ReadUInt32(0x8C20_000C));
        Assert.True(cpu.State.T);
        Assert.Equal(0x8C0F_7D14u, cpu.State.Pc);
        Assert.Equal(1UL + skippedInstructions, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void PartiallyFastForwardsPredecrementStoreDtLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C0F_7D0C, 0x2126);
        WriteInstruction(memory, 0x8C0F_7D0E, 0x4010);
        WriteInstruction(memory, 0x8C0F_7D10, 0x8FFC);
        WriteInstruction(memory, 0x8C0F_7D12, 0x0009);
        var cpu = new Sh4Cpu(memory, 0x8C0F_7D10);
        cpu.State.R[0] = 4;
        cpu.State.R[1] = 0x8C20_0010;
        cpu.State.R[2] = 0x1122_3344;
        cpu.State.T = false;

        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardPredecrementStoreDtLoop(branch, 8, out var skippedInstructions));
        Assert.Equal(8UL, skippedInstructions);
        Assert.Equal(2u, cpu.State.R[0]);
        Assert.Equal(0x8C20_0008u, cpu.State.R[1]);
        Assert.Equal(0x1122_3344u, memory.ReadUInt32(0x8C20_0008));
        Assert.Equal(0x1122_3344u, memory.ReadUInt32(0x8C20_000C));
        Assert.False(cpu.State.T);
        Assert.Equal(0x8C0F_7D0Cu, cpu.State.Pc);
        Assert.Equal(1UL + skippedInstructions, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void FastForwardsPostincrementStoreDtLoop()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C0F_7CFC, 0x2122); // mov.l r2,@r1
        WriteInstruction(memory, 0x8C0F_7CFE, 0x4010); // dt r0
        WriteInstruction(memory, 0x8C0F_7D00, 0x8FFC); // bf/s 0x8C0F7CFC
        WriteInstruction(memory, 0x8C0F_7D02, 0x7104); // add #4,r1
        var cpu = new Sh4Cpu(memory, 0x8C0F_7D00);
        cpu.State.R[0] = 4;
        cpu.State.R[1] = 0x8C20_0000;
        cpu.State.R[2] = 0xAABB_CCDD;
        cpu.State.T = false;

        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardPostincrementStoreDtLoop(branch, 100, out var skippedInstructions));
        Assert.Equal(16UL, skippedInstructions);
        Assert.Equal(0u, cpu.State.R[0]);
        Assert.Equal(0x8C20_0014u, cpu.State.R[1]);
        Assert.Equal(0xAABB_CCDDu, memory.ReadUInt32(0x8C20_0004));
        Assert.Equal(0xAABB_CCDDu, memory.ReadUInt32(0x8C20_0008));
        Assert.Equal(0xAABB_CCDDu, memory.ReadUInt32(0x8C20_000C));
        Assert.Equal(0xAABB_CCDDu, memory.ReadUInt32(0x8C20_0010));
        Assert.True(cpu.State.T);
        Assert.Equal(0x8C0F_7D04u, cpu.State.Pc);
        Assert.Equal(1UL + skippedInstructions, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void PartiallyFastForwardsPostincrementStoreDtLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C0F_7CFC, 0x2122);
        WriteInstruction(memory, 0x8C0F_7CFE, 0x4010);
        WriteInstruction(memory, 0x8C0F_7D00, 0x8FFC);
        WriteInstruction(memory, 0x8C0F_7D02, 0x7104);
        var cpu = new Sh4Cpu(memory, 0x8C0F_7D00);
        cpu.State.R[0] = 4;
        cpu.State.R[1] = 0x8C20_0000;
        cpu.State.R[2] = 0x1122_3344;
        cpu.State.T = false;

        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardPostincrementStoreDtLoop(branch, 8, out var skippedInstructions));
        Assert.Equal(8UL, skippedInstructions);
        Assert.Equal(2u, cpu.State.R[0]);
        Assert.Equal(0x8C20_000Cu, cpu.State.R[1]);
        Assert.Equal(0x1122_3344u, memory.ReadUInt32(0x8C20_0004));
        Assert.Equal(0x1122_3344u, memory.ReadUInt32(0x8C20_0008));
        Assert.False(cpu.State.T);
        Assert.Equal(0x8C0F_7CFCu, cpu.State.Pc);
        Assert.Equal(1UL + skippedInstructions, cpu.State.InstructionsExecuted);
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
    public void ExecutesGbrDisplacementLoads()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xC401);
        WriteInstruction(memory, 0x8C01_0002, 0xC502);
        WriteInstruction(memory, 0x8C01_0004, 0xC603);
        memory.Write(0x8C02_0001, [0xFE]);
        memory.WriteUInt16(0x8C02_0004, 0x8001);
        memory.WriteUInt32(0x8C02_000C, 0x1234_5678);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Gbr = 0x8C02_0000;

        var byteLoad = cpu.Step();
        Assert.Equal(0xFFFF_FFFEu, cpu.State.R[0]);
        Assert.Equal("mov.b @(0x01,gbr),r0 ; [0x8C020001]=0xFFFFFFFE", byteLoad.Trace);

        var wordLoad = cpu.Step();
        Assert.Equal(0xFFFF_8001u, cpu.State.R[0]);
        Assert.Equal("mov.w @(0x02,gbr),r0 ; [0x8C020004]=0xFFFF8001", wordLoad.Trace);

        var longLoad = cpu.Step();
        Assert.Equal(0x1234_5678u, cpu.State.R[0]);
        Assert.Equal("mov.l @(0x03,gbr),r0 ; [0x8C02000C]=0x12345678", longLoad.Trace);
    }

    [Fact]
    public void ExecutesGbrDisplacementStores()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xC001);
        WriteInstruction(memory, 0x8C01_0002, 0xC102);
        WriteInstruction(memory, 0x8C01_0004, 0xC203);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Gbr = 0x8C02_0000;

        cpu.State.R[0] = 0x1234_56A5;
        cpu.Step();
        Assert.Equal(0xA5, memory.ReadByte(0x8C02_0001));

        cpu.State.R[0] = 0xCAFE_BABE;
        cpu.Step();
        Assert.Equal(0xBABE, memory.ReadUInt16(0x8C02_0004));

        cpu.State.R[0] = 0x1020_3040;
        cpu.Step();
        Assert.Equal(0x1020_3040u, memory.ReadUInt32(0x8C02_000C));
    }

    [Fact]
    public void ExecutesGbrByteLogicalOperations()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xCC10);
        WriteInstruction(memory, 0x8C01_0002, 0xCD3F);
        WriteInstruction(memory, 0x8C01_0004, 0xCE0F);
        WriteInstruction(memory, 0x8C01_0006, 0xCF80);
        memory.Write(0x8C02_0004, [0x52]);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Gbr = 0x8C02_0000;
        cpu.State.R[0] = 4;

        cpu.Step();
        Assert.False(cpu.State.T);

        cpu.Step();
        Assert.Equal(0x12, memory.ReadByte(0x8C02_0004));

        cpu.Step();
        Assert.Equal(0x1D, memory.ReadByte(0x8C02_0004));

        cpu.Step();
        Assert.Equal(0x9D, memory.ReadByte(0x8C02_0004));
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
    public void ExecutesSignedDivideInitialization()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x2327);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[2] = 0xFFFF_FFFF;
        cpu.State.R[3] = 0x0000_0001;

        cpu.Step();

        Assert.False(cpu.State.Q);
        Assert.True(cpu.State.M);
        Assert.True(cpu.State.T);
    }

    [Theory]
    [InlineData(0x0000_0020u, 0x0000_0003u, false, false, false, 0x0000_003Du, false, true)]
    [InlineData(0x8000_0000u, 0x0000_0002u, false, false, true, 0xFFFF_FFFFu, false, true)]
    [InlineData(0x7FFF_FFFFu, 0xFFFF_FFFDu, false, true, false, 0xFFFF_FFFBu, true, false)]
    [InlineData(0x8000_0000u, 0xFFFF_FFFEu, true, true, true, 0x0000_0003u, true, true)]
    public void ExecutesDivideStepFromManualState(
        uint dividend,
        uint divisor,
        bool initialM,
        bool initialQ,
        bool initialT,
        uint expectedDividend,
        bool expectedQ,
        bool expectedT)
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x3124); // div1 r2,r1
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[1] = dividend;
        cpu.State.R[2] = divisor;
        cpu.State.M = initialM;
        cpu.State.Q = initialQ;
        cpu.State.T = initialT;

        cpu.Step();

        Assert.Equal(expectedDividend, cpu.State.R[1]);
        Assert.Equal(initialM, cpu.State.M);
        Assert.Equal(expectedQ, cpu.State.Q);
        Assert.Equal(expectedT, cpu.State.T);
    }

    [Fact]
    public void StoresStatusRegister()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x0002);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Sr = Sh4State.SrMachineBit | 0xF0;

        cpu.Step();

        Assert.Equal(Sh4State.SrMachineBit | 0xF0, cpu.State.R[0]);
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
    public void ExecutesFloatingPointVectorInnerProduct()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF08D);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fr[0] = BitConverter.SingleToUInt32Bits(1.0f);
        cpu.State.Fr[1] = BitConverter.SingleToUInt32Bits(2.0f);
        cpu.State.Fr[2] = BitConverter.SingleToUInt32Bits(3.0f);
        cpu.State.Fr[3] = BitConverter.SingleToUInt32Bits(4.0f);
        cpu.State.Fr[8] = BitConverter.SingleToUInt32Bits(5.0f);
        cpu.State.Fr[9] = BitConverter.SingleToUInt32Bits(6.0f);
        cpu.State.Fr[10] = BitConverter.SingleToUInt32Bits(7.0f);
        cpu.State.Fr[11] = BitConverter.SingleToUInt32Bits(8.0f);

        var step = cpu.Step();

        Assert.Equal(BitConverter.SingleToUInt32Bits(70.0f), cpu.State.Fr[3]);
        Assert.Equal("fipr fv8,fv0 ; fr3=0x428C0000", step.Trace);
    }

    [Fact]
    public void FloatingPointVectorInnerProductTraceIncludesOperandsForNonFiniteResult()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF08D);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fr[0] = 0x7FC0_0000;
        cpu.State.Fr[8] = BitConverter.SingleToUInt32Bits(1.0f);

        var step = cpu.Step();

        Assert.True(float.IsNaN(BitConverter.UInt32BitsToSingle(cpu.State.Fr[3])));
        Assert.Contains("fipr fv8,fv0 ; fr3=0x", step.Trace, StringComparison.Ordinal);
        Assert.Contains("fv0=[0x7FC00000,0x00000000,0x00000000,0x00000000]", step.Trace, StringComparison.Ordinal);
        Assert.Contains("fv8=[0x3F800000,0x00000000,0x00000000,0x00000000]", step.Trace, StringComparison.Ordinal);
    }

    [Fact]
    public void FloatingPointArithmeticTraceIncludesOperandsForNonFiniteResult()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF453);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);

        var step = cpu.Step();

        Assert.True(float.IsNaN(BitConverter.UInt32BitsToSingle(cpu.State.Fr[4])));
        Assert.Contains("fdiv fr5,fr4 ; fr4=0x", step.Trace, StringComparison.Ordinal);
        Assert.Contains("nonfinite fr4old=0x00000000,fr5=0x00000000", step.Trace, StringComparison.Ordinal);
    }

    [Fact]
    public void FloatingPointMultiplyOverflowRoundToZeroSaturatesAndSetsFpscrBits()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF452);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fr[4] = BitConverter.SingleToUInt32Bits(float.MaxValue);
        cpu.State.Fr[5] = BitConverter.SingleToUInt32Bits(2.0f);

        cpu.Step();

        Assert.Equal(0x7F7F_FFFFu, cpu.State.Fr[4]);
        Assert.Equal(
            Sh4State.FpscrCauseOverflowBit | Sh4State.FpscrCauseInexactBit,
            cpu.State.Fpscr & Sh4State.FpscrCauseMask);
        Assert.Equal(
            Sh4State.FpscrFlagOverflowBit | Sh4State.FpscrFlagInexactBit,
            cpu.State.Fpscr & Sh4State.FpscrFlagMask);
    }

    [Fact]
    public void FloatingPointMultiplyOverflowRoundNearestProducesInfinityAndSetsFpscrBits()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF452);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fpscr = Sh4State.FpscrDnBit;
        cpu.State.Fr[4] = BitConverter.SingleToUInt32Bits(float.MaxValue);
        cpu.State.Fr[5] = BitConverter.SingleToUInt32Bits(2.0f);

        cpu.Step();

        Assert.Equal(0x7F80_0000u, cpu.State.Fr[4]);
        Assert.Equal(
            Sh4State.FpscrCauseOverflowBit | Sh4State.FpscrCauseInexactBit,
            cpu.State.Fpscr & Sh4State.FpscrCauseMask);
        Assert.Equal(
            Sh4State.FpscrFlagOverflowBit | Sh4State.FpscrFlagInexactBit,
            cpu.State.Fpscr & Sh4State.FpscrFlagMask);
    }

    [Fact]
    public void FloatingPointDivideByZeroSetsFpscrDivisionByZeroCauseAndStickyFlagBits()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF453);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fr[4] = BitConverter.SingleToUInt32Bits(1.0f);
        cpu.State.Fr[5] = BitConverter.SingleToUInt32Bits(0.0f);

        cpu.Step();

        Assert.Equal(0x7F80_0000u, cpu.State.Fr[4]);
        Assert.Equal(Sh4State.FpscrCauseDivisionByZeroBit, cpu.State.Fpscr & Sh4State.FpscrCauseMask);
        Assert.Equal(Sh4State.FpscrFlagDivisionByZeroBit, cpu.State.Fpscr & Sh4State.FpscrFlagMask);
    }

    [Fact]
    public void FloatingPointInvalidOperationSetsFpscrInvalidCauseAndStickyFlagBits()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF450);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fr[4] = BitConverter.SingleToUInt32Bits(float.PositiveInfinity);
        cpu.State.Fr[5] = BitConverter.SingleToUInt32Bits(float.NegativeInfinity);

        cpu.Step();

        Assert.True(float.IsNaN(BitConverter.UInt32BitsToSingle(cpu.State.Fr[4])));
        Assert.Equal(Sh4State.FpscrCauseInvalidBit, cpu.State.Fpscr & Sh4State.FpscrCauseMask);
        Assert.Equal(Sh4State.FpscrFlagInvalidBit, cpu.State.Fpscr & Sh4State.FpscrFlagMask);
    }

    [Fact]
    public void NormalFloatingPointArithmeticClearsCauseBitsButKeepsStickyFlagBits()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF450);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fpscr = Sh4State.FpscrCauseInvalidBit | Sh4State.FpscrFlagOverflowBit;
        cpu.State.Fr[4] = BitConverter.SingleToUInt32Bits(1.0f);
        cpu.State.Fr[5] = BitConverter.SingleToUInt32Bits(2.0f);

        cpu.Step();

        Assert.Equal(BitConverter.SingleToUInt32Bits(3.0f), cpu.State.Fr[4]);
        Assert.Equal(0u, cpu.State.Fpscr & Sh4State.FpscrCauseMask);
        Assert.Equal(Sh4State.FpscrFlagOverflowBit, cpu.State.Fpscr & Sh4State.FpscrFlagMask);
    }

    [Fact]
    public void FloatingPointVectorInnerProductOverflowRoundToZeroSaturatesAndSetsFpscrBits()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF08D);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fr[0] = BitConverter.SingleToUInt32Bits(float.MaxValue);
        cpu.State.Fr[8] = BitConverter.SingleToUInt32Bits(2.0f);

        cpu.Step();

        Assert.Equal(0x7F7F_FFFFu, cpu.State.Fr[3]);
        Assert.Equal(
            Sh4State.FpscrCauseOverflowBit | Sh4State.FpscrCauseInexactBit,
            cpu.State.Fpscr & Sh4State.FpscrCauseMask);
        Assert.Equal(
            Sh4State.FpscrFlagOverflowBit | Sh4State.FpscrFlagInexactBit,
            cpu.State.Fpscr & Sh4State.FpscrFlagMask);
    }

    [Fact]
    public void FloatingPointVectorInnerProductAlwaysSetsInexactCauseAndStickyFlagBits()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF08D);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fr[0] = BitConverter.SingleToUInt32Bits(2.0f);
        cpu.State.Fr[8] = BitConverter.SingleToUInt32Bits(3.0f);

        cpu.Step();

        Assert.Equal(BitConverter.SingleToUInt32Bits(6.0f), cpu.State.Fr[3]);
        Assert.Equal(Sh4State.FpscrCauseInexactBit, cpu.State.Fpscr & Sh4State.FpscrCauseMask);
        Assert.Equal(Sh4State.FpscrFlagInexactBit, cpu.State.Fpscr & Sh4State.FpscrFlagMask);
    }

    [Fact]
    public void ExecutesFloatingPointMultiplyAccumulate()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF32E);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fr[0] = BitConverter.SingleToUInt32Bits(2.0f);
        cpu.State.Fr[2] = BitConverter.SingleToUInt32Bits(3.0f);
        cpu.State.Fr[3] = BitConverter.SingleToUInt32Bits(4.0f);

        var step = cpu.Step();

        Assert.Equal(BitConverter.SingleToUInt32Bits(10.0f), cpu.State.Fr[3]);
        Assert.Equal("fmac fr0,fr2,fr3 ; fr3=0x41200000", step.Trace);
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
    public void FloatingPointRegisterBankToggleSwapsFrAndXf()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xFBFD);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fr[0] = 0x1111_1111;
        cpu.State.Xf[0] = 0x2222_2222;
        cpu.State.Fr[15] = 0xAAAA_AAAA;
        cpu.State.Xf[15] = 0xBBBB_BBBB;

        cpu.Step();

        Assert.Equal(0x2222_2222u, cpu.State.Fr[0]);
        Assert.Equal(0x1111_1111u, cpu.State.Xf[0]);
        Assert.Equal(0xBBBB_BBBBu, cpu.State.Fr[15]);
        Assert.Equal(0xAAAA_AAAAu, cpu.State.Xf[15]);
        Assert.Equal(Sh4State.FpscrFrBit | 0x0004_0001u, cpu.State.Fpscr);
    }

    [Fact]
    public void LoadingFpscrRegisterBankBitSwapsFrAndXfOnce()
    {
        var cpu = new Sh4Cpu(new DreamcastMemory(), 0x8C01_0000);
        cpu.State.Fr[2] = 0x1111_1111;
        cpu.State.Xf[2] = 0x2222_2222;

        cpu.State.Fpscr = Sh4State.FpscrFrBit | 0x0004_0001u;
        cpu.State.Fpscr = Sh4State.FpscrFrBit | 0x0004_0001u;

        Assert.Equal(0x2222_2222u, cpu.State.Fr[2]);
        Assert.Equal(0x1111_1111u, cpu.State.Xf[2]);

        cpu.State.Fpscr = 0x0004_0001u;

        Assert.Equal(0x1111_1111u, cpu.State.Fr[2]);
        Assert.Equal(0x2222_2222u, cpu.State.Xf[2]);
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
        WriteInstruction(memory, 0x8C01_0000, 0x0E2A);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Pr = 0x8C04_0000;

        cpu.Step();

        Assert.Equal(0x8C04_0000u, cpu.State.R[14]);
    }

    [Fact]
    public void LoadsProcedureRegisterFromGeneralRegister()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x402A);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[0] = 0x8C04_0000;

        cpu.Step();

        Assert.Equal(0x8C04_0000u, cpu.State.Pr);
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
    public void BranchInstructionInDelaySlotEntersSlotIllegalInstructionException()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xA00E);
        WriteInstruction(memory, 0x8C01_0002, 0xA000);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Vbr = 0x8C02_0000;
        cpu.State.Sr = 0x0000_0041;

        cpu.Step();
        var delaySlotStep = cpu.Step();

        Assert.Equal(0x8C01_0002u, delaySlotStep.Pc);
        Assert.Equal(0xA000, delaySlotStep.Opcode);
        Assert.Equal("slot illegal instruction ; expevt=0x000001A0, target=0x8C020100", delaySlotStep.Trace);
        Assert.Equal(0x8C02_0100u, cpu.State.Pc);
        Assert.Equal(0x8C01_0000u, cpu.State.Spc);
        Assert.Equal(0x0000_0041u, cpu.State.Ssr);
        Assert.Equal(0x0000_01A0u, memory.ReadUInt32(0xFF00_0024));
    }

    [Fact]
    public void TrapInstructionInDelaySlotEntersSlotIllegalInstructionExceptionWithoutWritingTrapEvent()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xA00E);
        WriteInstruction(memory, 0x8C01_0002, 0xC32A);
        memory.WriteUInt32(0xFF00_0020, 0x0000_00F0);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Vbr = 0x8C02_0000;

        cpu.Step();
        cpu.Step();

        Assert.Equal(0x8C02_0100u, cpu.State.Pc);
        Assert.Equal(0x8C01_0000u, cpu.State.Spc);
        Assert.Equal(0x0000_00F0u, memory.ReadUInt32(0xFF00_0020));
        Assert.Equal(0x0000_01A0u, memory.ReadUInt32(0xFF00_0024));
    }

    [Fact]
    public void StatusRegisterLoadInDelaySlotEntersSlotIllegalInstructionExceptionWithoutLoadingStatusRegister()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xA00E);
        WriteInstruction(memory, 0x8C01_0002, 0x400E);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[0] = 0;
        cpu.State.Vbr = 0x8C02_0000;
        cpu.State.Sr = 0x0000_0051;

        cpu.Step();
        cpu.Step();

        Assert.Equal(0x8C02_0100u, cpu.State.Pc);
        Assert.Equal(0x8C01_0000u, cpu.State.Spc);
        Assert.Equal(0x0000_0051u, cpu.State.Ssr);
        Assert.Equal(Sh4State.SrMachineBit | Sh4State.SrRegisterBankBit | Sh4State.SrBlockBit | 0x51u, cpu.State.Sr);
        Assert.Equal(0x0000_01A0u, memory.ReadUInt32(0xFF00_0024));
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
    public void VbrZeroExternalInterruptDispatchesThroughLowBiosVectorTable()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x0009);
        WriteInstruction(memory, 0x8C12_BF20, 0x002B);
        WriteInstruction(memory, 0x8C12_BF22, 0x0009);
        memory.WriteUInt32(0x0000_0224, 0x8C12_BF20);
        RaiseVBlankIrq9(memory);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);

        var step = cpu.Step();

        Assert.Equal(0x8C01_0000u, step.Pc);
        Assert.Equal(0, step.Opcode);
        Assert.Equal(0x8C12_BF20u, cpu.State.Pc);
        Assert.Equal(0x8C00_00F0u, cpu.State.Pr);
        Assert.Equal(0x0320u, cpu.State.R[4]);
        Assert.Equal(0x8C01_0000u, cpu.State.Spc);
        Assert.Equal(0u, cpu.State.Ssr);
        Assert.Equal(0x0320u, memory.ReadUInt32(0xFF00_0028));
        Assert.Equal(Sh4State.SrMachineBit | Sh4State.SrRegisterBankBit | Sh4State.SrBlockBit | 0x90u, cpu.State.Sr);
        Assert.Equal("interrupt event=0x0320, level=9, target=0x8C12BF20, bios-vector=0x00000224", step.Trace);

        cpu.Step();
        cpu.Step();

        Assert.Equal(0x8C01_0000u, cpu.State.Pc);
        Assert.Equal(0u, cpu.State.Sr);
    }

    [Fact]
    public void VbrZeroBiosInterruptCallbackReturnsThroughFirmwareWrapper()
    {
        var memory = new DreamcastMemory();
        FirmwareStubs.Install(memory);
        WriteInstruction(memory, 0x8C01_0000, 0x0009);
        WriteInstruction(memory, 0x8C12_BF20, 0x000B);
        WriteInstruction(memory, 0x8C12_BF22, 0x0009);
        memory.WriteUInt32(0x0000_0224, 0x8C12_BF20);
        RaiseVBlankIrq9(memory);
        var trapHandler = FirmwareStubs.CreateTrapHandler();
        var cpu = new Sh4Cpu(memory, 0x8C01_0000, trapHandler.TryHandle)
        {
            State =
            {
                Sr = 0x0000_0001,
                R = { [4] = 0x4455_6677 },
                Pr = 0x8C01_2340
            }
        };

        var interrupt = cpu.Step();
        var callbackReturn = cpu.Step();
        var delaySlot = cpu.Step();
        var firmwareReturn = cpu.Step();

        Assert.Equal("interrupt event=0x0320, level=9, target=0x8C12BF20, bios-vector=0x00000224", interrupt.Trace);
        Assert.Equal("rts ; target=0x8C0000F0", callbackReturn.Trace);
        Assert.Equal("nop", delaySlot.Trace);
        Assert.Equal("firmware interrupt return hle ; pc=0x8C010000, sr=0x00000001, pr=0x8C012340", firmwareReturn.Trace);
        Assert.Equal(0x8C01_0000u, cpu.State.Pc);
        Assert.Equal(0x8C01_2340u, cpu.State.Pr);
        Assert.Equal(0x4455_6677u, cpu.State.R[4]);
        Assert.Equal(0x0000_0001u, cpu.State.Sr);
        Assert.Equal(0x8C01_0000u, cpu.State.Spc);
        Assert.Equal(0x0000_0001u, cpu.State.Ssr);
    }

    [Fact]
    public void VbrZeroBiosInterruptCallbackClearsAcceptedAsicSourceOnFirmwareReturn()
    {
        var memory = new DreamcastMemory();
        FirmwareStubs.Install(memory);
        WriteInstruction(memory, 0x8C01_0000, 0x0009);
        WriteInstruction(memory, 0x8C12_BF20, 0x000B);
        WriteInstruction(memory, 0x8C12_BF22, 0x0009);
        memory.WriteUInt32(0x0000_022C, 0x8C12_BF20);
        memory.WriteUInt32(0xA05F_6920, 1u << 14);
        memory.RaiseAsicEventForDiagnostics(0x000E);
        var trapHandler = FirmwareStubs.CreateTrapHandler();
        var cpu = new Sh4Cpu(memory, 0x8C01_0000, trapHandler.TryHandle);

        var interrupt = cpu.Step();
        Assert.Equal(0x0360u, cpu.State.R[4]);
        var callbackReturn = cpu.Step();
        var delaySlot = cpu.Step();
        var firmwareReturn = cpu.Step();

        Assert.Equal("interrupt event=0x0360, level=11, target=0x8C12BF20, bios-vector=0x0000022C", interrupt.Trace);
        Assert.Equal("rts ; target=0x8C0000F0", callbackReturn.Trace);
        Assert.Equal("nop", delaySlot.Trace);
        Assert.Equal("firmware interrupt return hle ; pc=0x8C010000, sr=0x00000000, pr=0x00000000", firmwareReturn.Trace);
        Assert.False(memory.TryGetPendingExternalInterrupt(out _, out _));
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
    public void PendingExternalInterruptDoesNotNestUntilReturnFromExceptionCompletes()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x0009);
        WriteInstruction(memory, 0x8C02_0600, 0x0009);
        WriteInstruction(memory, 0x8C02_0602, 0x002B);
        WriteInstruction(memory, 0x8C02_0604, 0x0009);
        RaiseVBlankIrq9(memory);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Vbr = 0x8C02_0000;

        var firstInterrupt = cpu.Step();

        Assert.Equal(0x8C01_0000u, firstInterrupt.Pc);
        Assert.Equal(0, firstInterrupt.Opcode);
        Assert.Equal(0x8C02_0600u, cpu.State.Pc);
        Assert.Equal(0x8C01_0000u, cpu.State.Spc);
        Assert.Equal(0u, cpu.State.Ssr);
        Assert.Equal(Sh4State.SrMachineBit | Sh4State.SrRegisterBankBit | Sh4State.SrBlockBit | 0x90u, cpu.State.Sr);

        var blockedNestedInterrupt = cpu.Step();

        Assert.Equal(0x8C02_0600u, blockedNestedInterrupt.Pc);
        Assert.Equal(0x0009, blockedNestedInterrupt.Opcode);
        Assert.Equal("nop", blockedNestedInterrupt.Trace);
        Assert.Equal(0x8C02_0602u, cpu.State.Pc);
        Assert.Equal(0x8C01_0000u, cpu.State.Spc);
        Assert.Equal(0x0320u, memory.ReadUInt32(0xFF00_0028));
        Assert.True(memory.TryGetPendingExternalInterrupt(out var pendingEventCode, out var pendingLevel));
        Assert.Equal(0x0320u, pendingEventCode);
        Assert.Equal(9, pendingLevel);

        var returnStep = cpu.Step();

        Assert.Equal(0x8C02_0602u, returnStep.Pc);
        Assert.Equal(0x002B, returnStep.Opcode);
        Assert.Equal(0x8C02_0604u, cpu.State.Pc);
        Assert.Equal(0u, cpu.State.Sr);

        var returnDelaySlot = cpu.Step();

        Assert.Equal(0x8C02_0604u, returnDelaySlot.Pc);
        Assert.Equal(0x0009, returnDelaySlot.Opcode);
        Assert.Equal("nop", returnDelaySlot.Trace);
        Assert.Equal(0x8C01_0000u, cpu.State.Pc);

        var secondInterrupt = cpu.Step();

        Assert.Equal(0x8C01_0000u, secondInterrupt.Pc);
        Assert.Equal(0, secondInterrupt.Opcode);
        Assert.Equal(0x8C02_0600u, cpu.State.Pc);
        Assert.Equal(0x8C01_0000u, cpu.State.Spc);
        Assert.Equal("interrupt event=0x0320, level=9, target=0x8C020600", secondInterrupt.Trace);
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
    public void ReturnFromHigherPriorityTimerInterruptAcceptsPendingAsicAfterTimerClears()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x0009);
        WriteInstruction(memory, 0x8C02_0600, 0x002B);
        WriteInstruction(memory, 0x8C02_0602, 0x0009);
        RaiseTimerUnderflow(memory, 0, 10);
        RaiseVBlankIrq9(memory);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Vbr = 0x8C02_0000;

        var timerInterrupt = cpu.Step();

        Assert.Equal(0x8C01_0000u, timerInterrupt.Pc);
        Assert.Equal(0, timerInterrupt.Opcode);
        Assert.Equal(0x0400u, memory.ReadUInt32(0xFF00_0028));
        Assert.Equal(0x8C02_0600u, cpu.State.Pc);
        Assert.Equal(Sh4State.SrMachineBit | Sh4State.SrRegisterBankBit | Sh4State.SrBlockBit | 0xA0u, cpu.State.Sr);
        Assert.Equal("interrupt event=0x0400, level=10, target=0x8C020600", timerInterrupt.Trace);

        memory.WriteUInt16(0xFFD8_0010, 0x0020);
        Assert.True(memory.TryGetPendingExternalInterrupt(out var pendingEventCode, out var pendingLevel));
        Assert.Equal(0x0320u, pendingEventCode);
        Assert.Equal(9, pendingLevel);

        var returnStep = cpu.Step();

        Assert.Equal(0x8C02_0600u, returnStep.Pc);
        Assert.Equal(0x002B, returnStep.Opcode);
        Assert.Equal(0x8C02_0602u, cpu.State.Pc);
        Assert.Equal(0u, cpu.State.Sr);

        var returnDelaySlot = cpu.Step();

        Assert.Equal(0x8C02_0602u, returnDelaySlot.Pc);
        Assert.Equal(0x0009, returnDelaySlot.Opcode);
        Assert.Equal(0x8C01_0000u, cpu.State.Pc);

        var asicInterrupt = cpu.Step();

        Assert.Equal(0x8C01_0000u, asicInterrupt.Pc);
        Assert.Equal(0, asicInterrupt.Opcode);
        Assert.Equal(0x0320u, memory.ReadUInt32(0xFF00_0028));
        Assert.Equal(0x8C02_0600u, cpu.State.Pc);
        Assert.Equal(0x8C01_0000u, cpu.State.Spc);
        Assert.Equal("interrupt event=0x0320, level=9, target=0x8C020600", asicInterrupt.Trace);
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

    private static void WriteDoa2StringScanLoop(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_EDAA, 0x62F2);
        WriteInstruction(memory, 0x8C10_EDAC, 0x7201);
        WriteInstruction(memory, 0x8C10_EDAE, 0x2F22);
        WriteInstruction(memory, 0x8C10_EDB0, 0x64F2);
        WriteInstruction(memory, 0x8C10_EDB2, 0x6440);
        WriteInstruction(memory, 0x8C10_EDB4, 0x2448);
        WriteInstruction(memory, 0x8C10_EDB6, 0x8902);
        WriteInstruction(memory, 0x8C10_EDB8, 0x6043);
        WriteInstruction(memory, 0x8C10_EDBA, 0x8825);
        WriteInstruction(memory, 0x8C10_EDBC, 0x8BF5);
    }

    private static void WriteDoa2CallbackTimeoutLoop(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C12_F99A, 0x4D0B);
        WriteInstruction(memory, 0x8C12_F99C, 0xE407);
        WriteInstruction(memory, 0x8C12_F99E, 0x63F2);
        WriteInstruction(memory, 0x8C12_F9A0, 0x6232);
        WriteInstruction(memory, 0x8C12_F9A2, 0x72FF);
        WriteInstruction(memory, 0x8C12_F9A4, 0x2322);
        WriteInstruction(memory, 0x8C12_F9A6, 0x53F1);
        WriteInstruction(memory, 0x8C12_F9A8, 0x6232);
        WriteInstruction(memory, 0x8C12_F9AA, 0x32E0);
        WriteInstruction(memory, 0x8C12_F9AC, 0x8B03);
        WriteInstruction(memory, 0x8C12_F9AE, 0x61F2);
        WriteInstruction(memory, 0x8C12_F9B0, 0x6312);
        WriteInstruction(memory, 0x8C12_F9B2, 0x2338);
        WriteInstruction(memory, 0x8C12_F9B4, 0x8BF1);
    }

    private static void WriteDoa2BusyBitWaitLoop(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C13_0460, 0x4A0B);
        WriteInstruction(memory, 0x8C13_0462, 0xE407);
        WriteInstruction(memory, 0x8C13_0464, 0xD22F);
        WriteInstruction(memory, 0x8C13_0466, 0x6422);
        WriteInstruction(memory, 0x8C13_0468, 0x2448);
        WriteInstruction(memory, 0x8C13_046A, 0x890A);
        WriteInstruction(memory, 0x8C13_0482, 0xD12A);
        WriteInstruction(memory, 0x8C13_0484, 0x63DB);
        WriteInstruction(memory, 0x8C13_0486, 0x6412);
        WriteInstruction(memory, 0x8C13_0488, 0x443D);
        WriteInstruction(memory, 0x8C13_048A, 0x24C9);
        WriteInstruction(memory, 0x8C13_048C, 0x2448);
        WriteInstruction(memory, 0x8C13_048E, 0x8BE7);
        memory.WriteUInt32(0x8C13_0524, 0x8C2F_6808);
        memory.WriteUInt32(0x8C13_0528, 0x8C2F_6814);
        memory.WriteUInt32(0x8C13_052C, 0x8C2F_67FC);
    }

    private static void WriteDoa2Slot8TaskCallback(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C13_0724, 0x9010);
        WriteInstruction(memory, 0x8C13_0726, 0xE302);
        WriteInstruction(memory, 0x8C13_0728, 0x0C36);
        WriteInstruction(memory, 0x8C13_072A, 0xD309);
        WriteInstruction(memory, 0x8C13_072C, 0x430B);
        WriteInstruction(memory, 0x8C13_072E, 0xE408);
        memory.WriteUInt32(0x8C13_0750, 0x8C12_D2C0);
    }

    private static void RaiseVBlankIrq9(DreamcastMemory memory)
    {
        memory.WriteUInt32(0xA05F_6930, 1u << 3);
        memory.RaiseVBlankBegin();
    }

    private static void RaiseTimerUnderflow(DreamcastMemory memory, int channel, int priority)
    {
        SetTimerPriority(memory, channel, priority);
        memory.WriteUInt16(TimerControlAddress(channel), 0x0120);
    }

    private static void SetTimerPriority(DreamcastMemory memory, int channel, int priority)
    {
        var shift = channel switch
        {
            0 => 12,
            1 => 8,
            _ => 4
        };
        var current = memory.ReadUInt16(0xFFD0_0004);
        var mask = 0xFu << shift;
        var value = (ushort)((current & ~mask) | (((uint)priority & 0xF) << shift));
        memory.WriteUInt16(0xFFD0_0004, value);
    }

    private static uint TimerControlAddress(int channel) => channel switch
    {
        0 => 0xFFD8_0010,
        1 => 0xFFD8_001C,
        _ => 0xFFD8_0028
    };

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
