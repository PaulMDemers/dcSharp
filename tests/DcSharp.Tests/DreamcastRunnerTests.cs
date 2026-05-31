using DcSharp.Core.Cpu;
using DcSharp.Core.Dreamcast.Asic;
using DcSharp.Core.Dreamcast.Audio;
using DcSharp.Core.Dreamcast.Input;
using DcSharp.Core.Dreamcast.Memory;
using DcSharp.Core.Dreamcast.Video;
using DcSharp.Core.Execution;
using DcSharp.Core.Loading;
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
    public void RunsRawBootBinaryAtDreamcastBootAddress()
    {
        var raw = new byte[16];
        for (var offset = 0; offset < raw.Length; offset += 2)
        {
            raw[offset] = 0x09;
            raw[offset + 1] = 0x00;
        }

        var result = new DreamcastRunner().RunRawBinary(raw, new DreamcastRunOptions(InstructionLimit: 3, TraceTailLength: 2));

        Assert.Equal(DreamcastStopReason.InstructionLimit, result.StopReason);
        Assert.Equal(0x8C01_0000u, result.Load.EntryPoint);
        Assert.Equal(0x0C01_0000u, result.Load.TranslatedEntryPoint);
        Assert.Equal(16u, result.Load.LoadedBytes);
        Assert.Equal(0x8C01_0006u, result.Cpu.Pc);
        Assert.Equal(2, result.TraceTail.Count);
    }

    [Fact]
    public void CapturesFpuAnomalyLogWhenRegisterBecomesNonFinite()
    {
        var raw = new byte[4];
        raw[0] = 0x53;
        raw[1] = 0xF4;

        var result = new DreamcastRunner().RunRawBinary(
            raw,
            new DreamcastRunOptions(
                InstructionLimit: 1,
                TraceTailLength: 0,
                FpuAnomalyCapture: new DreamcastFpuAnomalyCaptureOptions(Limit: 4, Register: "fr4")));

        var anomaly = Assert.Single(result.FpuAnomalies);
        Assert.Equal(1UL, anomaly.Instruction);
        Assert.Equal(0x8C01_0000u, anomaly.Pc);
        Assert.Equal(0xF453, anomaly.Opcode);
        Assert.Equal("fr4", anomaly.Register);
        Assert.Equal(0u, anomaly.OldValue);
        Assert.Equal("nan", anomaly.Kind);
        Assert.Contains("fdiv fr5,fr4", anomaly.Trace, StringComparison.Ordinal);
    }

    [Fact]
    public void FiltersFpuAnomalyLogByRegister()
    {
        var raw = new byte[4];
        raw[0] = 0x53;
        raw[1] = 0xF4;

        var result = new DreamcastRunner().RunRawBinary(
            raw,
            new DreamcastRunOptions(
                InstructionLimit: 1,
                TraceTailLength: 0,
                FpuAnomalyCapture: new DreamcastFpuAnomalyCaptureOptions(Limit: 4, Register: "fr5")));

        Assert.Empty(result.FpuAnomalies);
    }

    [Fact]
    public void CapturesFpuAnomalyLogAcrossInstructionRange()
    {
        var raw = new byte[6];
        raw[0] = 0x09;
        raw[1] = 0x00;
        raw[2] = 0x53;
        raw[3] = 0xF4;

        var result = new DreamcastRunner().RunRawBinary(
            raw,
            new DreamcastRunOptions(
                InstructionLimit: 2,
                TraceTailLength: 0,
                FpuAnomalyCapture: new DreamcastFpuAnomalyCaptureOptions(
                    Limit: 4,
                    StartInstruction: 2,
                    EndInstruction: 2)));

        var anomaly = Assert.Single(result.FpuAnomalies);
        Assert.Equal(2UL, anomaly.Instruction);
        Assert.Equal(0x8C01_0002u, anomaly.Pc);
    }

    [Fact]
    public void CapturesFpuRegisterWriteLogForSelectedRegister()
    {
        var raw = new byte[4];
        raw[0] = 0x53;
        raw[1] = 0xF4;

        var result = new DreamcastRunner().RunRawBinary(
            raw,
            new DreamcastRunOptions(
                InstructionLimit: 1,
                TraceTailLength: 0,
                FpuRegisterWatch: new DreamcastFpuRegisterWatchOptions(Limit: 4, Register: "fr4")));

        var write = Assert.Single(result.FpuRegisterWrites);
        Assert.Equal(1UL, write.Instruction);
        Assert.Equal(0x8C01_0000u, write.Pc);
        Assert.Equal("fr4", write.Register);
        Assert.Equal(0u, write.OldValue);
        Assert.NotEqual(0u, write.NewValue);
        Assert.Contains("fdiv fr5,fr4", write.Trace, StringComparison.Ordinal);
    }

    [Fact]
    public void CapturesFpuRegisterWriteLogAcrossInstructionRange()
    {
        var raw = new byte[6];
        raw[0] = 0x09;
        raw[1] = 0x00;
        raw[2] = 0x53;
        raw[3] = 0xF4;

        var result = new DreamcastRunner().RunRawBinary(
            raw,
            new DreamcastRunOptions(
                InstructionLimit: 2,
                TraceTailLength: 0,
                FpuRegisterWatch: new DreamcastFpuRegisterWatchOptions(
                    Limit: 4,
                    Register: "fr4",
                    StartInstruction: 2,
                    EndInstruction: 2)));

        var write = Assert.Single(result.FpuRegisterWrites);
        Assert.Equal(2UL, write.Instruction);
        Assert.Equal(0x8C01_0002u, write.Pc);
    }

    [Fact]
    public void CapturesFpscrChangeLogWhenFpuStatusChanges()
    {
        var raw = new byte[4];
        raw[0] = 0x53;
        raw[1] = 0xF4;

        var result = new DreamcastRunner().RunRawBinary(
            raw,
            new DreamcastRunOptions(
                InstructionLimit: 1,
                TraceTailLength: 0,
                FpscrWatch: new DreamcastFpscrWatchOptions(Limit: 4)));

        var fpscrEvent = Assert.Single(result.FpscrEvents);
        Assert.Equal(1UL, fpscrEvent.Instruction);
        Assert.Equal(0x8C01_0000u, fpscrEvent.Pc);
        Assert.Equal(0xF453, fpscrEvent.Opcode);
        Assert.Equal(0x0004_0001u, fpscrEvent.OldValue);
        Assert.Equal(Sh4State.FpscrCauseInvalidBit | Sh4State.FpscrFlagInvalidBit | 0x0004_0001u, fpscrEvent.NewValue);
        Assert.Equal("change", fpscrEvent.Kind);
        Assert.Contains("fdiv fr5,fr4", fpscrEvent.Trace, StringComparison.Ordinal);
    }

    [Fact]
    public void CapturesFpscrAccessLogWhenInstructionReadsStatusRegister()
    {
        var raw = new byte[4];
        raw[0] = 0x6A;
        raw[1] = 0x01;

        var result = new DreamcastRunner().RunRawBinary(
            raw,
            new DreamcastRunOptions(
                InstructionLimit: 1,
                TraceTailLength: 0,
                FpscrWatch: new DreamcastFpscrWatchOptions(Limit: 4)));

        var fpscrEvent = Assert.Single(result.FpscrEvents);
        Assert.Equal(1UL, fpscrEvent.Instruction);
        Assert.Equal(0x8C01_0000u, fpscrEvent.Pc);
        Assert.Equal(0x016A, fpscrEvent.Opcode);
        Assert.Equal(0x0004_0001u, fpscrEvent.OldValue);
        Assert.Equal(0x0004_0001u, fpscrEvent.NewValue);
        Assert.Equal("access", fpscrEvent.Kind);
        Assert.Contains("sts fpscr,r1", fpscrEvent.Trace, StringComparison.Ordinal);
    }

    [Fact]
    public void CapturesFpuSnapshotBeforeMatchingInstructionExecutes()
    {
        var raw = new byte[4];
        raw[0] = 0x53;
        raw[1] = 0xF4;

        var result = new DreamcastRunner().RunRawBinary(
            raw,
            new DreamcastRunOptions(
                InstructionLimit: 1,
                TraceTailLength: 0,
                FpuSnapshotCapture: new DreamcastFpuSnapshotCaptureOptions(
                    Limit: 4,
                    Ranges: [new DreamcastTracePcRange(0x8C01_0000, 0x8C01_0000)])));

        var snapshot = Assert.Single(result.FpuSnapshots);
        Assert.Equal(1UL, snapshot.Instruction);
        Assert.Equal(0x8C01_0000u, snapshot.Pc);
        Assert.Equal(0xF453, snapshot.Opcode);
        Assert.Equal(0u, snapshot.Fr[4]);
        Assert.Equal(0x0004_0001u, snapshot.Fpscr);
        Assert.Contains("fdiv fr5,fr4", snapshot.Trace, StringComparison.Ordinal);
    }

    [Fact]
    public void CapturesFpuMemoryTransferLogForSelectedRegister()
    {
        var raw = new byte[4];
        raw[0] = 0x5B;
        raw[1] = 0xFF;

        var result = new DreamcastRunner().RunRawBinary(
            raw,
            new DreamcastRunOptions(
                InstructionLimit: 1,
                TraceTailLength: 0,
                InitialStackPointer: 0x8C01_0100,
                FpuMemoryWatch: new DreamcastFpuMemoryWatchOptions(
                    Limit: 4,
                    Register: "fr5",
                    AddressRanges: [new DreamcastMemoryAddressRange(0x8C01_00F0, 0x8C01_0100)])));

        var transfer = Assert.Single(result.FpuMemoryTransfers);
        Assert.Equal(1UL, transfer.Instruction);
        Assert.Equal(0x8C01_0000u, transfer.Pc);
        Assert.Equal(0xFF5B, transfer.Opcode);
        Assert.Equal("store", transfer.Direction);
        Assert.Equal("fr5", transfer.Register);
        Assert.Equal(0x8C01_00FCu, transfer.Address);
        Assert.Equal(4, transfer.Size);
        Assert.Contains("fmov.s fr5,@-r15", transfer.Trace, StringComparison.Ordinal);
    }

    [Fact]
    public void RunCanStopOnUnmappedDeviceAccess()
    {
        var result = new DreamcastRunner().RunRawBinary(
            CreateUnmappedReadBootBinary(),
            new DreamcastRunOptions(InstructionLimit: 10, TraceTailLength: 4, StopOnUnmappedAccess: true));

        Assert.Equal(DreamcastStopReason.DeviceAccessStop, result.StopReason);
        Assert.Contains("Stopped on UnmappedRead", result.StopDetail);
        var access = Assert.Single(result.DeviceAccesses, access => access.Kind == MemoryAccessKind.UnmappedRead);
        Assert.Equal(0x0800_0010u, access.Address);
    }

    [Fact]
    public void RunCanStopOnDeviceDomain()
    {
        var result = new DreamcastRunner().RunRawBinary(
            CreateOtherDeviceReadBootBinary(),
            new DreamcastRunOptions(
                InstructionLimit: 10,
                TraceTailLength: 4,
                StopOnDeviceDomain: DreamcastDeviceDomainClassifier.Other));

        Assert.Equal(DreamcastStopReason.DeviceAccessStop, result.StopReason);
        Assert.Contains("Stopped on device domain 'other'", result.StopDetail);
        var access = Assert.Single(result.DeviceAccesses, access => access.Address == 0xFF00_001Cu);
        Assert.Equal(MemoryAccessKind.Read, access.Kind);
    }

    [Fact]
    public void RunReportsBootRegionWrites()
    {
        var result = new DreamcastRunner().RunRawBinary(
            CreateBootWorkWriteBinary(),
            new DreamcastRunOptions(InstructionLimit: 4, TraceTailLength: 0));

        var bootWork = Assert.Single(result.MemoryRegionWrites, region => region.Name == "Boot work");
        Assert.Equal(1UL, bootWork.WriteCount);
        Assert.Equal(4UL, bootWork.BytesWritten);
        Assert.Equal("0x8C00C000", bootWork.FirstAddressHex);
        Assert.Equal("0x8C00C003", bootWork.LastAddressHex);
    }

    [Fact]
    public void RunCapturesWatchedMemoryWritesAfterLoading()
    {
        var result = new DreamcastRunner().RunRawBinary(
            CreateBootWorkWriteBinary(),
            new DreamcastRunOptions(
                InstructionLimit: 4,
                TraceTailLength: 0,
                MemoryWriteWatch: new DreamcastMemoryWriteWatch(0x8C00_C000, 0x8C00_C003)));

        var access = Assert.Single(result.WatchedMemoryWrites);
        Assert.Equal(MemoryAccessKind.Write, access.Kind);
        Assert.Equal(0x8C00_C000u, access.Address);
        Assert.Equal(4, access.Size);
        Assert.Equal(0u, access.Value);
        Assert.Equal(0x8C01_0002u, access.Pc);
    }

    [Fact]
    public void RunCapturesWatchedMemoryReadsAfterLoading()
    {
        var result = new DreamcastRunner().RunRawBinary(
            CreateBootWorkReadBinary(),
            new DreamcastRunOptions(
                InstructionLimit: 4,
                TraceTailLength: 0,
                MemoryReadWatch: new DreamcastMemoryReadWatch(0x8C00_C000, 0x8C00_C003)));

        var access = Assert.Single(result.WatchedMemoryReads);
        Assert.Equal(MemoryAccessKind.Read, access.Kind);
        Assert.Equal(0x8C00_C000u, access.Address);
        Assert.Equal(4, access.Size);
        Assert.Equal(0u, access.Value);
        Assert.Equal(0x8C01_0002u, access.Pc);
    }

    [Fact]
    public void RunCanSeedInitialVBlankEvent()
    {
        var result = new DreamcastRunner().RunRawBinary(
            CreateAsicEventReadBinary(),
            new DreamcastRunOptions(InstructionLimit: 2, TraceTailLength: 2, SeedInitialVBlank: true));

        Assert.Contains(result.DeviceAccesses, access =>
            access.Kind == MemoryAccessKind.Read
            && access.Address == 0xA05F_6900
            && access.Value == 0x0000_0008);
    }

    [Fact]
    public void RunCanSeedInitialStatusRegister()
    {
        var result = new DreamcastRunner().RunRawBinary(
            CreateStatusRegisterReadBinary(),
            new DreamcastRunOptions(InstructionLimit: 1, TraceTailLength: 1, InitialStatusRegister: Sh4State.SrMachineBit | 0xF0));

        Assert.Equal(Sh4State.SrMachineBit | 0xF0, result.Cpu.R[0]);
        Assert.Equal("stc sr,r0 ; r0=0x400000F0", Assert.Single(result.TraceTail).Trace);
    }

    [Fact]
    public void RunCanStartAtRawBinaryAlternateEntryPoint()
    {
        var result = new DreamcastRunner().RunRawBinary(
            CreateDualEntryBinary(),
            new DreamcastRunOptions(InstructionLimit: 1, TraceTailLength: 1),
            entryPoint: 0x8C01_0004);

        Assert.Equal(0x8C01_0006u, result.Cpu.Pc);
        Assert.Equal(0xE001, Assert.Single(result.TraceTail).Opcode);
    }

    [Fact]
    public void CapturesFilteredTraceLog()
    {
        var elf = ElfFile.Read(new MemoryStream(CreateNopElf()));
        var options = new DreamcastRunOptions(
            InstructionLimit: 3,
            TraceTailLength: 0,
            TraceCapture: new DreamcastTraceCaptureOptions(StartPc: 0x8C01_0002, EndPc: 0x8C01_0004, Limit: 1));

        var result = new DreamcastRunner().Run(elf, options);

        var step = Assert.Single(result.TraceLog);
        Assert.Equal(0x8C01_0002u, step.Pc);
    }

    [Fact]
    public void CapturesFilteredTraceLogAcrossPcRanges()
    {
        var elf = ElfFile.Read(new MemoryStream(CreateNopElf()));
        var options = new DreamcastRunOptions(
            InstructionLimit: 3,
            TraceTailLength: 0,
            TraceCapture: new DreamcastTraceCaptureOptions(
                Limit: 4,
                Ranges:
                [
                    new DreamcastTracePcRange(0x8C01_0000, 0x8C01_0000),
                    new DreamcastTracePcRange(0x8C01_0004, 0x8C01_0004)
                ]));

        var result = new DreamcastRunner().Run(elf, options);

        Assert.Collection(
            result.TraceLog,
            first => Assert.Equal(0x8C01_0000u, first.Pc),
            second => Assert.Equal(0x8C01_0004u, second.Pc));
    }

    [Fact]
    public void CapturesFilteredTraceLogAcrossInstructionRange()
    {
        var elf = ElfFile.Read(new MemoryStream(CreateNopElf()));
        var options = new DreamcastRunOptions(
            InstructionLimit: 3,
            TraceTailLength: 0,
            TraceCapture: new DreamcastTraceCaptureOptions(
                Limit: 4,
                StartInstruction: 2,
                EndInstruction: 3));

        var result = new DreamcastRunner().Run(elf, options);

        Assert.Collection(
            result.TraceLog,
            first =>
            {
                Assert.Equal(2UL, first.Instruction);
                Assert.Equal(0x8C01_0002u, first.Pc);
            },
            second =>
            {
                Assert.Equal(3UL, second.Instruction);
                Assert.Equal(0x8C01_0004u, second.Pc);
            });
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
    public void CapturesTrapExceptionRegistersInCpuSnapshot()
    {
        var elf = ElfFile.Read(new MemoryStream(CreateTrapElf()));

        var result = new DreamcastRunner().Run(elf, new DreamcastRunOptions(InstructionLimit: 10, TraceTailLength: 4));

        Assert.Equal(DreamcastStopReason.UnsupportedInstruction, result.StopReason);
        Assert.Equal(0x8C01_0100u, result.StopPc);
        Assert.Equal(0x8C01_0006u, result.Cpu.Spc);
        Assert.Equal(0u, result.Cpu.Ssr);
        Assert.Equal(0x0000_0014u, result.Cpu.Tra);
        Assert.Equal(0x0000_0160u, result.Cpu.Expevt);
        Assert.Equal(0u, result.Cpu.Intevt);

        var summary = DreamcastRunSummary.FromResult(result);

        Assert.Equal("0x8C010006", summary.Cpu.SpcHex);
        Assert.Equal("0x00000014", summary.Cpu.TraHex);
        Assert.Equal("0x00000160", summary.Cpu.ExpevtHex);
        Assert.Equal("0x00000000", summary.Cpu.IntevtHex);
    }

    [Fact]
    public void CapturesStackWordsInCpuSnapshot()
    {
        var memory = new DreamcastMemory();
        var state = new Sh4State();
        state.R[15] = 0x7E00_0FF8;
        memory.WriteUInt32(0x7E00_0FF8, 0x1234_5678);
        memory.WriteUInt32(0x7E00_0FFC, 0xAABB_CCDD);

        var snapshot = Sh4StateSnapshot.From(state, memory);

        Assert.Collection(
            snapshot.StackWords!,
            word =>
            {
                Assert.Equal(0x7E00_0FF8u, word.Address);
                Assert.Equal("0x7E000FF8", word.AddressHex);
                Assert.Equal(0x1234_5678u, word.Value);
                Assert.Equal("0x12345678", word.ValueHex);
            },
            word =>
            {
                Assert.Equal(0x7E00_0FFCu, word.Address);
                Assert.Equal("0x7E000FFC", word.AddressHex);
                Assert.Equal(0xAABB_CCDDu, word.Value);
                Assert.Equal("0xAABBCCDD", word.ValueHex);
            });
        Assert.Empty(memory.DeviceAccesses);
    }

    [Fact]
    public void CapturesIpBinResetFrameModeWordsInCpuSnapshot()
    {
        var memory = new DreamcastMemory();
        var state = new Sh4State();
        state.R[15] = 0x7E00_0FD0;
        for (var index = 0u; index < 10; index++)
        {
            memory.WriteUInt32(state.R[15] + (index * 4), 0xA000_0000u + index);
        }

        var snapshot = Sh4StateSnapshot.From(state, memory);

        Assert.Equal(10, snapshot.StackWords!.Count);
        Assert.Equal(0x7E00_0FF0u, snapshot.StackWords[8].Address);
        Assert.Equal(0xA000_0008u, snapshot.StackWords[8].Value);
        Assert.Equal(0x7E00_0FF4u, snapshot.StackWords[9].Address);
        Assert.Equal(0xA000_0009u, snapshot.StackWords[9].Value);
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
        Assert.Equal(result.DeviceAccesses.Count, summary.DeviceAccessCount);
        Assert.Contains(summary.DeviceAccessDomains, domain => domain.Domain == DreamcastDeviceDomainClassifier.Scif && domain.Count == result.DeviceAccesses.Count);
        Assert.Contains(summary.DeviceAccessKinds, kind => kind.Kind == MemoryAccessKind.Write && kind.Count == result.DeviceAccesses.Count);
        Assert.Single(summary.RecentDeviceAccesses);
        Assert.Equal(2, summary.TraceTail.Count);
        Assert.Equal(result.Asic.PendingEventCodeHex, summary.Asic.PendingEventCodeHex);
        Assert.Equal(result.Timer?.PendingEventCodeHex, summary.Timer.PendingEventCodeHex);
        Assert.Equal(3, summary.Timer.Channels.Count);
        Assert.Equal(result.Video.Fnv1A32Hex, summary.Video.Fnv1A32Hex);
        Assert.Equal(result.Scheduler.VBlankEventsRaised, summary.Scheduler.VBlankEventsRaised);
        Assert.Equal(result.Scheduler.HardwareAdvanceTicks, summary.Scheduler.HardwareAdvanceTicks);
        Assert.Equal(result.Scheduler.HardwareAdvanceBatches, summary.Scheduler.HardwareAdvanceBatches);
        Assert.Equal(result.Scheduler.MaxHardwareAdvanceBatch, summary.Scheduler.MaxHardwareAdvanceBatch);
        Assert.Equal(result.Scheduler.CpuFastForwardInstructions, summary.Scheduler.CpuFastForwardInstructions);
        Assert.Equal(result.Maple.Transfers.Count, summary.Maple.TransferCount);
    }

    [Fact]
    public void StructuredRunSummaryIncludesNearestSymbols()
    {
        var symbol = new ElfSymbol("main", 0x8C01_0000, 8, 0x12, 0, 1);
        var load = new ElfLoadResult(
            EntryPoint: 0x8C01_0000,
            TranslatedEntryPoint: 0x0C01_0000,
            LoadedSegments: [new LoadedSegment(0, 0x8C01_0000, 0x0C01_0000, 8, 8, 5, 32)],
            Symbols: [symbol]);
        var result = new DreamcastRunResult(
            load,
            new Sh4StateSnapshot(
                new uint[16],
                0x8C01_0008,
                0x8C02_0000,
                0x4000_00F0,
                0x8C03_0000,
                0x8C04_0000,
                0x0004_0001,
                4,
                Spc: 0x8C01_0002,
                Ssr: 0x0000_00F1,
                Tra: 0x0000_00F0,
                Expevt: 0x0000_0160,
                Intevt: 0x0000_0320),
            [new Sh4StepResult(0x8C01_0004, 0x0009, "nop")],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            new DreamcastAsicSnapshot([], null, null, null, null),
            new DreamcastVideoSnapshot(0, 0, 0, "0x00000000", null, null, [], [], [], [], [], [], []),
            new DreamcastAudioSnapshot(0, 0, 0, "0x00000000", [], [], [], []),
            new DreamcastMapleSnapshot([]),
            new DreamcastSchedulerSnapshot(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            DreamcastStopReason.UnsupportedInstruction,
            "unsupported",
            0x8C01_0006,
            0xFFFF);

        var summary = DreamcastRunSummary.FromResult(result);

        Assert.Equal(1, summary.Load.SymbolCount);
        Assert.Equal("main+0x6", summary.StopSymbol?.Display);
        Assert.Equal("main+0x4", Assert.Single(summary.TraceTail).Symbol?.Display);
        Assert.Equal("0x8C010008", summary.Cpu.PcHex);
        Assert.Equal("0x8C020000", summary.Cpu.PrHex);
        Assert.Equal("0x400000F0", summary.Cpu.SrHex);
        Assert.Equal("0x8C030000", summary.Cpu.GbrHex);
        Assert.Equal("0x8C040000", summary.Cpu.VbrHex);
        Assert.Equal("0x8C010002", summary.Cpu.SpcHex);
        Assert.Equal("0x000000F1", summary.Cpu.SsrHex);
        Assert.Equal("0x00040001", summary.Cpu.FpscrHex);
        Assert.Equal("0x000000F0", summary.Cpu.TraHex);
        Assert.Equal("0x00000160", summary.Cpu.ExpevtHex);
        Assert.Equal("0x00000320", summary.Cpu.IntevtHex);
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

    [Fact]
    public void SummaryUsesAdvancedSchedulerTicksForControllerScriptState()
    {
        var load = new ElfLoadResult(
            EntryPoint: 0x8C01_0000,
            TranslatedEntryPoint: 0x0C01_0000,
            LoadedSegments: [],
            Symbols: []);
        var result = new DreamcastRunResult(
            load,
            new Sh4StateSnapshot(new uint[16], 0x8C01_0002, 0, 0, 0, 0, 0, 1),
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            new DreamcastAsicSnapshot([], null, null, null, null),
            new DreamcastVideoSnapshot(0, 0, 0, "0x00000000", null, null, [], [], [], [], [], [], []),
            new DreamcastAudioSnapshot(0, 0, 0, "0x00000000", [], [], [], []),
            new DreamcastMapleSnapshot([]),
            new DreamcastSchedulerSnapshot(0, 0, 0, 5, 1, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1),
            DreamcastStopReason.InstructionLimit,
            "limit",
            null,
            null);
        var options = new DreamcastRunOptions(
            ControllerAScript: new DreamcastControllerScript(
                new DreamcastControllerScriptFrame(0, DreamcastControllerState.Neutral),
                new DreamcastControllerScriptFrame(5, new DreamcastControllerState(Buttons: DreamcastControllerButtons.Start))));

        var summary = DreamcastRunSummary.FromResult(result, options);

        Assert.Equal(DreamcastControllerButtons.Start, summary.ControllerA.Buttons);
    }

    [Fact]
    public void SummaryUsesMappedControllerScriptStateAtStopInstruction()
    {
        var elf = ElfFile.Read(new MemoryStream(CreateNopElf()));
        var options = new DreamcastRunOptions(
            InstructionLimit: 3,
            TraceTailLength: 0,
            ControllerScripts: new Dictionary<byte, DreamcastControllerScript>
            {
                [0x20] = new(
                    new DreamcastControllerScriptFrame(0, DreamcastControllerState.Neutral),
                    new DreamcastControllerScriptFrame(2, new DreamcastControllerState(Buttons: DreamcastControllerButtons.Start)))
            });

        var result = new DreamcastRunner().Run(elf, options);

        var summary = DreamcastRunSummary.FromResult(result, options);

        Assert.Equal(DreamcastControllerButtons.Start, summary.ControllerA.Buttons);
    }

    [Fact]
    public void RunCoalescesReadOnlyPollingLoopToControllerScriptBoundary()
    {
        var elf = ElfFile.Read(new MemoryStream(CreateReadOnlyPollingLoopElf()));
        var options = new DreamcastRunOptions(
            InstructionLimit: 8,
            TraceTailLength: 0,
            VBlankInterval: 0,
            ControllerAScript: new DreamcastControllerScript(
                new DreamcastControllerScriptFrame(0, DreamcastControllerState.Neutral),
                new DreamcastControllerScriptFrame(5, new DreamcastControllerState(Buttons: DreamcastControllerButtons.Start))));

        var result = new DreamcastRunner().Run(elf, options);

        Assert.Equal(DreamcastStopReason.InstructionLimit, result.StopReason);
        Assert.Equal(8UL, result.Cpu.InstructionsExecuted);
        Assert.Equal(8UL, result.Scheduler.HardwareAdvanceTicks);
        Assert.Equal(7UL, result.Scheduler.HardwareAdvanceBatches);
        Assert.Equal(2UL, result.Scheduler.MaxHardwareAdvanceBatch);
        Assert.Equal(2UL, result.Scheduler.IdleAdvanceTicks);
        Assert.Equal(1UL, result.Scheduler.IdleAdvanceBatches);
        Assert.Equal(2UL, result.Scheduler.MaxIdleAdvanceBatch);
        Assert.Equal(1UL, result.Scheduler.IdleInputWakeCount);
        Assert.Equal(1UL, result.Scheduler.ControllerScriptChanges);

        var summary = DreamcastRunSummary.FromResult(result, options);
        Assert.Equal(DreamcastControllerButtons.Start, summary.ControllerA.Buttons);
    }

    [Fact]
    public void RunCatchesHardwareUpWhenCountedIdleFastForwardReachesLimit()
    {
        var elf = ElfFile.Read(new MemoryStream(CreateCountedIdleLoopElf()));

        var result = new DreamcastRunner().Run(elf, new DreamcastRunOptions(
            InstructionLimit: 15,
            TraceTailLength: 0,
            VBlankInterval: 0));

        Assert.Equal(DreamcastStopReason.InstructionLimit, result.StopReason);
        Assert.Equal(15UL, result.Cpu.InstructionsExecuted);
        Assert.Equal(15UL, result.Scheduler.HardwareAdvanceTicks);
        Assert.Equal(9UL, result.Scheduler.CpuFastForwardInstructions);
        Assert.Equal(1UL, result.Scheduler.CpuFastForwardBatches);
        Assert.Equal(9UL, result.Scheduler.MaxCpuFastForwardBatch);
    }

    [Fact]
    public void RunFastForwardsUncapturedLoopsWhenTraceCaptureIsActive()
    {
        var elf = ElfFile.Read(new MemoryStream(CreateCountedIdleLoopElf()));

        var result = new DreamcastRunner().Run(elf, new DreamcastRunOptions(
            InstructionLimit: 15,
            TraceTailLength: 0,
            VBlankInterval: 0,
            TraceCapture: new DreamcastTraceCaptureOptions(StartPc: 0x8C01_0000, EndPc: 0x8C01_0004, Limit: 8)));

        Assert.Equal(DreamcastStopReason.InstructionLimit, result.StopReason);
        Assert.Equal(15UL, result.Cpu.InstructionsExecuted);
        Assert.Equal(9UL, result.Scheduler.CpuFastForwardInstructions);
        Assert.Equal(1UL, result.Scheduler.CpuFastForwardBatches);
        Assert.Equal([0x8C01_0000u, 0x8C01_0002u, 0x8C01_0004u], result.TraceLog.Select(step => step.Pc));
    }

    [Fact]
    public void RunDoesNotFastForwardLoopBodyInsideTraceCaptureRange()
    {
        var elf = ElfFile.Read(new MemoryStream(CreateCountedIdleLoopElf()));

        var result = new DreamcastRunner().Run(elf, new DreamcastRunOptions(
            InstructionLimit: 15,
            TraceTailLength: 0,
            VBlankInterval: 0,
            TraceCapture: new DreamcastTraceCaptureOptions(StartPc: 0x8C01_0006, EndPc: 0x8C01_000C, Limit: 16)));

        Assert.Equal(DreamcastStopReason.InstructionLimit, result.StopReason);
        Assert.Equal(0UL, result.Scheduler.CpuFastForwardInstructions);
        Assert.Contains(result.TraceLog, step => step.Pc == 0x8C01_0006);
        Assert.Contains(result.TraceLog, step => step.Pc == 0x8C01_000A);
    }

    [Fact]
    public void DetectsSideEffectFreeIdleLoops()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt16(0x8C01_0000, 0xAFFE);
        memory.WriteUInt16(0x8C01_0002, 0x0009);
        memory.WriteUInt16(0x8C01_0010, 0x0009);
        memory.WriteUInt16(0x8C01_0012, 0x0009);
        memory.WriteUInt16(0x8C01_0014, 0x89FC);

        Assert.True(DreamcastRunner.IsSideEffectFreeIdleLoop(new Sh4StepResult(0x8C01_0000, 0xAFFE, "bra 0x8C010000"), memory));
        Assert.True(DreamcastRunner.IsSideEffectFreeIdleLoop(new Sh4StepResult(0x8C01_0004, 0x89FE, "bt 0x8C010004 ; taken"), memory));
        Assert.True(DreamcastRunner.IsSideEffectFreeIdleLoop(new Sh4StepResult(0x8C01_0008, 0x8BFE, "bf 0x8C010008 ; taken"), memory));
        Assert.True(DreamcastRunner.IsSideEffectFreeIdleLoop(new Sh4StepResult(0x8C01_0014, 0x89FC, "bt 0x8C010010 ; taken"), memory));
        Assert.False(DreamcastRunner.IsSideEffectFreeIdleLoop(new Sh4StepResult(0x8C01_000C, 0x8BFE, "bf ; not taken"), memory));
    }

    [Fact]
    public void DetectsReadOnlyPollingIdleLoops()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt16(0x8C01_0020, 0x6200); // mov.b @r0,r2
        memory.WriteUInt16(0x8C01_0022, 0x622C); // extu.b r2,r2
        memory.WriteUInt16(0x8C01_0024, 0xC802); // tst #0x02,r0
        memory.WriteUInt16(0x8C01_0026, 0x8BFB); // bf 0x8C010020
        memory.WriteUInt16(0x8C01_0030, 0x8401); // mov.b @(0x1,r0),r0
        memory.WriteUInt16(0x8C01_0032, 0xC9F0); // and #0xF0,r0
        memory.WriteUInt16(0x8C01_0034, 0x8800); // cmp/eq #0,r0
        memory.WriteUInt16(0x8C01_0036, 0x89FB); // bt 0x8C010030

        Assert.True(DreamcastRunner.IsSideEffectFreeIdleLoop(new Sh4StepResult(0x8C01_0026, 0x8BFB, "bf 0x8C010020 ; taken"), memory));
        Assert.True(DreamcastRunner.IsSideEffectFreeIdleLoop(new Sh4StepResult(0x8C01_0036, 0x89FB, "bt 0x8C010030 ; taken"), memory));
    }

    [Fact]
    public void RejectsBranchToSelfWhenDelaySlotHasSideEffects()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt16(0x8C01_0000, 0xAFFE);
        memory.WriteUInt16(0x8C01_0002, 0xE001);

        Assert.False(DreamcastRunner.IsSideEffectFreeIdleLoop(new Sh4StepResult(0x8C01_0000, 0xAFFE, "bra 0x8C010000"), memory));
    }

    [Fact]
    public void RejectsBackwardConditionalLoopWhenBodyHasSideEffects()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt16(0x8C01_0010, 0x0009);
        memory.WriteUInt16(0x8C01_0012, 0xE001);
        memory.WriteUInt16(0x8C01_0014, 0x89FC);
        memory.WriteUInt16(0x8C01_0020, 0x0009);
        memory.WriteUInt16(0x8C01_0022, 0x2100);
        memory.WriteUInt16(0x8C01_0024, 0x8BFC);

        Assert.False(DreamcastRunner.IsSideEffectFreeIdleLoop(new Sh4StepResult(0x8C01_0014, 0x89FC, "bt 0x8C010010 ; taken"), memory));
        Assert.False(DreamcastRunner.IsSideEffectFreeIdleLoop(new Sh4StepResult(0x8C01_0024, 0x8BFC, "bf 0x8C010020 ; taken"), memory));
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

    private static byte[] CreateTrapElf()
    {
        var bytes = new byte[0x102];
        WriteUInt16(bytes, 0x00, 0xD002); // mov.l @(0x02,pc),r0
        WriteUInt16(bytes, 0x02, 0x402E); // ldc r0,vbr
        WriteUInt16(bytes, 0x04, 0xC305); // trapa #5
        WriteUInt16(bytes, 0x06, 0x0009); // nop
        WriteUInt32(bytes, 0x0C, 0x8C01_0000);
        WriteUInt16(bytes, 0x100, 0xFFFF);
        return CreateElfWithSegment(bytes);
    }

    private static byte[] CreateReadOnlyPollingLoopElf()
    {
        return CreateElfWithSegment(
        [
            0x00, 0x62, // mov.b @r0,r2
            0x28, 0x22, // tst r2,r2
            0xFC, 0x89  // bt 0x8C010000
        ]);
    }

    private static byte[] CreateCountedIdleLoopElf()
    {
        return CreateElfWithSegment(
        [
            0xF0, 0xE0, // mov #-16,r0
            0x0E, 0x40, // ldc r0,sr
            0x03, 0xE1, // mov #3,r1
            0x09, 0x00, // nop
            0x10, 0x41, // dt r1
            0xFC, 0x8F, // bf/s 0x8C010006
            0x09, 0x00, // nop
            0x09, 0x00  // fallthrough
        ]);
    }

    private static byte[] CreateUnmappedReadBootBinary() =>
    [
        0x01, 0xD1, // mov.l @(0x01,pc),r1
        0x10, 0x60, // mov.b @r1,r0
        0xFE, 0xAF, // bra 0x8C010004
        0x09, 0x00, // nop
        0x10, 0x00, 0x00, 0x08
    ];

    private static byte[] CreateOtherDeviceReadBootBinary() =>
    [
        0x01, 0xD1, // mov.l @(0x01,pc),r1
        0x12, 0x60, // mov.l @r1,r0
        0xFE, 0xAF, // bra 0x8C010004
        0x09, 0x00, // nop
        0x1C, 0x00, 0x00, 0xFF
    ];

    private static byte[] CreateBootWorkWriteBinary() =>
    [
        0x01, 0xD1, // mov.l @(0x01,pc),r1
        0x02, 0x21, // mov.l r0,@r1
        0xFE, 0xAF, // bra 0x8C010004
        0x09, 0x00, // nop
        0x00, 0xC0, 0x00, 0x8C
    ];

    private static byte[] CreateBootWorkReadBinary() =>
    [
        0x01, 0xD1, // mov.l @(0x01,pc),r1
        0x12, 0x60, // mov.l @r1,r0
        0xFE, 0xAF, // bra 0x8C010004
        0x09, 0x00, // nop
        0x00, 0xC0, 0x00, 0x8C
    ];

    private static byte[] CreateAsicEventReadBinary() =>
    [
        0x01, 0xD1, // mov.l @(0x01,pc),r1
        0x12, 0x60, // mov.l @r1,r0
        0xFE, 0xAF, // bra 0x8C010004
        0x09, 0x00, // nop
        0x00, 0x69, 0x5F, 0xA0
    ];

    private static byte[] CreateStatusRegisterReadBinary() =>
    [
        0x02, 0x00 // stc sr,r0
    ];

    private static byte[] CreateDualEntryBinary() =>
    [
        0x09, 0x00, // nop
        0x09, 0x00, // nop
        0x01, 0xE0  // mov #1,r0
    ];

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
