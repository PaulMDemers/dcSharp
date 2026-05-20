using DcSharp.Core.Dreamcast.Input;
using DcSharp.Core.Execution;
using DcSharp.Core.Media;
using System.Text;

namespace DcSharp.Tests;

public class DreamcastRunnerTests
{
    [Fact]
    public void RunsUntilInstructionLimit()
    {
        var elf = ElfFile.Read(new MemoryStream(CreateNopElf()));

        var result = new DreamcastRunner().Run(elf, new DreamcastRunOptions(InstructionLimit: 3, TraceTailLength: 2));

        Assert.Equal(DreamcastStopReason.InstructionLimit, result.StopReason);
        Assert.Equal(3u, result.Cpu.InstructionsExecuted);
        Assert.Equal(0x8C01_0006u, result.Cpu.Pc);
        Assert.Equal(2, result.TraceTail.Count);
    }

    [Fact]
    public void ReportsProgramExitWhenKosExitBannerFallsOutOfExecutableCode()
    {
        var elf = ElfFile.Read(new MemoryStream(CreateKosExitFallthroughElf()));

        var result = new DreamcastRunner().Run(elf, new DreamcastRunOptions(InstructionLimit: 1_000, TraceTailLength: 8));

        Assert.Equal(DreamcastStopReason.ProgramExit, result.StopReason);
        Assert.Equal(0x8CFF_FFF2u, result.StopPc);
        Assert.Contains("Program returned after KOS shutdown", result.StopDetail);
        Assert.Contains("arch: exit return code", Encoding.ASCII.GetString(result.SerialOutput.ToArray()));
    }

    [Fact]
    public void BuildsStructuredRunSummary()
    {
        var elf = ElfFile.Read(new MemoryStream(CreateKosExitFallthroughElf()));
        var result = new DreamcastRunner().Run(elf, new DreamcastRunOptions(InstructionLimit: 1_000, TraceTailLength: 2));

        var summary = DreamcastRunSummary.FromResult(result, recentDeviceAccessCount: 1);

        Assert.Equal(DreamcastStopReason.ProgramExit, summary.StopReason);
        Assert.Equal(result.Cpu.InstructionsExecuted, summary.InstructionsExecuted);
        Assert.Equal("0x8CFFFFF2", summary.StopPcHex);
        Assert.Equal("0x8C010000", summary.Load.EntryPointHex);
        Assert.Contains("arch: exit return code", summary.SerialText);
        Assert.Single(summary.RecentDeviceAccesses);
        Assert.Equal(2, summary.TraceTail.Count);
    }

    [Fact]
    public void SummaryIncludesConfiguredControllerState()
    {
        var elf = ElfFile.Read(new MemoryStream(CreateNopElf()));
        var options = new DreamcastRunOptions(
            InstructionLimit: 1,
            TraceTailLength: 0,
            ControllerA: new DreamcastControllerState(Buttons: DreamcastControllerButtons.Start | DreamcastControllerButtons.A, LeftTrigger: 7));

        var result = new DreamcastRunner().Run(elf, options);

        var summary = DreamcastRunSummary.FromResult(result, options);

        Assert.Equal(DreamcastControllerButtons.Start | DreamcastControllerButtons.A, summary.ControllerA.Buttons);
        Assert.Equal(7, summary.ControllerA.LeftTrigger);
    }

    [Fact]
    public void SummaryUsesControllerScriptStateAtStopInstruction()
    {
        var elf = ElfFile.Read(new MemoryStream(CreateNopElf()));
        var options = new DreamcastRunOptions(
            InstructionLimit: 3,
            TraceTailLength: 0,
            ControllerAScript: new DreamcastControllerScript(
                new DreamcastControllerScriptFrame(0, DreamcastControllerState.Neutral),
                new DreamcastControllerScriptFrame(2, new DreamcastControllerState(Buttons: DreamcastControllerButtons.Start))));

        var result = new DreamcastRunner().Run(elf, options);

        var summary = DreamcastRunSummary.FromResult(result, options);

        Assert.Equal(DreamcastControllerButtons.Start, summary.ControllerA.Buttons);
    }

    private static byte[] CreateNopElf()
    {
        return CreateElfWithSegment(
        [
            0x09, 0x00,
            0x09, 0x00,
            0x09, 0x00
        ]);
    }

    private static byte[] CreateKosExitFallthroughElf()
    {
        const uint baseAddress = 0x8C01_0000;

        var bytes = new byte[0x2C + 32];
        WriteUInt16(bytes, 0x00, 0xD107); // mov.l @(0x07,pc),r1
        WriteUInt16(bytes, 0x02, 0xD208); // mov.l @(0x08,pc),r2
        WriteUInt16(bytes, 0x04, 0xD308); // mov.l @(0x08,pc),r3
        WriteUInt16(bytes, 0x06, 0x6024); // mov.b @r2+,r0
        WriteUInt16(bytes, 0x08, 0x8800); // cmp/eq #0,r0
        WriteUInt16(bytes, 0x0A, 0x8903); // bt done
        WriteUInt16(bytes, 0x0C, 0x2100); // mov.b r0,@r1
        WriteUInt16(bytes, 0x0E, 0xAFFA); // bra loop
        WriteUInt16(bytes, 0x10, 0x0009); // nop
        WriteUInt16(bytes, 0x14, 0x432B); // jmp @r3
        WriteUInt16(bytes, 0x16, 0x0009); // nop
        WriteUInt32(bytes, 0x20, 0xFFE8_000C);
        WriteUInt32(bytes, 0x24, baseAddress + 0x2C);
        WriteUInt32(bytes, 0x28, 0x8CFF_FFF2);
        Encoding.ASCII.GetBytes("\narch: exit return code 0\n\0").CopyTo(bytes, 0x2C);

        return CreateElfWithSegment(bytes);
    }

    private static byte[] CreateElfWithSegment(byte[] segmentBytes)
    {
        var bytes = new byte[84 + segmentBytes.Length];
        bytes[0] = 0x7F;
        bytes[1] = (byte)'E';
        bytes[2] = (byte)'L';
        bytes[3] = (byte)'F';
        bytes[4] = 1;
        bytes[5] = 1;
        bytes[6] = 1;

        WriteUInt16(bytes, 16, 2);
        WriteUInt16(bytes, 18, 42);
        WriteUInt32(bytes, 20, 1);
        WriteUInt32(bytes, 24, 0x8C01_0000);
        WriteUInt32(bytes, 28, 52);
        WriteUInt16(bytes, 40, 52);
        WriteUInt16(bytes, 42, 32);
        WriteUInt16(bytes, 44, 1);
        WriteUInt16(bytes, 46, 40);
        WriteUInt16(bytes, 48, 3);

        WriteUInt32(bytes, 52, 1);
        WriteUInt32(bytes, 56, 84);
        WriteUInt32(bytes, 60, 0x8C01_0000);
        WriteUInt32(bytes, 64, 0x0C01_0000);
        WriteUInt32(bytes, 68, (uint)segmentBytes.Length);
        WriteUInt32(bytes, 72, (uint)segmentBytes.Length);
        WriteUInt32(bytes, 76, 5);
        WriteUInt32(bytes, 80, 32);

        segmentBytes.CopyTo(bytes, 84);

        return bytes;
    }

    private static void WriteUInt16(byte[] bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }
}
