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
    public void FastForwardsIpBinAsicEventWaitLoopPartially()
    {
        var normalMemory = new DreamcastMemory();
        WriteIpBinAsicEventWaitLoop(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteIpBinAsicEventWaitLoop(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C00_909A);
        var fast = new Sh4Cpu(fastMemory, 0x8C00_909A);

        var normalStart = StepMany(normal, 4);
        var fastStart = StepMany(fast, 4);
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardIpBinAsicEventWaitLoop(fastStart, 8, out var skippedInstructions));
        Assert.Equal(8UL, skippedInstructions);
        StepMany(normal, skippedInstructions);

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(0x8C00_909Au, fast.State.Pc);
        Assert.True(fast.State.T);
    }

    [Fact]
    public void DoesNotFastForwardIpBinAsicEventWaitLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteIpBinAsicEventWaitLoop(memory);
        var cpu = new Sh4Cpu(memory, 0x8C00_909A);

        var start = StepMany(cpu, 4);

        Assert.False(cpu.TryFastForwardIpBinAsicEventWaitLoop(start, 3, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C00_909Au, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardIpBinAsicEventWaitLoopWhenEventArrives()
    {
        var memory = new DreamcastMemory();
        WriteIpBinAsicEventWaitLoop(memory);
        var cpu = new Sh4Cpu(memory, 0x8C00_909A);

        var start = StepMany(cpu, 4);
        memory.RaiseAsicEventForDiagnostics(0x0003);

        Assert.False(cpu.TryFastForwardIpBinAsicEventWaitLoop(start, 4, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void DoesNotFastForwardIpBinAsicEventWaitLoopWhenLiteralDiffers()
    {
        var memory = new DreamcastMemory();
        WriteIpBinAsicEventWaitLoop(memory);
        memory.WriteUInt32(0x8C00_90E8, 0xA05F_6904);
        var cpu = new Sh4Cpu(memory, 0x8C00_909A);

        var start = StepMany(cpu, 4);

        Assert.False(cpu.TryFastForwardIpBinAsicEventWaitLoop(start, 4, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
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
    public void FastForwardsPredecrementByteCopyDtLoop()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C10_784E, 0x77FF); // add #-1,r7
        WriteInstruction(memory, 0x8C10_7850, 0x6370); // mov.b @r7,r3
        WriteInstruction(memory, 0x8C10_7852, 0x4610); // dt r6
        WriteInstruction(memory, 0x8C10_7854, 0x8FFB); // bf/s 0x8C10784E
        WriteInstruction(memory, 0x8C10_7856, 0x2534); // mov.b r3,@-r5
        memory.Write(0x8C20_0000, [0x10, 0x20, 0x30, 0x40]);
        var cpu = new Sh4Cpu(memory, 0x8C10_7854);
        cpu.State.R[3] = 0x40;
        cpu.State.R[5] = 0x8C20_1004;
        cpu.State.R[6] = 3;
        cpu.State.R[7] = 0x8C20_0003;
        cpu.State.T = false;

        var branch = cpu.Step();

        Assert.True(cpu.TryFastForwardPredecrementByteCopyDtLoop(branch, 100, out var skippedInstructions));
        Assert.Equal(16UL, skippedInstructions);
        Assert.Equal(0x8C20_0000u, cpu.State.R[7]);
        Assert.Equal(0x8C20_1000u, cpu.State.R[5]);
        Assert.Equal(0u, cpu.State.R[6]);
        Assert.Equal(0x10u, cpu.State.R[3]);
        Assert.Equal(0x10, memory.ReadByte(0x8C20_1000));
        Assert.Equal(0x20, memory.ReadByte(0x8C20_1001));
        Assert.Equal(0x30, memory.ReadByte(0x8C20_1002));
        Assert.Equal(0x40, memory.ReadByte(0x8C20_1003));
        Assert.True(cpu.State.T);
        Assert.Equal(0x8C10_7858u, cpu.State.Pc);
        Assert.Equal(1UL + skippedInstructions, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void DoesNotFastForwardPredecrementByteCopyDtLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C10_784E, 0x77FF);
        WriteInstruction(memory, 0x8C10_7850, 0x6370);
        WriteInstruction(memory, 0x8C10_7852, 0x4610);
        WriteInstruction(memory, 0x8C10_7854, 0x8FFB);
        WriteInstruction(memory, 0x8C10_7856, 0x2534);
        memory.Write(0x8C20_0000, [0x10, 0x20, 0x30, 0x40]);
        var cpu = new Sh4Cpu(memory, 0x8C10_7854);
        cpu.State.R[3] = 0x40;
        cpu.State.R[5] = 0x8C20_1004;
        cpu.State.R[6] = 3;
        cpu.State.R[7] = 0x8C20_0003;
        cpu.State.T = false;

        var branch = cpu.Step();

        Assert.False(cpu.TryFastForwardPredecrementByteCopyDtLoop(branch, 15, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_7856u, cpu.State.Pc);
        Assert.Equal(0, memory.ReadByte(0x8C20_1003));
    }

    [Fact]
    public void FastForwardsDoa2ByteFillLoop()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2ByteFillLoop(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2ByteFillLoop(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_7874);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_7874);
        InitializeDoa2ByteFillState(normal);
        InitializeDoa2ByteFillState(fast);

        var normalBranch = normal.Step();
        var fastBranch = fast.Step();
        Assert.Equal(normalBranch.Trace, fastBranch.Trace);

        Assert.True(fast.TryFastForwardDoa2ByteFillLoop(fastBranch, 100, out var skippedInstructions));
        Assert.Equal(26UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        for (var offset = 1u; offset <= 5; offset++)
        {
            Assert.Equal(normalMemory.ReadByte(0x8C20_1000 + offset), fastMemory.ReadByte(0x8C20_1000 + offset));
            Assert.Equal(0x5A, fastMemory.ReadByte(0x8C20_1000 + offset));
        }
    }

    [Fact]
    public void DoesNotFastForwardDoa2ByteFillLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ByteFillLoop(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_7874);
        InitializeDoa2ByteFillState(cpu);

        var branch = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ByteFillLoop(branch, 25, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_7876u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ByteFillLoopWhenDestinationIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ByteFillLoop(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_7874);
        InitializeDoa2ByteFillState(cpu);
        cpu.State.R[0] = 0xA500_0000;

        var branch = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ByteFillLoop(branch, 100, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void FastForwardsDoa2ByteFillWrapper()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2ByteFillLoop(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2ByteFillLoop(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_7864);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_7864);
        InitializeDoa2ByteFillWrapperState(normal, 8);
        InitializeDoa2ByteFillWrapperState(fast, 8);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2ByteFillWrapper(fastStart, 46, out var skippedInstructions));
        Assert.Equal(46UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        for (var offset = 0u; offset < 8; offset++)
        {
            Assert.Equal(normalMemory.ReadByte(0x8C20_1000 + offset), fastMemory.ReadByte(0x8C20_1000 + offset));
            Assert.Equal(0x5A, fastMemory.ReadByte(0x8C20_1000 + offset));
        }
    }

    [Fact]
    public void FastForwardsDoa2ByteFillWrapperWhenByteCountIsZero()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2ByteFillLoop(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2ByteFillLoop(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_7864);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_7864);
        InitializeDoa2ByteFillWrapperState(normal, 0);
        InitializeDoa2ByteFillWrapperState(fast, 0);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2ByteFillWrapper(fastStart, 6, out var skippedInstructions));
        Assert.Equal(6UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(0, fastMemory.ReadByte(0x8C20_1000));
    }

    [Fact]
    public void DoesNotFastForwardDoa2ByteFillWrapperWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ByteFillLoop(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_7864);
        InitializeDoa2ByteFillWrapperState(cpu, 8);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ByteFillWrapper(start, 45, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_7866u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ByteFillWrapperWhenDestinationIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ByteFillLoop(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_7864);
        InitializeDoa2ByteFillWrapperState(cpu, 8);
        cpu.State.R[4] = 0xA500_0000;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ByteFillWrapper(start, 46, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ByteFillWrapperWhenOpcodeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ByteFillLoop(memory);
        WriteInstruction(memory, 0x8C10_7878, 0x0009);
        var cpu = new Sh4Cpu(memory, 0x8C10_7864);
        InitializeDoa2ByteFillWrapperState(cpu, 8);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ByteFillWrapper(start, 46, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void FastForwardsDoa2TableDivideSetupLoop()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2TableDivideSetupLoop(normalMemory);
        WriteDoa2TableDivideSetupData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2TableDivideSetupLoop(fastMemory);
        WriteDoa2TableDivideSetupData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C11_C7B0);
        var fast = new Sh4Cpu(fastMemory, 0x8C11_C7B0);
        InitializeDoa2TableDivideSetupState(normal);
        InitializeDoa2TableDivideSetupState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2TableDivideSetupLoop(fastStart, 759, out var skippedInstructions));
        Assert.Equal(759UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.M, fast.State.M);
        Assert.Equal(normal.State.Q, fast.State.Q);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(normalMemory.ReadUInt32(0x8C20_2FFC), fastMemory.ReadUInt32(0x8C20_2FFC));
        for (var offset = 4u; offset <= 32; offset += 4)
        {
            Assert.Equal(normalMemory.ReadUInt32(0x8C20_1000 + offset), fastMemory.ReadUInt32(0x8C20_1000 + offset));
        }
    }

    [Fact]
    public void DoesNotFastForwardDoa2TableDivideSetupLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2TableDivideSetupLoop(memory);
        WriteDoa2TableDivideSetupData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C11_C7B0);
        InitializeDoa2TableDivideSetupState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2TableDivideSetupLoop(start, 758, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_C7B2u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2TableDivideSetupLoopWhenDestinationIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2TableDivideSetupLoop(memory);
        WriteDoa2TableDivideSetupData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C11_C7B0);
        InitializeDoa2TableDivideSetupState(cpu);
        cpu.State.R[14] = 0xA500_0000;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2TableDivideSetupLoop(start, 759, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void DoesNotFastForwardDoa2TableDivideSetupLoopWhenLiteralDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2TableDivideSetupLoop(memory);
        WriteDoa2TableDivideSetupData(memory);
        memory.WriteUInt32(0x8C11_C8C4, 0x8C10_751C);
        var cpu = new Sh4Cpu(memory, 0x8C11_C7B0);
        InitializeDoa2TableDivideSetupState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2TableDivideSetupLoop(start, 759, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void FastForwardsDoa2PostTableVectorCopyLoop()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2PostTableVectorCopyLoop(normalMemory);
        WriteDoa2PostTableVectorCopyData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2PostTableVectorCopyLoop(fastMemory);
        WriteDoa2PostTableVectorCopyData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C11_C7EA);
        var fast = new Sh4Cpu(fastMemory, 0x8C11_C7EA);
        InitializeDoa2PostTableVectorCopyState(normal);
        InitializeDoa2PostTableVectorCopyState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2PostTableVectorCopyLoop(fastStart, 89, out var skippedInstructions));
        Assert.Equal(89UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        for (var offset = 0u; offset < 32; offset += 4)
        {
            Assert.Equal(normalMemory.ReadUInt32(0x8C20_1024 + offset), fastMemory.ReadUInt32(0x8C20_1024 + offset));
        }
    }

    [Fact]
    public void DoesNotFastForwardDoa2PostTableVectorCopyLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2PostTableVectorCopyLoop(memory);
        WriteDoa2PostTableVectorCopyData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C11_C7EA);
        InitializeDoa2PostTableVectorCopyState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2PostTableVectorCopyLoop(start, 88, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_C7ECu, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2PostTableVectorCopyLoopWhenDestinationIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2PostTableVectorCopyLoop(memory);
        WriteDoa2PostTableVectorCopyData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C11_C7EA);
        InitializeDoa2PostTableVectorCopyState(cpu);
        cpu.State.R[14] = 0xA500_0000;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2PostTableVectorCopyLoop(start, 89, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void DoesNotFastForwardDoa2PostTableVectorCopyLoopWhenLiteralDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2PostTableVectorCopyLoop(memory);
        WriteDoa2PostTableVectorCopyData(memory);
        WriteInstruction(memory, 0x8C11_C8AC, 0x0914);
        var cpu = new Sh4Cpu(memory, 0x8C11_C7EA);
        InitializeDoa2PostTableVectorCopyState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2PostTableVectorCopyLoop(start, 89, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void FastForwardsDoa2EmptyCallbackTableScan()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2EmptyCallbackTableScan(normalMemory);
        WriteDoa2EmptyCallbackTableData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2EmptyCallbackTableScan(fastMemory);
        WriteDoa2EmptyCallbackTableData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C12_FDA0);
        var fast = new Sh4Cpu(fastMemory, 0x8C12_FDA0);
        InitializeDoa2EmptyCallbackTableScanState(normal);
        InitializeDoa2EmptyCallbackTableScanState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2EmptyCallbackTableScan(fastStart, 70, out var skippedInstructions));
        Assert.Equal(70UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
    }

    [Fact]
    public void DoesNotFastForwardDoa2EmptyCallbackTableScanWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2EmptyCallbackTableScan(memory);
        WriteDoa2EmptyCallbackTableData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C12_FDA0);
        InitializeDoa2EmptyCallbackTableScanState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2EmptyCallbackTableScan(start, 69, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C12_FDA2u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2EmptyCallbackTableScanWhenEntryIsActive()
    {
        var memory = new DreamcastMemory();
        WriteDoa2EmptyCallbackTableScan(memory);
        WriteDoa2EmptyCallbackTableData(memory);
        memory.WriteUInt32(0x8C2F_6820 + (3 * 0x1C4), 1);
        var cpu = new Sh4Cpu(memory, 0x8C12_FDA0);
        InitializeDoa2EmptyCallbackTableScanState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2EmptyCallbackTableScan(start, 70, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void DoesNotFastForwardDoa2EmptyCallbackTableScanWhenLiteralDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2EmptyCallbackTableScan(memory);
        WriteDoa2EmptyCallbackTableData(memory);
        memory.WriteUInt32(0x8C12_FE44, 0x8C2F_6800);
        var cpu = new Sh4Cpu(memory, 0x8C12_FDA0);
        InitializeDoa2EmptyCallbackTableScanState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2EmptyCallbackTableScan(start, 70, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void FastForwardsDoa2FiveWordTableCopyLoop()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2FiveWordTableCopyLoop(normalMemory);
        WriteDoa2FiveWordTableCopyData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2FiveWordTableCopyLoop(fastMemory);
        WriteDoa2FiveWordTableCopyData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C11_B226);
        var fast = new Sh4Cpu(fastMemory, 0x8C11_B226);
        InitializeDoa2FiveWordTableCopyState(normal, offset: 0);
        InitializeDoa2FiveWordTableCopyState(fast, offset: 0);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2FiveWordTableCopyLoop(fastStart, 119, out var skippedInstructions));
        Assert.Equal(119UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        for (var offset = 0u; offset < 20; offset += 4)
        {
            Assert.Equal(normalMemory.ReadUInt32(0x8C20_11B0 + offset), fastMemory.ReadUInt32(0x8C20_11B0 + offset));
        }
    }

    [Fact]
    public void FastForwardsDoa2FiveWordTableCopyLoopFromMiddle()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2FiveWordTableCopyLoop(normalMemory);
        WriteDoa2FiveWordTableCopyData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2FiveWordTableCopyLoop(fastMemory);
        WriteDoa2FiveWordTableCopyData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C11_B226);
        var fast = new Sh4Cpu(fastMemory, 0x8C11_B226);
        InitializeDoa2FiveWordTableCopyState(normal, offset: 8);
        InitializeDoa2FiveWordTableCopyState(fast, offset: 8);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2FiveWordTableCopyLoop(fastStart, 71, out var skippedInstructions));
        Assert.Equal(71UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
    }

    [Fact]
    public void DoesNotFastForwardDoa2FiveWordTableCopyLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2FiveWordTableCopyLoop(memory);
        WriteDoa2FiveWordTableCopyData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C11_B226);
        InitializeDoa2FiveWordTableCopyState(cpu, offset: 0);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2FiveWordTableCopyLoop(start, 118, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_B228u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2FiveWordTableCopyLoopWhenDestinationIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2FiveWordTableCopyLoop(memory);
        WriteDoa2FiveWordTableCopyData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C11_B226);
        InitializeDoa2FiveWordTableCopyState(cpu, offset: 0);
        cpu.State.R[12] = 0x1900_0000;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2FiveWordTableCopyLoop(start, 119, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void DoesNotFastForwardDoa2FiveWordTableCopyLoopWhenLiteralDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2FiveWordTableCopyLoop(memory);
        WriteDoa2FiveWordTableCopyData(memory);
        WriteInstruction(memory, 0x8C11_B2C0, 0x00B4);
        var cpu = new Sh4Cpu(memory, 0x8C11_B226);
        InitializeDoa2FiveWordTableCopyState(cpu, offset: 0);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2FiveWordTableCopyLoop(start, 119, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void FastForwardsDoa2FiveWordMirrorCopyLoop()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2FiveWordMirrorCopyLoop(normalMemory);
        WriteDoa2FiveWordMirrorCopyData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2FiveWordMirrorCopyLoop(fastMemory);
        WriteDoa2FiveWordMirrorCopyData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C0F_90B8);
        var fast = new Sh4Cpu(fastMemory, 0x8C0F_90B8);
        InitializeDoa2FiveWordMirrorCopyState(normal);
        InitializeDoa2FiveWordMirrorCopyState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2FiveWordMirrorCopyLoop(fastStart, 29, out var skippedInstructions));
        Assert.Equal(29UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        for (var offset = 0u; offset < 20; offset += 4)
        {
            Assert.Equal(normalMemory.ReadUInt32(0x8C20_2000 + offset), fastMemory.ReadUInt32(0x8C20_2000 + offset));
        }
    }

    [Fact]
    public void DoesNotFastForwardDoa2FiveWordMirrorCopyLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2FiveWordMirrorCopyLoop(memory);
        WriteDoa2FiveWordMirrorCopyData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C0F_90B8);
        InitializeDoa2FiveWordMirrorCopyState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2FiveWordMirrorCopyLoop(start, 28, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C0F_90BAu, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2FiveWordMirrorCopyLoopWhenDestinationIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2FiveWordMirrorCopyLoop(memory);
        WriteDoa2FiveWordMirrorCopyData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C0F_90B8);
        InitializeDoa2FiveWordMirrorCopyState(cpu);
        cpu.State.R[5] = 0xA500_0000;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2FiveWordMirrorCopyLoop(start, 29, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void DoesNotFastForwardDoa2FiveWordMirrorCopyLoopWhenLiteralDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2FiveWordMirrorCopyLoop(memory);
        WriteDoa2FiveWordMirrorCopyData(memory);
        memory.WriteUInt32(0x8C0F_9184, 0x8C2B_6F6C);
        var cpu = new Sh4Cpu(memory, 0x8C0F_90B8);
        InitializeDoa2FiveWordMirrorCopyState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2FiveWordMirrorCopyLoop(start, 29, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void FastForwardsDoa2EmptyStackWordScanLoop()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2EmptyStackWordScanLoop(normalMemory);
        WriteDoa2EmptyStackWordScanData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2EmptyStackWordScanLoop(fastMemory);
        WriteDoa2EmptyStackWordScanData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C11_B33C);
        var fast = new Sh4Cpu(fastMemory, 0x8C11_B33C);
        InitializeDoa2EmptyStackWordScanState(normal, 0x8C20_1000);
        InitializeDoa2EmptyStackWordScanState(fast, 0x8C20_1000);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2EmptyStackWordScanLoop(fastStart, 41, out var skippedInstructions));
        Assert.Equal(41UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(0x8C11_B340u, fast.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2EmptyStackWordScanLoopFromMiddle()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2EmptyStackWordScanLoop(normalMemory);
        WriteDoa2EmptyStackWordScanData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2EmptyStackWordScanLoop(fastMemory);
        WriteDoa2EmptyStackWordScanData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C11_B33C);
        var fast = new Sh4Cpu(fastMemory, 0x8C11_B33C);
        InitializeDoa2EmptyStackWordScanState(normal, 0x8C20_1008);
        InitializeDoa2EmptyStackWordScanState(fast, 0x8C20_1008);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2EmptyStackWordScanLoop(fastStart, 25, out var skippedInstructions));
        Assert.Equal(25UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
    }

    [Fact]
    public void DoesNotFastForwardDoa2EmptyStackWordScanLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2EmptyStackWordScanLoop(memory);
        WriteDoa2EmptyStackWordScanData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C11_B33C);
        InitializeDoa2EmptyStackWordScanState(cpu, 0x8C20_1000);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2EmptyStackWordScanLoop(start, 40, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_B33Eu, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2EmptyStackWordScanLoopWhenEntryIsNonzero()
    {
        var memory = new DreamcastMemory();
        WriteDoa2EmptyStackWordScanLoop(memory);
        WriteDoa2EmptyStackWordScanData(memory);
        memory.WriteUInt32(0x8C20_1008, 0x0000_0001);
        var cpu = new Sh4Cpu(memory, 0x8C11_B33C);
        InitializeDoa2EmptyStackWordScanState(cpu, 0x8C20_1000);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2EmptyStackWordScanLoop(start, 41, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_B33Eu, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2EmptyStackWordScanLoopWhenLiteralDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2EmptyStackWordScanLoop(memory);
        WriteDoa2EmptyStackWordScanData(memory);
        WriteInstruction(memory, 0x8C11_B304, 0x8916);
        var cpu = new Sh4Cpu(memory, 0x8C11_B33C);
        InitializeDoa2EmptyStackWordScanState(cpu, 0x8C20_1000);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2EmptyStackWordScanLoop(start, 41, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void FastForwardsDoa2EmptyTaskHelperReturn()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2EmptyTaskHelperReturn(normalMemory);
        WriteDoa2EmptyTaskHelperReturnData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2EmptyTaskHelperReturn(fastMemory);
        WriteDoa2EmptyTaskHelperReturnData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C13_06A0);
        var fast = new Sh4Cpu(fastMemory, 0x8C13_06A0);
        InitializeDoa2EmptyTaskHelperReturnState(normal, index: 0);
        InitializeDoa2EmptyTaskHelperReturnState(fast, index: 0);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2EmptyTaskHelperReturn(fastStart, 51, out var skippedInstructions));
        Assert.Equal(51UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(normalMemory.ReadUInt32(0x8C20_6FFC), fastMemory.ReadUInt32(0x8C20_6FFC));
        Assert.Equal(normalMemory.ReadUInt32(0x8C20_6FF8), fastMemory.ReadUInt32(0x8C20_6FF8));
        Assert.Equal(normalMemory.ReadUInt32(0x8C20_6FF4), fastMemory.ReadUInt32(0x8C20_6FF4));
        Assert.Equal(normalMemory.ReadUInt32(0x8C20_6FF0), fastMemory.ReadUInt32(0x8C20_6FF0));
    }

    [Fact]
    public void FastForwardsDoa2EmptyTaskHelperReturnForLaterEntry()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2EmptyTaskHelperReturn(normalMemory);
        WriteDoa2EmptyTaskHelperReturnData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2EmptyTaskHelperReturn(fastMemory);
        WriteDoa2EmptyTaskHelperReturnData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C13_06A0);
        var fast = new Sh4Cpu(fastMemory, 0x8C13_06A0);
        InitializeDoa2EmptyTaskHelperReturnState(normal, index: 4);
        InitializeDoa2EmptyTaskHelperReturnState(fast, index: 4);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2EmptyTaskHelperReturn(fastStart, 51, out var skippedInstructions));
        Assert.Equal(51UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
    }

    [Fact]
    public void DoesNotFastForwardDoa2EmptyTaskHelperReturnWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2EmptyTaskHelperReturn(memory);
        WriteDoa2EmptyTaskHelperReturnData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C13_06A0);
        InitializeDoa2EmptyTaskHelperReturnState(cpu, index: 0);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2EmptyTaskHelperReturn(start, 50, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C13_06A2u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2EmptyTaskHelperReturnWhenPrimaryTableEntryIsNonzero()
    {
        var memory = new DreamcastMemory();
        WriteDoa2EmptyTaskHelperReturn(memory);
        WriteDoa2EmptyTaskHelperReturnData(memory);
        memory.WriteUInt32(0x8C2F_6650, 1);
        var cpu = new Sh4Cpu(memory, 0x8C13_06A0);
        InitializeDoa2EmptyTaskHelperReturnState(cpu, index: 0);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2EmptyTaskHelperReturn(start, 51, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void DoesNotFastForwardDoa2EmptyTaskHelperReturnWhenQueueEntryIsNonzero()
    {
        var memory = new DreamcastMemory();
        WriteDoa2EmptyTaskHelperReturn(memory);
        WriteDoa2EmptyTaskHelperReturnData(memory);
        memory.WriteUInt32(0x8C2F_6910, 1);
        var cpu = new Sh4Cpu(memory, 0x8C13_06A0);
        InitializeDoa2EmptyTaskHelperReturnState(cpu, index: 3);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2EmptyTaskHelperReturn(start, 51, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void DoesNotFastForwardDoa2EmptyTaskHelperReturnWhenLiteralDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2EmptyTaskHelperReturn(memory);
        WriteDoa2EmptyTaskHelperReturnData(memory);
        WriteInstruction(memory, 0x8C13_06BC, 0x8D08);
        var cpu = new Sh4Cpu(memory, 0x8C13_06A0);
        InitializeDoa2EmptyTaskHelperReturnState(cpu, index: 0);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2EmptyTaskHelperReturn(start, 51, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void FastForwardsDoa2EmptyTaskHelperCallerLoop()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2EmptyTaskHelperCallerLoop(normalMemory);
        WriteDoa2EmptyTaskHelperCallerLoopData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2EmptyTaskHelperCallerLoop(fastMemory);
        WriteDoa2EmptyTaskHelperCallerLoopData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C13_07D6);
        var fast = new Sh4Cpu(fastMemory, 0x8C13_07D6);
        InitializeDoa2EmptyTaskHelperCallerLoopState(normal, normalMemory, index: 0);
        InitializeDoa2EmptyTaskHelperCallerLoopState(fast, fastMemory, index: 0);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2EmptyTaskHelperCallerLoop(fastStart, 520, out var skippedInstructions));
        Assert.Equal(520UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(normalMemory.ReadUInt32(0x8C20_7008), fastMemory.ReadUInt32(0x8C20_7008));
        Assert.Equal(normalMemory.ReadUInt32(0x8C20_6FFC), fastMemory.ReadUInt32(0x8C20_6FFC));
        Assert.Equal(normalMemory.ReadUInt32(0x8C20_6FF8), fastMemory.ReadUInt32(0x8C20_6FF8));
        Assert.Equal(normalMemory.ReadUInt32(0x8C20_6FF4), fastMemory.ReadUInt32(0x8C20_6FF4));
        Assert.Equal(normalMemory.ReadUInt32(0x8C20_6FF0), fastMemory.ReadUInt32(0x8C20_6FF0));
        for (var offset = 0u; offset < 20; offset += 4)
        {
            Assert.Equal(normalMemory.ReadUInt32(0x8C2F_6864 + offset), fastMemory.ReadUInt32(0x8C2F_6864 + offset));
            Assert.Equal(normalMemory.ReadUInt32(0x8C2F_6904 + offset), fastMemory.ReadUInt32(0x8C2F_6904 + offset));
        }
    }

    [Fact]
    public void FastForwardsDoa2EmptyTaskHelperCallerLoopFromFinalEntry()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2EmptyTaskHelperCallerLoop(normalMemory);
        WriteDoa2EmptyTaskHelperCallerLoopData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2EmptyTaskHelperCallerLoop(fastMemory);
        WriteDoa2EmptyTaskHelperCallerLoopData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C13_07D6);
        var fast = new Sh4Cpu(fastMemory, 0x8C13_07D6);
        InitializeDoa2EmptyTaskHelperCallerLoopState(normal, normalMemory, index: 4);
        InitializeDoa2EmptyTaskHelperCallerLoopState(fast, fastMemory, index: 4);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2EmptyTaskHelperCallerLoop(fastStart, 104, out var skippedInstructions));
        Assert.Equal(104UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
    }

    [Fact]
    public void DoesNotFastForwardDoa2EmptyTaskHelperCallerLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2EmptyTaskHelperCallerLoop(memory);
        WriteDoa2EmptyTaskHelperCallerLoopData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C13_07D6);
        InitializeDoa2EmptyTaskHelperCallerLoopState(cpu, memory, index: 0);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2EmptyTaskHelperCallerLoop(start, 519, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C13_07D8u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2EmptyTaskHelperCallerLoopWhenSourceEntryIsNonzero()
    {
        var memory = new DreamcastMemory();
        WriteDoa2EmptyTaskHelperCallerLoop(memory);
        WriteDoa2EmptyTaskHelperCallerLoopData(memory);
        memory.WriteUInt32(0x8C20_2010, 1);
        var cpu = new Sh4Cpu(memory, 0x8C13_07D6);
        InitializeDoa2EmptyTaskHelperCallerLoopState(cpu, memory, index: 0);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2EmptyTaskHelperCallerLoop(start, 520, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void DoesNotFastForwardDoa2EmptyTaskHelperCallerLoopWhenLoopCounterDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2EmptyTaskHelperCallerLoop(memory);
        WriteDoa2EmptyTaskHelperCallerLoopData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C13_07D6);
        InitializeDoa2EmptyTaskHelperCallerLoopState(cpu, memory, index: 0);
        memory.WriteUInt32(0x8C20_7008, 4);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2EmptyTaskHelperCallerLoop(start, 520, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void DoesNotFastForwardDoa2EmptyTaskHelperCallerLoopWhenLiteralDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2EmptyTaskHelperCallerLoop(memory);
        WriteDoa2EmptyTaskHelperCallerLoopData(memory);
        WriteInstruction(memory, 0x8C13_0818, 0xBF41);
        var cpu = new Sh4Cpu(memory, 0x8C13_07D6);
        InitializeDoa2EmptyTaskHelperCallerLoopState(cpu, memory, index: 0);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2EmptyTaskHelperCallerLoop(start, 520, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void FastForwardsDoa2UnrolledWordCopyReturn()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2UnrolledWordCopyReturn(normalMemory);
        WriteWordCopyData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2UnrolledWordCopyReturn(fastMemory);
        WriteWordCopyData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_E60A);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_E60A);
        InitializeDoa2WordCopyState(normal);
        InitializeDoa2WordCopyState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2UnrolledWordCopyReturn(fastStart, 100, out var skippedInstructions));
        Assert.Equal(16UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        for (var offset = 0u; offset < 32; offset += 4)
        {
            Assert.Equal(normalMemory.ReadUInt32(0x8C20_2000 + offset), fastMemory.ReadUInt32(0x8C20_2000 + offset));
            Assert.Equal(normalMemory.ReadUInt32(0x8C20_1000 + offset), fastMemory.ReadUInt32(0x8C20_2000 + offset));
        }
    }

    [Fact]
    public void DoesNotFastForwardDoa2UnrolledWordCopyReturnWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2UnrolledWordCopyReturn(memory);
        WriteWordCopyData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_E60A);
        InitializeDoa2WordCopyState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2UnrolledWordCopyReturn(start, 15, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_E60Cu, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2UnrolledWordCopyReturnWhenMemoryIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2UnrolledWordCopyReturn(memory);
        WriteWordCopyData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_E60A);
        InitializeDoa2WordCopyState(cpu);
        cpu.State.R[2] = 0xA500_0000;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2UnrolledWordCopyReturn(start, 100, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void FastForwardsDoa2AicaZeroMailboxTimeoutLoopPartially()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2AicaZeroMailboxTimeoutLoop(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2AicaZeroMailboxTimeoutLoop(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_4D4C);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_4D4C);
        InitializeDoa2AicaZeroMailboxTimeoutState(normal, 0);
        InitializeDoa2AicaZeroMailboxTimeoutState(fast, 0);

        var normalStart = StepMany(normal, 6);
        var fastStart = StepMany(fast, 6);
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2AicaZeroMailboxTimeoutLoop(fastStart, 12, out var skippedInstructions));
        Assert.Equal(12UL, skippedInstructions);
        StepMany(normal, skippedInstructions);

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
    }

    [Fact]
    public void FastForwardsDoa2AicaZeroMailboxTimeoutLoopToEnd()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2AicaZeroMailboxTimeoutLoop(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2AicaZeroMailboxTimeoutLoop(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_4D4C);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_4D4C);
        InitializeDoa2AicaZeroMailboxTimeoutState(normal, 0x003F_FFFE);
        InitializeDoa2AicaZeroMailboxTimeoutState(fast, 0x003F_FFFE);

        var normalStart = StepMany(normal, 6);
        var fastStart = StepMany(fast, 6);
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2AicaZeroMailboxTimeoutLoop(fastStart, 6, out var skippedInstructions));
        Assert.Equal(6UL, skippedInstructions);
        StepMany(normal, skippedInstructions);

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(0x8C10_4D58u, fast.State.Pc);
        Assert.Equal(0x0040_0000u, fast.State.R[4]);
        Assert.True(fast.State.T);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
    }

    [Fact]
    public void DoesNotFastForwardDoa2AicaZeroMailboxTimeoutLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2AicaZeroMailboxTimeoutLoop(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_4D4C);
        InitializeDoa2AicaZeroMailboxTimeoutState(cpu, 0);

        var start = StepMany(cpu, 6);

        Assert.False(cpu.TryFastForwardDoa2AicaZeroMailboxTimeoutLoop(start, 5, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_4D4Cu, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2AicaZeroMailboxTimeoutLoopWhenMailboxIsNonzero()
    {
        var memory = new DreamcastMemory();
        WriteDoa2AicaZeroMailboxTimeoutLoop(memory);
        memory.WriteUInt32(0xA080_005C, 1);
        var cpu = new Sh4Cpu(memory, 0x8C10_4D4C);
        InitializeDoa2AicaZeroMailboxTimeoutState(cpu, 1);
        cpu.State.R[3] = 0;
        cpu.State.Pc = 0x8C10_4D4C;
        cpu.State.T = false;
        var step = new Sh4StepResult(0x8C10_4D56, 0x8BF9, "bf 0x8C104D4C ; taken", 1);

        Assert.False(cpu.TryFastForwardDoa2AicaZeroMailboxTimeoutLoop(step, 6, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void DoesNotFastForwardDoa2AicaZeroMailboxTimeoutLoopWhenLiteralDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2AicaZeroMailboxTimeoutLoop(memory);
        memory.WriteUInt32(0x8C10_4E10, 0xA080_0060);
        var cpu = new Sh4Cpu(memory, 0x8C10_4D4C);
        InitializeDoa2AicaZeroMailboxTimeoutState(cpu, 0);

        var start = StepMany(cpu, 6);

        Assert.False(cpu.TryFastForwardDoa2AicaZeroMailboxTimeoutLoop(start, 6, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void FastForwardsDoa2HighRamZeroFillLoopPartially()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2HighRamZeroFillLoop(normalMemory);
        FillBytes(normalMemory, 0x8C20_1000, 6, 0xAA);
        var fastMemory = new DreamcastMemory();
        WriteDoa2HighRamZeroFillLoop(fastMemory);
        FillBytes(fastMemory, 0x8C20_1000, 6, 0xAA);
        var normal = new Sh4Cpu(normalMemory, 0x8C11_333C);
        var fast = new Sh4Cpu(fastMemory, 0x8C11_333C);
        InitializeDoa2HighRamZeroFillState(normalMemory, normal, 0x8C20_1000, 0x8C20_1006);
        InitializeDoa2HighRamZeroFillState(fastMemory, fast, 0x8C20_1000, 0x8C20_1006);

        var normalStart = StepMany(normal, 5);
        var fastStart = StepMany(fast, 5);
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2HighRamZeroFillLoop(fastStart, 10, out var skippedInstructions));
        Assert.Equal(10UL, skippedInstructions);
        StepMany(normal, skippedInstructions);

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(0u, fastMemory.ReadByte(0x8C20_1000));
        Assert.Equal(0u, fastMemory.ReadByte(0x8C20_1001));
        Assert.Equal(0u, fastMemory.ReadByte(0x8C20_1002));
        Assert.Equal(0xAAu, fastMemory.ReadByte(0x8C20_1003));
    }

    [Fact]
    public void FastForwardsDoa2HighRamZeroFillLoopToEnd()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2HighRamZeroFillLoop(normalMemory);
        FillBytes(normalMemory, 0x8C20_2000, 3, 0xAA);
        var fastMemory = new DreamcastMemory();
        WriteDoa2HighRamZeroFillLoop(fastMemory);
        FillBytes(fastMemory, 0x8C20_2000, 3, 0xAA);
        var normal = new Sh4Cpu(normalMemory, 0x8C11_333C);
        var fast = new Sh4Cpu(fastMemory, 0x8C11_333C);
        InitializeDoa2HighRamZeroFillState(normalMemory, normal, 0x8C20_2000, 0x8C20_2003);
        InitializeDoa2HighRamZeroFillState(fastMemory, fast, 0x8C20_2000, 0x8C20_2003);

        var normalStart = StepMany(normal, 5);
        var fastStart = StepMany(fast, 5);
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2HighRamZeroFillLoop(fastStart, 10, out var skippedInstructions));
        Assert.Equal(10UL, skippedInstructions);
        StepMany(normal, skippedInstructions);

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(0x8C11_3346u, fast.State.Pc);
        Assert.Equal(0x8C20_2003u, fast.State.R[5]);
        Assert.True(fast.State.T);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        for (var offset = 0u; offset < 3; offset++)
        {
            Assert.Equal(0u, fastMemory.ReadByte(0x8C20_2000 + offset));
        }
    }

    [Fact]
    public void DoesNotFastForwardDoa2HighRamZeroFillLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2HighRamZeroFillLoop(memory);
        var cpu = new Sh4Cpu(memory, 0x8C11_333C);
        InitializeDoa2HighRamZeroFillState(memory, cpu, 0x8C20_3000, 0x8C20_3004);

        var start = StepMany(cpu, 5);

        Assert.False(cpu.TryFastForwardDoa2HighRamZeroFillLoop(start, 4, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_333Cu, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2HighRamZeroFillLoopWhenFillByteIsNonzero()
    {
        var memory = new DreamcastMemory();
        WriteDoa2HighRamZeroFillLoop(memory);
        var cpu = new Sh4Cpu(memory, 0x8C11_333C);
        InitializeDoa2HighRamZeroFillState(memory, cpu, 0x8C20_4000, 0x8C20_4004);
        cpu.State.R[4] = 1;

        var start = StepMany(cpu, 5);

        Assert.False(cpu.TryFastForwardDoa2HighRamZeroFillLoop(start, 5, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void DoesNotFastForwardDoa2HighRamZeroFillLoopWhenLiteralDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2HighRamZeroFillLoop(memory);
        memory.WriteUInt32(0x8C11_343C, 0x8C14_D4E4);
        var cpu = new Sh4Cpu(memory, 0x8C11_333C);
        InitializeDoa2HighRamZeroFillState(memory, cpu, 0x8C20_5000, 0x8C20_5004);

        var start = StepMany(cpu, 5);

        Assert.False(cpu.TryFastForwardDoa2HighRamZeroFillLoop(start, 5, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void FastForwardsDoa2CacheBlockPurgeLoopPartially()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2CacheBlockPurgeLoop(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2CacheBlockPurgeLoop(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C11_1ED8);
        var fast = new Sh4Cpu(fastMemory, 0x8C11_1ED8);
        InitializeDoa2CacheBlockPurgeState(normal, 0x8CC0_0000, 5);
        InitializeDoa2CacheBlockPurgeState(fast, 0x8CC0_0000, 5);

        var normalStart = StepMany(normal, 4);
        var fastStart = StepMany(fast, 4);
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2CacheBlockPurgeLoop(fastStart, 8, out var skippedInstructions));
        Assert.Equal(8UL, skippedInstructions);
        StepMany(normal, skippedInstructions);

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(0x8CC0_0060u, fast.State.R[0]);
        Assert.Equal(2u, fast.State.R[5]);
    }

    [Fact]
    public void FastForwardsDoa2CacheBlockPurgeLoopToEnd()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2CacheBlockPurgeLoop(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2CacheBlockPurgeLoop(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C11_1ED8);
        var fast = new Sh4Cpu(fastMemory, 0x8C11_1ED8);
        InitializeDoa2CacheBlockPurgeState(normal, 0x8CC0_1000, 3);
        InitializeDoa2CacheBlockPurgeState(fast, 0x8CC0_1000, 3);

        var normalStart = StepMany(normal, 4);
        var fastStart = StepMany(fast, 4);
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2CacheBlockPurgeLoop(fastStart, 8, out var skippedInstructions));
        Assert.Equal(8UL, skippedInstructions);
        StepMany(normal, skippedInstructions);

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(0x8C11_1EE0u, fast.State.Pc);
        Assert.Equal(0x8CC0_1060u, fast.State.R[0]);
        Assert.Equal(0u, fast.State.R[5]);
        Assert.True(fast.State.T);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
    }

    [Fact]
    public void DoesNotFastForwardDoa2CacheBlockPurgeLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2CacheBlockPurgeLoop(memory);
        var cpu = new Sh4Cpu(memory, 0x8C11_1ED8);
        InitializeDoa2CacheBlockPurgeState(cpu, 0x8CC0_2000, 3);

        var start = StepMany(cpu, 4);

        Assert.False(cpu.TryFastForwardDoa2CacheBlockPurgeLoop(start, 3, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_1ED8u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2CacheBlockPurgeLoopWhenOpcodeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2CacheBlockPurgeLoop(memory);
        WriteInstruction(memory, 0x8C11_1EDC, 0x7024);
        var cpu = new Sh4Cpu(memory, 0x8C11_1ED8);
        InitializeDoa2CacheBlockPurgeState(cpu, 0x8CC0_3000, 3);

        var start = StepMany(cpu, 4);

        Assert.False(cpu.TryFastForwardDoa2CacheBlockPurgeLoop(start, 4, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void FastForwardsDoa2ScratchVectorCopyWrapper()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2ScratchVectorCopyWrapper(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2ScratchVectorCopyWrapper(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_3AF0);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_3AF0);
        InitializeDoa2ScratchVectorCopyWrapperState(normal);
        InitializeDoa2ScratchVectorCopyWrapperState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2ScratchVectorCopyWrapper(fastStart, 27, out var skippedInstructions));
        Assert.Equal(27UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Fr, fast.State.Fr);
        Assert.Equal(normal.State.Fpscr, fast.State.Fpscr);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        for (var offset = 0u; offset < 28; offset += 4)
        {
            Assert.Equal(normalMemory.ReadUInt32(0x8C1C_A920 + offset), fastMemory.ReadUInt32(0x8C1C_A920 + offset));
        }

        Assert.Equal(normalMemory.ReadUInt32(0x8C20_7FF8), fastMemory.ReadUInt32(0x8C20_7FF8));
        Assert.Equal(normalMemory.ReadUInt32(0x8C20_7FFC), fastMemory.ReadUInt32(0x8C20_7FFC));
    }

    [Fact]
    public void DoesNotFastForwardDoa2ScratchVectorCopyWrapperWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ScratchVectorCopyWrapper(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_3AF0);
        InitializeDoa2ScratchVectorCopyWrapperState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ScratchVectorCopyWrapper(start, 26, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_3AF2u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ScratchVectorCopyWrapperWhenFpscrModeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ScratchVectorCopyWrapper(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_3AF0);
        InitializeDoa2ScratchVectorCopyWrapperState(cpu);
        cpu.State.Fpscr = Sh4State.FpscrSzBit;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ScratchVectorCopyWrapper(start, 27, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ScratchVectorCopyWrapperWhenLiteralDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ScratchVectorCopyWrapper(memory);
        memory.WriteUInt32(0x8C10_E638, 0x8C10_E60A);
        var cpu = new Sh4Cpu(memory, 0x8C10_3AF0);
        InitializeDoa2ScratchVectorCopyWrapperState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ScratchVectorCopyWrapper(start, 27, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ScratchVectorCopyWrapperWhenStackIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ScratchVectorCopyWrapper(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_3AF0);
        InitializeDoa2ScratchVectorCopyWrapperState(cpu);
        cpu.State.R[15] = 0xA500_0008;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ScratchVectorCopyWrapper(start, 27, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void FastForwardsDoa2TableEntryAddressHelper()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2TableEntryAddressHelper(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2TableEntryAddressHelper(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C0F_29DE);
        var fast = new Sh4Cpu(fastMemory, 0x8C0F_29DE);
        InitializeDoa2TableEntryAddressHelperState(normal, 7);
        InitializeDoa2TableEntryAddressHelperState(fast, 7);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2TableEntryAddressHelper(fastStart, 10, out var skippedInstructions));
        Assert.Equal(10UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
    }

    [Fact]
    public void FastForwardsDoa2TableEntryAddressHelperWithWrappedShiftCarry()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2TableEntryAddressHelper(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2TableEntryAddressHelper(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C0F_29DE);
        var fast = new Sh4Cpu(fastMemory, 0x8C0F_29DE);
        InitializeDoa2TableEntryAddressHelperState(normal, 0x0800_0000);
        InitializeDoa2TableEntryAddressHelperState(fast, 0x0800_0000);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2TableEntryAddressHelper(fastStart, 10, out var skippedInstructions));
        Assert.Equal(10UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
    }

    [Fact]
    public void DoesNotFastForwardDoa2TableEntryAddressHelperWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2TableEntryAddressHelper(memory);
        var cpu = new Sh4Cpu(memory, 0x8C0F_29DE);
        InitializeDoa2TableEntryAddressHelperState(cpu, 3);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2TableEntryAddressHelper(start, 9, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C0F_29E0u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2TableEntryAddressHelperWhenLiteralDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2TableEntryAddressHelper(memory);
        memory.WriteUInt32(0x8C0F_2AD4, 0x8C20_0000);
        var cpu = new Sh4Cpu(memory, 0x8C0F_29DE);
        InitializeDoa2TableEntryAddressHelperState(cpu, 3);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2TableEntryAddressHelper(start, 10, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void FastForwardsDoa2ZeroStatusByteTableScan()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2ZeroStatusByteTableScan(normalMemory);
        WriteZeroStatusByteTableScanData(normalMemory);
        WriteZeroStatusByteTableScanStack(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2ZeroStatusByteTableScan(fastMemory);
        WriteZeroStatusByteTableScanData(fastMemory);
        WriteZeroStatusByteTableScanStack(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C0F_3678);
        var fast = new Sh4Cpu(fastMemory, 0x8C0F_3678);
        InitializeDoa2ZeroStatusByteTableScanState(normal, 0);
        InitializeDoa2ZeroStatusByteTableScanState(fast, 0);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2ZeroStatusByteTableScan(fastStart, 180, out var skippedInstructions));
        Assert.Equal(180UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(8, fastMemory.ReadByte(0x8C2A_DA94));
    }

    [Fact]
    public void FastForwardsDoa2ZeroStatusByteTableScanFromFinalEntry()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2ZeroStatusByteTableScan(normalMemory);
        WriteZeroStatusByteTableScanData(normalMemory);
        WriteZeroStatusByteTableScanStack(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2ZeroStatusByteTableScan(fastMemory);
        WriteZeroStatusByteTableScanData(fastMemory);
        WriteZeroStatusByteTableScanStack(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C0F_3678);
        var fast = new Sh4Cpu(fastMemory, 0x8C0F_3678);
        InitializeDoa2ZeroStatusByteTableScanState(normal, 7);
        InitializeDoa2ZeroStatusByteTableScanState(fast, 7);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2ZeroStatusByteTableScan(fastStart, 33, out var skippedInstructions));
        Assert.Equal(33UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(8, fastMemory.ReadByte(0x8C2A_DA94));
    }

    [Fact]
    public void DoesNotFastForwardDoa2ZeroStatusByteTableScanWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ZeroStatusByteTableScan(memory);
        WriteZeroStatusByteTableScanData(memory);
        WriteZeroStatusByteTableScanStack(memory);
        var cpu = new Sh4Cpu(memory, 0x8C0F_3678);
        InitializeDoa2ZeroStatusByteTableScanState(cpu, 0);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ZeroStatusByteTableScan(start, 179, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C0F_367Au, cpu.State.Pc);
        Assert.Equal(0, memory.ReadByte(0x8C2A_DA94));
    }

    [Fact]
    public void DoesNotFastForwardDoa2ZeroStatusByteTableScanWhenStatusByteIsNonzero()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ZeroStatusByteTableScan(memory);
        WriteZeroStatusByteTableScanData(memory);
        WriteZeroStatusByteTableScanStack(memory);
        memory.Write(0x8C2A_D830, [1]);
        var cpu = new Sh4Cpu(memory, 0x8C0F_3678);
        InitializeDoa2ZeroStatusByteTableScanState(cpu, 0);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ZeroStatusByteTableScan(start, 180, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C0F_367Au, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ZeroStatusByteTableScanWhenStackIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ZeroStatusByteTableScan(memory);
        WriteZeroStatusByteTableScanData(memory);
        WriteZeroStatusByteTableScanStack(memory);
        var cpu = new Sh4Cpu(memory, 0x8C0F_3678);
        InitializeDoa2ZeroStatusByteTableScanState(cpu, 0);
        cpu.State.R[15] = 0xA500_0000;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ZeroStatusByteTableScan(start, 180, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ZeroStatusByteTableScanWhenLiteralDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ZeroStatusByteTableScan(memory);
        WriteZeroStatusByteTableScanData(memory);
        WriteZeroStatusByteTableScanStack(memory);
        memory.WriteUInt32(0x8C0F_3708, 0x8C20_1000);
        var cpu = new Sh4Cpu(memory, 0x8C0F_3678);
        InitializeDoa2ZeroStatusByteTableScanState(cpu, 0);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ZeroStatusByteTableScan(start, 180, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void FastForwardsDoa2ZeroRecordGroupScan()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2ZeroRecordGroupScan(normalMemory);
        WriteZeroRecordGroupData(normalMemory);
        WriteZeroRecordGroupStack(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2ZeroRecordGroupScan(fastMemory);
        WriteZeroRecordGroupData(fastMemory);
        WriteZeroRecordGroupStack(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C01_3BF6);
        var fast = new Sh4Cpu(fastMemory, 0x8C01_3BF6);
        InitializeDoa2ZeroRecordGroupScanState(normal, 0);
        InitializeDoa2ZeroRecordGroupScanState(fast, 0);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2ZeroRecordGroupScan(fastStart, 376, out var skippedInstructions));
        Assert.Equal(376UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
    }

    [Fact]
    public void FastForwardsDoa2ZeroRecordGroupScanFromFinalGroup()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2ZeroRecordGroupScan(normalMemory);
        WriteZeroRecordGroupData(normalMemory);
        WriteZeroRecordGroupStack(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2ZeroRecordGroupScan(fastMemory);
        WriteZeroRecordGroupData(fastMemory);
        WriteZeroRecordGroupStack(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C01_3BF6);
        var fast = new Sh4Cpu(fastMemory, 0x8C01_3BF6);
        InitializeDoa2ZeroRecordGroupScanState(normal, 28);
        InitializeDoa2ZeroRecordGroupScanState(fast, 28);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2ZeroRecordGroupScan(fastStart, 54, out var skippedInstructions));
        Assert.Equal(54UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ZeroRecordGroupScanWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ZeroRecordGroupScan(memory);
        WriteZeroRecordGroupData(memory);
        WriteZeroRecordGroupStack(memory);
        var cpu = new Sh4Cpu(memory, 0x8C01_3BF6);
        InitializeDoa2ZeroRecordGroupScanState(cpu, 0);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ZeroRecordGroupScan(start, 375, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C01_3BF8u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ZeroRecordGroupScanWhenRecordIsNonzero()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ZeroRecordGroupScan(memory);
        WriteZeroRecordGroupData(memory);
        WriteZeroRecordGroupStack(memory);
        memory.WriteUInt16(0x8C1E_FFB8 + (12 * 8), 1);
        var cpu = new Sh4Cpu(memory, 0x8C01_3BF6);
        InitializeDoa2ZeroRecordGroupScanState(cpu, 0);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ZeroRecordGroupScan(start, 376, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ZeroRecordGroupScanWhenStackIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ZeroRecordGroupScan(memory);
        WriteZeroRecordGroupData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C01_3BF6);
        InitializeDoa2ZeroRecordGroupScanState(cpu, 0);
        cpu.State.R[15] = 0xA500_0000;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ZeroRecordGroupScan(start, 376, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void FastForwardsDoa2ColorPackCommonPath()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2ColorPackCommonPath(normalMemory);
        WriteColorPackData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2ColorPackCommonPath(fastMemory);
        WriteColorPackData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_06EC);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_06EC);
        InitializeDoa2ColorPackState(normal);
        InitializeDoa2ColorPackState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2ColorPackCommonPath(fastStart, 100, out var skippedInstructions));
        Assert.Equal(50UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Fr, fast.State.Fr);
        Assert.Equal(normal.State.Fpul, fast.State.Fpul);
        Assert.Equal(normal.State.Fpscr, fast.State.Fpscr);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        for (var offset = 0u; offset < 28; offset += 4)
        {
            Assert.Equal(normalMemory.ReadUInt32(0x8C20_2000 + offset), fastMemory.ReadUInt32(0x8C20_2000 + offset));
        }
    }

    [Fact]
    public void DoesNotFastForwardDoa2ColorPackCommonPathWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ColorPackCommonPath(memory);
        WriteColorPackData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_06EC);
        InitializeDoa2ColorPackState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ColorPackCommonPath(start, 49, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_06EEu, cpu.State.Pc);
        Assert.Equal(1UL, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ColorPackCommonPathWhenFpscrModeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ColorPackCommonPath(memory);
        WriteColorPackData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_06EC);
        InitializeDoa2ColorPackState(cpu);
        cpu.State.Fpscr = Sh4State.FpscrPrBit;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ColorPackCommonPath(start, 100, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_06EEu, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ColorPackCommonPathWhenFlagsSelectAlternatePath()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ColorPackCommonPath(memory);
        WriteColorPackData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_06EC);
        InitializeDoa2ColorPackState(cpu);
        memory.WriteUInt32(0x8C20_1000 + 52, 0x0000_8000);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ColorPackCommonPath(start, 100, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_06EEu, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2TaEmitCommonPath()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2TaEmitCommonPath(normalMemory);
        WriteTaEmitData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2TaEmitCommonPath(fastMemory);
        WriteTaEmitData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_077C);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_077C);
        InitializeDoa2TaEmitState(normal);
        InitializeDoa2TaEmitState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2TaEmitCommonPath(fastStart, 200, out var skippedInstructions));
        Assert.Equal(132UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Fr, fast.State.Fr);
        Assert.Equal(normal.State.Fpul, fast.State.Fpul);
        Assert.Equal(normal.State.Fpscr, fast.State.Fpscr);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(normalMemory.ReadUInt32(0x8C20_5008), fastMemory.ReadUInt32(0x8C20_5008));
        Assert.Equal(
            normalMemory.DeviceAccesses.Select(access => access with { Pc = null }),
            fastMemory.DeviceAccesses.Select(access => access with { Pc = null }));
    }

    [Fact]
    public void DoesNotFastForwardDoa2TaEmitCommonPathWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2TaEmitCommonPath(memory);
        WriteTaEmitData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_077C);
        InitializeDoa2TaEmitState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2TaEmitCommonPath(start, 131, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_077Eu, cpu.State.Pc);
        Assert.Equal(1UL, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void DoesNotFastForwardDoa2TaEmitCommonPathWhenAlternateFlagPathIsSelected()
    {
        var memory = new DreamcastMemory();
        WriteDoa2TaEmitCommonPath(memory);
        WriteTaEmitData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_077C);
        InitializeDoa2TaEmitState(cpu);
        memory.WriteUInt32(0x8C20_1000 + 52, 0x0002_0000);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2TaEmitCommonPath(start, 200, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_077Eu, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2TaEmitCommonPathWhenFpscrModeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2TaEmitCommonPath(memory);
        WriteTaEmitData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_077C);
        InitializeDoa2TaEmitState(cpu);
        cpu.State.Fpscr = Sh4State.FpscrPrBit;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2TaEmitCommonPath(start, 200, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_077Eu, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2TextGlyphSetupCommonPath()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2TextGlyphSetupCommonPath(normalMemory);
        WriteTextGlyphSetupData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2TextGlyphSetupCommonPath(fastMemory);
        WriteTextGlyphSetupData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C0E_1E08);
        var fast = new Sh4Cpu(fastMemory, 0x8C0E_1E08);
        InitializeDoa2TextGlyphSetupState(normal);
        InitializeDoa2TextGlyphSetupState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2TextGlyphSetupCommonPath(fastStart, 100, out var skippedInstructions));
        Assert.Equal(62UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Fr, fast.State.Fr);
        Assert.Equal(normal.State.Fpul, fast.State.Fpul);
        Assert.Equal(normal.State.Fpscr, fast.State.Fpscr);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        for (var offset = 0u; offset <= 36; offset += 4)
        {
            Assert.Equal(normalMemory.ReadUInt32(0x8C20_4000 + offset), fastMemory.ReadUInt32(0x8C20_4000 + offset));
        }

        Assert.Equal(0x8C10_0430u, fast.State.Pc);
        Assert.Equal(0x8C0E_1EB2u, fast.State.Pr);
        Assert.Equal(0x8C20_4000u, fast.State.R[4]);
    }

    [Fact]
    public void DoesNotFastForwardDoa2TextGlyphSetupCommonPathWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2TextGlyphSetupCommonPath(memory);
        WriteTextGlyphSetupData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C0E_1E08);
        InitializeDoa2TextGlyphSetupState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2TextGlyphSetupCommonPath(start, 61, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C0E_1E0Au, cpu.State.Pc);
        Assert.Equal(1UL, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void DoesNotFastForwardDoa2TextGlyphSetupCommonPathWhenFpscrModeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2TextGlyphSetupCommonPath(memory);
        WriteTextGlyphSetupData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C0E_1E08);
        InitializeDoa2TextGlyphSetupState(cpu);
        cpu.State.Fpscr = Sh4State.FpscrSzBit;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2TextGlyphSetupCommonPath(start, 100, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C0E_1E0Au, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2TextGlyphSetupCommonPathWhenAlternateCharacterPathIsSelected()
    {
        var memory = new DreamcastMemory();
        WriteDoa2TextGlyphSetupCommonPath(memory);
        WriteTextGlyphSetupData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C0E_1E08);
        InitializeDoa2TextGlyphSetupState(cpu);
        memory.Write(0x8C20_2000, [(byte)'@']);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2TextGlyphSetupCommonPath(start, 100, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C0E_1E0Au, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2TextGlyphSetupCommonPathOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2TextGlyphSetupCommonPath(memory);
        WriteTextGlyphSetupData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C0E_1E08);
        InitializeDoa2TextGlyphSetupState(cpu);
        cpu.State.R[8] = 0xA500_0000;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2TextGlyphSetupCommonPath(start, 100, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C0E_1E0Au, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2TextAdvanceToNextGlyph()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2TextAdvanceToNextGlyph(normalMemory);
        WriteTextAdvanceData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2TextAdvanceToNextGlyph(fastMemory);
        WriteTextAdvanceData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C0E_1EB2);
        var fast = new Sh4Cpu(fastMemory, 0x8C0E_1EB2);
        InitializeDoa2TextAdvanceState(normal);
        InitializeDoa2TextAdvanceState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2TextAdvanceToNextGlyph(fastStart, 20, out var skippedInstructions));
        Assert.Equal(9UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(normalMemory.ReadUInt16(0x8C20_1000 + 22), fastMemory.ReadUInt16(0x8C20_1000 + 22));
        Assert.Equal(0x8C0E_1E08u, fast.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2TextAdvanceToNextGlyphWhenStringEnds()
    {
        var memory = new DreamcastMemory();
        WriteDoa2TextAdvanceToNextGlyph(memory);
        WriteTextAdvanceData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C0E_1EB2);
        InitializeDoa2TextAdvanceState(cpu);
        memory.Write(0x8C20_2001, [0]);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2TextAdvanceToNextGlyph(start, 20, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C0E_1EB4u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2TextAdvanceToNextGlyphWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2TextAdvanceToNextGlyph(memory);
        WriteTextAdvanceData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C0E_1EB2);
        InitializeDoa2TextAdvanceState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2TextAdvanceToNextGlyph(start, 8, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C0E_1EB4u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2Fac40TrigArgumentWrapper()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2Fac40TrigArgumentWrapper(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2Fac40TrigArgumentWrapper(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C0F_AC40);
        var fast = new Sh4Cpu(fastMemory, 0x8C0F_AC40);
        InitializeDoa2Fac40State(normal);
        InitializeDoa2Fac40State(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2Fac40TrigArgumentWrapper(fastStart, 20, out var skippedInstructions));
        Assert.Equal(8UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(normalMemory.ReadUInt32(0x8C20_3FFC), fastMemory.ReadUInt32(0x8C20_3FFC));
        Assert.Equal(0x8C0F_B1C0u, fast.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2Fac40TrigArgumentWrapperWhenAlternatePathIsSelected()
    {
        var memory = new DreamcastMemory();
        WriteDoa2Fac40TrigArgumentWrapper(memory);
        var cpu = new Sh4Cpu(memory, 0x8C0F_AC40);
        InitializeDoa2Fac40State(cpu);
        cpu.State.R[4] = 0x8001;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2Fac40TrigArgumentWrapper(start, 20, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C0F_AC42u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2RendererPrologueCommonPath()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2RendererPrologueCommonPath(normalMemory);
        WriteRendererPrologueData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2RendererPrologueCommonPath(fastMemory);
        WriteRendererPrologueData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_0430);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_0430);
        InitializeDoa2RendererPrologueState(normal);
        InitializeDoa2RendererPrologueState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2RendererPrologueCommonPath(fastStart, 40, out var skippedInstructions));
        Assert.Equal(19UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Fr, fast.State.Fr);
        Assert.Equal(normal.State.Fpscr, fast.State.Fpscr);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        for (var offset = 0u; offset < 108; offset += 4)
        {
            Assert.Equal(normalMemory.ReadUInt32(0x8C20_5000 - 108 + offset), fastMemory.ReadUInt32(0x8C20_5000 - 108 + offset));
        }

        Assert.Equal(0x8C10_04A0u, fast.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererPrologueCommonPathWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererPrologueCommonPath(memory);
        WriteRendererPrologueData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0430);
        InitializeDoa2RendererPrologueState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererPrologueCommonPath(start, 18, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0432u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererPrologueCommonPathWhenAlternatePathIsSelected()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererPrologueCommonPath(memory);
        WriteRendererPrologueData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0430);
        InitializeDoa2RendererPrologueState(cpu);
        memory.WriteUInt32(0x8C20_1000, 0x0000_0800);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererPrologueCommonPath(start, 40, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0432u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererPrologueCommonPathWhenFpscrModeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererPrologueCommonPath(memory);
        WriteRendererPrologueData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0430);
        InitializeDoa2RendererPrologueState(cpu);
        cpu.State.Fpscr = Sh4State.FpscrPrBit;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererPrologueCommonPath(start, 40, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0432u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2RendererMode2EntryToFirstTrigCall()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2RendererMode2EntryToFirstTrigCall(normalMemory);
        WriteRendererMode2EntryToFirstTrigCallData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2RendererMode2EntryToFirstTrigCall(fastMemory);
        WriteRendererMode2EntryToFirstTrigCallData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_0430);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_0430);
        InitializeDoa2RendererPrologueState(normal);
        InitializeDoa2RendererPrologueState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2RendererMode2EntryToFirstTrigCall(fastStart, 80, out var skippedInstructions));
        Assert.Equal(67UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Fr, fast.State.Fr);
        Assert.Equal(normal.State.Fpscr, fast.State.Fpscr);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        for (var offset = 0u; offset < 108; offset += 4)
        {
            Assert.Equal(normalMemory.ReadUInt32(0x8C20_5000 - 108 + offset), fastMemory.ReadUInt32(0x8C20_5000 - 108 + offset));
        }

        Assert.Equal(0x8C0F_B1C0u, fast.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererMode2EntryToFirstTrigCallWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererMode2EntryToFirstTrigCall(memory);
        WriteRendererMode2EntryToFirstTrigCallData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0430);
        InitializeDoa2RendererPrologueState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererMode2EntryToFirstTrigCall(start, 66, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0432u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererMode2EntryToFirstTrigCallWhenModeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererMode2EntryToFirstTrigCall(memory);
        WriteRendererMode2EntryToFirstTrigCallData(memory);
        memory.WriteUInt32(0x8C20_1000 + 48, 1);
        var cpu = new Sh4Cpu(memory, 0x8C10_0430);
        InitializeDoa2RendererPrologueState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererMode2EntryToFirstTrigCall(start, 80, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0432u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererMode2EntryToFirstTrigCallWhenFpscrModeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererMode2EntryToFirstTrigCall(memory);
        WriteRendererMode2EntryToFirstTrigCallData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0430);
        InitializeDoa2RendererPrologueState(cpu);
        cpu.State.Fpscr = Sh4State.FpscrPrBit;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererMode2EntryToFirstTrigCall(start, 80, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0432u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2RendererMode2LookupCommonPath()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2RendererMode2LookupCommonPath(normalMemory);
        WriteRendererMode2LookupData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2RendererMode2LookupCommonPath(fastMemory);
        WriteRendererMode2LookupData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_04A0);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_04A0);
        InitializeDoa2RendererMode2LookupState(normal);
        InitializeDoa2RendererMode2LookupState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2RendererMode2LookupCommonPath(fastStart, 40, out var skippedInstructions));
        Assert.Equal(20UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(0x8C10_0500u, fast.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererMode2LookupCommonPathWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererMode2LookupCommonPath(memory);
        WriteRendererMode2LookupData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_04A0);
        InitializeDoa2RendererMode2LookupState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererMode2LookupCommonPath(start, 19, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_04A2u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererMode2LookupCommonPathWhenIndexIsMissing()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererMode2LookupCommonPath(memory);
        WriteRendererMode2LookupData(memory);
        memory.WriteUInt16(0x8C20_B3D4 + (0x1A7 * 2), 0xFFFF);
        var cpu = new Sh4Cpu(memory, 0x8C10_04A0);
        InitializeDoa2RendererMode2LookupState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererMode2LookupCommonPath(start, 40, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_04A2u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererMode2LookupCommonPathWhenModeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererMode2LookupCommonPath(memory);
        WriteRendererMode2LookupData(memory);
        memory.WriteUInt32(0x8C20_1000 + 48, 1);
        var cpu = new Sh4Cpu(memory, 0x8C10_04A0);
        InitializeDoa2RendererMode2LookupState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererMode2LookupCommonPath(start, 40, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_04A2u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2RendererMode2TrigSetupToFirstCall()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2RendererMode2TrigSetupToFirstCall(normalMemory);
        WriteRendererMode2TrigSetupData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2RendererMode2TrigSetupToFirstCall(fastMemory);
        WriteRendererMode2TrigSetupData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_0500);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_0500);
        InitializeDoa2RendererMode2TrigSetupState(normal);
        InitializeDoa2RendererMode2TrigSetupState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2RendererMode2TrigSetupToFirstCall(fastStart, 40, out var skippedInstructions));
        Assert.Equal(26UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Fr, fast.State.Fr);
        Assert.Equal(normal.State.Fpscr, fast.State.Fpscr);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(normalMemory.ReadUInt32(0x8C20_3004), fastMemory.ReadUInt32(0x8C20_3004));
        Assert.Equal(0x8C0F_B1C0u, fast.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererMode2TrigSetupToFirstCallWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererMode2TrigSetupToFirstCall(memory);
        WriteRendererMode2TrigSetupData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0500);
        InitializeDoa2RendererMode2TrigSetupState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererMode2TrigSetupToFirstCall(start, 25, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0502u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererMode2TrigSetupToFirstCallWhenFpscrModeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererMode2TrigSetupToFirstCall(memory);
        WriteRendererMode2TrigSetupData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0500);
        InitializeDoa2RendererMode2TrigSetupState(cpu);
        cpu.State.Fpscr = Sh4State.FpscrSzBit;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererMode2TrigSetupToFirstCall(start, 40, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0502u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererMode2TrigSetupToFirstCallWhenMemoryIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererMode2TrigSetupToFirstCall(memory);
        WriteRendererMode2TrigSetupData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0500);
        InitializeDoa2RendererMode2TrigSetupState(cpu);
        cpu.State.R[12] = 0xA500_0000;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererMode2TrigSetupToFirstCall(start, 40, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0502u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2RendererSecondTrigCallBridge()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2RendererSecondTrigCallBridge(normalMemory);
        WriteRendererSecondTrigCallData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2RendererSecondTrigCallBridge(fastMemory);
        WriteRendererSecondTrigCallData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_0536);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_0536);
        InitializeDoa2RendererSecondTrigCallState(normal);
        InitializeDoa2RendererSecondTrigCallState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2RendererSecondTrigCallBridge(fastStart, 10, out var skippedInstructions));
        Assert.Equal(4UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Fr, fast.State.Fr);
        Assert.Equal(normal.State.Fpscr, fast.State.Fpscr);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(normalMemory.ReadUInt32(0x8C20_3008), fastMemory.ReadUInt32(0x8C20_3008));
        Assert.Equal(0x8C0F_AC40u, fast.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererSecondTrigCallBridgeWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererSecondTrigCallBridge(memory);
        WriteRendererSecondTrigCallData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0536);
        InitializeDoa2RendererSecondTrigCallState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererSecondTrigCallBridge(start, 3, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0538u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererSecondTrigCallBridgeWhenFpscrModeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererSecondTrigCallBridge(memory);
        WriteRendererSecondTrigCallData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0536);
        InitializeDoa2RendererSecondTrigCallState(cpu);
        cpu.State.Fpscr = Sh4State.FpscrPrBit;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererSecondTrigCallBridge(start, 10, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0538u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererSecondTrigCallBridgeWhenMemoryIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererSecondTrigCallBridge(memory);
        WriteRendererSecondTrigCallData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0536);
        InitializeDoa2RendererSecondTrigCallState(cpu);
        cpu.State.R[15] = 0xA500_0000;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererSecondTrigCallBridge(start, 10, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0538u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2RendererPostSecondTrigBridge()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2RendererPostSecondTrigBridge(normalMemory);
        WriteRendererPostSecondTrigData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2RendererPostSecondTrigBridge(fastMemory);
        WriteRendererPostSecondTrigData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_0540);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_0540);
        InitializeDoa2RendererPostSecondTrigState(normal);
        InitializeDoa2RendererPostSecondTrigState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2RendererPostSecondTrigBridge(fastStart, 20, out var skippedInstructions));
        Assert.Equal(13UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Fr, fast.State.Fr);
        Assert.Equal(normal.State.Fpscr, fast.State.Fpscr);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(0x8C10_0A30u, fast.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererPostSecondTrigBridgeWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererPostSecondTrigBridge(memory);
        WriteRendererPostSecondTrigData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0540);
        InitializeDoa2RendererPostSecondTrigState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererPostSecondTrigBridge(start, 12, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0542u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererPostSecondTrigBridgeWhenFpscrModeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererPostSecondTrigBridge(memory);
        WriteRendererPostSecondTrigData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0540);
        InitializeDoa2RendererPostSecondTrigState(cpu);
        cpu.State.Fpscr = Sh4State.FpscrSzBit;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererPostSecondTrigBridge(start, 20, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0542u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererPostSecondTrigBridgeWhenMemoryIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererPostSecondTrigBridge(memory);
        WriteRendererPostSecondTrigData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0540);
        InitializeDoa2RendererPostSecondTrigState(cpu);
        cpu.State.R[15] = 0xA500_0000;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererPostSecondTrigBridge(start, 20, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0542u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2RendererPostCallScaleSetup()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2RendererPostCallScaleSetup(normalMemory);
        WriteRendererPostCallScaleSetupData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2RendererPostCallScaleSetup(fastMemory);
        WriteRendererPostCallScaleSetupData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_055C);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_055C);
        InitializeDoa2RendererPostCallScaleSetupState(normal);
        InitializeDoa2RendererPostCallScaleSetupState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2RendererPostCallScaleSetup(fastStart, 20, out var skippedInstructions));
        Assert.Equal(12UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Fr, fast.State.Fr);
        Assert.Equal(normal.State.Fpscr, fast.State.Fpscr);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(0x8C10_05B0u, fast.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererPostCallScaleSetupWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererPostCallScaleSetup(memory);
        WriteRendererPostCallScaleSetupData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_055C);
        InitializeDoa2RendererPostCallScaleSetupState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererPostCallScaleSetup(start, 11, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_055Eu, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererPostCallScaleSetupWhenFpscrModeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererPostCallScaleSetup(memory);
        WriteRendererPostCallScaleSetupData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_055C);
        InitializeDoa2RendererPostCallScaleSetupState(cpu);
        cpu.State.Fpscr = Sh4State.FpscrPrBit;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererPostCallScaleSetup(start, 20, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_055Eu, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererPostCallScaleSetupWhenConstantMemoryIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererPostCallScaleSetup(memory);
        WriteRendererPostCallScaleSetupData(memory);
        memory.WriteUInt32(0x8C10_0594, 0xA500_0000);
        var cpu = new Sh4Cpu(memory, 0x8C10_055C);
        InitializeDoa2RendererPostCallScaleSetupState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererPostCallScaleSetup(start, 20, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_055Eu, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2RendererModeWordSetupToColorPack()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2RendererModeWordSetupToColorPack(normalMemory);
        WriteRendererModeWordSetupData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2RendererModeWordSetupToColorPack(fastMemory);
        WriteRendererModeWordSetupData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_05B4);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_05B4);
        InitializeDoa2RendererModeWordSetupState(normal);
        InitializeDoa2RendererModeWordSetupState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2RendererModeWordSetupToColorPack(fastStart, 80, out var skippedInstructions));
        Assert.Equal(68UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Fr, fast.State.Fr);
        Assert.Equal(normal.State.Fpscr, fast.State.Fpscr);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        for (var offset = 0u; offset < 12; offset += 4)
        {
            Assert.Equal(normalMemory.ReadUInt32(0x8C20_2000 + offset), fastMemory.ReadUInt32(0x8C20_2000 + offset));
        }

        Assert.Equal(0x8C10_0AC0u, fast.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererModeWordSetupToColorPackWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererModeWordSetupToColorPack(memory);
        WriteRendererModeWordSetupData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_05B4);
        InitializeDoa2RendererModeWordSetupState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererModeWordSetupToColorPack(start, 67, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_05B6u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererModeWordSetupToColorPackWhenFpscrModeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererModeWordSetupToColorPack(memory);
        WriteRendererModeWordSetupData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_05B4);
        InitializeDoa2RendererModeWordSetupState(cpu);
        cpu.State.Fpscr = Sh4State.FpscrSzBit;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererModeWordSetupToColorPack(start, 80, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_05B6u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererModeWordSetupToColorPackWhenModeFlagsChooseAlternatePath()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererModeWordSetupToColorPack(memory);
        WriteRendererModeWordSetupData(memory);
        memory.WriteUInt32(0x8C20_1000 + 52, 0x0000_2005);
        var cpu = new Sh4Cpu(memory, 0x8C10_05B4);
        InitializeDoa2RendererModeWordSetupState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererModeWordSetupToColorPack(start, 80, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_05B6u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererModeWordSetupToColorPackWhenMemoryIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererModeWordSetupToColorPack(memory);
        WriteRendererModeWordSetupData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_05B4);
        InitializeDoa2RendererModeWordSetupState(cpu);
        cpu.State.R[12] = 0xA500_0000;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererModeWordSetupToColorPack(start, 80, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_05B6u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2RendererColorPackReturnBridge()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2RendererColorPackReturnBridge(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2RendererColorPackReturnBridge(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_0670);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_0670);
        InitializeDoa2RendererColorPackReturnState(normal);
        InitializeDoa2RendererColorPackReturnState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2RendererColorPackReturnBridge(fastStart, 2, out var skippedInstructions));
        Assert.Equal(1UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(normalMemory.ReadUInt32(0x8C20_2010), fastMemory.ReadUInt32(0x8C20_2010));
        Assert.Equal(0x8C10_06ECu, fast.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererColorPackReturnBridgeWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererColorPackReturnBridge(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0670);
        InitializeDoa2RendererColorPackReturnState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererColorPackReturnBridge(start, 0, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0672u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererColorPackReturnBridgeWhenMemoryIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererColorPackReturnBridge(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0670);
        InitializeDoa2RendererColorPackReturnState(cpu);
        cpu.State.R[12] = 0xA500_0000;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererColorPackReturnBridge(start, 2, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0672u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2RendererInterpolationPrologueToCopyTail()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2RendererInterpolationPrologueToCopyTail(normalMemory);
        WriteRendererInterpolationPrologueData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2RendererInterpolationPrologueToCopyTail(fastMemory);
        WriteRendererInterpolationPrologueData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_0A30);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_0A30);
        InitializeDoa2RendererInterpolationPrologueState(normal);
        InitializeDoa2RendererInterpolationPrologueState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2RendererInterpolationPrologueToCopyTail(fastStart, 40, out var skippedInstructions));
        Assert.Equal(22UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Fr, fast.State.Fr);
        Assert.Equal(normal.State.Fpscr, fast.State.Fpscr);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        for (var offset = 0u; offset < 48; offset += 4)
        {
            Assert.Equal(normalMemory.ReadUInt32(0x8C20_3FD0 + offset), fastMemory.ReadUInt32(0x8C20_3FD0 + offset));
        }

        Assert.Equal(0x8C10_E60Au, fast.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererInterpolationPrologueToCopyTailWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererInterpolationPrologueToCopyTail(memory);
        WriteRendererInterpolationPrologueData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0A30);
        InitializeDoa2RendererInterpolationPrologueState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererInterpolationPrologueToCopyTail(start, 21, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0A32u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererInterpolationPrologueToCopyTailWhenFpscrModeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererInterpolationPrologueToCopyTail(memory);
        WriteRendererInterpolationPrologueData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0A30);
        InitializeDoa2RendererInterpolationPrologueState(cpu);
        cpu.State.Fpscr = Sh4State.FpscrPrBit;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererInterpolationPrologueToCopyTail(start, 40, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0A32u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererInterpolationPrologueToCopyTailWhenSourceIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererInterpolationPrologueToCopyTail(memory);
        WriteRendererInterpolationPrologueData(memory);
        memory.WriteUInt32(0x8C10_0B68, 0xA500_0000);
        var cpu = new Sh4Cpu(memory, 0x8C10_0A30);
        InitializeDoa2RendererInterpolationPrologueState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererInterpolationPrologueToCopyTail(start, 40, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0A32u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2RendererInterpolationSetupToLoopExit()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2RendererInterpolationSetupToLoopExit(normalMemory);
        WriteRendererInterpolationSetupData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2RendererInterpolationSetupToLoopExit(fastMemory);
        WriteRendererInterpolationSetupData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_0A52);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_0A52);
        InitializeDoa2RendererInterpolationSetupState(normal);
        InitializeDoa2RendererInterpolationSetupState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2RendererInterpolationSetupToLoopExit(fastStart, 117, out var skippedInstructions));
        Assert.Equal(117UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Fr, fast.State.Fr);
        Assert.Equal(normal.State.Fpscr, fast.State.Fpscr);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(normalMemory.ReadUInt32(0x8C20_7000), fastMemory.ReadUInt32(0x8C20_7000));
        for (var offset = 0u; offset < 16; offset += 4)
        {
            Assert.Equal(normalMemory.ReadUInt32(0x8C20_3000 + offset), fastMemory.ReadUInt32(0x8C20_3000 + offset));
            Assert.Equal(normalMemory.ReadUInt32(0x8C20_4000 + offset), fastMemory.ReadUInt32(0x8C20_4000 + offset));
        }

        Assert.Equal(0x8C10_0AB6u, fast.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererInterpolationSetupToLoopExitWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererInterpolationSetupToLoopExit(memory);
        WriteRendererInterpolationSetupData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0A52);
        InitializeDoa2RendererInterpolationSetupState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererInterpolationSetupToLoopExit(start, 116, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0A54u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererInterpolationSetupToLoopExitWhenFpscrModeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererInterpolationSetupToLoopExit(memory);
        WriteRendererInterpolationSetupData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0A52);
        InitializeDoa2RendererInterpolationSetupState(cpu);
        cpu.State.Fpscr = Sh4State.FpscrPrBit;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererInterpolationSetupToLoopExit(start, 135, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0A54u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererInterpolationSetupToLoopExitWhenModeWordDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererInterpolationSetupToLoopExit(memory);
        WriteRendererInterpolationSetupData(memory);
        memory.WriteUInt32(0x8C20_5034, 0x0000_0000);
        var cpu = new Sh4Cpu(memory, 0x8C10_0A52);
        InitializeDoa2RendererInterpolationSetupState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererInterpolationSetupToLoopExit(start, 135, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0A54u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererInterpolationSetupToLoopExitWhenSourceIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererInterpolationSetupToLoopExit(memory);
        WriteRendererInterpolationSetupData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0A52);
        InitializeDoa2RendererInterpolationSetupState(cpu);
        cpu.State.R[7] = 0xA500_0000;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererInterpolationSetupToLoopExit(start, 135, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0A54u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2RendererInterpolationEpilogueReturn()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2RendererInterpolationEpilogueReturn(normalMemory);
        WriteRendererInterpolationEpilogueStack(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2RendererInterpolationEpilogueReturn(fastMemory);
        WriteRendererInterpolationEpilogueStack(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_0AB6);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_0AB6);
        InitializeDoa2RendererInterpolationEpilogueState(normal);
        InitializeDoa2RendererInterpolationEpilogueState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2RendererInterpolationEpilogueReturn(fastStart, 3, out var skippedInstructions));
        Assert.Equal(3UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Fr, fast.State.Fr);
        Assert.Equal(normal.State.Fpscr, fast.State.Fpscr);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(0x8C10_055Cu, fast.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererInterpolationEpilogueReturnWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererInterpolationEpilogueReturn(memory);
        WriteRendererInterpolationEpilogueStack(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0AB6);
        InitializeDoa2RendererInterpolationEpilogueState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererInterpolationEpilogueReturn(start, 2, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0AB8u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2RendererInterpolationEpilogueReturnWhenReturnAddressDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2RendererInterpolationEpilogueReturn(memory);
        WriteRendererInterpolationEpilogueStack(memory);
        memory.WriteUInt32(0x8C20_7024, 0x8C10_066E);
        var cpu = new Sh4Cpu(memory, 0x8C10_0AB6);
        InitializeDoa2RendererInterpolationEpilogueState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2RendererInterpolationEpilogueReturn(start, 3, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0AB8u, cpu.State.Pc);
    }

    [Theory]
    [InlineData(0u, 88ul)]
    [InlineData(1u, 88ul)]
    [InlineData(35u, 85ul)]
    [InlineData(0xFFFF_FFFFu, 88ul)]
    public void FastForwardsDoa2SignedRemainderHelper(uint dividend, ulong expectedSkippedInstructions)
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2SignedRemainderHelper(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2SignedRemainderHelper(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_751C);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_751C);
        InitializeDoa2SignedRemainderState(normal, dividend, 6);
        InitializeDoa2SignedRemainderState(fast, dividend, 6);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2SignedRemainderHelper(fastStart, 88, out var skippedInstructions));
        Assert.Equal(expectedSkippedInstructions, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.M, fast.State.M);
        Assert.Equal(normal.State.Q, fast.State.Q);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
    }

    [Fact]
    public void DoesNotFastForwardDoa2SignedRemainderHelperWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2SignedRemainderHelper(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_751C);
        InitializeDoa2SignedRemainderState(cpu, 35, 6);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2SignedRemainderHelper(start, 87, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_751Eu, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2SignedRemainderHelperWhenDivisorIsZero()
    {
        var memory = new DreamcastMemory();
        WriteDoa2SignedRemainderHelper(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_751C);
        InitializeDoa2SignedRemainderState(cpu, 35, 0);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2SignedRemainderHelper(start, 88, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_751Eu, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2SignedRemainderHelperWhenStackIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2SignedRemainderHelper(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_751C);
        InitializeDoa2SignedRemainderState(cpu, 35, 6);
        cpu.State.R[15] = 0xA500_0000;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2SignedRemainderHelper(start, 88, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_751Eu, cpu.State.Pc);
    }

    [Theory]
    [InlineData(0u, 100u)]
    [InlineData(12345u, 100u)]
    [InlineData(0xFFFF_FFFFu, 100u)]
    [InlineData(0x8000_0000u, 3u)]
    public void FastForwardsDoa2UnsignedDivideHelper(uint dividend, uint divisor)
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2UnsignedDivideHelper(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2UnsignedDivideHelper(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_7424);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_7424);
        InitializeDoa2UnsignedDivideState(normal, dividend, divisor);
        InitializeDoa2UnsignedDivideState(fast, dividend, divisor);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2UnsignedDivideHelper(fastStart, 72, out var skippedInstructions));
        Assert.Equal(72UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.M, fast.State.M);
        Assert.Equal(normal.State.Q, fast.State.Q);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
    }

    [Fact]
    public void DoesNotFastForwardDoa2UnsignedDivideHelperWhenDivisorIsZero()
    {
        var memory = new DreamcastMemory();
        WriteDoa2UnsignedDivideHelper(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_7424);
        InitializeDoa2UnsignedDivideState(cpu, 12345, 0);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2UnsignedDivideHelper(start, 72, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_7426u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2UnsignedDivideHelperWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2UnsignedDivideHelper(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_7424);
        InitializeDoa2UnsignedDivideState(cpu, 12345, 100);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2UnsignedDivideHelper(start, 71, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_7426u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2UnsignedDivideHelperWhenStackIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2UnsignedDivideHelper(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_7424);
        InitializeDoa2UnsignedDivideState(cpu, 12345, 100);
        cpu.State.R[15] = 0xA500_0000;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2UnsignedDivideHelper(start, 72, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_7426u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2UnsignedDivideHelperWhenOpcodeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2UnsignedDivideHelper(memory);
        WriteInstruction(memory, 0x8C10_74AE, 0x0009);
        var cpu = new Sh4Cpu(memory, 0x8C10_7424);
        InitializeDoa2UnsignedDivideState(cpu, 12345, 100);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2UnsignedDivideHelper(start, 72, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_7426u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2ListEntryAllocatorPair()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2ListEntryAllocatorPair(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2ListEntryAllocatorPair(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C11_7500);
        var fast = new Sh4Cpu(fastMemory, 0x8C11_7500);
        InitializeDoa2ListEntryAllocatorState(normal);
        InitializeDoa2ListEntryAllocatorState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2ListEntryAllocatorPair(fastStart, 31, out var skippedInstructions));
        Assert.Equal(31UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Macl, fast.State.Macl);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(0x8C11_750Eu, fast.State.Pc);
        Assert.Equal(0x8C30_F534u, fast.State.R[0]);
        Assert.Equal(0x8C30_3704u, fast.State.R[11]);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ListEntryAllocatorPairWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ListEntryAllocatorPair(memory);
        var cpu = new Sh4Cpu(memory, 0x8C11_7500);
        InitializeDoa2ListEntryAllocatorState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ListEntryAllocatorPair(start, 30, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_7502u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ListEntryAllocatorPairWhenLiteralDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ListEntryAllocatorPair(memory);
        memory.WriteUInt32(0x8C11_75C8, 0x8C12_4638);
        var cpu = new Sh4Cpu(memory, 0x8C11_7500);
        InitializeDoa2ListEntryAllocatorState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ListEntryAllocatorPair(start, 31, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_7502u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ListEntryAllocatorPairWhenStackIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ListEntryAllocatorPair(memory);
        var cpu = new Sh4Cpu(memory, 0x8C11_7500);
        InitializeDoa2ListEntryAllocatorState(cpu);
        cpu.State.R[15] = 0xA500_0000;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ListEntryAllocatorPair(start, 31, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_7502u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2ListEntrySetupToClassifier()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2ListEntrySetupToClassifier(normalMemory);
        WriteListEntrySetupData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2ListEntrySetupToClassifier(fastMemory);
        WriteListEntrySetupData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C11_750E);
        var fast = new Sh4Cpu(fastMemory, 0x8C11_750E);
        InitializeDoa2ListEntrySetupState(normal);
        InitializeDoa2ListEntrySetupState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2ListEntrySetupToClassifier(fastStart, 17, out var skippedInstructions));
        Assert.Equal(17UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Macl, fast.State.Macl);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(normalMemory.ReadUInt32(0x8C2F_B8E0), fastMemory.ReadUInt32(0x8C2F_B8E0));
        Assert.Equal(normalMemory.ReadUInt32(0x8C2F_B8DC), fastMemory.ReadUInt32(0x8C2F_B8DC));
        Assert.Equal(normalMemory.ReadUInt32(0x8C20_6FFC), fastMemory.ReadUInt32(0x8C20_6FFC));
        Assert.Equal(0x8C11_7482u, fast.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ListEntrySetupToClassifierWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ListEntrySetupToClassifier(memory);
        WriteListEntrySetupData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C11_750E);
        InitializeDoa2ListEntrySetupState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ListEntrySetupToClassifier(start, 16, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_7510u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ListEntrySetupToClassifierWhenLiteralDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ListEntrySetupToClassifier(memory);
        WriteListEntrySetupData(memory);
        memory.WriteUInt32(0x8C11_75D0, 0x8C20_1000);
        var cpu = new Sh4Cpu(memory, 0x8C11_750E);
        InitializeDoa2ListEntrySetupState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ListEntrySetupToClassifier(start, 17, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_7510u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ListEntrySetupToClassifierWhenStackIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ListEntrySetupToClassifier(memory);
        WriteListEntrySetupData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C11_750E);
        InitializeDoa2ListEntrySetupState(cpu);
        cpu.State.R[15] = 0xA500_0000;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ListEntrySetupToClassifier(start, 17, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_7510u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2ListEntryPostClassifierToRemainder()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2ListEntryPostClassifierToRemainder(normalMemory);
        WriteListEntryPostClassifierStack(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2ListEntryPostClassifierToRemainder(fastMemory);
        WriteListEntryPostClassifierStack(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C11_7532);
        var fast = new Sh4Cpu(fastMemory, 0x8C11_7532);
        InitializeDoa2ListEntryPostClassifierState(normal);
        InitializeDoa2ListEntryPostClassifierState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2ListEntryPostClassifierToRemainder(fastStart, 5, out var skippedInstructions));
        Assert.Equal(5UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(normalMemory.ReadUInt32(0x8C20_5000), fastMemory.ReadUInt32(0x8C20_5000));
        Assert.Equal(0x8C10_751Cu, fast.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ListEntryPostClassifierToRemainderWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ListEntryPostClassifierToRemainder(memory);
        WriteListEntryPostClassifierStack(memory);
        var cpu = new Sh4Cpu(memory, 0x8C11_7532);
        InitializeDoa2ListEntryPostClassifierState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ListEntryPostClassifierToRemainder(start, 4, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_7534u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ListEntryPostClassifierToRemainderWhenTargetLiteralDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ListEntryPostClassifierToRemainder(memory);
        WriteListEntryPostClassifierStack(memory);
        memory.WriteUInt32(0x8C11_75D8, 0x8C10_7520);
        var cpu = new Sh4Cpu(memory, 0x8C11_7532);
        InitializeDoa2ListEntryPostClassifierState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ListEntryPostClassifierToRemainder(start, 5, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_7534u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ListEntryPostClassifierToRemainderWhenDestinationIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ListEntryPostClassifierToRemainder(memory);
        memory.WriteUInt32(0x8C20_6FFC, 0xA500_0000);
        var cpu = new Sh4Cpu(memory, 0x8C11_7532);
        InitializeDoa2ListEntryPostClassifierState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ListEntryPostClassifierToRemainder(start, 5, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_7534u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2ListEntryNonzeroRemainderTail()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2ListEntryNonzeroRemainderTail(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2ListEntryNonzeroRemainderTail(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C11_753E);
        var fast = new Sh4Cpu(fastMemory, 0x8C11_753E);
        InitializeDoa2ListEntryNonzeroTailState(normal, 1);
        InitializeDoa2ListEntryNonzeroTailState(fast, 1);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2ListEntryNonzeroRemainderTail(fastStart, 7, out var skippedInstructions));
        Assert.Equal(7UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(normalMemory.ReadUInt32(0x8C2F_B86C), fastMemory.ReadUInt32(0x8C2F_B86C));
        Assert.Equal(0x8C30_F458u, fastMemory.ReadUInt32(0x8C2F_B86C));
        Assert.Equal(0x8C11_7500u, fast.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2ListEntryNonzeroRemainderTailAtLoopExit()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2ListEntryNonzeroRemainderTail(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2ListEntryNonzeroRemainderTail(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C11_753E);
        var fast = new Sh4Cpu(fastMemory, 0x8C11_753E);
        InitializeDoa2ListEntryNonzeroTailState(normal, 23);
        InitializeDoa2ListEntryNonzeroTailState(fast, 23);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2ListEntryNonzeroRemainderTail(fastStart, 7, out var skippedInstructions));
        Assert.Equal(7UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.True(fast.State.T);
        Assert.Equal(0x8C11_75AAu, fast.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ListEntryNonzeroRemainderTailWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ListEntryNonzeroRemainderTail(memory);
        var cpu = new Sh4Cpu(memory, 0x8C11_753E);
        InitializeDoa2ListEntryNonzeroTailState(cpu, 1);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ListEntryNonzeroRemainderTail(start, 6, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_7540u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ListEntryNonzeroRemainderTailWhenRemainderIsZero()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ListEntryNonzeroRemainderTail(memory);
        var cpu = new Sh4Cpu(memory, 0x8C11_753E);
        InitializeDoa2ListEntryNonzeroTailState(cpu, 1);
        cpu.State.R[0] = 0;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ListEntryNonzeroRemainderTail(start, 7, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_7540u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ListEntryNonzeroRemainderTailWhenDestinationIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ListEntryNonzeroRemainderTail(memory);
        var cpu = new Sh4Cpu(memory, 0x8C11_753E);
        InitializeDoa2ListEntryNonzeroTailState(cpu, 1);
        cpu.State.R[14] = 0xA500_0000;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ListEntryNonzeroRemainderTail(start, 7, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_7540u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2ZeroStatusTableScanToNonzeroEntry()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2ZeroStatusTableScan(normalMemory);
        normalMemory.WriteUInt16(0x8C2A_DC14, 1);
        var fastMemory = new DreamcastMemory();
        WriteDoa2ZeroStatusTableScan(fastMemory);
        fastMemory.WriteUInt16(0x8C2A_DC14, 1);
        var normal = new Sh4Cpu(normalMemory, 0x8C0F_7D2A);
        var fast = new Sh4Cpu(fastMemory, 0x8C0F_7D2A);
        InitializeDoa2ZeroStatusTableScanState(normal);
        InitializeDoa2ZeroStatusTableScanState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2ZeroStatusTableScan(fastStart, 30, out var skippedInstructions));
        Assert.Equal(30UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(normalMemory.ReadUInt16(0x8C0F_7EE4), fastMemory.ReadUInt16(0x8C0F_7EE4));
        Assert.Equal(2, fastMemory.ReadUInt16(0x8C0F_7EE4));
        Assert.Equal(0x8C0F_7D2Cu, fast.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2ZeroStatusTableScanToEnd()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2ZeroStatusTableScan(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2ZeroStatusTableScan(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C0F_7D2A);
        var fast = new Sh4Cpu(fastMemory, 0x8C0F_7D2A);
        InitializeDoa2ZeroStatusTableScanState(normal);
        InitializeDoa2ZeroStatusTableScanState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2ZeroStatusTableScan(fastStart, 269, out var skippedInstructions));
        Assert.Equal(269UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.True(fast.State.T);
        Assert.Equal(18, fastMemory.ReadUInt16(0x8C0F_7EE4));
        Assert.Equal(0x8C0F_7D5Au, fast.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ZeroStatusTableScanWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ZeroStatusTableScan(memory);
        memory.WriteUInt16(0x8C2A_DC14, 1);
        var cpu = new Sh4Cpu(memory, 0x8C0F_7D2A);
        InitializeDoa2ZeroStatusTableScanState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ZeroStatusTableScan(start, 29, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C0F_7D2Cu, cpu.State.Pc);
        Assert.Equal(0, memory.ReadUInt16(0x8C0F_7EE4));
    }

    [Fact]
    public void DoesNotFastForwardDoa2ZeroStatusTableScanWhenCurrentEntryIsNonzero()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ZeroStatusTableScan(memory);
        memory.WriteUInt16(0x8C2A_DC04, 1);
        var cpu = new Sh4Cpu(memory, 0x8C0F_7D2A);
        InitializeDoa2ZeroStatusTableScanState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ZeroStatusTableScan(start, 30, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C0F_7D2Cu, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ZeroStatusTableScanWhenAddressIsOutsideTable()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ZeroStatusTableScan(memory);
        var cpu = new Sh4Cpu(memory, 0x8C0F_7D2A);
        InitializeDoa2ZeroStatusTableScanState(cpu);
        cpu.State.R[1] = 0x8C20_0000;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ZeroStatusTableScan(start, 30, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C0F_7D2Cu, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2ZeroByteClassifier()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2ZeroByteClassifier(normalMemory);
        WriteZeroByteClassifierData(normalMemory, 0);
        var fastMemory = new DreamcastMemory();
        WriteDoa2ZeroByteClassifier(fastMemory);
        WriteZeroByteClassifierData(fastMemory, 0);
        var normal = new Sh4Cpu(normalMemory, 0x8C11_7482);
        var fast = new Sh4Cpu(fastMemory, 0x8C11_7482);
        InitializeDoa2ZeroByteClassifierState(normal);
        InitializeDoa2ZeroByteClassifierState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2ZeroByteClassifier(fastStart, 17, out var skippedInstructions));
        Assert.Equal(17UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.Pr, fast.State.Pr);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(0x8C11_7532u, fast.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ZeroByteClassifierWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ZeroByteClassifier(memory);
        WriteZeroByteClassifierData(memory, 0);
        var cpu = new Sh4Cpu(memory, 0x8C11_7482);
        InitializeDoa2ZeroByteClassifierState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ZeroByteClassifier(start, 16, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_7484u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ZeroByteClassifierWhenByteIsNonzero()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ZeroByteClassifier(memory);
        WriteZeroByteClassifierData(memory, 5);
        var cpu = new Sh4Cpu(memory, 0x8C11_7482);
        InitializeDoa2ZeroByteClassifierState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ZeroByteClassifier(start, 17, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_7484u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ZeroByteClassifierWhenReturnAddressDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ZeroByteClassifier(memory);
        WriteZeroByteClassifierData(memory, 0);
        var cpu = new Sh4Cpu(memory, 0x8C11_7482);
        InitializeDoa2ZeroByteClassifierState(cpu);
        cpu.State.Pr = 0x8C11_7540;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ZeroByteClassifier(start, 17, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C11_7484u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2ColorBytePackCommonPath()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2ColorBytePackCommonPath(normalMemory);
        WriteColorBytePackConstants(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2ColorBytePackCommonPath(fastMemory);
        WriteColorBytePackConstants(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_0AC0);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_0AC0);
        InitializeDoa2ColorBytePackState(normal);
        InitializeDoa2ColorBytePackState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2ColorBytePackCommonPath(fastStart, 100, out var skippedInstructions));
        Assert.Equal(49UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Fr, fast.State.Fr);
        Assert.Equal(normal.State.Fpul, fast.State.Fpul);
        Assert.Equal(normal.State.Fpscr, fast.State.Fpscr);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(0x8C10_0670u, fast.State.Pc);
        Assert.Equal(0x193F7FBFu, fast.State.R[0]);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ColorBytePackCommonPathWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ColorBytePackCommonPath(memory);
        WriteColorBytePackConstants(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0AC0);
        InitializeDoa2ColorBytePackState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ColorBytePackCommonPath(start, 48, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0AC2u, cpu.State.Pc);
        Assert.Equal(1UL, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ColorBytePackCommonPathWhenFpscrModeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ColorBytePackCommonPath(memory);
        WriteColorBytePackConstants(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0AC0);
        InitializeDoa2ColorBytePackState(cpu);
        cpu.State.Fpscr = Sh4State.FpscrSzBit;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ColorBytePackCommonPath(start, 100, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0AC2u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2ColorBytePackCommonPathWhenComponentNeedsClamp()
    {
        var memory = new DreamcastMemory();
        WriteDoa2ColorBytePackCommonPath(memory);
        WriteColorBytePackConstants(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0AC0);
        InitializeDoa2ColorBytePackState(cpu);
        cpu.State.Fr[5] = BitConverter.SingleToUInt32Bits(2.0f);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2ColorBytePackCommonPath(start, 100, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0AC2u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2FpuRecurrenceLoop()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2FpuRecurrenceLoop(normalMemory);
        WriteInstruction(normalMemory, 0x8C0F_B20E, 0x0009);
        var fastMemory = new DreamcastMemory();
        WriteDoa2FpuRecurrenceLoop(fastMemory);
        WriteInstruction(fastMemory, 0x8C0F_B20E, 0x0009);
        var normal = new Sh4Cpu(normalMemory, 0x8C0F_B20A);
        var fast = new Sh4Cpu(fastMemory, 0x8C0F_B20A);
        InitializeDoa2FpuRecurrenceState(normal);
        InitializeDoa2FpuRecurrenceState(fast);

        var normalBranch = normal.Step();
        var fastBranch = fast.Step();
        Assert.Equal(normalBranch.Trace, fastBranch.Trace);

        Assert.True(fast.TryFastForwardDoa2FpuRecurrenceLoop(fastBranch, 100, out var skippedInstructions));
        Assert.Equal(33UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Fr, fast.State.Fr);
        Assert.Equal(normal.State.Fpul, fast.State.Fpul);
        Assert.Equal(normal.State.Fpscr, fast.State.Fpscr);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(0x8C0F_B20Eu, fast.State.Pc);
        Assert.False(fast.State.T);
    }

    [Fact]
    public void DoesNotFastForwardDoa2FpuRecurrenceLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2FpuRecurrenceLoop(memory);
        var cpu = new Sh4Cpu(memory, 0x8C0F_B20A);
        InitializeDoa2FpuRecurrenceState(cpu);

        var branch = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2FpuRecurrenceLoop(branch, 32, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C0F_B20Cu, cpu.State.Pc);
        Assert.Equal(1UL, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void DoesNotFastForwardDoa2FpuRecurrenceLoopWhenFpscrModeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2FpuRecurrenceLoop(memory);
        var cpu = new Sh4Cpu(memory, 0x8C0F_B20A);
        InitializeDoa2FpuRecurrenceState(cpu);
        cpu.State.Fpscr = Sh4State.FpscrSzBit;

        var branch = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2FpuRecurrenceLoop(branch, 100, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C0F_B20Cu, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2TrigSetupAndRecurrenceLoop()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2TrigSetupAndRecurrenceLoop(normalMemory);
        WriteDoa2TrigSetupConstants(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2TrigSetupAndRecurrenceLoop(fastMemory);
        WriteDoa2TrigSetupConstants(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C0F_B1C0);
        var fast = new Sh4Cpu(fastMemory, 0x8C0F_B1C0);
        InitializeDoa2TrigSetupState(normal);
        InitializeDoa2TrigSetupState(fast);

        var normalSetupStart = normal.Step();
        var fastSetupStart = fast.Step();
        Assert.Equal(normalSetupStart.Trace, fastSetupStart.Trace);

        Assert.True(fast.TryFastForwardDoa2TrigSetupAndRecurrenceLoop(fastSetupStart, 100, out var skippedInstructions));
        Assert.Equal(74UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Fr, fast.State.Fr);
        Assert.Equal(normal.State.Fpul, fast.State.Fpul);
        Assert.Equal(normal.State.Fpscr, fast.State.Fpscr);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(0x8C0F_B216u, fast.State.Pc);
        Assert.False(fast.State.T);
    }

    [Fact]
    public void DoesNotFastForwardDoa2TrigSetupAndRecurrenceLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2TrigSetupAndRecurrenceLoop(memory);
        WriteDoa2TrigSetupConstants(memory);
        var cpu = new Sh4Cpu(memory, 0x8C0F_B1C0);
        InitializeDoa2TrigSetupState(cpu);

        var setupStart = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2TrigSetupAndRecurrenceLoop(setupStart, 73, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C0F_B1C2u, cpu.State.Pc);
        Assert.Equal(1UL, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void DoesNotFastForwardDoa2TrigSetupAndRecurrenceLoopWhenFpscrModeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2TrigSetupAndRecurrenceLoop(memory);
        WriteDoa2TrigSetupConstants(memory);
        var cpu = new Sh4Cpu(memory, 0x8C0F_B1C0);
        InitializeDoa2TrigSetupState(cpu);
        cpu.State.Fpscr = Sh4State.FpscrPrBit;

        var setupStart = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2TrigSetupAndRecurrenceLoop(setupStart, 100, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C0F_B1C2u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2PostTrigHelperReturn()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2PostTrigHelperReturn(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2PostTrigHelperReturn(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C0F_B216);
        var fast = new Sh4Cpu(fastMemory, 0x8C0F_B216);
        InitializeDoa2PostTrigHelperReturnState(normal);
        InitializeDoa2PostTrigHelperReturnState(fast);

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2PostTrigHelperReturn(fastStart, 100, out var skippedInstructions));
        Assert.Equal(10UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Fr, fast.State.Fr);
        Assert.Equal(normal.State.Fpul, fast.State.Fpul);
        Assert.Equal(normal.State.Fpscr, fast.State.Fpscr);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(0x8C10_0536u, fast.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2PostTrigHelperReturnWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2PostTrigHelperReturn(memory);
        var cpu = new Sh4Cpu(memory, 0x8C0F_B216);
        InitializeDoa2PostTrigHelperReturnState(cpu);

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2PostTrigHelperReturn(start, 9, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C0F_B218u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2PostTrigHelperTakenReturn()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2PostTrigHelperReturn(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2PostTrigHelperReturn(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C0F_B216);
        var fast = new Sh4Cpu(fastMemory, 0x8C0F_B216);
        InitializeDoa2PostTrigHelperReturnState(normal);
        InitializeDoa2PostTrigHelperReturnState(fast);
        normal.State.Pr = 0x8C10_0540;
        normal.State.R[3] = 1;
        normal.State.R[6] = 1;
        fast.State.Pr = 0x8C10_0540;
        fast.State.R[3] = 1;
        fast.State.R[6] = 1;

        var normalStart = normal.Step();
        var fastStart = fast.Step();
        Assert.Equal(normalStart.Trace, fastStart.Trace);

        Assert.True(fast.TryFastForwardDoa2PostTrigHelperReturn(fastStart, 100, out var skippedInstructions));
        Assert.Equal(12UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Fr, fast.State.Fr);
        Assert.Equal(normal.State.Fpul, fast.State.Fpul);
        Assert.Equal(normal.State.Fpscr, fast.State.Fpscr);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        Assert.Equal(0x8C10_0540u, fast.State.Pc);
        Assert.False(fast.State.T);
    }

    [Fact]
    public void DoesNotFastForwardDoa2PostTrigHelperTakenReturnWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2PostTrigHelperReturn(memory);
        var cpu = new Sh4Cpu(memory, 0x8C0F_B216);
        InitializeDoa2PostTrigHelperReturnState(cpu);
        cpu.State.R[3] = 1;
        cpu.State.R[6] = 1;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2PostTrigHelperReturn(start, 11, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C0F_B218u, cpu.State.Pc);
        Assert.False(cpu.State.T);
    }

    [Fact]
    public void DoesNotFastForwardDoa2PostTrigHelperReturnWhenFpscrModeDiffers()
    {
        var memory = new DreamcastMemory();
        WriteDoa2PostTrigHelperReturn(memory);
        var cpu = new Sh4Cpu(memory, 0x8C0F_B216);
        InitializeDoa2PostTrigHelperReturnState(cpu);
        cpu.State.Fpscr = Sh4State.FpscrSzBit;

        var start = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2PostTrigHelperReturn(start, 100, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C0F_B218u, cpu.State.Pc);
    }

    [Fact]
    public void FastForwardsDoa2VectorScaleLoop()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2VectorScaleLoop(normalMemory);
        WriteVectorScaleData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2VectorScaleLoop(fastMemory);
        WriteVectorScaleData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_05B2);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_05B2);
        InitializeDoa2VectorScaleState(normal);
        InitializeDoa2VectorScaleState(fast);

        var normalBranch = normal.Step();
        var fastBranch = fast.Step();
        Assert.Equal(normalBranch.Trace, fastBranch.Trace);

        Assert.True(fast.TryFastForwardDoa2VectorScaleLoop(fastBranch, 100, out var skippedInstructions));
        Assert.Equal(30UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Fr, fast.State.Fr);
        Assert.Equal(normal.State.Fpscr, fast.State.Fpscr);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        for (var offset = 4u; offset < 16; offset += 4)
        {
            Assert.Equal(normalMemory.ReadUInt32(0x8C20_1000 + offset), fastMemory.ReadUInt32(0x8C20_1000 + offset));
            Assert.Equal(normalMemory.ReadUInt32(0x8C20_2000 + offset), fastMemory.ReadUInt32(0x8C20_2000 + offset));
        }
    }

    [Fact]
    public void DoesNotFastForwardDoa2VectorScaleLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2VectorScaleLoop(memory);
        WriteVectorScaleData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_05B2);
        InitializeDoa2VectorScaleState(cpu);

        var branch = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2VectorScaleLoop(branch, 29, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_05A0u, cpu.State.Pc);
        Assert.Equal(1UL, cpu.State.InstructionsExecuted);
    }

    [Fact]
    public void DoesNotFastForwardDoa2VectorScaleLoopWhenMemoryIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2VectorScaleLoop(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_05B2);
        InitializeDoa2VectorScaleState(cpu);
        cpu.State.R[11] = 0xA500_0000;

        var branch = cpu.Step();

        Assert.False(cpu.TryFastForwardDoa2VectorScaleLoop(branch, 100, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
    }

    [Fact]
    public void FastForwardsDoa2InterpolationLoop()
    {
        var normalMemory = new DreamcastMemory();
        WriteDoa2InterpolationLoop(normalMemory);
        WriteInterpolationData(normalMemory);
        var fastMemory = new DreamcastMemory();
        WriteDoa2InterpolationLoop(fastMemory);
        WriteInterpolationData(fastMemory);
        var normal = new Sh4Cpu(normalMemory, 0x8C10_0A7A);
        var fast = new Sh4Cpu(fastMemory, 0x8C10_0A7A);
        InitializeDoa2InterpolationState(normal);
        InitializeDoa2InterpolationState(fast);

        var normalBranch = StepUntilPc(normal, 0x8C10_0AB2);
        var fastBranch = StepUntilPc(fast, 0x8C10_0AB2);
        Assert.Equal(normalBranch.Trace, fastBranch.Trace);

        Assert.True(fast.TryFastForwardDoa2InterpolationLoop(fastBranch, 100, out var skippedInstructions));
        Assert.Equal(91UL, skippedInstructions);
        for (var index = 0ul; index < skippedInstructions; index++)
        {
            normal.Step();
        }

        Assert.Equal(normal.State.Pc, fast.State.Pc);
        Assert.Equal(normal.State.R, fast.State.R);
        Assert.Equal(normal.State.Fr, fast.State.Fr);
        Assert.Equal(normal.State.Fpscr, fast.State.Fpscr);
        Assert.Equal(normal.State.T, fast.State.T);
        Assert.Equal(normal.State.InstructionsExecuted, fast.State.InstructionsExecuted);
        for (var offset = 0u; offset < 16; offset += 4)
        {
            Assert.Equal(normalMemory.ReadUInt32(0x8C20_3000 + offset), fastMemory.ReadUInt32(0x8C20_3000 + offset));
            Assert.Equal(normalMemory.ReadUInt32(0x8C20_4000 + offset), fastMemory.ReadUInt32(0x8C20_4000 + offset));
        }
    }

    [Fact]
    public void DoesNotFastForwardDoa2InterpolationLoopWhenBudgetIsShort()
    {
        var memory = new DreamcastMemory();
        WriteDoa2InterpolationLoop(memory);
        WriteInterpolationData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0A7A);
        InitializeDoa2InterpolationState(cpu);
        var branch = StepUntilPc(cpu, 0x8C10_0AB2);

        Assert.False(cpu.TryFastForwardDoa2InterpolationLoop(branch, 90, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
        Assert.Equal(0x8C10_0AB4u, cpu.State.Pc);
    }

    [Fact]
    public void DoesNotFastForwardDoa2InterpolationLoopWhenSourceIsOutsideSystemRam()
    {
        var memory = new DreamcastMemory();
        WriteDoa2InterpolationLoop(memory);
        WriteInterpolationData(memory);
        var cpu = new Sh4Cpu(memory, 0x8C10_0A7A);
        InitializeDoa2InterpolationState(cpu);
        cpu.State.R[7] = 0xA500_0000;
        var branch = StepUntilPc(cpu, 0x8C10_0AB2);

        Assert.False(cpu.TryFastForwardDoa2InterpolationLoop(branch, 100, out var skippedInstructions));
        Assert.Equal(0UL, skippedInstructions);
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
    public void ConvertsFloatingPointRegisterToFpulByTruncatingTowardZero()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF43D);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fr[4] = BitConverter.SingleToUInt32Bits(-3.75f);

        cpu.Step();

        Assert.Equal(unchecked((uint)-3), cpu.State.Fpul);
        Assert.Equal(0u, cpu.State.Fpscr & Sh4State.FpscrCauseMask);
    }

    [Fact]
    public void FloatingPointToFpulConversionSaturatesPositiveOverflowAndSetsInvalidCause()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF43D);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fr[4] = BitConverter.SingleToUInt32Bits(2147483648.0f);

        cpu.Step();

        Assert.Equal(0x7FFF_FFFFu, cpu.State.Fpul);
        Assert.Equal(Sh4State.FpscrCauseInvalidBit, cpu.State.Fpscr & Sh4State.FpscrCauseMask);
        Assert.Equal(Sh4State.FpscrFlagInvalidBit, cpu.State.Fpscr & Sh4State.FpscrFlagMask);
    }

    [Fact]
    public void FloatingPointToFpulConversionSaturatesNegativeOverflowAndSetsInvalidCause()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF43D);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fr[4] = BitConverter.SingleToUInt32Bits(-2147483904.0f);

        cpu.Step();

        Assert.Equal(0x8000_0000u, cpu.State.Fpul);
        Assert.Equal(Sh4State.FpscrCauseInvalidBit, cpu.State.Fpscr & Sh4State.FpscrCauseMask);
        Assert.Equal(Sh4State.FpscrFlagInvalidBit, cpu.State.Fpscr & Sh4State.FpscrFlagMask);
    }

    [Fact]
    public void FloatingPointToFpulConversionMapsNanToMinimumIntegerAndSetsInvalidCause()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF43D);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fr[4] = Sh4State.DefaultSingleQNaN;

        cpu.Step();

        Assert.Equal(0x8000_0000u, cpu.State.Fpul);
        Assert.Equal(Sh4State.FpscrCauseInvalidBit, cpu.State.Fpscr & Sh4State.FpscrCauseMask);
        Assert.Equal(Sh4State.FpscrFlagInvalidBit, cpu.State.Fpscr & Sh4State.FpscrFlagMask);
    }

    [Fact]
    public void DoublePrecisionFloatingPointToFpulConversionSaturatesOverflowAndSetsInvalidCause()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF43D);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fpscr = Sh4State.FpscrPrBit;
        WriteDouble(cpu, 4, 2147483648.0);

        cpu.Step();

        Assert.Equal(0x7FFF_FFFFu, cpu.State.Fpul);
        Assert.Equal(Sh4State.FpscrCauseInvalidBit, cpu.State.Fpscr & Sh4State.FpscrCauseMask);
        Assert.Equal(Sh4State.FpscrFlagInvalidBit, cpu.State.Fpscr & Sh4State.FpscrFlagMask);
    }

    [Fact]
    public void ExecutesFloatingPointVectorInnerProduct()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF2ED);
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
        WriteInstruction(memory, 0x8C01_0000, 0xF2ED);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fr[0] = 0x7FC0_0000;
        cpu.State.Fr[8] = BitConverter.SingleToUInt32Bits(1.0f);

        var step = cpu.Step();

        Assert.Equal(Sh4State.DefaultSingleQNaN, cpu.State.Fr[3]);
        Assert.Contains("fipr fv8,fv0 ; fr3=0x", step.Trace, StringComparison.Ordinal);
        Assert.Contains("fv0=[0x7FC00000,0x00000000,0x00000000,0x00000000]", step.Trace, StringComparison.Ordinal);
        Assert.Contains("fv8=[0x3F800000,0x00000000,0x00000000,0x00000000]", step.Trace, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutesFloatingPointImmediateZeroAndOneLoads()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF58D);
        WriteInstruction(memory, 0x8C01_0002, 0xF79D);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fr[5] = BitConverter.SingleToUInt32Bits(123.0f);
        cpu.State.Fr[7] = BitConverter.SingleToUInt32Bits(456.0f);

        var zero = cpu.Step();
        var one = cpu.Step();

        Assert.Equal(0u, cpu.State.Fr[5]);
        Assert.Equal(BitConverter.SingleToUInt32Bits(1.0f), cpu.State.Fr[7]);
        Assert.Equal("fldi0 fr5 ; fr5=0x00000000", zero.Trace);
        Assert.Equal("fldi1 fr7 ; fr7=0x3F800000", one.Trace);
    }

    [Fact]
    public void FloatingPointArithmeticTraceIncludesOperandsForNonFiniteResult()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF453);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);

        var step = cpu.Step();

        Assert.Equal(Sh4State.DefaultSingleQNaN, cpu.State.Fr[4]);
        Assert.Contains("fdiv fr5,fr4 ; fr4=0x", step.Trace, StringComparison.Ordinal);
        Assert.Contains("nonfinite fr4old=0x00000000,fr5=0x00000000", step.Trace, StringComparison.Ordinal);
    }

    [Fact]
    public void FloatingPointDivideZeroByZeroProducesDefaultQNanAndSetsInvalidCause()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF453);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);

        cpu.Step();

        Assert.Equal(Sh4State.DefaultSingleQNaN, cpu.State.Fr[4]);
        Assert.Equal(Sh4State.FpscrCauseInvalidBit, cpu.State.Fpscr & Sh4State.FpscrCauseMask);
        Assert.Equal(Sh4State.FpscrFlagInvalidBit, cpu.State.Fpscr & Sh4State.FpscrFlagMask);
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

        Assert.Equal(Sh4State.DefaultSingleQNaN, cpu.State.Fr[4]);
        Assert.Equal(Sh4State.FpscrCauseInvalidBit, cpu.State.Fpscr & Sh4State.FpscrCauseMask);
        Assert.Equal(Sh4State.FpscrFlagInvalidBit, cpu.State.Fpscr & Sh4State.FpscrFlagMask);
    }

    [Fact]
    public void FloatingPointQuietNanInputDoesNotSetInvalidCause()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF450);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fpscr = Sh4State.FpscrDnBit;
        cpu.State.Fr[4] = Sh4State.DefaultSingleQNaN;
        cpu.State.Fr[5] = BitConverter.SingleToUInt32Bits(1.0f);

        cpu.Step();

        Assert.Equal(Sh4State.DefaultSingleQNaN, cpu.State.Fr[4]);
        Assert.Equal(0u, cpu.State.Fpscr & Sh4State.FpscrCauseMask);
        Assert.Equal(0u, cpu.State.Fpscr & Sh4State.FpscrFlagMask);
    }

    [Fact]
    public void FloatingPointSignalingNanInputSetsInvalidCause()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0xF450);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.Fr[4] = 0x7FC0_0000;
        cpu.State.Fr[5] = BitConverter.SingleToUInt32Bits(1.0f);

        cpu.Step();

        Assert.Equal(Sh4State.DefaultSingleQNaN, cpu.State.Fr[4]);
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
        WriteInstruction(memory, 0x8C01_0000, 0xF2ED);
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
        WriteInstruction(memory, 0x8C01_0000, 0xF2ED);
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
    public void ExecutesPrefetchAsStoreQueueFlush()
    {
        var memory = new DreamcastMemory();
        WriteInstruction(memory, 0x8C01_0000, 0x0483);
        memory.WriteUInt32(0xFF00_0038, 0x10);
        memory.WriteUInt32(0xE000_0000, 0x8084_0000);
        var cpu = new Sh4Cpu(memory, 0x8C01_0000);
        cpu.State.R[4] = 0xE000_0000;

        var step = cpu.Step();

        Assert.Equal("pref @r4", step.Trace);
        Assert.Equal("0x10000000", memory.CreateVideoSnapshot().PvrTaCommandWrites[0].AddressHex);
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

    private static Sh4StepResult StepMany(Sh4Cpu cpu, ulong count)
    {
        Sh4StepResult step = new(cpu.State.Pc, 0, string.Empty, cpu.State.InstructionsExecuted);
        for (var index = 0ul; index < count; index++)
        {
            step = cpu.Step();
        }

        return step;
    }

    private static void WriteIpBinAsicEventWaitLoop(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C00_909A, 0xD313);
        WriteInstruction(memory, 0x8C00_909C, 0x6032);
        WriteInstruction(memory, 0x8C00_909E, 0xC808);
        WriteInstruction(memory, 0x8C00_90A0, 0x89FB);
        memory.WriteUInt32(0x8C00_90E8, 0xA05F_6900);
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

    private static void WriteDoa2ByteFillLoop(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_7864, 0xE700);
        WriteInstruction(memory, 0x8C10_7866, 0x6373);
        WriteInstruction(memory, 0x8C10_7868, 0x3362);
        WriteInstruction(memory, 0x8C10_786A, 0x8D05);
        WriteInstruction(memory, 0x8C10_786C, 0x6043);
        WriteInstruction(memory, 0x8C10_786E, 0x7701);
        WriteInstruction(memory, 0x8C10_7870, 0x2050);
        WriteInstruction(memory, 0x8C10_7872, 0x3762);
        WriteInstruction(memory, 0x8C10_7874, 0x8FFB);
        WriteInstruction(memory, 0x8C10_7876, 0x7001);
        WriteInstruction(memory, 0x8C10_7878, 0x000B);
        WriteInstruction(memory, 0x8C10_787A, 0x6043);
    }

    private static void InitializeDoa2ByteFillState(Sh4Cpu cpu)
    {
        cpu.State.R[0] = 0x8C20_1000;
        cpu.State.R[5] = 0x5A;
        cpu.State.R[6] = 8;
        cpu.State.R[7] = 3;
        cpu.State.T = false;
    }

    private static void InitializeDoa2ByteFillWrapperState(Sh4Cpu cpu, uint byteCount)
    {
        cpu.State.R[0] = 0xDEAD_BEEF;
        cpu.State.R[3] = 0xFEED_FACE;
        cpu.State.R[4] = 0x8C20_1000;
        cpu.State.R[5] = 0x5A;
        cpu.State.R[6] = byteCount;
        cpu.State.R[7] = 0xFFFF_FFFF;
        cpu.State.Pr = 0x8C01_206A;
        cpu.State.T = false;
    }

    private static void WriteDoa2TableDivideSetupLoop(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C11_C7B0, 0x8928);
        WriteInstruction(memory, 0x8C11_C7B2, 0x9079);
        WriteInstruction(memory, 0x8C11_C7B4, 0x64E3);
        WriteInstruction(memory, 0x8C11_C7B6, 0x7404);
        WriteInstruction(memory, 0x8C11_C7B8, 0x05DE);
        WriteInstruction(memory, 0x8C11_C7BA, 0xE664);
        WriteInstruction(memory, 0x8C11_C7BC, 0xE707);
        WriteInstruction(memory, 0x8C11_C7BE, 0x75FF);
        WriteInstruction(memory, 0x8C11_C7C0, 0x9373);
        WriteInstruction(memory, 0x8C11_C7C2, 0x2579);
        WriteInstruction(memory, 0x8C11_C7C4, 0x6153);
        WriteInstruction(memory, 0x8C11_C7C6, 0x4108);
        WriteInstruction(memory, 0x8C11_C7C8, 0x33DC);
        WriteInstruction(memory, 0x8C11_C7CA, 0x313C);
        WriteInstruction(memory, 0x8C11_C7CC, 0xD33D);
        WriteInstruction(memory, 0x8C11_C7CE, 0x6112);
        WriteInstruction(memory, 0x8C11_C7D0, 0x75FF);
        WriteInstruction(memory, 0x8C11_C7D2, 0x4108);
        WriteInstruction(memory, 0x8C11_C7D4, 0x4108);
        WriteInstruction(memory, 0x8C11_C7D6, 0x4108);
        WriteInstruction(memory, 0x8C11_C7D8, 0x4100);
        WriteInstruction(memory, 0x8C11_C7DA, 0x430B);
        WriteInstruction(memory, 0x8C11_C7DC, 0x6063);
        WriteInstruction(memory, 0x8C11_C7DE, 0x62E3);
        WriteInstruction(memory, 0x8C11_C7E0, 0x2402);
        WriteInstruction(memory, 0x8C11_C7E2, 0x7224);
        WriteInstruction(memory, 0x8C11_C7E4, 0x7404);
        WriteInstruction(memory, 0x8C11_C7E6, 0x3422);
        WriteInstruction(memory, 0x8C11_C7E8, 0x8BEA);
        WriteInstruction(memory, 0x8C11_C8A8, 0x0938);
        WriteInstruction(memory, 0x8C11_C8AA, 0x0914);
        memory.WriteUInt32(0x8C11_C8C4, 0x8C10_7424);
        WriteDoa2UnsignedDivideHelper(memory);
    }

    private static void WriteDoa2TableDivideSetupData(DreamcastMemory memory)
    {
        for (var index = 0u; index < 8; index++)
        {
            memory.WriteUInt32(0x8C20_4914 + (index * 4), 0x100 + (index * 37));
        }

        memory.WriteUInt32(0x8C20_4938, 0);
        for (var offset = 4u; offset <= 32; offset += 4)
        {
            memory.WriteUInt32(0x8C20_1000 + offset, 0xDEAD_BEEF);
        }
    }

    private static void InitializeDoa2TableDivideSetupState(Sh4Cpu cpu)
    {
        cpu.State.R[0] = 0xCAFE_BABE;
        cpu.State.R[1] = 0x1111_2222;
        cpu.State.R[2] = 0x3333_4444;
        cpu.State.R[3] = 0x5555_6666;
        cpu.State.R[4] = 0x7777_8888;
        cpu.State.R[5] = 0x9999_AAAA;
        cpu.State.R[6] = 0xBBBB_CCCC;
        cpu.State.R[7] = 0xDDDD_EEEE;
        cpu.State.R[13] = 0x8C20_4000;
        cpu.State.R[14] = 0x8C20_1000;
        cpu.State.R[15] = 0x8C20_3000;
        cpu.State.Pr = 0x8C01_2904;
        cpu.State.M = true;
        cpu.State.Q = true;
        cpu.State.T = false;
    }

    private static void WriteDoa2PostTableVectorCopyLoop(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C11_C7EA, 0xE400);
        WriteInstruction(memory, 0x8C11_C7EC, 0xE520);
        WriteInstruction(memory, 0x8C11_C7EE, 0x935D);
        WriteInstruction(memory, 0x8C11_C7F0, 0x62E3);
        WriteInstruction(memory, 0x8C11_C7F2, 0x7224);
        WriteInstruction(memory, 0x8C11_C7F4, 0x33DC);
        WriteInstruction(memory, 0x8C11_C7F6, 0x334C);
        WriteInstruction(memory, 0x8C11_C7F8, 0x324C);
        WriteInstruction(memory, 0x8C11_C7FA, 0x6132);
        WriteInstruction(memory, 0x8C11_C7FC, 0x7404);
        WriteInstruction(memory, 0x8C11_C7FE, 0x3452);
        WriteInstruction(memory, 0x8C11_C800, 0x8FF5);
        WriteInstruction(memory, 0x8C11_C802, 0x2212);
        WriteInstruction(memory, 0x8C11_C8AC, 0x08F4);
    }

    private static void WriteDoa2PostTableVectorCopyData(DreamcastMemory memory)
    {
        for (var offset = 0u; offset < 32; offset += 4)
        {
            memory.WriteUInt32(0x8C20_48F4 + offset, 0x6000_0000 + offset);
            memory.WriteUInt32(0x8C20_1024 + offset, 0xDEAD_BEEF);
        }
    }

    private static void InitializeDoa2PostTableVectorCopyState(Sh4Cpu cpu)
    {
        cpu.State.R[1] = 0x1111_2222;
        cpu.State.R[2] = 0x3333_4444;
        cpu.State.R[3] = 0x5555_6666;
        cpu.State.R[4] = 0x7777_8888;
        cpu.State.R[5] = 0x9999_AAAA;
        cpu.State.R[13] = 0x8C20_4000;
        cpu.State.R[14] = 0x8C20_1000;
        cpu.State.T = false;
    }

    private static void WriteDoa2EmptyCallbackTableScan(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C12_FDA0, 0x974E);
        WriteInstruction(memory, 0x8C12_FDA2, 0xE400);
        WriteInstruction(memory, 0x8C12_FDA4, 0x964D);
        WriteInstruction(memory, 0x8C12_FDA6, 0x6543);
        WriteInstruction(memory, 0x8C12_FDA8, 0xD026);
        WriteInstruction(memory, 0x8C12_FDAA, 0x004E);
        WriteInstruction(memory, 0x8C12_FDAC, 0x8801);
        WriteInstruction(memory, 0x8C12_FDAE, 0x8B06);
        WriteInstruction(memory, 0x8C12_FDBE, 0x346C);
        WriteInstruction(memory, 0x8C12_FDC0, 0x3472);
        WriteInstruction(memory, 0x8C12_FDC2, 0x8FF1);
        WriteInstruction(memory, 0x8C12_FDC4, 0x356C);
        WriteInstruction(memory, 0x8C12_FDC6, 0xE000);
        WriteInstruction(memory, 0x8C12_FDC8, 0x000B);
        WriteInstruction(memory, 0x8C12_FDCA, 0x0009);
        WriteInstruction(memory, 0x8C12_FE40, 0x0E20);
        WriteInstruction(memory, 0x8C12_FE42, 0x01C4);
        memory.WriteUInt32(0x8C12_FE44, 0x8C2F_6820);
    }

    private static void WriteDoa2EmptyCallbackTableData(DreamcastMemory memory)
    {
        for (var offset = 0u; offset < 0xE20; offset += 0x1C4)
        {
            memory.WriteUInt32(0x8C2F_6820 + offset, 0);
        }
    }

    private static void InitializeDoa2EmptyCallbackTableScanState(Sh4Cpu cpu)
    {
        cpu.State.R[0] = 0xCAFE_BABE;
        cpu.State.R[4] = 0x1111_2222;
        cpu.State.R[5] = 0x3333_4444;
        cpu.State.R[6] = 0x5555_6666;
        cpu.State.R[7] = 0x7777_8888;
        cpu.State.Pr = 0x8C13_057E;
        cpu.State.T = false;
    }

    private static void WriteDoa2FiveWordTableCopyLoop(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C11_B226, 0xE340);
        WriteInstruction(memory, 0x8C11_B228, 0x914B);
        WriteInstruction(memory, 0x8C11_B22A, 0x33EC);
        WriteInstruction(memory, 0x8C11_B22C, 0x9048);
        WriteInstruction(memory, 0x8C11_B22E, 0x6332);
        WriteInstruction(memory, 0x8C11_B230, 0x31EC);
        WriteInstruction(memory, 0x8C11_B232, 0x30EC);
        WriteInstruction(memory, 0x8C11_B234, 0x6233);
        WriteInstruction(memory, 0x8C11_B236, 0x4308);
        WriteInstruction(memory, 0x8C11_B238, 0x332C);
        WriteInstruction(memory, 0x8C11_B23A, 0x4308);
        WriteInstruction(memory, 0x8C11_B23C, 0x4308);
        WriteInstruction(memory, 0x8C11_B23E, 0x4300);
        WriteInstruction(memory, 0x8C11_B240, 0x331C);
        WriteInstruction(memory, 0x8C11_B242, 0x6233);
        WriteInstruction(memory, 0x8C11_B244, 0x32CC);
        WriteInstruction(memory, 0x8C11_B246, 0x6323);
        WriteInstruction(memory, 0x8C11_B248, 0x334C);
        WriteInstruction(memory, 0x8C11_B24A, 0x6232);
        WriteInstruction(memory, 0x8C11_B24C, 0x30CC);
        WriteInstruction(memory, 0x8C11_B24E, 0x0426);
        WriteInstruction(memory, 0x8C11_B250, 0x7404);
        WriteInstruction(memory, 0x8C11_B252, 0x3452);
        WriteInstruction(memory, 0x8C11_B254, 0x8BE7);
        WriteInstruction(memory, 0x8C11_B2C0, 0x00B0);
        WriteInstruction(memory, 0x8C11_B2C2, 0x0170);
    }

    private static void WriteDoa2FiveWordTableCopyData(DreamcastMemory memory)
    {
        memory.WriteUInt32(0x8C20_1040, 2);
        for (var offset = 0u; offset < 20; offset += 4)
        {
            memory.WriteUInt32(0x8C20_1370 + offset, 0x7000_0000 + offset);
            memory.WriteUInt32(0x8C20_11B0 + offset, 0xDEAD_BEEF);
        }
    }

    private static void InitializeDoa2FiveWordTableCopyState(Sh4Cpu cpu, uint offset)
    {
        cpu.State.R[0] = 0xCAFE_BABE;
        cpu.State.R[1] = 0x1111_2222;
        cpu.State.R[2] = 0x3333_4444;
        cpu.State.R[3] = 0x5555_6666;
        cpu.State.R[4] = offset;
        cpu.State.R[5] = 20;
        cpu.State.R[12] = 0x100;
        cpu.State.R[14] = 0x8C20_1000;
        cpu.State.T = false;
    }

    private static void WriteDoa2FiveWordMirrorCopyLoop(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C0F_90B0, 0xD333);
        WriteInstruction(memory, 0x8C0F_90B2, 0xE405);
        WriteInstruction(memory, 0x8C0F_90B4, 0xD533);
        WriteInstruction(memory, 0x8C0F_90B6, 0x6632);
        WriteInstruction(memory, 0x8C0F_90B8, 0x6366);
        WriteInstruction(memory, 0x8C0F_90BA, 0x74FF);
        WriteInstruction(memory, 0x8C0F_90BC, 0x2448);
        WriteInstruction(memory, 0x8C0F_90BE, 0x2532);
        WriteInstruction(memory, 0x8C0F_90C0, 0x8FFA);
        WriteInstruction(memory, 0x8C0F_90C2, 0x7504);
        WriteInstruction(memory, 0x8C0F_90C4, 0x000B);
        WriteInstruction(memory, 0x8C0F_90C6, 0x0009);
        memory.WriteUInt32(0x8C0F_9180, 0x8C2B_6F34);
        memory.WriteUInt32(0x8C0F_9184, 0x8C2B_6F68);
    }

    private static void WriteDoa2FiveWordMirrorCopyData(DreamcastMemory memory)
    {
        for (var offset = 0u; offset < 20; offset += 4)
        {
            memory.WriteUInt32(0x8C20_1000 + offset, 0x7100_0000 + offset);
            memory.WriteUInt32(0x8C20_2000 + offset, 0xDEAD_BEEF);
        }
    }

    private static void InitializeDoa2FiveWordMirrorCopyState(Sh4Cpu cpu)
    {
        cpu.State.R[3] = 0xCAFE_BABE;
        cpu.State.R[4] = 5;
        cpu.State.R[5] = 0x8C20_2000;
        cpu.State.R[6] = 0x8C20_1000;
        cpu.State.T = false;
    }

    private static void WriteDoa2EmptyStackWordScanLoop(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C11_B300, 0x6272);
        WriteInstruction(memory, 0x8C11_B302, 0x2228);
        WriteInstruction(memory, 0x8C11_B304, 0x8917);
        WriteInstruction(memory, 0x8C11_B336, 0x7704);
        WriteInstruction(memory, 0x8C11_B338, 0x7604);
        WriteInstruction(memory, 0x8C11_B33A, 0x35AC);
        WriteInstruction(memory, 0x8C11_B33C, 0x37D2);
        WriteInstruction(memory, 0x8C11_B33E, 0x8BDF);
    }

    private static void WriteDoa2EmptyStackWordScanData(DreamcastMemory memory)
    {
        for (var offset = 0u; offset < 20; offset += 4)
        {
            memory.WriteUInt32(0x8C20_1000 + offset, 0);
        }
    }

    private static void InitializeDoa2EmptyStackWordScanState(Sh4Cpu cpu, uint startAddress)
    {
        cpu.State.R[2] = 0xDEAD_BEEF;
        cpu.State.R[5] = 0;
        cpu.State.R[6] = 0x8C20_2000;
        cpu.State.R[7] = startAddress;
        cpu.State.R[10] = 0x0100_0000;
        cpu.State.R[13] = 0x8C20_1014;
        cpu.State.T = false;
    }

    private static void WriteDoa2EmptyTaskHelperReturn(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C13_06A0, 0x2FE6);
        WriteInstruction(memory, 0x8C13_06A2, 0x6363);
        WriteInstruction(memory, 0x8C13_06A4, 0x2FD6);
        WriteInstruction(memory, 0x8C13_06A6, 0x6D63);
        WriteInstruction(memory, 0x8C13_06A8, 0x2FC6);
        WriteInstruction(memory, 0x8C13_06AA, 0x4D08);
        WriteInstruction(memory, 0x8C13_06AC, 0xDC27);
        WriteInstruction(memory, 0x8C13_06AE, 0x3D3C);
        WriteInstruction(memory, 0x8C13_06B0, 0x9046);
        WriteInstruction(memory, 0x8C13_06B2, 0x4D08);
        WriteInstruction(memory, 0x8C13_06B4, 0x4F22);
        WriteInstruction(memory, 0x8C13_06B6, 0x02CE);
        WriteInstruction(memory, 0x8C13_06B8, 0x6E73);
        WriteInstruction(memory, 0x8C13_06BA, 0x2228);
        WriteInstruction(memory, 0x8C13_06BC, 0x8D09);
        WriteInstruction(memory, 0x8C13_06BE, 0x4E08);
        WriteInstruction(memory, 0x8C13_06D2, 0xE400);
        WriteInstruction(memory, 0x8C13_06D4, 0x9736);
        WriteInstruction(memory, 0x8C13_06D6, 0x6243);
        WriteInstruction(memory, 0x8C13_06D8, 0x3262);
        WriteInstruction(memory, 0x8C13_06DA, 0x6143);
        WriteInstruction(memory, 0x8C13_06DC, 0x8D07);
        WriteInstruction(memory, 0x8C13_06DE, 0x375C);
        WriteInstruction(memory, 0x8C13_06EE, 0x9328);
        WriteInstruction(memory, 0x8C13_06F0, 0x9028);
        WriteInstruction(memory, 0x8C13_06F2, 0x33CC);
        WriteInstruction(memory, 0x8C13_06F4, 0x305C);
        WriteInstruction(memory, 0x8C13_06F6, 0x30DC);
        WriteInstruction(memory, 0x8C13_06F8, 0x33EC);
        WriteInstruction(memory, 0x8C13_06FA, 0x06EE);
        WriteInstruction(memory, 0x8C13_06FC, 0x6232);
        WriteInstruction(memory, 0x8C13_06FE, 0x364C);
        WriteInstruction(memory, 0x8C13_0700, 0x3248);
        WriteInstruction(memory, 0x8C13_0702, 0x6423);
        WriteInstruction(memory, 0x8C13_0704, 0x901F);
        WriteInstruction(memory, 0x8C13_0706, 0x30CC);
        WriteInstruction(memory, 0x8C13_0708, 0x30DC);
        WriteInstruction(memory, 0x8C13_070A, 0x03EE);
        WriteInstruction(memory, 0x8C13_070C, 0x3636);
        WriteInstruction(memory, 0x8C13_070E, 0x8B03);
        WriteInstruction(memory, 0x8C13_0718, 0x9014);
        WriteInstruction(memory, 0x8C13_071A, 0x305C);
        WriteInstruction(memory, 0x8C13_071C, 0x30DC);
        WriteInstruction(memory, 0x8C13_071E, 0x03EE);
        WriteInstruction(memory, 0x8C13_0720, 0x3432);
        WriteInstruction(memory, 0x8C13_0722, 0x8905);
        WriteInstruction(memory, 0x8C13_0730, 0x4F26);
        WriteInstruction(memory, 0x8C13_0732, 0xE000);
        WriteInstruction(memory, 0x8C13_0734, 0x6CF6);
        WriteInstruction(memory, 0x8C13_0736, 0x6DF6);
        WriteInstruction(memory, 0x8C13_0738, 0x000B);
        WriteInstruction(memory, 0x8C13_073A, 0x6EF6);
        WriteInstruction(memory, 0x8C13_0740, 0x07D0);
        WriteInstruction(memory, 0x8C13_0742, 0x0730);
        WriteInstruction(memory, 0x8C13_0744, 0x00E4);
        WriteInstruction(memory, 0x8C13_0746, 0x0854);
        memory.WriteUInt32(0x8C13_074C, 0x8C2F_5E80);
    }

    private static void WriteDoa2EmptyTaskHelperReturnData(DreamcastMemory memory)
    {
        memory.WriteUInt32(0x8C2F_6650, 0);
        for (var index = 0u; index < 5; index++)
        {
            var offset = index * 4;
            memory.WriteUInt32(0x8C2F_6904 + offset, 0);
            memory.WriteUInt32(0x8C2F_65B0 + offset, 0);
            memory.WriteUInt32(0x8C2F_66D4 + offset, 0);
        }
    }

    private static void InitializeDoa2EmptyTaskHelperReturnState(Sh4Cpu cpu, uint index)
    {
        cpu.State.Pr = 0x8C13_081C;
        cpu.State.R[4] = 0x8C2E_26C0;
        cpu.State.R[5] = 0x8C2F_6820;
        cpu.State.R[6] = 0;
        cpu.State.R[7] = index;
        cpu.State.R[12] = 0x8C2E_26C0;
        cpu.State.R[13] = index;
        cpu.State.R[14] = 0x8C2F_6820;
        cpu.State.R[15] = 0x8C20_7000;
        cpu.State.T = false;
    }

    private static void WriteDoa2EmptyTaskHelperCallerLoop(DreamcastMemory memory)
    {
        WriteDoa2EmptyTaskHelperReturn(memory);
        WriteInstruction(memory, 0x8C13_07D6, 0x9053);
        WriteInstruction(memory, 0x8C13_07D8, 0x69D3);
        WriteInstruction(memory, 0x8C13_07DA, 0x4908);
        WriteInstruction(memory, 0x8C13_07DC, 0x62E3);
        WriteInstruction(memory, 0x8C13_07DE, 0x30EC);
        WriteInstruction(memory, 0x8C13_07E0, 0x308C);
        WriteInstruction(memory, 0x8C13_07E2, 0x309C);
        WriteInstruction(memory, 0x8C13_07E4, 0x2F06);
        WriteInstruction(memory, 0x8C13_07E6, 0x7244);
        WriteInstruction(memory, 0x8C13_07E8, 0x51C2);
        WriteInstruction(memory, 0x8C13_07EA, 0x328C);
        WriteInstruction(memory, 0x8C13_07EC, 0x60C2);
        WriteInstruction(memory, 0x8C13_07EE, 0x329C);
        WriteInstruction(memory, 0x8C13_07F0, 0x5113);
        WriteInstruction(memory, 0x8C13_07F2, 0x039E);
        WriteInstruction(memory, 0x8C13_07F4, 0x50C3);
        WriteInstruction(memory, 0x8C13_07F6, 0x4108);
        WriteInstruction(memory, 0x8C13_07F8, 0x001E);
        WriteInstruction(memory, 0x8C13_07FA, 0x308C);
        WriteInstruction(memory, 0x8C13_07FC, 0x019E);
        WriteInstruction(memory, 0x8C13_07FE, 0x2212);
        WriteInstruction(memory, 0x8C13_0800, 0x3318);
        WriteInstruction(memory, 0x8C13_0802, 0x62F6);
        WriteInstruction(memory, 0x8C13_0804, 0x2232);
        WriteInstruction(memory, 0x8C13_0806, 0xD221);
        WriteInstruction(memory, 0x8C13_0808, 0x6122);
        WriteInstruction(memory, 0x8C13_080A, 0x2118);
        WriteInstruction(memory, 0x8C13_080C, 0x8901);
        WriteInstruction(memory, 0x8C13_0812, 0x65E3);
        WriteInstruction(memory, 0x8C13_0814, 0x66B3);
        WriteInstruction(memory, 0x8C13_0816, 0x67D3);
        WriteInstruction(memory, 0x8C13_0818, 0xBF42);
        WriteInstruction(memory, 0x8C13_081A, 0x64C3);
        WriteInstruction(memory, 0x8C13_081C, 0x9030);
        WriteInstruction(memory, 0x8C13_081E, 0x30EC);
        WriteInstruction(memory, 0x8C13_0820, 0x308C);
        WriteInstruction(memory, 0x8C13_0822, 0x039E);
        WriteInstruction(memory, 0x8C13_0824, 0x2338);
        WriteInstruction(memory, 0x8C13_0826, 0x8B01);
        WriteInstruction(memory, 0x8C13_0828, 0xA0BA);
        WriteInstruction(memory, 0x8C13_082A, 0x0009);
        WriteInstruction(memory, 0x8C13_0880, 0x00E4);
        memory.WriteUInt32(0x8C13_088C, 0x8C2F_7640);
        WriteInstruction(memory, 0x8C13_09A0, 0xE305);
        WriteInstruction(memory, 0x8C13_09A2, 0x7D01);
        WriteInstruction(memory, 0x8C13_09A4, 0x3D32);
        WriteInstruction(memory, 0x8C13_09A6, 0x8B00);
        WriteInstruction(memory, 0x8C13_09A8, 0xED00);
        WriteInstruction(memory, 0x8C13_09AA, 0x53F2);
        WriteInstruction(memory, 0x8C13_09AC, 0x73FF);
        WriteInstruction(memory, 0x8C13_09AE, 0x2338);
        WriteInstruction(memory, 0x8C13_09B0, 0x8D02);
        WriteInstruction(memory, 0x8C13_09B2, 0x1F32);
        WriteInstruction(memory, 0x8C13_09B4, 0xAF0F);
        WriteInstruction(memory, 0x8C13_09B6, 0x0009);
        WriteInstruction(memory, 0x8C13_09B8, 0xE000);
        WriteInstruction(memory, 0x8C13_09BA, 0x7F0C);
    }

    private static void WriteDoa2EmptyTaskHelperCallerLoopData(DreamcastMemory memory)
    {
        WriteDoa2EmptyTaskHelperReturnData(memory);
        memory.WriteUInt32(0x8C2F_7640, 0);
        memory.WriteUInt32(0x8C20_1000, 0x8C20_2000);
        memory.WriteUInt32(0x8C20_1008, 0);
        memory.WriteUInt32(0x8C20_100C, 0);
        memory.WriteUInt32(0x0000_000C, 0);
        for (var offset = 0u; offset < 20; offset += 4)
        {
            memory.WriteUInt32(0x8C20_2000 + offset, 0);
            memory.WriteUInt32(0x8C2F_6864 + offset, 0xDEAD_BEEF);
            memory.WriteUInt32(0x8C2F_6904 + offset, 0);
        }
    }

    private static void InitializeDoa2EmptyTaskHelperCallerLoopState(Sh4Cpu cpu, DreamcastMemory memory, uint index)
    {
        cpu.State.Pr = 0x8C11_B5B6;
        cpu.State.R[8] = 0;
        cpu.State.R[11] = 0;
        cpu.State.R[12] = 0x8C20_1000;
        cpu.State.R[13] = index;
        cpu.State.R[14] = 0x8C2F_6820;
        cpu.State.R[15] = 0x8C20_7000;
        cpu.State.T = false;
        cpu.State.R[0] = 0x1234_5678;
        cpu.State.R[9] = 0xCAFE_BABE;
        cpu.State.R[5] = 0x5555_5555;
        cpu.State.R[6] = 0x6666_6666;
        cpu.State.R[7] = 0x7777_7777;
        cpu.State.R[4] = 0x4444_4444;
        cpu.State.R[10] = 0xAAAA_AAAA;
        memory.WriteUInt32(0x8C20_7008, 5 - index);
    }

    private static void WriteDoa2UnrolledWordCopyReturn(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_E60A, 0x5326);
        WriteInstruction(memory, 0x8C10_E60C, 0x1107);
        WriteInstruction(memory, 0x8C10_E60E, 0x5025);
        WriteInstruction(memory, 0x8C10_E610, 0x1136);
        WriteInstruction(memory, 0x8C10_E612, 0x5324);
        WriteInstruction(memory, 0x8C10_E614, 0x1105);
        WriteInstruction(memory, 0x8C10_E616, 0x5023);
        WriteInstruction(memory, 0x8C10_E618, 0x1134);
        WriteInstruction(memory, 0x8C10_E61A, 0x5322);
        WriteInstruction(memory, 0x8C10_E61C, 0x1103);
        WriteInstruction(memory, 0x8C10_E61E, 0x5021);
        WriteInstruction(memory, 0x8C10_E620, 0x1132);
        WriteInstruction(memory, 0x8C10_E622, 0x6322);
        WriteInstruction(memory, 0x8C10_E624, 0x1101);
        WriteInstruction(memory, 0x8C10_E626, 0x2132);
        WriteInstruction(memory, 0x8C10_E628, 0x000B);
        WriteInstruction(memory, 0x8C10_E62A, 0x63F6);
    }

    private static void WriteWordCopyData(DreamcastMemory memory)
    {
        for (var index = 0u; index < 8; index++)
        {
            memory.WriteUInt32(0x8C20_1000 + (index * 4), 0xA500_0000 + index);
            memory.WriteUInt32(0x8C20_2000 + (index * 4), 0);
        }

        memory.WriteUInt32(0x8C20_3000, 0x8C10_E5CC);
    }

    private static void InitializeDoa2WordCopyState(Sh4Cpu cpu)
    {
        cpu.State.R[0] = 0xA500_0007;
        cpu.State.R[1] = 0x8C20_2000;
        cpu.State.R[2] = 0x8C20_1000;
        cpu.State.R[15] = 0x8C20_3000;
        cpu.State.Pr = 0x8C10_0A52;
    }

    private static void WriteDoa2AicaZeroMailboxTimeoutLoop(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_4D4C, 0x6362);
        WriteInstruction(memory, 0x8C10_4D4E, 0x2338);
        WriteInstruction(memory, 0x8C10_4D50, 0x8B06);
        WriteInstruction(memory, 0x8C10_4D52, 0x7401);
        WriteInstruction(memory, 0x8C10_4D54, 0x3452);
        WriteInstruction(memory, 0x8C10_4D56, 0x8BF9);
        memory.WriteUInt32(0x8C10_4E10, 0xA080_005C);
        memory.WriteUInt32(0x8C10_4E14, 0x0040_0000);
    }

    private static void InitializeDoa2AicaZeroMailboxTimeoutState(Sh4Cpu cpu, uint counter)
    {
        cpu.State.R[4] = counter;
        cpu.State.R[5] = 0x0040_0000;
        cpu.State.R[6] = 0xA080_005C;
        cpu.State.T = false;
    }

    private static void WriteDoa2HighRamZeroFillLoop(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C11_333C, 0x2540);
        WriteInstruction(memory, 0x8C11_333E, 0x7501);
        WriteInstruction(memory, 0x8C11_3340, 0x6262);
        WriteInstruction(memory, 0x8C11_3342, 0x3522);
        WriteInstruction(memory, 0x8C11_3344, 0x8BFA);
        WriteInstruction(memory, 0x8C11_3346, 0x0009);
        memory.WriteUInt32(0x8C11_343C, 0x8C14_D4E0);
        memory.WriteUInt32(0x8C11_3440, 0x8C14_D4D4);
    }

    private static void InitializeDoa2HighRamZeroFillState(DreamcastMemory memory, Sh4Cpu cpu, uint start, uint end)
    {
        cpu.State.R[2] = end;
        cpu.State.R[4] = 0;
        cpu.State.R[5] = start;
        cpu.State.R[6] = 0x8C14_D4E0;
        cpu.State.T = false;
        memory.WriteUInt32(0x8C14_D4D4, start);
        memory.WriteUInt32(0x8C14_D4E0, end);
    }

    private static void FillBytes(DreamcastMemory memory, uint start, uint count, byte value)
    {
        for (var offset = 0u; offset < count; offset++)
        {
            memory.Write(start + offset, [value]);
        }
    }

    private static void WriteDoa2CacheBlockPurgeLoop(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C11_1ED8, 0x00A3);
        WriteInstruction(memory, 0x8C11_1EDA, 0x4510);
        WriteInstruction(memory, 0x8C11_1EDC, 0x7020);
        WriteInstruction(memory, 0x8C11_1EDE, 0x8BFB);
        WriteInstruction(memory, 0x8C11_1EE0, 0x0009);
    }

    private static void InitializeDoa2CacheBlockPurgeState(Sh4Cpu cpu, uint address, uint count)
    {
        cpu.State.R[0] = address;
        cpu.State.R[5] = count;
        cpu.State.T = false;
    }

    private static void WriteDoa2ScratchVectorCopyWrapper(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_3AF0, 0xD416);
        WriteInstruction(memory, 0x8C10_3AF2, 0xE004);
        WriteInstruction(memory, 0x8C10_3AF4, 0x4F22);
        WriteInstruction(memory, 0x8C10_3AF6, 0xD116);
        WriteInstruction(memory, 0x8C10_3AF8, 0x6243);
        WriteInstruction(memory, 0x8C10_3AFA, 0xD316);
        WriteInstruction(memory, 0x8C10_3AFC, 0xF44A);
        WriteInstruction(memory, 0x8C10_3AFE, 0xF457);
        WriteInstruction(memory, 0x8C10_3B00, 0xE008);
        WriteInstruction(memory, 0x8C10_3B02, 0xF467);
        WriteInstruction(memory, 0x8C10_3B04, 0x430B);
        WriteInstruction(memory, 0x8C10_3B06, 0xE00C);
        WriteInstruction(memory, 0x8C10_3B08, 0x4F26);
        WriteInstruction(memory, 0x8C10_3B0A, 0x000B);
        WriteInstruction(memory, 0x8C10_3B0C, 0x0009);
        memory.WriteUInt32(0x8C10_3B4C, 0x8C1C_A920);
        memory.WriteUInt32(0x8C10_3B50, 0x8C1C_A930);
        memory.WriteUInt32(0x8C10_3B54, 0x8C10_E5D8);

        WriteInstruction(memory, 0x8C10_E5D8, 0x2F36);
        WriteInstruction(memory, 0x8C10_E5DA, 0xD302);
        WriteInstruction(memory, 0x8C10_E5DC, 0x033E);
        WriteInstruction(memory, 0x8C10_E5DE, 0x70FC);
        WriteInstruction(memory, 0x8C10_E5E0, 0x432B);
        WriteInstruction(memory, 0x8C10_E5E2, 0x032E);
        memory.WriteUInt32(0x8C10_E5E4, 0x8C10_E62C);
        memory.WriteUInt32(0x8C10_E638, 0x8C10_E61E);

        WriteInstruction(memory, 0x8C10_E61E, 0x5021);
        WriteInstruction(memory, 0x8C10_E620, 0x1132);
        WriteInstruction(memory, 0x8C10_E622, 0x6322);
        WriteInstruction(memory, 0x8C10_E624, 0x1101);
        WriteInstruction(memory, 0x8C10_E626, 0x2132);
        WriteInstruction(memory, 0x8C10_E628, 0x000B);
        WriteInstruction(memory, 0x8C10_E62A, 0x63F6);
    }

    private static void InitializeDoa2ScratchVectorCopyWrapperState(Sh4Cpu cpu)
    {
        cpu.State.R[0] = 0xDEAD_BEEF;
        cpu.State.R[1] = 0xCAFE_BABE;
        cpu.State.R[2] = 0xFEED_FACE;
        cpu.State.R[3] = 0x8C10_E5D8;
        cpu.State.R[15] = 0x8C20_8000;
        cpu.State.Pr = 0x8C0E_1F06;
        cpu.State.Fr[4] = BitConverter.SingleToUInt32Bits(536870912.0f);
        cpu.State.Fr[5] = BitConverter.SingleToUInt32Bits(0.25f);
        cpu.State.Fr[6] = BitConverter.SingleToUInt32Bits(2.0f);
        cpu.State.T = true;
    }

    private static void WriteDoa2TableEntryAddressHelper(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C0F_29DE, 0x6043);
        WriteInstruction(memory, 0x8C0F_29E0, 0x0009);
        WriteInstruction(memory, 0x8C0F_29E2, 0x4000);
        WriteInstruction(memory, 0x8C0F_29E4, 0x6343);
        WriteInstruction(memory, 0x8C0F_29E6, 0x303C);
        WriteInstruction(memory, 0x8C0F_29E8, 0xD23A);
        WriteInstruction(memory, 0x8C0F_29EA, 0x4008);
        WriteInstruction(memory, 0x8C0F_29EC, 0x4008);
        WriteInstruction(memory, 0x8C0F_29EE, 0x4000);
        WriteInstruction(memory, 0x8C0F_29F0, 0x000B);
        WriteInstruction(memory, 0x8C0F_29F2, 0x302C);
        memory.WriteUInt32(0x8C0F_2AD4, 0x8C2A_D770);
    }

    private static void InitializeDoa2TableEntryAddressHelperState(Sh4Cpu cpu, uint index)
    {
        cpu.State.R[1] = 0xCAFE_BABE;
        cpu.State.R[2] = 0xDEAD_BEEF;
        cpu.State.R[3] = 0xFEED_FACE;
        cpu.State.R[4] = index;
        cpu.State.Pr = 0x8C0F_367C;
        cpu.State.T = true;
    }

    private static void WriteDoa2ZeroStatusByteTableScan(DreamcastMemory memory)
    {
        WriteDoa2TableEntryAddressHelper(memory);
        WriteInstruction(memory, 0x8C0F_3660, 0x2FC6);
        WriteInstruction(memory, 0x8C0F_3662, 0x6C43);
        WriteInstruction(memory, 0x8C0F_3664, 0x2FB6);
        WriteInstruction(memory, 0x8C0F_3666, 0xEB01);
        WriteInstruction(memory, 0x8C0F_3668, 0x2FA6);
        WriteInstruction(memory, 0x8C0F_366A, 0xEA08);
        WriteInstruction(memory, 0x8C0F_366C, 0x2F96);
        WriteInstruction(memory, 0x8C0F_366E, 0x2F86);
        WriteInstruction(memory, 0x8C0F_3670, 0x4F22);
        WriteInstruction(memory, 0x8C0F_3672, 0xD822);
        WriteInstruction(memory, 0x8C0F_3674, 0xD922);
        WriteInstruction(memory, 0x8C0F_3676, 0xDD23);
        WriteInstruction(memory, 0x8C0F_3678, 0x480B);
        WriteInstruction(memory, 0x8C0F_367A, 0x64E3);
        WriteInstruction(memory, 0x8C0F_367C, 0x6403);
        WriteInstruction(memory, 0x8C0F_367E, 0x6040);
        WriteInstruction(memory, 0x8C0F_3680, 0x600C);
        WriteInstruction(memory, 0x8C0F_3682, 0x8801);
        WriteInstruction(memory, 0x8C0F_3684, 0x8B12);
        WriteInstruction(memory, 0x8C0F_36AC, 0x7E01);
        WriteInstruction(memory, 0x8C0F_36AE, 0x3EA3);
        WriteInstruction(memory, 0x8C0F_36B0, 0x8BE2);
        WriteInstruction(memory, 0x8C0F_36B2, 0xD315);
        WriteInstruction(memory, 0x8C0F_36B4, 0x23E0);
        WriteInstruction(memory, 0x8C0F_36B6, 0x60C3);
        WriteInstruction(memory, 0x8C0F_36B8, 0x0009);
        WriteInstruction(memory, 0x8C0F_36BA, 0x4F26);
        WriteInstruction(memory, 0x8C0F_36BC, 0x68F6);
        WriteInstruction(memory, 0x8C0F_36BE, 0x69F6);
        WriteInstruction(memory, 0x8C0F_36C0, 0x6AF6);
        WriteInstruction(memory, 0x8C0F_36C2, 0x6BF6);
        WriteInstruction(memory, 0x8C0F_36C4, 0x6CF6);
        WriteInstruction(memory, 0x8C0F_36C6, 0x6DF6);
        WriteInstruction(memory, 0x8C0F_36C8, 0x000B);
        WriteInstruction(memory, 0x8C0F_36CA, 0x6EF6);
        memory.WriteUInt32(0x8C0F_36FC, 0x8C0F_29DE);
        memory.WriteUInt32(0x8C0F_3700, 0x8CE7_E1D0);
        memory.WriteUInt32(0x8C0F_3704, 0x8C1C_938C);
        memory.WriteUInt32(0x8C0F_3708, 0x8C2A_DA94);
    }

    private static void WriteZeroStatusByteTableScanData(DreamcastMemory memory)
    {
        for (var index = 0u; index < 8; index++)
        {
            memory.Write(0x8C2A_D770 + (index * 96), [0]);
        }
    }

    private static void WriteZeroStatusByteTableScanStack(DreamcastMemory memory)
    {
        memory.WriteUInt32(0x8C20_8000, 0x8C0F_31D4);
        memory.WriteUInt32(0x8C20_8004, 0x8888_0008);
        memory.WriteUInt32(0x8C20_8008, 0x9999_0009);
        memory.WriteUInt32(0x8C20_800C, 0xAAAA_000A);
        memory.WriteUInt32(0x8C20_8010, 0xBBBB_000B);
        memory.WriteUInt32(0x8C20_8014, 0xCCCC_000C);
        memory.WriteUInt32(0x8C20_8018, 0xDDDD_000D);
        memory.WriteUInt32(0x8C20_801C, 0xEEEE_000E);
    }

    private static void InitializeDoa2ZeroStatusByteTableScanState(Sh4Cpu cpu, uint index)
    {
        cpu.State.R[0] = 0xDEAD_BEEF;
        cpu.State.R[2] = 0xCAFE_BABE;
        cpu.State.R[3] = 0xFEED_FACE;
        cpu.State.R[4] = 0x1234_5678;
        cpu.State.R[8] = 0x8C0F_29DE;
        cpu.State.R[9] = 0x8CE7_E1D0;
        cpu.State.R[10] = 8;
        cpu.State.R[11] = 1;
        cpu.State.R[12] = 0x1357_2468;
        cpu.State.R[13] = 0x8C1C_938C;
        cpu.State.R[14] = index;
        cpu.State.R[15] = 0x8C20_8000;
        cpu.State.Pr = 0x8C0F_367C;
        cpu.State.T = false;
    }

    private static void WriteDoa2ZeroRecordGroupScan(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C01_3BF6, 0x63B3);
        WriteInstruction(memory, 0x8C01_3BF8, 0x4308);
        WriteInstruction(memory, 0x8C01_3BFA, 0x4300);
        WriteInstruction(memory, 0x8C01_3BFC, 0x33AC);
        WriteInstruction(memory, 0x8C01_3BFE, 0x6231);
        WriteInstruction(memory, 0x8C01_3C00, 0x2228);
        WriteInstruction(memory, 0x8C01_3C02, 0x890F);
        WriteInstruction(memory, 0x8C01_3C24, 0x7B01);
        WriteInstruction(memory, 0x8C01_3C26, 0x63B3);
        WriteInstruction(memory, 0x8C01_3C28, 0x4308);
        WriteInstruction(memory, 0x8C01_3C2A, 0x4300);
        WriteInstruction(memory, 0x8C01_3C2C, 0x33AC);
        WriteInstruction(memory, 0x8C01_3C2E, 0x6231);
        WriteInstruction(memory, 0x8C01_3C30, 0x7C08);
        WriteInstruction(memory, 0x8C01_3C32, 0x7E08);
        WriteInstruction(memory, 0x8C01_3C34, 0x2228);
        WriteInstruction(memory, 0x8C01_3C36, 0x8D10);
        WriteInstruction(memory, 0x8C01_3C38, 0x7D08);
        WriteInstruction(memory, 0x8C01_3C5A, 0x7B01);
        WriteInstruction(memory, 0x8C01_3C5C, 0x63B3);
        WriteInstruction(memory, 0x8C01_3C5E, 0x4308);
        WriteInstruction(memory, 0x8C01_3C60, 0x4300);
        WriteInstruction(memory, 0x8C01_3C62, 0x33AC);
        WriteInstruction(memory, 0x8C01_3C64, 0x6231);
        WriteInstruction(memory, 0x8C01_3C66, 0x7C08);
        WriteInstruction(memory, 0x8C01_3C68, 0x7E08);
        WriteInstruction(memory, 0x8C01_3C6A, 0x2228);
        WriteInstruction(memory, 0x8C01_3C6C, 0x8D10);
        WriteInstruction(memory, 0x8C01_3C6E, 0x7D08);
        WriteInstruction(memory, 0x8C01_3C90, 0x7B01);
        WriteInstruction(memory, 0x8C01_3C92, 0x63B3);
        WriteInstruction(memory, 0x8C01_3C94, 0x4308);
        WriteInstruction(memory, 0x8C01_3C96, 0x4300);
        WriteInstruction(memory, 0x8C01_3C98, 0x33AC);
        WriteInstruction(memory, 0x8C01_3C9A, 0x6231);
        WriteInstruction(memory, 0x8C01_3C9C, 0x7C08);
        WriteInstruction(memory, 0x8C01_3C9E, 0x7E08);
        WriteInstruction(memory, 0x8C01_3CA0, 0x2228);
        WriteInstruction(memory, 0x8C01_3CA2, 0x8D10);
        WriteInstruction(memory, 0x8C01_3CA4, 0x7D08);
        WriteInstruction(memory, 0x8C01_3CC6, 0x7B01);
        WriteInstruction(memory, 0x8C01_3CC8, 0x3B82);
        WriteInstruction(memory, 0x8C01_3CCA, 0x7C08);
        WriteInstruction(memory, 0x8C01_3CCC, 0x7E08);
        WriteInstruction(memory, 0x8C01_3CCE, 0x8F92);
        WriteInstruction(memory, 0x8C01_3CD0, 0x7D08);
        WriteInstruction(memory, 0x8C01_3CD2, 0x4F26);
        WriteInstruction(memory, 0x8C01_3CD4, 0x68F6);
        WriteInstruction(memory, 0x8C01_3CD6, 0x69F6);
        WriteInstruction(memory, 0x8C01_3CD8, 0x6AF6);
        WriteInstruction(memory, 0x8C01_3CDA, 0x6BF6);
        WriteInstruction(memory, 0x8C01_3CDC, 0x6CF6);
        WriteInstruction(memory, 0x8C01_3CDE, 0x6DF6);
        WriteInstruction(memory, 0x8C01_3CE0, 0x000B);
        WriteInstruction(memory, 0x8C01_3CE2, 0x6EF6);
        memory.WriteUInt32(0x8C01_3CF8, 0x8C1E_FFB8);
    }

    private static void WriteZeroRecordGroupData(DreamcastMemory memory)
    {
        for (var index = 0u; index < 32; index++)
        {
            memory.WriteUInt16(0x8C1E_FFB8 + (index * 8), 0);
        }
    }

    private static void WriteZeroRecordGroupStack(DreamcastMemory memory)
    {
        memory.WriteUInt32(0x8C20_8000, 0x8C01_2DB8);
        memory.WriteUInt32(0x8C20_8004, 0x8888_0008);
        memory.WriteUInt32(0x8C20_8008, 0x9999_0009);
        memory.WriteUInt32(0x8C20_800C, 0xAAAA_000A);
        memory.WriteUInt32(0x8C20_8010, 0xBBBB_000B);
        memory.WriteUInt32(0x8C20_8014, 0xCCCC_000C);
        memory.WriteUInt32(0x8C20_8018, 0xDDDD_000D);
        memory.WriteUInt32(0x8C20_801C, 0xEEEE_000E);
    }

    private static void InitializeDoa2ZeroRecordGroupScanState(Sh4Cpu cpu, uint index)
    {
        cpu.State.R[8] = 32;
        cpu.State.R[9] = 0x8C10_56A0;
        cpu.State.R[10] = 0x8C1E_FFB8;
        cpu.State.R[11] = index;
        cpu.State.R[12] = 0x8C1E_FFB8 + (index * 8);
        cpu.State.R[13] = 0x8C1E_FFB8 + (index * 8);
        cpu.State.R[14] = 0x8C1E_FFB8 + (index * 8);
        cpu.State.R[15] = 0x8C20_8000;
        cpu.State.Pr = 0x8C01_2DB8;
        cpu.State.T = false;
    }

    private static void WriteDoa2ColorPackCommonPath(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_06EC, 0x50DD);
        WriteInstruction(memory, 0x8C10_06EE, 0xC930);
        WriteInstruction(memory, 0x8C10_06F0, 0x6403);
        WriteInstruction(memory, 0x8C10_06F2, 0xE018);
        WriteInstruction(memory, 0x8C10_06F4, 0xF7D6);
        WriteInstruction(memory, 0x8C10_06F6, 0x2948);
        WriteInstruction(memory, 0x8C10_06F8, 0xE020);
        WriteInstruction(memory, 0x8C10_06FA, 0x8D06);
        WriteInstruction(memory, 0x8C10_06FC, 0xF4D6);
        WriteInstruction(memory, 0x8C10_070A, 0xE01C);
        WriteInstruction(memory, 0x8C10_070C, 0xE320);
        WriteInstruction(memory, 0x8C10_070E, 0xF5D6);
        WriteInstruction(memory, 0x8C10_0710, 0x2438);
        WriteInstruction(memory, 0x8C10_0712, 0xE024);
        WriteInstruction(memory, 0x8C10_0714, 0x8F06);
        WriteInstruction(memory, 0x8C10_0716, 0xF6D6);
        WriteInstruction(memory, 0x8C10_0718, 0xF35C);
        WriteInstruction(memory, 0x8C10_071A, 0xF5FC);
        WriteInstruction(memory, 0x8C10_071C, 0xF531);
        WriteInstruction(memory, 0x8C10_071E, 0xF36C);
        WriteInstruction(memory, 0x8C10_0720, 0xF6FC);
        WriteInstruction(memory, 0x8C10_0722, 0xF631);
        WriteInstruction(memory, 0x8C10_0724, 0x50DD);
        WriteInstruction(memory, 0x8C10_0726, 0xD376);
        WriteInstruction(memory, 0x8C10_0728, 0xD176);
        WriteInstruction(memory, 0x8C10_072A, 0x2039);
        WriteInstruction(memory, 0x8C10_072C, 0x3010);
        WriteInstruction(memory, 0x8C10_072E, 0x8907);
        WriteInstruction(memory, 0x8C10_0730, 0xD173);
        WriteInstruction(memory, 0x8C10_0732, 0x3010);
        WriteInstruction(memory, 0x8C10_0734, 0x890C);
        WriteInstruction(memory, 0x8C10_0736, 0xA00F);
        WriteInstruction(memory, 0x8C10_0738, 0x0009);
        WriteInstruction(memory, 0x8C10_0758, 0x64F3);
        WriteInstruction(memory, 0x8C10_075A, 0x7418);
        WriteInstruction(memory, 0x8C10_075C, 0xD56B);
        WriteInstruction(memory, 0x8C10_075E, 0xF47A);
        WriteInstruction(memory, 0x8C10_0760, 0x6342);
        WriteInstruction(memory, 0x8C10_0762, 0x2359);
        WriteInstruction(memory, 0x8C10_0764, 0x1F35);
        WriteInstruction(memory, 0x8C10_0766, 0xF44A);
        WriteInstruction(memory, 0x8C10_0768, 0x6342);
        WriteInstruction(memory, 0x8C10_076A, 0x2539);
        WriteInstruction(memory, 0x8C10_076C, 0x2F52);
        WriteInstruction(memory, 0x8C10_076E, 0xF45A);
        WriteInstruction(memory, 0x8C10_0770, 0x6942);
        WriteInstruction(memory, 0x8C10_0772, 0xF46A);
        WriteInstruction(memory, 0x8C10_0774, 0x6242);
        WriteInstruction(memory, 0x8C10_0776, 0x4929);
        WriteInstruction(memory, 0x8C10_0778, 0x4229);
        WriteInstruction(memory, 0x8C10_077A, 0x1F23);
    }

    private static void WriteColorPackData(DreamcastMemory memory)
    {
        memory.WriteUInt32(0x8C10_0900, 0x0000_C000);
        memory.WriteUInt32(0x8C10_0904, 0x0000_8000);
        memory.WriteUInt32(0x8C10_090C, 0xFFFF_0000);
        memory.WriteUInt32(0x8C20_1000 + 24, BitConverter.SingleToUInt32Bits(1.25f));
        memory.WriteUInt32(0x8C20_1000 + 28, BitConverter.SingleToUInt32Bits(2.5f));
        memory.WriteUInt32(0x8C20_1000 + 32, BitConverter.SingleToUInt32Bits(3.75f));
        memory.WriteUInt32(0x8C20_1000 + 36, BitConverter.SingleToUInt32Bits(4.5f));
        memory.WriteUInt32(0x8C20_1000 + 52, 0x0000_2805);
    }

    private static void InitializeDoa2ColorPackState(Sh4Cpu cpu)
    {
        cpu.State.R[9] = 0x10;
        cpu.State.R[13] = 0x8C20_1000;
        cpu.State.R[15] = 0x8C20_2000;
        cpu.State.Fr[15] = BitConverter.SingleToUInt32Bits(4096.0f);
        cpu.State.T = true;
    }

    private static void WriteDoa2TaEmitCommonPath(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_077C, 0x50DC);
        WriteInstruction(memory, 0x8C10_077E, 0x88FF);
        WriteInstruction(memory, 0x8C10_0780, 0x8F07);
        WriteInstruction(memory, 0x8C10_0782, 0x6403);
        WriteInstruction(memory, 0x8C10_0792, 0xD35F);
        WriteInstruction(memory, 0x8C10_0794, 0x4408);
        WriteInstruction(memory, 0x8C10_0796, 0x1F44);
        WriteInstruction(memory, 0x8C10_0798, 0x6032);
        WriteInstruction(memory, 0x8C10_079A, 0xD35F);
        WriteInstruction(memory, 0x8C10_079C, 0x044E);
        WriteInstruction(memory, 0x8C10_079E, 0xDE5F);
        WriteInstruction(memory, 0x8C10_07A0, 0x6243);
        WriteInstruction(memory, 0x8C10_07A2, 0x4229);
        WriteInstruction(memory, 0x8C10_07A4, 0x4219);
        WriteInstruction(memory, 0x8C10_07A6, 0xD15B);
        WriteInstruction(memory, 0x8C10_07A8, 0x2E49);
        WriteInstruction(memory, 0x8C10_07AA, 0xD45E);
        WriteInstruction(memory, 0x8C10_07AC, 0x2122);
        WriteInstruction(memory, 0x8C10_07AE, 0x2322);
        WriteInstruction(memory, 0x8C10_07B0, 0xD352);
        WriteInstruction(memory, 0x8C10_07B2, 0x50DD);
        WriteInstruction(memory, 0x8C10_07B4, 0xD25A);
        WriteInstruction(memory, 0x8C10_07B6, 0x2038);
        WriteInstruction(memory, 0x8C10_07B8, 0x8D0A);
        WriteInstruction(memory, 0x8C10_07BA, 0x2E2B);
        WriteInstruction(memory, 0x8C10_07D0, 0x60C2);
        WriteInstruction(memory, 0x8C10_07D2, 0x6142);
        WriteInstruction(memory, 0x8C10_07D4, 0x201B);
        WriteInstruction(memory, 0x8C10_07D6, 0x2E02);
        WriteInstruction(memory, 0x8C10_07D8, 0x53C1);
        WriteInstruction(memory, 0x8C10_07DA, 0x1E31);
        WriteInstruction(memory, 0x8C10_07DC, 0x52C2);
        WriteInstruction(memory, 0x8C10_07DE, 0x5341);
        WriteInstruction(memory, 0x8C10_07E0, 0x223B);
        WriteInstruction(memory, 0x8C10_07E2, 0x1E22);
        WriteInstruction(memory, 0x8C10_07E4, 0x53C3);
        WriteInstruction(memory, 0x8C10_07E6, 0x1E33);
        WriteInstruction(memory, 0x8C10_07E8, 0x52C4);
        WriteInstruction(memory, 0x8C10_07EA, 0x1E24);
        WriteInstruction(memory, 0x8C10_07EC, 0xB288);
        WriteInstruction(memory, 0x8C10_07EE, 0xF4EC);
        WriteInstruction(memory, 0x8C10_07F0, 0x52DF);
        WriteInstruction(memory, 0x8C10_07F2, 0x4028);
        WriteInstruction(memory, 0x8C10_07F4, 0x4018);
        WriteInstruction(memory, 0x8C10_07F6, 0x202B);
        WriteInstruction(memory, 0x8C10_07F8, 0x1E05);
        WriteInstruction(memory, 0x8C10_07FA, 0x0E83);
        WriteInstruction(memory, 0x8C10_07FC, 0xD34A);
        WriteInstruction(memory, 0x8C10_07FE, 0x7E20);
        WriteInstruction(memory, 0x8C10_0800, 0x64A3);
        WriteInstruction(memory, 0x8C10_0802, 0x65B3);
        WriteInstruction(memory, 0x8C10_0804, 0x2E32);
        WriteInstruction(memory, 0x8C10_0806, 0xE00C);
        WriteInstruction(memory, 0x8C10_0808, 0x6252);
        WriteInstruction(memory, 0x8C10_080A, 0x1E21);
        WriteInstruction(memory, 0x8C10_080C, 0x6342);
        WriteInstruction(memory, 0x8C10_080E, 0x1E32);
        WriteInstruction(memory, 0x8C10_0810, 0xFED7);
        WriteInstruction(memory, 0x8C10_0812, 0xE018);
        WriteInstruction(memory, 0x8C10_0814, 0x5351);
        WriteInstruction(memory, 0x8C10_0816, 0x1E34);
        WriteInstruction(memory, 0x8C10_0818, 0x5241);
        WriteInstruction(memory, 0x8C10_081A, 0x1E25);
        WriteInstruction(memory, 0x8C10_081C, 0xFED7);
        WriteInstruction(memory, 0x8C10_081E, 0xE024);
        WriteInstruction(memory, 0x8C10_0820, 0x5352);
        WriteInstruction(memory, 0x8C10_0822, 0x1E37);
        WriteInstruction(memory, 0x8C10_0824, 0x5242);
        WriteInstruction(memory, 0x8C10_0826, 0x1E28);
        WriteInstruction(memory, 0x8C10_0828, 0xFED7);
        WriteInstruction(memory, 0x8C10_082A, 0x5353);
        WriteInstruction(memory, 0x8C10_082C, 0x1E3A);
        WriteInstruction(memory, 0x8C10_082E, 0x5243);
        WriteInstruction(memory, 0x8C10_0830, 0xD33E);
        WriteInstruction(memory, 0x8C10_0832, 0x1E2B);
        WriteInstruction(memory, 0x8C10_0834, 0x283B);
        WriteInstruction(memory, 0x8C10_0836, 0x1E8C);
        WriteInstruction(memory, 0x8C10_0838, 0x52F5);
        WriteInstruction(memory, 0x8C10_083A, 0x229B);
        WriteInstruction(memory, 0x8C10_083C, 0x1E2D);
        WriteInstruction(memory, 0x8C10_083E, 0x61F2);
        WriteInstruction(memory, 0x8C10_0840, 0x291B);
        WriteInstruction(memory, 0x8C10_0842, 0x1E9E);
        WriteInstruction(memory, 0x8C10_0844, 0x52F3);
        WriteInstruction(memory, 0x8C10_0846, 0x61F2);
        WriteInstruction(memory, 0x8C10_0848, 0x212B);
        WriteInstruction(memory, 0x8C10_084A, 0x1E1F);
        WriteInstruction(memory, 0x8C10_084C, 0x0E83);
        WriteInstruction(memory, 0x8C10_084E, 0x7E20);
        WriteInstruction(memory, 0x8C10_0850, 0x0E83);
        WriteInstruction(memory, 0x8C10_0852, 0xD12F);
        WriteInstruction(memory, 0x8C10_0854, 0x7E20);
        WriteInstruction(memory, 0x8C10_0856, 0x54F4);
        WriteInstruction(memory, 0x8C10_0858, 0x6212);
        WriteInstruction(memory, 0x8C10_085A, 0xD335);
        WriteInstruction(memory, 0x8C10_085C, 0x342C);
        WriteInstruction(memory, 0x8C10_085E, 0xD22F);
        WriteInstruction(memory, 0x8C10_0860, 0x6042);
        WriteInstruction(memory, 0x8C10_0862, 0x2E29);
        WriteInstruction(memory, 0x8C10_0864, 0x2039);
        WriteInstruction(memory, 0x8C10_0866, 0x20EB);
        WriteInstruction(memory, 0x8C10_0868, 0x2402);
        WriteInstruction(memory, 0x8C10_086A, 0xE000);
        WriteInstruction(memory, 0x8C10_086C, 0x7F3C);
        WriteInstruction(memory, 0x8C10_086E, 0x4F26);
        WriteInstruction(memory, 0x8C10_0870, 0xFCF9);
        WriteInstruction(memory, 0x8C10_0872, 0xFDF9);
        WriteInstruction(memory, 0x8C10_0874, 0xFEF9);
        WriteInstruction(memory, 0x8C10_0876, 0xFFF9);
        WriteInstruction(memory, 0x8C10_0878, 0x68F6);
        WriteInstruction(memory, 0x8C10_087A, 0x69F6);
        WriteInstruction(memory, 0x8C10_087C, 0x6AF6);
        WriteInstruction(memory, 0x8C10_087E, 0x6BF6);
        WriteInstruction(memory, 0x8C10_0880, 0x6CF6);
        WriteInstruction(memory, 0x8C10_0882, 0x6DF6);
        WriteInstruction(memory, 0x8C10_0884, 0x000B);
        WriteInstruction(memory, 0x8C10_0886, 0x6EF6);
        WriteInstruction(memory, 0x8C10_0D00, 0xC70E);
        WriteInstruction(memory, 0x8C10_0D02, 0xF308);
        WriteInstruction(memory, 0x8C10_0D04, 0xC70E);
        WriteInstruction(memory, 0x8C10_0D06, 0xF108);
        WriteInstruction(memory, 0x8C10_0D08, 0xF432);
        WriteInstruction(memory, 0x8C10_0D0A, 0xF415);
        WriteInstruction(memory, 0x8C10_0D0C, 0x8F08);
        WriteInstruction(memory, 0x8C10_0D0E, 0xF54C);
        WriteInstruction(memory, 0x8C10_0D20, 0xF25C);
        WriteInstruction(memory, 0x8C10_0D22, 0xF23D);
        WriteInstruction(memory, 0x8C10_0D24, 0x9505);
        WriteInstruction(memory, 0x8C10_0D26, 0x045A);
        WriteInstruction(memory, 0x8C10_0D28, 0x3456);
        WriteInstruction(memory, 0x8C10_0D2A, 0x8B00);
        WriteInstruction(memory, 0x8C10_0D2E, 0x000B);
        WriteInstruction(memory, 0x8C10_0D30, 0x6043);
    }

    private static void WriteTaEmitData(DreamcastMemory memory)
    {
        memory.WriteUInt32(0x8C10_08FC, 0x0002_0000);
        memory.WriteUInt32(0x8C10_0910, 0x8C20_4000);
        memory.WriteUInt32(0x8C10_0914, 0xFF00_0038);
        memory.WriteUInt32(0x8C10_0918, 0xFF00_003C);
        memory.WriteUInt32(0x8C10_091C, 0x03FF_FFFF);
        memory.WriteUInt32(0x8C10_0920, 0xE000_0000);
        memory.WriteUInt32(0x8C10_0924, 0x8C20_6000);
        memory.WriteUInt32(0x8C10_0928, 0xF000_0000);
        memory.WriteUInt32(0x8C10_092C, 0x5350_0000);
        memory.WriteUInt32(0x8C10_0930, 0xFC00_0000);
        memory.WriteUInt32(0x8C10_0D3C, BitConverter.SingleToUInt32Bits(255.0f));
        memory.WriteUInt32(0x8C10_0D40, BitConverter.SingleToUInt32Bits(2147483648.0f));
        WriteInstruction(memory, 0x8C10_0D32, 0x00FF);

        memory.WriteUInt32(0x8C20_1000 + 48, 2);
        memory.WriteUInt32(0x8C20_1000 + 52, 0x0000_2805);
        memory.WriteUInt32(0x8C20_1000 + 60, 1);
        memory.WriteUInt32(0x8C20_3000, 0xA200_0009);
        memory.WriteUInt32(0x8C20_3004, 0x8000_0000);
        memory.WriteUInt32(0x8C20_3008, 0x9410_04C0);
        memory.WriteUInt32(0x8C20_300C, 0);
        memory.WriteUInt32(0x8C20_3010, 0x193F_3F3F);
        memory.WriteUInt32(0x8C20_4000, 0x8C20_5000);
        memory.WriteUInt32(0x8C20_5008, 0x0000_0120);
        memory.WriteUInt32(0x8C20_6000, 0);
        memory.WriteUInt32(0x8C20_6004, 0);

        for (var index = 0u; index < 4; index++)
        {
            memory.WriteUInt32(0x8C20_7000 + (index * 4), 0xFFC0_0000 + index);
            memory.WriteUInt32(0x8C20_8000 + (index * 4), 0x1111_0000 + index);
        }

        memory.WriteUInt32(0x8C20_2000, 0x3F64_0000);
        memory.WriteUInt32(0x8C20_200C, 0x0000_4E00);
        memory.WriteUInt32(0x8C20_2014, 0x3F5D_0000);
        memory.WriteUInt32(0x8C20_203C, 0x8C0E_1EB2);
        memory.WriteUInt32(0x8C20_2040, BitConverter.SingleToUInt32Bits(0.02f));
        memory.WriteUInt32(0x8C20_2044, BitConverter.SingleToUInt32Bits(0.25f));
        memory.WriteUInt32(0x8C20_2048, BitConverter.SingleToUInt32Bits(0.5f));
        memory.WriteUInt32(0x8C20_204C, BitConverter.SingleToUInt32Bits(0.75f));
        memory.WriteUInt32(0x8C20_2050, 0x8C2A_AE2C);
        memory.WriteUInt32(0x8C20_2054, 0x8C10_0430);
        memory.WriteUInt32(0x8C20_2058, 0x20);
        memory.WriteUInt32(0x8C20_205C, 0x1A7);
        memory.WriteUInt32(0x8C20_2060, 0x8C2A_AE2C);
        memory.WriteUInt32(0x8C20_2064, 0x8C2A_BFAC);
        memory.WriteUInt32(0x8C20_2068, 0x8C14_80B6);
    }

    private static void InitializeDoa2TaEmitState(Sh4Cpu cpu)
    {
        cpu.State.R[8] = 0x23;
        cpu.State.R[9] = 0x4E00;
        cpu.State.R[10] = 0x8C20_7000;
        cpu.State.R[11] = 0x8C20_8000;
        cpu.State.R[12] = 0x8C20_3000;
        cpu.State.R[13] = 0x8C20_1000;
        cpu.State.R[15] = 0x8C20_2000;
        cpu.State.Fr[13] = BitConverter.SingleToUInt32Bits(4096.0f);
        cpu.State.Fr[14] = BitConverter.SingleToUInt32Bits(0.1f);
        cpu.State.T = true;
    }

    private static void WriteDoa2TextGlyphSetupCommonPath(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C0E_1E08, 0x64E0);
        WriteInstruction(memory, 0x8C0E_1E0A, 0x644C);
        WriteInstruction(memory, 0x8C0E_1E0C, 0x34A3);
        WriteInstruction(memory, 0x8C0E_1E0E, 0x8B65);
        WriteInstruction(memory, 0x8C0E_1E10, 0x9257);
        WriteInstruction(memory, 0x8C0E_1E12, 0x3427);
        WriteInstruction(memory, 0x8C0E_1E14, 0x8962);
        WriteInstruction(memory, 0x8C0E_1E16, 0x60E0);
        WriteInstruction(memory, 0x8C0E_1E18, 0x600C);
        WriteInstruction(memory, 0x8C0E_1E1A, 0x8840);
        WriteInstruction(memory, 0x8C0E_1E1C, 0x8B15);
        WriteInstruction(memory, 0x8C0E_1E4A, 0x6CE0);
        WriteInstruction(memory, 0x8C0E_1E4C, 0xE018);
        WriteInstruction(memory, 0x8C0E_1E4E, 0x2FB2);
        WriteInstruction(memory, 0x8C0E_1E50, 0x6CCC);
        WriteInstruction(memory, 0x8C0E_1E52, 0x7CE0);
        WriteInstruction(memory, 0x8C0E_1E54, 0x63C3);
        WriteInstruction(memory, 0x8C0E_1E56, 0x4C08);
        WriteInstruction(memory, 0x8C0E_1E58, 0x3C3C);
        WriteInstruction(memory, 0x8C0E_1E5A, 0x4C08);
        WriteInstruction(memory, 0x8C0E_1E5C, 0x3C8C);
        WriteInstruction(memory, 0x8C0E_1E5E, 0xF3C8);
        WriteInstruction(memory, 0x8C0E_1E60, 0xFF37);
        WriteInstruction(memory, 0x8C0E_1E62, 0xE008);
        WriteInstruction(memory, 0x8C0E_1E64, 0xF3C6);
        WriteInstruction(memory, 0x8C0E_1E66, 0xE01C);
        WriteInstruction(memory, 0x8C0E_1E68, 0xFF37);
        WriteInstruction(memory, 0x8C0E_1E6A, 0xE004);
        WriteInstruction(memory, 0x8C0E_1E6C, 0xF3C6);
        WriteInstruction(memory, 0x8C0E_1E6E, 0xE020);
        WriteInstruction(memory, 0x8C0E_1E70, 0xFF37);
        WriteInstruction(memory, 0x8C0E_1E72, 0xE00C);
        WriteInstruction(memory, 0x8C0E_1E74, 0xF3C6);
        WriteInstruction(memory, 0x8C0E_1E76, 0xE024);
        WriteInstruction(memory, 0x8C0E_1E78, 0xFF37);
        WriteInstruction(memory, 0x8C0E_1E7A, 0xE010);
        WriteInstruction(memory, 0x8C0E_1E7C, 0x03CC);
        WriteInstruction(memory, 0x8C0E_1E7E, 0xE010);
        WriteInstruction(memory, 0x8C0E_1E80, 0x435A);
        WriteInstruction(memory, 0x8C0E_1E82, 0xF32D);
        WriteInstruction(memory, 0x8C0E_1E84, 0xF3E2);
        WriteInstruction(memory, 0x8C0E_1E86, 0xFF37);
        WriteInstruction(memory, 0x8C0E_1E88, 0xE011);
        WriteInstruction(memory, 0x8C0E_1E8A, 0x03CC);
        WriteInstruction(memory, 0x8C0E_1E8C, 0xE014);
        WriteInstruction(memory, 0x8C0E_1E8E, 0x435A);
        WriteInstruction(memory, 0x8C0E_1E90, 0xF32D);
        WriteInstruction(memory, 0x8C0E_1E92, 0xF3F2);
        WriteInstruction(memory, 0x8C0E_1E94, 0xFF37);
        WriteInstruction(memory, 0x8C0E_1E96, 0x85DB);
        WriteInstruction(memory, 0x8C0E_1E98, 0x6303);
        WriteInstruction(memory, 0x8C0E_1E9A, 0x435A);
        WriteInstruction(memory, 0x8C0E_1E9C, 0xE004);
        WriteInstruction(memory, 0x8C0E_1E9E, 0xF32D);
        WriteInstruction(memory, 0x8C0E_1EA0, 0xFF37);
        WriteInstruction(memory, 0x8C0E_1EA2, 0x85DC);
        WriteInstruction(memory, 0x8C0E_1EA4, 0x6303);
        WriteInstruction(memory, 0x8C0E_1EA6, 0x435A);
        WriteInstruction(memory, 0x8C0E_1EA8, 0xE008);
        WriteInstruction(memory, 0x8C0E_1EAA, 0xF32D);
        WriteInstruction(memory, 0x8C0E_1EAC, 0xFF37);
        WriteInstruction(memory, 0x8C0E_1EAE, 0x490B);
        WriteInstruction(memory, 0x8C0E_1EB0, 0x64F3);
        WriteInstruction(memory, 0x8C0E_1EC2, 0x00FF);
    }

    private static void WriteTextGlyphSetupData(DreamcastMemory memory)
    {
        memory.Write(0x8C20_2000, [(byte)' ']);
        memory.WriteUInt32(0x8C20_3000, BitConverter.SingleToUInt32Bits(0.9f));
        memory.WriteUInt32(0x8C20_3004, BitConverter.SingleToUInt32Bits(1.2f));
        memory.WriteUInt32(0x8C20_3008, BitConverter.SingleToUInt32Bits(0.5625f));
        memory.WriteUInt32(0x8C20_300C, BitConverter.SingleToUInt32Bits(0.75f));
        memory.Write(0x8C20_3010, [7]);
        memory.Write(0x8C20_3011, [24]);
        memory.WriteUInt16(0x8C20_1000 + 22, 0x0198);
        memory.WriteUInt16(0x8C20_1000 + 24, 0x0168);
    }

    private static void InitializeDoa2TextGlyphSetupState(Sh4Cpu cpu)
    {
        cpu.State.R[8] = 0x8C20_3000;
        cpu.State.R[9] = 0x8C10_0430;
        cpu.State.R[10] = 0x20;
        cpu.State.R[11] = 0x0000_01A7;
        cpu.State.R[13] = 0x8C20_1000;
        cpu.State.R[14] = 0x8C20_2000;
        cpu.State.R[15] = 0x8C20_4000;
        cpu.State.Fr[14] = BitConverter.SingleToUInt32Bits(0.125f);
        cpu.State.Fr[15] = BitConverter.SingleToUInt32Bits(0.25f);
        cpu.State.T = true;
    }

    private static void WriteDoa2TextAdvanceToNextGlyph(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C0E_1EB2, 0xE010);
        WriteInstruction(memory, 0x8C0E_1EB4, 0x03CC);
        WriteInstruction(memory, 0x8C0E_1EB6, 0x85DB);
        WriteInstruction(memory, 0x8C0E_1EB8, 0x303C);
        WriteInstruction(memory, 0x8C0E_1EBA, 0xA01A);
        WriteInstruction(memory, 0x8C0E_1EBC, 0x81DB);
        WriteInstruction(memory, 0x8C0E_1EF2, 0x7E01);
        WriteInstruction(memory, 0x8C0E_1EF4, 0x63E0);
        WriteInstruction(memory, 0x8C0E_1EF6, 0x2338);
        WriteInstruction(memory, 0x8C0E_1EF8, 0x8B86);
    }

    private static void WriteTextAdvanceData(DreamcastMemory memory)
    {
        memory.Write(0x8C20_2010, [10]);
        memory.Write(0x8C20_2001, [(byte)'A']);
        memory.WriteUInt16(0x8C20_1000 + 22, 0x0198);
    }

    private static void InitializeDoa2TextAdvanceState(Sh4Cpu cpu)
    {
        cpu.State.R[12] = 0x8C20_2000;
        cpu.State.R[13] = 0x8C20_1000;
        cpu.State.R[14] = 0x8C20_2000;
        cpu.State.T = true;
    }

    private static void WriteDoa2Fac40TrigArgumentWrapper(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C0F_AC40, 0xD338);
        WriteInstruction(memory, 0x8C0F_AC42, 0x2FE6);
        WriteInstruction(memory, 0x8C0F_AC44, 0x6E4D);
        WriteInstruction(memory, 0x8C0F_AC46, 0x3E37);
        WriteInstruction(memory, 0x8C0F_AC48, 0x8B02);
        WriteInstruction(memory, 0x8C0F_AC50, 0x9466);
        WriteInstruction(memory, 0x8C0F_AC52, 0x34E8);
        WriteInstruction(memory, 0x8C0F_AC54, 0xA2B4);
        WriteInstruction(memory, 0x8C0F_AC56, 0x6EF6);
        WriteInstruction(memory, 0x8C0F_AD20, 0x4000);
        memory.WriteUInt32(0x8C0F_AD24, 0x0000_8000);
    }

    private static void InitializeDoa2Fac40State(Sh4Cpu cpu)
    {
        cpu.State.R[4] = 0;
        cpu.State.R[14] = 1;
        cpu.State.R[15] = 0x8C20_4000;
        cpu.State.T = true;
    }

    private static void WriteDoa2RendererPrologueCommonPath(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_0430, 0x2FE6);
        WriteInstruction(memory, 0x8C10_0432, 0x2FD6);
        WriteInstruction(memory, 0x8C10_0434, 0x2FC6);
        WriteInstruction(memory, 0x8C10_0436, 0x2FB6);
        WriteInstruction(memory, 0x8C10_0438, 0x2FA6);
        WriteInstruction(memory, 0x8C10_043A, 0x2F96);
        WriteInstruction(memory, 0x8C10_043C, 0x2F86);
        WriteInstruction(memory, 0x8C10_043E, 0xFFFB);
        WriteInstruction(memory, 0x8C10_0440, 0xFFEB);
        WriteInstruction(memory, 0x8C10_0442, 0xFFDB);
        WriteInstruction(memory, 0x8C10_0444, 0xFFCB);
        WriteInstruction(memory, 0x8C10_0446, 0x4F22);
        WriteInstruction(memory, 0x8C10_0448, 0x7FC4);
        WriteInstruction(memory, 0x8C10_044A, 0xD112);
        WriteInstruction(memory, 0x8C10_044C, 0x6D43);
        WriteInstruction(memory, 0x8C10_044E, 0x62D2);
        WriteInstruction(memory, 0x8C10_0450, 0x6312);
        WriteInstruction(memory, 0x8C10_0452, 0x3233);
        WriteInstruction(memory, 0x8C10_0454, 0x8F24);
        WriteInstruction(memory, 0x8C10_0456, 0xFF9D);
        memory.WriteUInt32(0x8C10_0494, 0x8C2F_07D0);
    }

    private static void WriteRendererPrologueData(DreamcastMemory memory)
    {
        memory.WriteUInt32(0x8C20_1000, 0x0000_01A7);
        memory.WriteUInt32(0x8C2F_07D0, 0x0000_0600);
    }

    private static void InitializeDoa2RendererPrologueState(Sh4Cpu cpu)
    {
        cpu.State.R[4] = 0x8C20_1000;
        cpu.State.R[8] = 0x8888_8888;
        cpu.State.R[9] = 0x9999_9999;
        cpu.State.R[10] = 0xAAAA_AAAA;
        cpu.State.R[11] = 0xBBBB_BBBB;
        cpu.State.R[12] = 0xCCCC_CCCC;
        cpu.State.R[13] = 0xDDDD_DDDD;
        cpu.State.R[14] = 0xEEEE_EEEE;
        cpu.State.R[15] = 0x8C20_5000;
        cpu.State.Pr = 0x8C0E_1EB2;
        for (var index = 8; index <= 15; index++)
        {
            cpu.State.Fr[index] = BitConverter.SingleToUInt32Bits(index + 0.25f);
        }

        cpu.State.T = true;
    }

    private static void WriteDoa2RendererMode2EntryToFirstTrigCall(DreamcastMemory memory)
    {
        WriteDoa2RendererPrologueCommonPath(memory);
        WriteDoa2RendererMode2LookupCommonPath(memory);
        WriteDoa2RendererMode2TrigSetupToFirstCall(memory);
    }

    private static void WriteRendererMode2EntryToFirstTrigCallData(DreamcastMemory memory)
    {
        WriteRendererPrologueData(memory);
        WriteRendererMode2LookupData(memory);
        memory.WriteUInt32(0x8C20_1000 + 12, BitConverter.SingleToUInt32Bits(1.25f));
        memory.WriteUInt32(0x8C20_1000 + 16, BitConverter.SingleToUInt32Bits(0.5f));
        memory.WriteUInt32(0x8C20_1000 + 20, BitConverter.SingleToUInt32Bits(0.75f));
        memory.WriteUInt32(0x8C20_1000 + 40, 0x0000_1234);
        memory.WriteUInt32(0x8C20_1000 + 44, BitConverter.SingleToUInt32Bits(0.1f));
        memory.WriteUInt32(0x8C20_C4F4 + 24, BitConverter.SingleToUInt32Bits(8.0f));
        memory.WriteUInt32(0x8C20_C4F4 + 28, BitConverter.SingleToUInt32Bits(6.0f));
    }

    private static void WriteDoa2RendererMode2LookupCommonPath(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_04A0, 0xD335);
        WriteInstruction(memory, 0x8C10_04A2, 0x68D2);
        WriteInstruction(memory, 0x8C10_04A4, 0x6032);
        WriteInstruction(memory, 0x8C10_04A6, 0x4800);
        WriteInstruction(memory, 0x8C10_04A8, 0x088D);
        WriteInstruction(memory, 0x8C10_04AA, 0x6083);
        WriteInstruction(memory, 0x8C10_04AC, 0x88FF);
        WriteInstruction(memory, 0x8C10_04AE, 0x8B01);
        WriteInstruction(memory, 0x8C10_04B4, 0xD231);
        WriteInstruction(memory, 0x8C10_04B6, 0x6C83);
        WriteInstruction(memory, 0x8C10_04B8, 0x4C08);
        WriteInstruction(memory, 0x8C10_04BA, 0x50DC);
        WriteInstruction(memory, 0x8C10_04BC, 0x6322);
        WriteInstruction(memory, 0x8C10_04BE, 0x4C08);
        WriteInstruction(memory, 0x8C10_04C0, 0x4C00);
        WriteInstruction(memory, 0x8C10_04C2, 0x8800);
        WriteInstruction(memory, 0x8C10_04C4, 0x3C3C);
        WriteInstruction(memory, 0x8C10_04C6, 0x8D13);
        WriteInstruction(memory, 0x8C10_04C8, 0xE400);
        WriteInstruction(memory, 0x8C10_04CA, 0x8802);
        WriteInstruction(memory, 0x8C10_04CC, 0x8918);
        memory.WriteUInt32(0x8C10_0578, 0x8C2F_07CC);
        memory.WriteUInt32(0x8C10_057C, 0x8C2F_07DC);
    }

    private static void WriteRendererMode2LookupData(DreamcastMemory memory)
    {
        memory.WriteUInt32(0x8C20_1000, 0x0000_01A7);
        memory.WriteUInt32(0x8C20_1000 + 48, 2);
        memory.WriteUInt32(0x8C2F_07CC, 0x8C20_B3D4);
        memory.WriteUInt16(0x8C20_B3D4 + (0x1A7 * 2), 0x0023);
        memory.WriteUInt32(0x8C2F_07DC, 0x8C20_C094);
    }

    private static void InitializeDoa2RendererMode2LookupState(Sh4Cpu cpu)
    {
        cpu.State.R[13] = 0x8C20_1000;
        cpu.State.T = true;
    }

    private static void WriteDoa2RendererMode2TrigSetupToFirstCall(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_0500, 0xE02C);
        WriteInstruction(memory, 0x8C10_0502, 0xFED6);
        WriteInstruction(memory, 0x8C10_0504, 0xEE01);
        WriteInstruction(memory, 0x8C10_0506, 0xC71E);
        WriteInstruction(memory, 0x8C10_0508, 0xFDFC);
        WriteInstruction(memory, 0x8C10_050A, 0xF408);
        WriteInstruction(memory, 0x8C10_050C, 0xE010);
        WriteInstruction(memory, 0x8C10_050E, 0xF3D6);
        WriteInstruction(memory, 0x8C10_0510, 0xE018);
        WriteInstruction(memory, 0x8C10_0512, 0xF2C6);
        WriteInstruction(memory, 0x8C10_0514, 0xE014);
        WriteInstruction(memory, 0x8C10_0516, 0xD31B);
        WriteInstruction(memory, 0x8C10_0518, 0xF232);
        WriteInstruction(memory, 0x8C10_051A, 0xF3D6);
        WriteInstruction(memory, 0x8C10_051C, 0xE01C);
        WriteInstruction(memory, 0x8C10_051E, 0xFC2C);
        WriteInstruction(memory, 0x8C10_0520, 0xFC42);
        WriteInstruction(memory, 0x8C10_0522, 0xF2C6);
        WriteInstruction(memory, 0x8C10_0524, 0xE004);
        WriteInstruction(memory, 0x8C10_0526, 0xF232);
        WriteInstruction(memory, 0x8C10_0528, 0xF242);
        WriteInstruction(memory, 0x8C10_052A, 0xFF27);
        WriteInstruction(memory, 0x8C10_052C, 0xE00C);
        WriteInstruction(memory, 0x8C10_052E, 0xF3D6);
        WriteInstruction(memory, 0x8C10_0530, 0xFD33);
        WriteInstruction(memory, 0x8C10_0532, 0x430B);
        WriteInstruction(memory, 0x8C10_0534, 0x54DA);
        memory.WriteUInt32(0x8C10_0580, 0x3F00_0000);
        memory.WriteUInt32(0x8C10_0584, 0x8C0F_B1C0);
    }

    private static void WriteRendererMode2TrigSetupData(DreamcastMemory memory)
    {
        memory.WriteUInt32(0x8C20_1000 + 12, BitConverter.SingleToUInt32Bits(1.25f));
        memory.WriteUInt32(0x8C20_1000 + 16, BitConverter.SingleToUInt32Bits(0.5f));
        memory.WriteUInt32(0x8C20_1000 + 20, BitConverter.SingleToUInt32Bits(0.75f));
        memory.WriteUInt32(0x8C20_1000 + 40, 0x0000_1234);
        memory.WriteUInt32(0x8C20_1000 + 44, BitConverter.SingleToUInt32Bits(0.1f));
        memory.WriteUInt32(0x8C20_2000 + 24, BitConverter.SingleToUInt32Bits(8.0f));
        memory.WriteUInt32(0x8C20_2000 + 28, BitConverter.SingleToUInt32Bits(6.0f));
    }

    private static void InitializeDoa2RendererMode2TrigSetupState(Sh4Cpu cpu)
    {
        cpu.State.R[12] = 0x8C20_2000;
        cpu.State.R[13] = 0x8C20_1000;
        cpu.State.R[15] = 0x8C20_3000;
        cpu.State.Fr[15] = BitConverter.SingleToUInt32Bits(32.0f);
        cpu.State.T = true;
    }

    private static void WriteDoa2RendererSecondTrigCallBridge(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_0536, 0xD314);
        WriteInstruction(memory, 0x8C10_0538, 0xE008);
        WriteInstruction(memory, 0x8C10_053A, 0xFF07);
        WriteInstruction(memory, 0x8C10_053C, 0x430B);
        WriteInstruction(memory, 0x8C10_053E, 0x54DA);
        memory.WriteUInt32(0x8C10_0588, 0x8C0F_AC40);
    }

    private static void WriteRendererSecondTrigCallData(DreamcastMemory memory)
    {
        memory.WriteUInt32(0x8C20_1000 + 40, 0x0000_1234);
    }

    private static void InitializeDoa2RendererSecondTrigCallState(Sh4Cpu cpu)
    {
        cpu.State.R[13] = 0x8C20_1000;
        cpu.State.R[15] = 0x8C20_3000;
        cpu.State.Fr[0] = 0xFFC0_0000;
        cpu.State.T = true;
    }

    private static void WriteDoa2RendererPostSecondTrigBridge(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_0540, 0xE008);
        WriteInstruction(memory, 0x8C10_0542, 0x6AF3);
        WriteInstruction(memory, 0x8C10_0544, 0xF6F6);
        WriteInstruction(memory, 0x8C10_0546, 0xE004);
        WriteInstruction(memory, 0x8C10_0548, 0x6BF3);
        WriteInstruction(memory, 0x8C10_054A, 0x7A1C);
        WriteInstruction(memory, 0x8C10_054C, 0xF5F6);
        WriteInstruction(memory, 0x8C10_054E, 0x7B2C);
        WriteInstruction(memory, 0x8C10_0550, 0x66A3);
        WriteInstruction(memory, 0x8C10_0552, 0x65B3);
        WriteInstruction(memory, 0x8C10_0554, 0xF70C);
        WriteInstruction(memory, 0x8C10_0556, 0xF4CC);
        WriteInstruction(memory, 0x8C10_0558, 0xB26A);
        WriteInstruction(memory, 0x8C10_055A, 0x64D3);
    }

    private static void WriteRendererPostSecondTrigData(DreamcastMemory memory)
    {
        memory.WriteUInt32(0x8C20_3004, BitConverter.SingleToUInt32Bits(0.75f));
        memory.WriteUInt32(0x8C20_3008, 0xFFC0_0000);
    }

    private static void InitializeDoa2RendererPostSecondTrigState(Sh4Cpu cpu)
    {
        cpu.State.R[13] = 0x8C20_1000;
        cpu.State.R[15] = 0x8C20_3000;
        cpu.State.Fr[0] = 0xFFC0_0000;
        cpu.State.Fr[12] = BitConverter.SingleToUInt32Bits(0.125f);
        cpu.State.T = true;
    }

    private static void WriteDoa2RendererPostCallScaleSetup(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_055C, 0xD20D);
        WriteInstruction(memory, 0x8C10_055E, 0xE004);
        WriteInstruction(memory, 0x8C10_0560, 0xD30B);
        WriteInstruction(memory, 0x8C10_0562, 0xD40A);
        WriteInstruction(memory, 0x8C10_0564, 0xF228);
        WriteInstruction(memory, 0x8C10_0566, 0xF546);
        WriteInstruction(memory, 0x8C10_0568, 0xF338);
        WriteInstruction(memory, 0x8C10_056A, 0xF448);
        WriteInstruction(memory, 0x8C10_056C, 0xF522);
        WriteInstruction(memory, 0x8C10_056E, 0xE400);
        WriteInstruction(memory, 0x8C10_0570, 0xF432);
        WriteInstruction(memory, 0x8C10_0572, 0xA01D);
        WriteInstruction(memory, 0x8C10_0574, 0xE910);
        memory.WriteUInt32(0x8C10_058C, 0x8C2F_07E0);
        memory.WriteUInt32(0x8C10_0590, 0x8C1C_A8D4);
        memory.WriteUInt32(0x8C10_0594, 0x8C1C_A8D8);
    }

    private static void WriteRendererPostCallScaleSetupData(DreamcastMemory memory)
    {
        memory.WriteUInt32(0x8C1C_A8D4, BitConverter.SingleToUInt32Bits(8.0f));
        memory.WriteUInt32(0x8C1C_A8D8, BitConverter.SingleToUInt32Bits(0.5f));
        memory.WriteUInt32(0x8C2F_07E0, BitConverter.SingleToUInt32Bits(0.25f));
        memory.WriteUInt32(0x8C2F_07E4, BitConverter.SingleToUInt32Bits(2.0f));
    }

    private static void InitializeDoa2RendererPostCallScaleSetupState(Sh4Cpu cpu)
    {
        cpu.State.T = true;
    }

    private static void WriteDoa2RendererModeWordSetupToColorPack(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_05B4, 0x62C2);
        WriteInstruction(memory, 0x8C10_05B6, 0xD331);
        WriteInstruction(memory, 0x8C10_05B8, 0x2239);
        WriteInstruction(memory, 0x8C10_05BA, 0x935B);
        WriteInstruction(memory, 0x8C10_05BC, 0x2C22);
        WriteInstruction(memory, 0x8C10_05BE, 0x51C1);
        WriteInstruction(memory, 0x8C10_05C0, 0xD22F);
        WriteInstruction(memory, 0x8C10_05C2, 0x2129);
        WriteInstruction(memory, 0x8C10_05C4, 0x1C11);
        WriteInstruction(memory, 0x8C10_05C6, 0x50C2);
        WriteInstruction(memory, 0x8C10_05C8, 0xD12E);
        WriteInstruction(memory, 0x8C10_05CA, 0x2019);
        WriteInstruction(memory, 0x8C10_05CC, 0x1C02);
        WriteInstruction(memory, 0x8C10_05CE, 0x50DD);
        WriteInstruction(memory, 0x8C10_05D0, 0x2038);
        WriteInstruction(memory, 0x8C10_05D2, 0x8B03);
        WriteInstruction(memory, 0x8C10_05DC, 0x53DD);
        WriteInstruction(memory, 0x8C10_05DE, 0x944A);
        WriteInstruction(memory, 0x8C10_05E0, 0x954A);
        WriteInstruction(memory, 0x8C10_05E2, 0x2348);
        WriteInstruction(memory, 0x8C10_05E4, 0x8902);
        WriteInstruction(memory, 0x8C10_05EC, 0x52DD);
        WriteInstruction(memory, 0x8C10_05EE, 0xD327);
        WriteInstruction(memory, 0x8C10_05F0, 0x2238);
        WriteInstruction(memory, 0x8C10_05F2, 0x8902);
        WriteInstruction(memory, 0x8C10_05FA, 0x50DD);
        WriteInstruction(memory, 0x8C10_05FC, 0xE3F8);
        WriteInstruction(memory, 0x8C10_05FE, 0xE407);
        WriteInstruction(memory, 0x8C10_0600, 0x403C);
        WriteInstruction(memory, 0x8C10_0602, 0x2409);
        WriteInstruction(memory, 0x8C10_0604, 0x2448);
        WriteInstruction(memory, 0x8C10_0606, 0x8F01);
        WriteInstruction(memory, 0x8C10_0608, 0xE31D);
        WriteInstruction(memory, 0x8C10_060A, 0xE404);
        WriteInstruction(memory, 0x8C10_060C, 0x52C1);
        WriteInstruction(memory, 0x8C10_060E, 0x443C);
        WriteInstruction(memory, 0x8C10_0610, 0x2EE8);
        WriteInstruction(memory, 0x8C10_0612, 0x224B);
        WriteInstruction(memory, 0x8C10_0614, 0x8D1C);
        WriteInstruction(memory, 0x8C10_0616, 0x1C21);
        WriteInstruction(memory, 0x8C10_0618, 0x50DC);
        WriteInstruction(memory, 0x8C10_061A, 0x8804);
        WriteInstruction(memory, 0x8C10_061C, 0x8B08);
        WriteInstruction(memory, 0x8C10_0630, 0xD219);
        WriteInstruction(memory, 0x8C10_0632, 0xD11A);
        WriteInstruction(memory, 0x8C10_0634, 0x6022);
        WriteInstruction(memory, 0x8C10_0636, 0x53C2);
        WriteInstruction(memory, 0x8C10_0638, 0xCAFC);
        WriteInstruction(memory, 0x8C10_063A, 0x4028);
        WriteInstruction(memory, 0x8C10_063C, 0x4018);
        WriteInstruction(memory, 0x8C10_063E, 0x201B);
        WriteInstruction(memory, 0x8C10_0640, 0x230B);
        WriteInstruction(memory, 0x8C10_0642, 0x1C32);
        WriteInstruction(memory, 0x8C10_0644, 0x60C2);
        WriteInstruction(memory, 0x8C10_0646, 0xD316);
        WriteInstruction(memory, 0x8C10_0648, 0x203B);
        WriteInstruction(memory, 0x8C10_064A, 0xA006);
        WriteInstruction(memory, 0x8C10_064C, 0x2C02);
        WriteInstruction(memory, 0x8C10_065A, 0x53DD);
        WriteInstruction(memory, 0x8C10_065C, 0x2538);
        WriteInstruction(memory, 0x8C10_065E, 0x892F);
        WriteInstruction(memory, 0x8C10_0660, 0xD312);
        WriteInstruction(memory, 0x8C10_0662, 0xD213);
        WriteInstruction(memory, 0x8C10_0664, 0xD110);
        WriteInstruction(memory, 0x8C10_0666, 0xF528);
        WriteInstruction(memory, 0x8C10_0668, 0xF718);
        WriteInstruction(memory, 0x8C10_066A, 0xF638);
        WriteInstruction(memory, 0x8C10_066C, 0xB228);
        WriteInstruction(memory, 0x8C10_066E, 0xF4EC);
        memory.WriteUInt16(0x8C10_0674, 0x0800);
        memory.WriteUInt16(0x8C10_0676, 0x1000);
        memory.WriteUInt16(0x8C10_0678, 0x2000);
        memory.WriteUInt32(0x8C10_067C, 0xF8FC_FFFF);
        memory.WriteUInt32(0x8C10_0680, 0x1FFF_FFFF);
        memory.WriteUInt32(0x8C10_0684, 0x0327_8FFF);
        memory.WriteUInt32(0x8C10_068C, 0x0001_0000);
        memory.WriteUInt32(0x8C10_0698, 0x8C1C_A5D8);
        memory.WriteUInt32(0x8C10_069C, 0x0010_0000);
        memory.WriteUInt32(0x8C10_06A0, 0x0200_0000);
        memory.WriteUInt32(0x8C10_06A8, 0x8C1C_A928);
        memory.WriteUInt32(0x8C10_06AC, 0x8C1C_A924);
        memory.WriteUInt32(0x8C10_06B0, 0x8C1C_A920);
    }

    private static void WriteRendererModeWordSetupData(DreamcastMemory memory)
    {
        memory.WriteUInt32(0x8C20_1000 + 48, 2);
        memory.WriteUInt32(0x8C20_1000 + 52, 0x0000_2805);
        memory.WriteUInt32(0x8C20_2000, 0xA200_0009);
        memory.WriteUInt32(0x8C20_2004, 0x8000_0000);
        memory.WriteUInt32(0x8C20_2008, 0x9410_04C0);
        memory.WriteUInt32(0x8C1C_A5D8, 0x0000_0068);
        memory.WriteUInt32(0x8C1C_A920, BitConverter.SingleToUInt32Bits(0.25f));
        memory.WriteUInt32(0x8C1C_A924, BitConverter.SingleToUInt32Bits(0.5f));
        memory.WriteUInt32(0x8C1C_A928, BitConverter.SingleToUInt32Bits(0.75f));
    }

    private static void InitializeDoa2RendererModeWordSetupState(Sh4Cpu cpu)
    {
        cpu.State.R[12] = 0x8C20_2000;
        cpu.State.R[13] = 0x8C20_1000;
        cpu.State.R[14] = 1;
        cpu.State.Fr[14] = BitConverter.SingleToUInt32Bits(0.1f);
        cpu.State.T = true;
    }

    private static void WriteDoa2RendererColorPackReturnBridge(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_0670, 0xA03C);
        WriteInstruction(memory, 0x8C10_0672, 0x1C04);
    }

    private static void InitializeDoa2RendererColorPackReturnState(Sh4Cpu cpu)
    {
        cpu.State.R[0] = 0x3D3F_3F3F;
        cpu.State.R[12] = 0x8C20_2000;
        cpu.State.T = true;
    }

    private static void WriteDoa2RendererInterpolationPrologueToCopyTail(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_0A30, 0xFFEB);
        WriteInstruction(memory, 0x8C10_0A32, 0x4F22);
        WriteInstruction(memory, 0x8C10_0A34, 0x7FDC);
        WriteInstruction(memory, 0x8C10_0A36, 0xF95C);
        WriteInstruction(memory, 0x8C10_0A38, 0xF572);
        WriteInstruction(memory, 0x8C10_0A3A, 0xF84C);
        WriteInstruction(memory, 0x8C10_0A3C, 0xF462);
        WriteInstruction(memory, 0x8C10_0A3E, 0xF872);
        WriteInstruction(memory, 0x8C10_0A40, 0x61F3);
        WriteInstruction(memory, 0x8C10_0A42, 0xF962);
        WriteInstruction(memory, 0x8C10_0A44, 0xD248);
        WriteInstruction(memory, 0x8C10_0A46, 0x67F3);
        WriteInstruction(memory, 0x8C10_0A48, 0xD348);
        WriteInstruction(memory, 0x8C10_0A4A, 0x7704);
        WriteInstruction(memory, 0x8C10_0A4C, 0x7104);
        WriteInstruction(memory, 0x8C10_0A4E, 0x430B);
        WriteInstruction(memory, 0x8C10_0A50, 0xE020);
        WriteInstruction(memory, 0x8C10_E5CC, 0x2F36);
        WriteInstruction(memory, 0x8C10_E5CE, 0xD305);
        WriteInstruction(memory, 0x8C10_E5D0, 0x033E);
        WriteInstruction(memory, 0x8C10_E5D2, 0x70FC);
        WriteInstruction(memory, 0x8C10_E5D4, 0x432B);
        WriteInstruction(memory, 0x8C10_E5D6, 0x002E);
        memory.WriteUInt32(0x8C10_0B68, 0x8C14_8EBC);
        memory.WriteUInt32(0x8C10_0B6C, 0x8C10_E5CC);
        memory.WriteUInt32(0x8C10_E5E4, 0x8C10_E62C);
        memory.WriteUInt32(0x8C10_E64C, 0x8C10_E60A);
    }

    private static void WriteRendererInterpolationPrologueData(DreamcastMemory memory)
    {
        for (var index = 0u; index < 8; index++)
        {
            memory.WriteUInt32(0x8C14_8EBC + (index * 4), BitConverter.SingleToUInt32Bits(index + 1.0f));
        }
    }

    private static void InitializeDoa2RendererInterpolationPrologueState(Sh4Cpu cpu)
    {
        cpu.State.R[4] = 0x8C20_1000;
        cpu.State.R[15] = 0x8C20_4000;
        cpu.State.Pr = 0x8C10_055C;
        cpu.State.Fr[4] = BitConverter.SingleToUInt32Bits(0.125f);
        cpu.State.Fr[5] = BitConverter.SingleToUInt32Bits(0.25f);
        cpu.State.Fr[6] = BitConverter.SingleToUInt32Bits(2.0f);
        cpu.State.Fr[7] = BitConverter.SingleToUInt32Bits(3.0f);
        cpu.State.Fr[14] = BitConverter.SingleToUInt32Bits(0.5f);
        cpu.State.T = true;
    }

    private static void WriteDoa2RendererInterpolationSetupToLoopExit(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_0A52, 0x504D);
        WriteInstruction(memory, 0x8C10_0A54, 0xC80F);
        WriteInstruction(memory, 0x8C10_0A56, 0x8B03);
        WriteInstruction(memory, 0x8C10_0A60, 0x2F02);
        WriteInstruction(memory, 0x8C10_0A62, 0xC903);
        WriteInstruction(memory, 0x8C10_0A64, 0x405A);
        WriteInstruction(memory, 0x8C10_0A66, 0x60F2);
        WriteInstruction(memory, 0x8C10_0A68, 0x4021);
        WriteInstruction(memory, 0x8C10_0A6A, 0xF32D);
        WriteInstruction(memory, 0x8C10_0A6C, 0x4021);
        WriteInstruction(memory, 0x8C10_0A6E, 0xC903);
        WriteInstruction(memory, 0x8C10_0A70, 0x405A);
        WriteInstruction(memory, 0x8C10_0A72, 0xF63C);
        WriteInstruction(memory, 0x8C10_0A74, 0xF32D);
        WriteInstruction(memory, 0x8C10_0A76, 0xF73C);
        WriteInstruction(memory, 0x8C10_0A78, 0xE104);
        WriteDoa2InterpolationLoop(memory);
        WriteDoa2RendererInterpolationEpilogueReturn(memory);
    }

    private static void WriteRendererInterpolationSetupData(DreamcastMemory memory)
    {
        memory.WriteUInt32(0x8C20_5034, 0x0000_2805);
        WriteInterpolationData(memory);
    }

    private static void InitializeDoa2RendererInterpolationSetupState(Sh4Cpu cpu)
    {
        cpu.State.R[4] = 0x8C20_5000;
        cpu.State.R[5] = 0x8C20_3000;
        cpu.State.R[6] = 0x8C20_4000;
        cpu.State.R[7] = 0x8C20_6000;
        cpu.State.R[15] = 0x8C20_7000;
        cpu.State.Pr = 0x8C10_055C;
        cpu.State.Fr[1] = 0xFFC0_0000;
        cpu.State.Fr[2] = 0xFFC0_0000;
        cpu.State.Fr[3] = 0xFFC0_0000;
        cpu.State.Fr[4] = 0xFFC0_0000;
        cpu.State.Fr[5] = 0xFFC0_0000;
        cpu.State.Fr[8] = 0xFFC0_0000;
        cpu.State.Fr[9] = 0xFFC0_0000;
        cpu.State.T = true;
    }

    private static void WriteDoa2RendererInterpolationEpilogueReturn(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_0AB6, 0x7F24);
        WriteInstruction(memory, 0x8C10_0AB8, 0x4F26);
        WriteInstruction(memory, 0x8C10_0ABA, 0x000B);
        WriteInstruction(memory, 0x8C10_0ABC, 0xFEF9);
    }

    private static void WriteRendererInterpolationEpilogueStack(DreamcastMemory memory)
    {
        memory.WriteUInt32(0x8C20_7024, 0x8C10_055C);
        memory.WriteUInt32(0x8C20_7028, BitConverter.SingleToUInt32Bits(0.1f));
    }

    private static void InitializeDoa2RendererInterpolationEpilogueState(Sh4Cpu cpu)
    {
        cpu.State.R[15] = 0x8C20_7000;
        cpu.State.Pr = 0x8C10_0A52;
        cpu.State.Fr[14] = BitConverter.SingleToUInt32Bits(2.0f);
        cpu.State.T = true;
    }

    private static void WriteDoa2SignedRemainderHelper(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_751C, 0x2008);
        WriteInstruction(memory, 0x8C10_751E, 0x2F26);
        WriteInstruction(memory, 0x8C10_7520, 0x8955);
        WriteInstruction(memory, 0x8C10_7522, 0x2F36);
        WriteInstruction(memory, 0x8C10_7524, 0xE200);
        WriteInstruction(memory, 0x8C10_7526, 0x2F46);
        WriteInstruction(memory, 0x8C10_7528, 0x2127);
        WriteInstruction(memory, 0x8C10_752A, 0x0429);
        WriteInstruction(memory, 0x8C10_752C, 0x333A);
        WriteInstruction(memory, 0x8C10_752E, 0x312A);
        WriteInstruction(memory, 0x8C10_7530, 0x2307);
        for (var address = 0x8C10_7532u; address <= 0x8C10_75B0; address += 4)
        {
            WriteInstruction(memory, address, 0x4124);
            WriteInstruction(memory, address + 2, 0x3304);
        }

        WriteInstruction(memory, 0x8C10_75B2, 0x2327);
        WriteInstruction(memory, 0x8C10_75B4, 0x0229);
        WriteInstruction(memory, 0x8C10_75B6, 0x224A);
        WriteInstruction(memory, 0x8C10_75B8, 0x4225);
        WriteInstruction(memory, 0x8C10_75BA, 0x8B02);
        WriteInstruction(memory, 0x8C10_75BC, 0x2307);
        WriteInstruction(memory, 0x8C10_75BE, 0x4321);
        WriteInstruction(memory, 0x8C10_75C0, 0x3304);
        WriteInstruction(memory, 0x8C10_75C2, 0x334C);
        WriteInstruction(memory, 0x8C10_75C4, 0x6033);
        WriteInstruction(memory, 0x8C10_75C6, 0x64F6);
        WriteInstruction(memory, 0x8C10_75C8, 0x63F6);
        WriteInstruction(memory, 0x8C10_75CA, 0x000B);
        WriteInstruction(memory, 0x8C10_75CC, 0x62F6);
    }

    private static void InitializeDoa2SignedRemainderState(Sh4Cpu cpu, uint dividend, uint divisor)
    {
        cpu.State.R[0] = divisor;
        cpu.State.R[1] = dividend;
        cpu.State.R[2] = 0x8C20_3000;
        cpu.State.R[3] = 0x8C10_751C;
        cpu.State.R[4] = 0x8C20_4000;
        cpu.State.R[15] = 0x8C20_7000;
        cpu.State.Pr = 0x8C11_753E;
        cpu.State.T = true;
        cpu.State.M = true;
        cpu.State.Q = true;
    }

    private static void WriteDoa2UnsignedDivideHelper(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_7424, 0x2008);
        WriteInstruction(memory, 0x8C10_7426, 0x2F26);
        WriteInstruction(memory, 0x8C10_7428, 0x8945);
        WriteInstruction(memory, 0x8C10_742A, 0xE200);
        WriteInstruction(memory, 0x8C10_742C, 0x0019);
        for (var address = 0x8C10_742Eu; address <= 0x8C10_74AC; address += 4)
        {
            WriteInstruction(memory, address, 0x4124);
            WriteInstruction(memory, address + 2, 0x3204);
        }

        WriteInstruction(memory, 0x8C10_74AE, 0x4124);
        WriteInstruction(memory, 0x8C10_74B0, 0x6013);
        WriteInstruction(memory, 0x8C10_74B2, 0x000B);
        WriteInstruction(memory, 0x8C10_74B4, 0x62F6);
    }

    private static void InitializeDoa2UnsignedDivideState(Sh4Cpu cpu, uint dividend, uint divisor)
    {
        cpu.State.R[0] = divisor;
        cpu.State.R[1] = dividend;
        cpu.State.R[2] = 0x8C20_3000;
        cpu.State.R[15] = 0x8C20_7000;
        cpu.State.Pr = 0x8C11_C7DE;
        cpu.State.T = true;
        cpu.State.M = true;
        cpu.State.Q = true;
    }

    private static void WriteDoa2ListEntryAllocatorPair(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C11_7500, 0xD331);
        WriteInstruction(memory, 0x8C11_7502, 0x430B);
        WriteInstruction(memory, 0x8C11_7504, 0x64D3);
        WriteInstruction(memory, 0x8C11_7506, 0xD231);
        WriteInstruction(memory, 0x8C11_7508, 0x6B03);
        WriteInstruction(memory, 0x8C11_750A, 0x420B);
        WriteInstruction(memory, 0x8C11_750C, 0x64D3);
        memory.WriteUInt32(0x8C11_75C8, 0x8C12_4634);
        memory.WriteUInt32(0x8C11_75CC, 0x8C12_4646);

        WriteInstruction(memory, 0x8C12_4634, 0x6043);
        WriteInstruction(memory, 0x8C12_4636, 0x4000);
        WriteInstruction(memory, 0x8C12_4638, 0x6343);
        WriteInstruction(memory, 0x8C12_463A, 0x303C);
        WriteInstruction(memory, 0x8C12_463C, 0xD212);
        WriteInstruction(memory, 0x8C12_463E, 0x4008);
        WriteInstruction(memory, 0x8C12_4640, 0x4000);
        WriteInstruction(memory, 0x8C12_4642, 0x000B);
        WriteInstruction(memory, 0x8C12_4644, 0x302C);
        memory.WriteUInt32(0x8C12_4688, 0x8C30_36BC);

        WriteInstruction(memory, 0x8C12_4646, 0x4F22);
        WriteInstruction(memory, 0x8C12_4648, 0xD311);
        WriteInstruction(memory, 0x8C12_464A, 0x430B);
        WriteInstruction(memory, 0x8C12_464C, 0x0009);
        WriteInstruction(memory, 0x8C12_464E, 0x4F26);
        WriteInstruction(memory, 0x8C12_4650, 0x000B);
        WriteInstruction(memory, 0x8C12_4652, 0x0009);
        memory.WriteUInt32(0x8C12_4690, 0x8C13_4F48);

        WriteInstruction(memory, 0x8C13_4F48, 0xE278);
        WriteInstruction(memory, 0x8C13_4F4A, 0xD30D);
        WriteInstruction(memory, 0x8C13_4F4C, 0x0427);
        WriteInstruction(memory, 0x8C13_4F4E, 0x9110);
        WriteInstruction(memory, 0x8C13_4F50, 0x6032);
        WriteInstruction(memory, 0x8C13_4F52, 0x041A);
        WriteInstruction(memory, 0x8C13_4F54, 0x301C);
        WriteInstruction(memory, 0x8C13_4F56, 0x000B);
        WriteInstruction(memory, 0x8C13_4F58, 0x304C);
        memory.WriteUInt16(0x8C13_4F72, 0x044C);
        memory.WriteUInt32(0x8C13_4F80, 0x8C31_007C);
        memory.WriteUInt32(0x8C31_007C, 0x8C30_EF80);
    }

    private static void InitializeDoa2ListEntryAllocatorState(Sh4Cpu cpu)
    {
        cpu.State.R[13] = 3;
        cpu.State.R[15] = 0x8C20_7000;
        cpu.State.Pr = 0x8C01_266E;
        cpu.State.T = true;
    }

    private static void WriteDoa2ListEntrySetupToClassifier(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C11_750E, 0xEE34);
        WriteInstruction(memory, 0x8C11_7510, 0xD32F);
        WriteInstruction(memory, 0x8C11_7512, 0x0DE7);
        WriteInstruction(memory, 0x8C11_7514, 0x61D3);
        WriteInstruction(memory, 0x8C11_7516, 0x4108);
        WriteInstruction(memory, 0x8C11_7518, 0x6C03);
        WriteInstruction(memory, 0x8C11_751A, 0x66B3);
        WriteInstruction(memory, 0x8C11_751C, 0x65C3);
        WriteInstruction(memory, 0x8C11_751E, 0x0E1A);
        WriteInstruction(memory, 0x8C11_7520, 0x3E3C);
        WriteInstruction(memory, 0x8C11_7522, 0xD32C);
        WriteInstruction(memory, 0x8C11_7524, 0x1E0C);
        WriteInstruction(memory, 0x8C11_7526, 0x52E2);
        WriteInstruction(memory, 0x8C11_7528, 0x331C);
        WriteInstruction(memory, 0x8C11_752A, 0x1E2B);
        WriteInstruction(memory, 0x8C11_752C, 0x2F36);
        WriteInstruction(memory, 0x8C11_752E, 0xBFA8);
        WriteInstruction(memory, 0x8C11_7530, 0x64E3);
        memory.WriteUInt32(0x8C11_75D0, 0x8C2F_B814);
        memory.WriteUInt32(0x8C11_75D4, 0x8C2F_BCF4);
    }

    private static void WriteListEntrySetupData(DreamcastMemory memory)
    {
        memory.WriteUInt32(0x8C2F_B8B8, 0xCAFE_BABE);
    }

    private static void InitializeDoa2ListEntrySetupState(Sh4Cpu cpu)
    {
        cpu.State.R[0] = 0x8C30_F534;
        cpu.State.R[11] = 0x8C30_3704;
        cpu.State.R[13] = 3;
        cpu.State.R[15] = 0x8C20_7000;
        cpu.State.T = true;
    }

    private static void WriteDoa2ListEntryPostClassifierToRemainder(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C11_7532, 0x62F6);
        WriteInstruction(memory, 0x8C11_7534, 0x61D3);
        WriteInstruction(memory, 0x8C11_7536, 0xD328);
        WriteInstruction(memory, 0x8C11_7538, 0x2202);
        WriteInstruction(memory, 0x8C11_753A, 0x430B);
        WriteInstruction(memory, 0x8C11_753C, 0x6093);
        memory.WriteUInt32(0x8C11_75D8, 0x8C10_751C);
    }

    private static void WriteListEntryPostClassifierStack(DreamcastMemory memory)
    {
        memory.WriteUInt32(0x8C20_6FFC, 0x8C20_5000);
    }

    private static void InitializeDoa2ListEntryPostClassifierState(Sh4Cpu cpu)
    {
        cpu.State.R[0] = 0;
        cpu.State.R[9] = 6;
        cpu.State.R[13] = 4;
        cpu.State.R[15] = 0x8C20_6FFC;
        cpu.State.T = true;
    }

    private static void WriteDoa2ListEntryNonzeroRemainderTail(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C11_753E, 0x2008);
        WriteInstruction(memory, 0x8C11_7540, 0x8B2D);
        WriteInstruction(memory, 0x8C11_759E, 0x63C3);
        WriteInstruction(memory, 0x8C11_75A0, 0x7314);
        WriteInstruction(memory, 0x8C11_75A2, 0x1E39);
        WriteInstruction(memory, 0x8C11_75A4, 0x7D01);
        WriteInstruction(memory, 0x8C11_75A6, 0x3DA3);
        WriteInstruction(memory, 0x8C11_75A8, 0x8BAA);
    }

    private static void InitializeDoa2ListEntryNonzeroTailState(Sh4Cpu cpu, uint index)
    {
        cpu.State.R[0] = 1;
        cpu.State.R[10] = 24;
        cpu.State.R[12] = 0x8C30_F444;
        cpu.State.R[13] = index;
        cpu.State.R[14] = 0x8C2F_B848;
        cpu.State.T = true;
    }

    private static void WriteDoa2ZeroStatusTableScan(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C0F_7D2A, 0x6011);
        WriteInstruction(memory, 0x8C0F_7D2C, 0x8800);
        WriteInstruction(memory, 0x8C0F_7D2E, 0x8908);
        WriteInstruction(memory, 0x8C0F_7D42, 0x7108);
        WriteInstruction(memory, 0x8C0F_7D44, 0x6313);
        WriteInstruction(memory, 0x8C0F_7D46, 0xD06A);
        WriteInstruction(memory, 0x8C0F_7D48, 0xD268);
        WriteInstruction(memory, 0x8C0F_7D4A, 0x3328);
        WriteInstruction(memory, 0x8C0F_7D4C, 0x6203);
        WriteInstruction(memory, 0x8C0F_7D4E, 0xD004);
        WriteInstruction(memory, 0x8C0F_7D50, 0x4301);
        WriteInstruction(memory, 0x8C0F_7D52, 0x3120);
        WriteInstruction(memory, 0x8C0F_7D54, 0x4309);
        WriteInstruction(memory, 0x8C0F_7D56, 0x8FE8);
        WriteInstruction(memory, 0x8C0F_7D58, 0x2031);
        memory.WriteUInt32(0x8C0F_7D60, 0x8C0F_7EE4);
        memory.WriteUInt32(0x8C0F_7EEC, 0x8C2A_DC04);
        memory.WriteUInt32(0x8C0F_7EF0, 0x8C2A_DC94);
    }

    private static void InitializeDoa2ZeroStatusTableScanState(Sh4Cpu cpu)
    {
        cpu.State.R[1] = 0x8C2A_DC04;
        cpu.State.R[2] = 0x1234_5678;
        cpu.State.R[3] = 0x8765_4321;
        cpu.State.T = false;
    }

    private static void WriteDoa2ZeroByteClassifier(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C11_7482, 0x8464);
        WriteInstruction(memory, 0x8C11_7484, 0x911E);
        WriteInstruction(memory, 0x8C11_7486, 0x600C);
        WriteInstruction(memory, 0x8C11_7488, 0x3010);
        WriteInstruction(memory, 0x8C11_748A, 0x890C);
        WriteInstruction(memory, 0x8C11_748C, 0x911B);
        WriteInstruction(memory, 0x8C11_748E, 0x3010);
        WriteInstruction(memory, 0x8C11_7490, 0x890C);
        WriteInstruction(memory, 0x8C11_7492, 0x8808);
        WriteInstruction(memory, 0x8C11_7494, 0x890A);
        WriteInstruction(memory, 0x8C11_7496, 0x8805);
        WriteInstruction(memory, 0x8C11_7498, 0x8908);
        WriteInstruction(memory, 0x8C11_749A, 0x8806);
        WriteInstruction(memory, 0x8C11_749C, 0x8906);
        WriteInstruction(memory, 0x8C11_749E, 0x8800);
        WriteInstruction(memory, 0x8C11_74A0, 0x8904);
        WriteInstruction(memory, 0x8C11_74AC, 0x000B);
        WriteInstruction(memory, 0x8C11_74AE, 0xE000);
        memory.WriteUInt16(0x8C11_74C4, 0x00FC);
        memory.WriteUInt16(0x8C11_74C6, 0x00FF);
    }

    private static void WriteZeroByteClassifierData(DreamcastMemory memory, byte value)
    {
        memory.Write(0x8C20_1004, [value]);
    }

    private static void InitializeDoa2ZeroByteClassifierState(Sh4Cpu cpu)
    {
        cpu.State.R[0] = 0xFFFF_FFFF;
        cpu.State.R[1] = 0x1234_5678;
        cpu.State.R[6] = 0x8C20_1000;
        cpu.State.Pr = 0x8C11_7532;
        cpu.State.T = false;
    }

    private static void WriteDoa2ColorBytePackCommonPath(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_0AC0, 0xC72B);
        WriteInstruction(memory, 0x8C10_0AC2, 0xF808);
        WriteInstruction(memory, 0x8C10_0AC4, 0xC72B);
        WriteInstruction(memory, 0x8C10_0AC6, 0xF908);
        WriteInstruction(memory, 0x8C10_0AC8, 0xF482);
        WriteInstruction(memory, 0x8C10_0ACA, 0xF582);
        WriteInstruction(memory, 0x8C10_0ACC, 0xF682);
        WriteInstruction(memory, 0x8C10_0ACE, 0xF782);
        WriteInstruction(memory, 0x8C10_0AD0, 0xF495);
        WriteInstruction(memory, 0x8C10_0AD2, 0x8F0D);
        WriteInstruction(memory, 0x8C10_0AD4, 0xF84C);
        WriteInstruction(memory, 0x8C10_0AF0, 0xF38C);
        WriteInstruction(memory, 0x8C10_0AF2, 0xF33D);
        WriteInstruction(memory, 0x8C10_0AF4, 0xF595);
        WriteInstruction(memory, 0x8C10_0AF6, 0x045A);
        WriteInstruction(memory, 0x8C10_0AF8, 0x8F0A);
        WriteInstruction(memory, 0x8C10_0AFA, 0xF45C);
        WriteInstruction(memory, 0x8C10_0B10, 0xF34C);
        WriteInstruction(memory, 0x8C10_0B12, 0xF33D);
        WriteInstruction(memory, 0x8C10_0B14, 0xF695);
        WriteInstruction(memory, 0x8C10_0B16, 0x055A);
        WriteInstruction(memory, 0x8C10_0B18, 0x8F0A);
        WriteInstruction(memory, 0x8C10_0B1A, 0xF46C);
        WriteInstruction(memory, 0x8C10_0B30, 0xF34C);
        WriteInstruction(memory, 0x8C10_0B32, 0xF33D);
        WriteInstruction(memory, 0x8C10_0B34, 0xF795);
        WriteInstruction(memory, 0x8C10_0B36, 0x065A);
        WriteInstruction(memory, 0x8C10_0B38, 0x8F22);
        WriteInstruction(memory, 0x8C10_0B3A, 0xF47C);
        WriteInstruction(memory, 0x8C10_0B80, 0xF34C);
        WriteInstruction(memory, 0x8C10_0B82, 0xF33D);
        WriteInstruction(memory, 0x8C10_0B84, 0x907B);
        WriteInstruction(memory, 0x8C10_0B86, 0x3406);
        WriteInstruction(memory, 0x8C10_0B88, 0x075A);
        WriteInstruction(memory, 0x8C10_0B8A, 0x8F01);
        WriteInstruction(memory, 0x8C10_0B8C, 0xE100);
        WriteInstruction(memory, 0x8C10_0B90, 0x3506);
        WriteInstruction(memory, 0x8C10_0B92, 0x8B00);
        WriteInstruction(memory, 0x8C10_0B96, 0x3606);
        WriteInstruction(memory, 0x8C10_0B98, 0x8B00);
        WriteInstruction(memory, 0x8C10_0B9C, 0x3706);
        WriteInstruction(memory, 0x8C10_0B9E, 0x8F01);
        WriteInstruction(memory, 0x8C10_0BA0, 0x4418);
        WriteInstruction(memory, 0x8C10_0BA4, 0x245B);
        WriteInstruction(memory, 0x8C10_0BA6, 0x4418);
        WriteInstruction(memory, 0x8C10_0BA8, 0x246B);
        WriteInstruction(memory, 0x8C10_0BAA, 0x4418);
        WriteInstruction(memory, 0x8C10_0BAC, 0x247B);
        WriteInstruction(memory, 0x8C10_0BAE, 0x000B);
        WriteInstruction(memory, 0x8C10_0BB0, 0x6043);
    }

    private static void WriteColorBytePackConstants(DreamcastMemory memory)
    {
        memory.WriteUInt32(0x8C10_0B70, BitConverter.SingleToUInt32Bits(255.0f));
        memory.WriteUInt32(0x8C10_0B74, BitConverter.SingleToUInt32Bits(2147483648.0f));
        WriteInstruction(memory, 0x8C10_0C7E, 0x00FF);
    }

    private static void InitializeDoa2ColorBytePackState(Sh4Cpu cpu)
    {
        cpu.State.Pr = 0x8C10_0670;
        cpu.State.R[1] = 0x1111_1111;
        cpu.State.R[3] = 0x3333_3333;
        cpu.State.R[4] = 0x4444_4444;
        cpu.State.R[5] = 0x5555_5555;
        cpu.State.R[6] = 0x6666_6666;
        cpu.State.R[7] = 0x7777_7777;
        cpu.State.Fr[4] = BitConverter.SingleToUInt32Bits(0.1f);
        cpu.State.Fr[5] = BitConverter.SingleToUInt32Bits(0.25f);
        cpu.State.Fr[6] = BitConverter.SingleToUInt32Bits(0.5f);
        cpu.State.Fr[7] = BitConverter.SingleToUInt32Bits(0.75f);
        cpu.State.T = true;
    }

    private static void WriteDoa2FpuRecurrenceLoop(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C0F_B1FE, 0x445A);
        WriteInstruction(memory, 0x8C0F_B200, 0x74FE);
        WriteInstruction(memory, 0x8C0F_B202, 0x3453);
        WriteInstruction(memory, 0x8C0F_B204, 0xF22D);
        WriteInstruction(memory, 0x8C0F_B206, 0xF251);
        WriteInstruction(memory, 0x8C0F_B208, 0xF56C);
        WriteInstruction(memory, 0x8C0F_B20A, 0x8DF8);
        WriteInstruction(memory, 0x8C0F_B20C, 0xF523);
    }

    private static void InitializeDoa2FpuRecurrenceState(Sh4Cpu cpu)
    {
        cpu.State.R[4] = 9;
        cpu.State.R[5] = 3;
        cpu.State.Fr[2] = BitConverter.SingleToUInt32Bits(6.0f);
        cpu.State.Fr[5] = BitConverter.SingleToUInt32Bits(3.0f);
        cpu.State.Fr[6] = BitConverter.SingleToUInt32Bits(9.0f);
        cpu.State.T = true;
    }

    private static void WriteDoa2TrigSetupAndRecurrenceLoop(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C0F_B1C0, 0x644D);
        WriteInstruction(memory, 0x8C0F_B1C2, 0xF79D);
        WriteInstruction(memory, 0x8C0F_B1C4, 0x445A);
        WriteInstruction(memory, 0x8C0F_B1C6, 0xC71D);
        WriteInstruction(memory, 0x8C0F_B1C8, 0xF208);
        WriteInstruction(memory, 0x8C0F_B1CA, 0xC71D);
        WriteInstruction(memory, 0x8C0F_B1CC, 0xF108);
        WriteInstruction(memory, 0x8C0F_B1CE, 0xF770);
        WriteInstruction(memory, 0x8C0F_B1D0, 0xF32D);
        WriteInstruction(memory, 0x8C0F_B1D2, 0xC71C);
        WriteInstruction(memory, 0x8C0F_B1D4, 0xF508);
        WriteInstruction(memory, 0x8C0F_B1D6, 0xC71C);
        WriteInstruction(memory, 0x8C0F_B1D8, 0xF008);
        WriteInstruction(memory, 0x8C0F_B1DA, 0xE40B);
        WriteInstruction(memory, 0x8C0F_B1DC, 0xE503);
        WriteInstruction(memory, 0x8C0F_B1DE, 0xF322);
        WriteInstruction(memory, 0x8C0F_B1E0, 0xF313);
        WriteInstruction(memory, 0x8C0F_B1E2, 0xF43C);
        WriteInstruction(memory, 0x8C0F_B1E4, 0xF473);
        WriteInstruction(memory, 0x8C0F_B1E6, 0xF34C);
        WriteInstruction(memory, 0x8C0F_B1E8, 0xF353);
        WriteInstruction(memory, 0x8C0F_B1EA, 0xF300);
        WriteInstruction(memory, 0x8C0F_B1EC, 0xF33D);
        WriteInstruction(memory, 0x8C0F_B1EE, 0x065A);
        WriteInstruction(memory, 0x8C0F_B1F0, 0x465A);
        WriteInstruction(memory, 0x8C0F_B1F2, 0xF32D);
        WriteInstruction(memory, 0x8C0F_B1F4, 0xF352);
        WriteInstruction(memory, 0x8C0F_B1F6, 0xF58D);
        WriteInstruction(memory, 0x8C0F_B1F8, 0xF431);
        WriteInstruction(memory, 0x8C0F_B1FA, 0xF64C);
        WriteInstruction(memory, 0x8C0F_B1FC, 0xF642);
        WriteDoa2FpuRecurrenceLoop(memory);
        WriteInstruction(memory, 0x8C0F_B20E, 0xF69D);
        WriteInstruction(memory, 0x8C0F_B210, 0xE301);
        WriteInstruction(memory, 0x8C0F_B212, 0xF36C);
        WriteInstruction(memory, 0x8C0F_B214, 0xF351);
    }

    private static void WriteDoa2TrigSetupConstants(DreamcastMemory memory)
    {
        memory.WriteUInt32(0x8C0F_B23C, BitConverter.SingleToUInt32Bits(MathF.PI));
        memory.WriteUInt32(0x8C0F_B240, BitConverter.SingleToUInt32Bits(65536.0f));
        memory.WriteUInt32(0x8C0F_B244, BitConverter.SingleToUInt32Bits(MathF.PI / 2.0f));
        memory.WriteUInt32(0x8C0F_B248, BitConverter.SingleToUInt32Bits(0.5f));
    }

    private static void InitializeDoa2TrigSetupState(Sh4Cpu cpu)
    {
        cpu.State.R[4] = 0xFFFF_4000;
        cpu.State.Fr[4] = BitConverter.SingleToUInt32Bits(0.25f);
        cpu.State.Fr[5] = BitConverter.SingleToUInt32Bits(0.5f);
        cpu.State.Fr[6] = BitConverter.SingleToUInt32Bits(0.75f);
        cpu.State.Fr[7] = BitConverter.SingleToUInt32Bits(1.0f);
        cpu.State.Fr[8] = BitConverter.SingleToUInt32Bits(1.25f);
        cpu.State.Fr[9] = BitConverter.SingleToUInt32Bits(1.5f);
        cpu.State.Fr[10] = BitConverter.SingleToUInt32Bits(1.75f);
        cpu.State.Fr[11] = BitConverter.SingleToUInt32Bits(2.0f);
    }

    private static void WriteDoa2PostTrigHelperReturn(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C0F_B216, 0x2638);
        WriteInstruction(memory, 0x8C0F_B218, 0xF433);
        WriteInstruction(memory, 0x8C0F_B21A, 0xF24C);
        WriteInstruction(memory, 0x8C0F_B21C, 0xF272);
        WriteInstruction(memory, 0x8C0F_B21E, 0xF04C);
        WriteInstruction(memory, 0x8C0F_B220, 0xF64E);
        WriteInstruction(memory, 0x8C0F_B222, 0xF42C);
        WriteInstruction(memory, 0x8C0F_B224, 0x8F14);
        WriteInstruction(memory, 0x8C0F_B226, 0xF463);
        WriteInstruction(memory, 0x8C0F_B228, 0x000B);
        WriteInstruction(memory, 0x8C0F_B22A, 0xF04C);
        WriteInstruction(memory, 0x8C0F_B250, 0xF04C);
        WriteInstruction(memory, 0x8C0F_B252, 0xF04D);
        WriteInstruction(memory, 0x8C0F_B254, 0x000B);
        WriteInstruction(memory, 0x8C0F_B256, 0x0009);
    }

    private static void InitializeDoa2PostTrigHelperReturnState(Sh4Cpu cpu)
    {
        cpu.State.Pr = 0x8C10_0536;
        cpu.State.R[3] = 0;
        cpu.State.R[6] = 0;
        cpu.State.Fr[0] = BitConverter.SingleToUInt32Bits(0.25f);
        cpu.State.Fr[2] = BitConverter.SingleToUInt32Bits(1.5f);
        cpu.State.Fr[3] = BitConverter.SingleToUInt32Bits(2.0f);
        cpu.State.Fr[4] = BitConverter.SingleToUInt32Bits(8.0f);
        cpu.State.Fr[6] = BitConverter.SingleToUInt32Bits(5.0f);
        cpu.State.Fr[7] = BitConverter.SingleToUInt32Bits(3.0f);
    }

    private static void WriteDoa2VectorScaleLoop(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_05A0, 0x6043);
        WriteInstruction(memory, 0x8C10_05A2, 0xF3B6);
        WriteInstruction(memory, 0x8C10_05A4, 0x7404);
        WriteInstruction(memory, 0x8C10_05A6, 0xF342);
        WriteInstruction(memory, 0x8C10_05A8, 0xFB37);
        WriteInstruction(memory, 0x8C10_05AA, 0xF2A6);
        WriteInstruction(memory, 0x8C10_05AC, 0xF252);
        WriteInstruction(memory, 0x8C10_05AE, 0xFA27);
        WriteInstruction(memory, 0x8C10_05B0, 0x3492);
        WriteInstruction(memory, 0x8C10_05B2, 0x8BF5);
        WriteInstruction(memory, 0x8C10_05B4, 0x0009);
    }

    private static void InitializeDoa2VectorScaleState(Sh4Cpu cpu)
    {
        cpu.State.R[4] = 4;
        cpu.State.R[9] = 16;
        cpu.State.R[10] = 0x8C20_1000;
        cpu.State.R[11] = 0x8C20_2000;
        cpu.State.Fr[4] = BitConverter.SingleToUInt32Bits(2.0f);
        cpu.State.Fr[5] = BitConverter.SingleToUInt32Bits(-3.0f);
        cpu.State.T = false;
    }

    private static void WriteVectorScaleData(DreamcastMemory memory)
    {
        for (var offset = 0u; offset < 16; offset += 4)
        {
            memory.WriteUInt32(0x8C20_1000 + offset, BitConverter.SingleToUInt32Bits(1.0f + offset));
            memory.WriteUInt32(0x8C20_2000 + offset, BitConverter.SingleToUInt32Bits(2.0f + offset));
        }
    }

    private static void WriteDoa2InterpolationLoop(DreamcastMemory memory)
    {
        WriteInstruction(memory, 0x8C10_0A7A, 0xFB79);
        WriteInstruction(memory, 0x8C10_0A7C, 0xE004);
        WriteInstruction(memory, 0x8C10_0A7E, 0xF38D);
        WriteInstruction(memory, 0x8C10_0A80, 0xFB61);
        WriteInstruction(memory, 0x8C10_0A82, 0xFA79);
        WriteInstruction(memory, 0x8C10_0A84, 0xF146);
        WriteInstruction(memory, 0x8C10_0A86, 0xE008);
        WriteInstruction(memory, 0x8C10_0A88, 0xFA71);
        WriteInstruction(memory, 0x8C10_0A8A, 0xFB34);
        WriteInstruction(memory, 0x8C10_0A8C, 0x8D06);
        WriteInstruction(memory, 0x8C10_0A8E, 0xFE46);
        WriteInstruction(memory, 0x8C10_0A90, 0xF24C);
        WriteInstruction(memory, 0x8C10_0A92, 0xF2B2);
        WriteInstruction(memory, 0x8C10_0A94, 0xF0BC);
        WriteInstruction(memory, 0x8C10_0A96, 0xF18E);
        WriteInstruction(memory, 0x8C10_0A98, 0xF24D);
        WriteInstruction(memory, 0x8C10_0A9A, 0xFE20);
        WriteInstruction(memory, 0x8C10_0A9C, 0xF38D);
        WriteInstruction(memory, 0x8C10_0A9E, 0xFA34);
        WriteInstruction(memory, 0x8C10_0AA0, 0x8902);
        WriteInstruction(memory, 0x8C10_0AA2, 0xF0AC);
        WriteInstruction(memory, 0x8C10_0AA4, 0xFE5E);
        WriteInstruction(memory, 0x8C10_0AA6, 0xF19E);
        WriteInstruction(memory, 0x8C10_0AA8, 0x71FF);
        WriteInstruction(memory, 0x8C10_0AAA, 0xF51A);
        WriteInstruction(memory, 0x8C10_0AAC, 0xF6EA);
        WriteInstruction(memory, 0x8C10_0AAE, 0x2118);
        WriteInstruction(memory, 0x8C10_0AB0, 0x7604);
        WriteInstruction(memory, 0x8C10_0AB2, 0x8FE2);
        WriteInstruction(memory, 0x8C10_0AB4, 0x7504);
        WriteInstruction(memory, 0x8C10_0AB6, 0x0009);
    }

    private static void InitializeDoa2InterpolationState(Sh4Cpu cpu)
    {
        cpu.State.R[1] = 4;
        cpu.State.R[4] = 0x8C20_5000;
        cpu.State.R[5] = 0x8C20_2FFC;
        cpu.State.R[6] = 0x8C20_4000;
        cpu.State.R[7] = 0x8C20_6000;
        cpu.State.Fr[1] = 0xFFC0_0000;
        cpu.State.Fr[2] = 0xFFC0_0000;
        cpu.State.Fr[3] = 0xFFC0_0000;
        cpu.State.Fr[4] = 0xFFC0_0000;
        cpu.State.Fr[5] = 0xFFC0_0000;
        cpu.State.Fr[8] = 0xFFC0_0000;
        cpu.State.Fr[9] = 0xFFC0_0000;
        cpu.State.T = false;
    }

    private static void WriteInterpolationData(DreamcastMemory memory)
    {
        memory.WriteUInt32(0x8C20_5004, BitConverter.SingleToUInt32Bits(480.0f));
        memory.WriteUInt32(0x8C20_5008, BitConverter.SingleToUInt32Bits(360.0f));
        var sourceValues = new[] { 1.0f, 1.0f, 3.0f, 1.0f, 3.0f, 3.0f, 1.0f, 3.0f };
        for (var index = 0; index < sourceValues.Length; index++)
        {
            memory.WriteUInt32(0x8C20_6000 + ((uint)index * 4), BitConverter.SingleToUInt32Bits(sourceValues[index]));
        }
    }

    private static Sh4StepResult StepUntilPc(Sh4Cpu cpu, uint pc)
    {
        while (true)
        {
            var step = cpu.Step();
            if (step.Pc == pc)
            {
                return step;
            }
        }
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
