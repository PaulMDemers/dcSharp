using DcSharp.Core.Dreamcast.Input;
using DcSharp.Core.Dreamcast.Memory;
using DcSharp.Core.Execution;
using DcSharp.Core.Media;

namespace DcSharp.Tests;

public class DreamcastMemoryTests
{
    [Theory]
    [InlineData(0x8C01_0000u, 0x0C01_0000u)]
    [InlineData(0xAC01_0000u, 0x0C01_0000u)]
    [InlineData(0x0C01_0000u, 0x0C01_0000u)]
    [InlineData(0x4C01_0000u, 0x0C01_0000u)]
    public void TranslatesDreamcastRamMirrors(uint address, uint expectedPhysical)
    {
        Assert.Equal(expectedPhysical, DreamcastMemory.TranslateAddress(address));
    }

    [Fact]
    public void LoadsTlbEntryForLowVirtualRamFetches()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt16(0x8C13_1B90, 0xE001);
        memory.WriteUInt32(0xFF00_0000, 0x0000_5800);
        memory.WriteUInt32(0xFF00_0004, 0x0C13_194A);

        memory.LoadTlbFromRegisters();

        Assert.Equal(0xE001, memory.ReadInstructionUInt16(0x0000_5B90));
    }

    [Fact]
    public void LoadsTlbEntryWithSh4PageSizeBits()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0x8C10_1234, 0x1234_5678);
        memory.WriteUInt32(0xFF00_0000, 0x0200_0000);
        memory.WriteUInt32(0xFF00_0004, 0x0C10_0190);

        memory.LoadTlbFromRegisters();

        Assert.Equal(0x1234_5678u, memory.ReadUInt32(0x0200_1234));
    }

    [Fact]
    public void BacksMmuEnabledLowVirtualRamWithoutUsingAreaZeroExternalMirror()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0xFF00_0010, 0x0000_0001);

        memory.WriteUInt32(0x01E3_24DC, 0x1234_5678);
        memory.WriteUInt32(0x0201_0000, 0x89AB_CDEF);

        Assert.Equal(0x1234_5678u, memory.ReadUInt32(0x01E3_24DC));
        Assert.Equal(0x89AB_CDEFu, memory.ReadUInt32(0x0201_0000));
        Assert.DoesNotContain(memory.DeviceAccesses, access => access.Address == 0x01E3_24DC);
        Assert.DoesNotContain(memory.DeviceAccesses, access => access.Address == 0x0201_0000);
    }

    [Fact]
    public void MapsWinCeSectionVirtualAddressBackToLoadedSourceBytes()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0xFF00_0010, 0x0000_0001);
        memory.WriteUInt16(0x8C03_34DC, 0x2F86);

        memory.WriteUInt32(0x8C08_DF84, 0x0001_A0EF);
        memory.WriteUInt32(0x8C08_DF88, 0x0000_1000);
        memory.WriteUInt32(0x8C08_DF8C, 0x0001_A200);
        memory.WriteUInt32(0x8C08_DF90, 0x8C03_2000);
        memory.WriteUInt32(0x8C08_DF94, 0x03E3_1000);
        memory.WriteUInt32(0x8C08_DF98, 0x6000_0020);

        memory.WriteUInt32(0x0201_0000, 0x0001_A0EF);
        memory.WriteUInt32(0x0201_0004, 0x0000_1000);
        memory.WriteUInt32(0x0201_0008, 0x03E3_1000);
        memory.WriteUInt32(0x0201_0010, 0x6000_0020);
        memory.WriteUInt32(0x0201_0014, 0x0001_A200);

        Assert.Equal(0x2F86, memory.ReadInstructionUInt16(0x01E3_24DC));
    }

    [Theory]
    [InlineData(0x0301_00C0u, 0x0101_00C0u)]
    [InlineData(0x025F_8000u, 0x005F_8000u)]
    [InlineData(0x0060_0004u, 0x0060_0004u)]
    public void NormalizesAreaZeroPhysicalMirror(uint physical, uint expectedCanonical)
    {
        Assert.Equal(expectedCanonical, DreamcastMemory.NormalizePhysicalAddress(physical));
    }

    [Fact]
    public void TreatsBootRomAreaAsMappedReadOnlySpace()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0x4000_00F0, 0x1234_5678);

        Assert.Equal(0u, memory.ReadUInt32(0x0000_00F0));
        Assert.DoesNotContain(memory.DeviceAccesses, access => access.Kind is MemoryAccessKind.UnmappedRead or MemoryAccessKind.UnmappedWrite);
        Assert.Contains(memory.DeviceAccesses, access =>
            access.Kind == MemoryAccessKind.Write
            && access.Address == 0x4000_00F0
            && access.Value == 0x1234_5678);
    }

    [Fact]
    public void PreservesLowBiosInterruptVectorTableWrites()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0x0000_0224, 0x8C12_BF20);

        Assert.Equal(0x8C12_BF20u, memory.ReadUInt32(0x4000_0224));
        Assert.True(memory.TryPeekUInt32(0x0000_0224, out var peeked));
        Assert.Equal(0x8C12_BF20u, peeked);
        Assert.True(memory.TryGetBiosInterruptHandler(9, out var vectorAddress, out var handlerAddress));
        Assert.Equal(0x0000_0224u, vectorAddress);
        Assert.Equal(0x8C12_BF20u, handlerAddress);
        Assert.DoesNotContain(memory.DeviceAccesses, access => access.Kind is MemoryAccessKind.UnmappedRead or MemoryAccessKind.UnmappedWrite);
    }

    [Fact]
    public void WritesThroughP1MirrorIntoSystemRam()
    {
        var memory = new DreamcastMemory();

        memory.Write(0x8C01_0000, [0xC0, 0xFF, 0xEE]);

        Assert.Equal(0xC0, memory.ReadByte(0x0C01_0000));
        Assert.Equal(0xFF, memory.ReadByte(0xAC01_0001));
        Assert.Equal(0xEE, memory.ReadByte(0x8C01_0002));
    }

    [Fact]
    public void MirrorsSecondSixteenMegabyteRamAperture()
    {
        var memory = new DreamcastMemory();

        memory.Write(0xACFF_FFFF, [0xBA]);
        memory.Write(0xADFF_FFFF, [0xAB]);

        Assert.Equal(0xAB, memory.ReadByte(0xACFF_FFFF));
        Assert.Equal(0xAB, memory.ReadByte(0xADFF_FFFF));
    }

    [Theory]
    [InlineData(0x7C00_0FFCu)]
    [InlineData(0x7E00_0FFCu)]
    public void MapsOperandCacheRamScratchpadWindows(uint address)
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(address, 0x1234_5678);

        Assert.Equal(0x1234_5678u, memory.ReadUInt32(address));
        Assert.Empty(memory.DeviceAccesses);
    }

    [Theory]
    [InlineData(0xA060_0004u)]
    [InlineData(0xA301_00C0u)]
    public void ReadsAbsentExpansionDevicesAsZeroWithoutUnmappedFault(uint address)
    {
        var memory = new DreamcastMemory();

        Assert.Equal(0, memory.ReadByte(address));

        var access = Assert.Single(memory.DeviceAccesses);
        Assert.Equal(MemoryAccessKind.Read, access.Kind);
        Assert.Equal(address, access.Address);
        Assert.Equal(1, access.Size);
        Assert.Equal(0u, access.Value);
    }

    [Theory]
    [InlineData(0xA060_0004u)]
    [InlineData(0xA301_00C0u)]
    public void WritesAbsentExpansionDevicesWithoutUnmappedFault(uint address)
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(address, 0x1234_5678);

        var access = Assert.Single(memory.DeviceAccesses);
        Assert.Equal(MemoryAccessKind.Write, access.Kind);
        Assert.Equal(address, access.Address);
        Assert.Equal(4, access.Size);
        Assert.Equal(0x1234_5678u, access.Value);
    }

    [Fact]
    public void MapsAicaRtcRegisters()
    {
        var memory = new DreamcastMemory();

        Assert.Equal(0u, memory.ReadUInt32(0xA071_0000));
        memory.WriteUInt32(0xA071_0004, 0x1234_5678);
        Assert.Equal(0x1234_5678u, memory.ReadUInt32(0xA071_0004));

        Assert.DoesNotContain(memory.DeviceAccesses, access => access.Kind is MemoryAccessKind.UnmappedRead or MemoryAccessKind.UnmappedWrite);
        Assert.Contains(memory.DeviceAccesses, access =>
            access.Kind == MemoryAccessKind.Read
            && access.Address == 0xA071_0000
            && access.Size == 4);
    }

    [Fact]
    public void CapturesWatchedMemoryWritesByAddressRange()
    {
        var memory = new DreamcastMemory(writeWatch: new DreamcastMemoryWriteWatch(0x8C01_0003, 0x8C01_0004));
        memory.CurrentInstructionPc = 0x8C02_0000;

        memory.Write(0x8C01_0000, [0x10, 0x20]);
        memory.Write(0x8C01_0002, [0xAA, 0xBB, 0xCC]);

        var access = Assert.Single(memory.WatchedWrites);
        Assert.Equal(MemoryAccessKind.Write, access.Kind);
        Assert.Equal(0x8C01_0002u, access.Address);
        Assert.Equal(3, access.Size);
        Assert.Equal(0x00CC_BBAAu, access.Value);
        Assert.Equal(0x8C02_0000u, access.Pc);
    }

    [Fact]
    public void CapturesWatchedMemoryWritesAcrossAddressRanges()
    {
        var memory = new DreamcastMemory(writeWatch: new DreamcastMemoryWriteWatch(
            Limit: 4,
            Ranges:
            [
                new DreamcastMemoryAddressRange(0x8C01_0000, 0x8C01_0003),
                new DreamcastMemoryAddressRange(0x8C02_0000, 0x8C02_0003)
            ]));

        memory.WriteUInt32(0x8C01_0000, 1);
        memory.WriteUInt32(0x8C01_0010, 2);
        memory.WriteUInt32(0x8C02_0000, 3);

        Assert.Collection(
            memory.WatchedWrites,
            first => Assert.Equal(0x8C01_0000u, first.Address),
            second => Assert.Equal(0x8C02_0000u, second.Address));
    }

    [Fact]
    public void RespectsWatchedMemoryWriteLimit()
    {
        var memory = new DreamcastMemory(writeWatch: new DreamcastMemoryWriteWatch(0x8C01_0000, 0x8C01_000F, Limit: 1));

        memory.WriteUInt32(0x8C01_0000, 1);
        memory.WriteUInt32(0x8C01_0004, 2);

        var access = Assert.Single(memory.WatchedWrites);
        Assert.Equal(0x8C01_0000u, access.Address);
        Assert.Equal(1u, access.Value);
    }

    [Fact]
    public void CapturesWatchedMemoryWritePreviousValue()
    {
        var memory = new DreamcastMemory(writeWatch: new DreamcastMemoryWriteWatch(0x8C01_0000, 0x8C01_0003));
        memory.WriteUInt32(0x8C01_0000, 0x1234_5678);
        memory.ResetWatchedWrites();

        memory.WriteUInt16(0x8C01_0000, 0xABCD);

        var access = Assert.Single(memory.WatchedWrites);
        Assert.Equal(2, access.Size);
        Assert.Equal(0xABCDu, access.Value);
        Assert.Equal(0x5678u, access.PreviousValue);
    }

    [Fact]
    public void CanCaptureOnlyWatchedMemoryWritesThatChangeMemory()
    {
        var memory = new DreamcastMemory(writeWatch: new DreamcastMemoryWriteWatch(
            StartAddress: 0x8C01_0000,
            EndAddress: 0x8C01_0003,
            ChangedOnly: true));

        memory.WriteUInt32(0x8C01_0000, 0);
        memory.WriteUInt32(0x8C01_0000, 1);
        memory.WriteUInt32(0x8C01_0000, 1);
        memory.WriteUInt32(0x8C01_0000, 2);

        Assert.Collection(
            memory.WatchedWrites,
            first =>
            {
                Assert.Equal(1u, first.Value);
                Assert.Equal(0u, first.PreviousValue);
            },
            second =>
            {
                Assert.Equal(2u, second.Value);
                Assert.Equal(1u, second.PreviousValue);
            });
    }

    [Fact]
    public void CanCaptureOnlyDistinctWatchedMemoryWrites()
    {
        var memory = new DreamcastMemory(writeWatch: new DreamcastMemoryWriteWatch(
            StartAddress: 0x8C01_0000,
            EndAddress: 0x8C01_0003,
            Distinct: true));
        memory.CurrentInstructionPc = 0x8C02_0000;
        memory.CurrentInstructionOpcode = 0x1234;

        memory.WriteUInt32(0x8C01_0000, 1);
        memory.WriteUInt32(0x8C01_0000, 1);
        memory.WriteUInt32(0x8C01_0000, 2);

        Assert.Collection(
            memory.WatchedWrites,
            first => Assert.Equal(1u, first.Value),
            second => Assert.Equal(2u, second.Value));
    }

    [Fact]
    public void CapturesWatchedMemoryWritesByProgramCounterRange()
    {
        var memory = new DreamcastMemory(writeWatch: new DreamcastMemoryWriteWatch(
            StartAddress: 0x8C01_0000,
            EndAddress: 0x8C01_000F,
            StartPc: 0x8C10_0800,
            EndPc: 0x8C10_0810));

        memory.CurrentInstructionPc = 0x8C10_07FE;
        memory.WriteUInt32(0x8C01_0000, 1);
        memory.CurrentInstructionPc = 0x8C10_080A;
        memory.WriteUInt32(0x8C01_0004, 2);
        memory.CurrentInstructionPc = null;
        memory.WriteUInt32(0x8C01_0008, 3);

        var access = Assert.Single(memory.WatchedWrites);
        Assert.Equal(0x8C01_0004u, access.Address);
        Assert.Equal(2u, access.Value);
        Assert.Equal(0x8C10_080Au, access.Pc);
    }

    [Fact]
    public void FormatsWatchedMemoryWriteProducerSource()
    {
        var access = new MemoryAccess(
            MemoryAccessKind.Write,
            0xE000_0024,
            4,
            0x4296_0000,
            0x8C10_080A,
            0x1E21);

        Assert.Equal(
            "op=0x1E21 source=r2 trace=\"mov.l r2,@(4,r14)\"",
            DreamcastMemoryAccessProducerFormatter.Format(access));
    }

    [Fact]
    public void CapturesWatchedMemoryReadsByAddressRange()
    {
        var memory = new DreamcastMemory(readWatch: new DreamcastMemoryReadWatch(0x8C01_0002, 0x8C01_0005));
        memory.WriteUInt32(0x8C01_0004, 0x1234_5678);
        memory.CurrentInstructionPc = 0x8C02_0000;

        Assert.Equal(0x1234_5678u, memory.ReadUInt32(0x8C01_0004));
        Assert.Equal(0, memory.ReadByte(0x8C01_0008));

        var access = Assert.Single(memory.WatchedReads);
        Assert.Equal(MemoryAccessKind.Read, access.Kind);
        Assert.Equal(0x8C01_0004u, access.Address);
        Assert.Equal(4, access.Size);
        Assert.Equal(0x1234_5678u, access.Value);
        Assert.Equal(0x8C02_0000u, access.Pc);
    }

    [Fact]
    public void CanCaptureOnlyDistinctWatchedMemoryReads()
    {
        var memory = new DreamcastMemory(readWatch: new DreamcastMemoryReadWatch(
            StartAddress: 0x8C01_0000,
            EndAddress: 0x8C01_0003,
            Distinct: true));
        memory.CurrentInstructionPc = 0x8C02_0000;
        memory.CurrentInstructionOpcode = 0x6252;

        memory.WriteUInt32(0x8C01_0000, 1);
        Assert.Equal(1u, memory.ReadUInt32(0x8C01_0000));
        Assert.Equal(1u, memory.ReadUInt32(0x8C01_0000));
        memory.WriteUInt32(0x8C01_0000, 2);
        Assert.Equal(2u, memory.ReadUInt32(0x8C01_0000));

        Assert.Collection(
            memory.WatchedReads,
            first => Assert.Equal(1u, first.Value),
            second => Assert.Equal(2u, second.Value));
    }

    [Fact]
    public void CapturesWatchedMemoryReadsAcrossAddressRanges()
    {
        var memory = new DreamcastMemory(readWatch: new DreamcastMemoryReadWatch(
            Limit: 4,
            Ranges:
            [
                new DreamcastMemoryAddressRange(0x8C01_0000, 0x8C01_0003),
                new DreamcastMemoryAddressRange(0x8C02_0000, 0x8C02_0003)
            ]));
        memory.WriteUInt32(0x8C01_0000, 1);
        memory.WriteUInt32(0x8C01_0010, 2);
        memory.WriteUInt32(0x8C02_0000, 3);

        Assert.Equal(1u, memory.ReadUInt32(0x8C01_0000));
        Assert.Equal(2u, memory.ReadUInt32(0x8C01_0010));
        Assert.Equal(3u, memory.ReadUInt32(0x8C02_0000));

        Assert.Collection(
            memory.WatchedReads,
            first => Assert.Equal(0x8C01_0000u, first.Address),
            second => Assert.Equal(0x8C02_0000u, second.Address));
    }

    [Fact]
    public void RespectsWatchedMemoryReadLimit()
    {
        var memory = new DreamcastMemory(readWatch: new DreamcastMemoryReadWatch(0x8C01_0000, 0x8C01_000F, Limit: 1));
        memory.WriteUInt32(0x8C01_0000, 1);
        memory.WriteUInt32(0x8C01_0004, 2);

        Assert.Equal(1u, memory.ReadUInt32(0x8C01_0000));
        Assert.Equal(2u, memory.ReadUInt32(0x8C01_0004));

        var access = Assert.Single(memory.WatchedReads);
        Assert.Equal(0x8C01_0000u, access.Address);
        Assert.Equal(1u, access.Value);
    }

    [Fact]
    public void CapturesWatchedMemoryReadsByProgramCounterRanges()
    {
        var memory = new DreamcastMemory(readWatch: new DreamcastMemoryReadWatch(
            StartAddress: 0x8C01_0000,
            EndAddress: 0x8C01_000F,
            PcRanges:
            [
                new DreamcastMemoryAddressRange(0x8C10_0800, 0x8C10_0810),
                new DreamcastMemoryAddressRange(0x8C10_0840, 0x8C10_0850)
            ]));
        memory.WriteUInt32(0x8C01_0000, 1);
        memory.WriteUInt32(0x8C01_0004, 2);
        memory.WriteUInt32(0x8C01_0008, 3);

        memory.CurrentInstructionPc = 0x8C10_07FE;
        Assert.Equal(1u, memory.ReadUInt32(0x8C01_0000));
        memory.CurrentInstructionPc = 0x8C10_080A;
        Assert.Equal(2u, memory.ReadUInt32(0x8C01_0004));
        memory.CurrentInstructionPc = 0x8C10_084C;
        Assert.Equal(3u, memory.ReadUInt32(0x8C01_0008));

        Assert.Collection(
            memory.WatchedReads,
            first => Assert.Equal(0x8C01_0004u, first.Address),
            second => Assert.Equal(0x8C01_0008u, second.Address));
    }

    [Theory]
    [InlineData(0x6252, "op=0x6252 target=r2 trace=\"mov.l @r5,r2\"")]
    [InlineData(0x5351, "op=0x5351 target=r3 trace=\"mov.l @(4,r5),r3\"")]
    [InlineData(0xD34A, "op=0xD34A target=r3 trace=\"mov.l @(296,pc),r3\"")]
    public void FormatsWatchedMemoryReadTargets(ushort opcode, string expected)
    {
        var access = new MemoryAccess(
            MemoryAccessKind.Read,
            0x8C2B_6BC0,
            4,
            0x4280_0000,
            0x8C10_0808,
            opcode);

        Assert.Equal(expected, DreamcastMemoryAccessProducerFormatter.Format(access));
    }

    [Fact]
    public void CapturesP4DeviceAccessProgramCounter()
    {
        var memory = new DreamcastMemory();
        memory.CurrentInstructionPc = 0x8C02_1234;

        memory.WriteUInt32(0xFFD8_000C, 0x1234_5678);
        Assert.Equal(0x1234_5678u, memory.ReadUInt32(0xFFD8_000C));

        Assert.Collection(
            memory.DeviceAccesses,
            write =>
            {
                Assert.Equal(MemoryAccessKind.Write, write.Kind);
                Assert.Equal(0xFFD8_000C, write.Address);
                Assert.Equal(0x1234_5678u, write.Value);
                Assert.Equal(0x8C02_1234u, write.Pc);
            },
            read =>
            {
                Assert.Equal(MemoryAccessKind.Read, read.Kind);
                Assert.Equal(0xFFD8_000C, read.Address);
                Assert.Equal(0x1234_5678u, read.Value);
                Assert.Equal(0x8C02_1234u, read.Pc);
            });
    }

    [Fact]
    public void TryPeekUInt32ReadsMappedRamWithoutRecordingDeviceAccess()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0x7E00_0FF8, 0x1234_5678);

        Assert.True(memory.TryPeekUInt32(0x7E00_0FF8, out var value));

        Assert.Equal(0x1234_5678u, value);
        Assert.Empty(memory.DeviceAccesses);
    }

    [Fact]
    public void TryPeekUInt32RejectsUnmappedMemoryWithoutRecordingDeviceAccess()
    {
        var memory = new DreamcastMemory();

        Assert.False(memory.TryPeekUInt32(0x1804_41F0, out var value));

        Assert.Equal(0u, value);
        Assert.Empty(memory.DeviceAccesses);
    }

    [Fact]
    public void ReadsDefaultSh4PortADataConfigurationBits()
    {
        var memory = new DreamcastMemory();

        Assert.Equal(0x0300u, memory.ReadUInt16(0xFF80_0030));
        Assert.Equal(0x0300u, memory.ReadUInt32(0xFF80_0030) & 0x0300u);
    }

    [Fact]
    public void GdromSnapshotReportsMediaReadCommands()
    {
        var media = new RawSectorMediaImage(CreateMediaData(2), 2048);
        var memory = new DreamcastMemory(media: media);
        memory.WriteUInt32(0x8C01_0000, 1);
        memory.WriteUInt32(0x8C01_0004, 0x8C02_0000);
        memory.WriteUInt32(0x8C01_0008, 1);

        var status = memory.ExecuteGdromCommand(0x8C01_0000);
        var snapshot = memory.CreateGdromSnapshot();

        Assert.Equal(0u, status);
        Assert.Equal(0x20, memory.ReadByte(0x8C02_0000));
        Assert.True(snapshot.HasMedia);
        Assert.Equal(2048, snapshot.SectorSize);
        Assert.Equal(2ul, snapshot.SectorCount);
        var read = Assert.Single(snapshot.ReadCommands);
        Assert.True(read.Success);
        Assert.Equal(1u, read.Sector);
        Assert.Equal("0x00000001", read.SectorHex);
        Assert.Equal(0x8C02_0000u, read.Destination);
        Assert.Equal("0x8C020000", read.DestinationHex);
        Assert.Equal(1u, read.SectorCount);
        Assert.Equal(2048, read.BytesRequested);
        Assert.Equal(2048, read.BytesRead);
        Assert.Equal("media read completed", read.Status);
    }

    [Fact]
    public void GdromPioReadCommandUsesKosParameterLayout()
    {
        var media = new RawSectorMediaImage(CreateMediaData(2), 2048);
        var memory = new DreamcastMemory(media: media);
        memory.WriteUInt32(0x8C01_0000, 1);
        memory.WriteUInt32(0x8C01_0004, 1);
        memory.WriteUInt32(0x8C01_0008, 0x8C02_0000);
        memory.WriteUInt32(0x8C01_000C, 0);

        var status = memory.ExecuteGdromPioReadCommand(0x8C01_0000);
        var snapshot = memory.CreateGdromSnapshot();

        Assert.Equal(0u, status);
        Assert.Equal(0x20, memory.ReadByte(0x8C02_0000));
        var read = Assert.Single(snapshot.ReadCommands);
        Assert.Equal(1u, read.Sector);
        Assert.Equal(1u, read.SectorCount);
        Assert.Equal(0x8C02_0000u, read.Destination);
        Assert.True(read.Success);
    }

    [Fact]
    public void GdromDmaReadCommandRaisesDmaCompleteAsicEvent()
    {
        var media = new RawSectorMediaImage(CreateMediaData(2), 2048);
        var memory = new DreamcastMemory(media: media);
        memory.WriteUInt32(0xA05F_6920, 1u << 14);
        memory.WriteUInt32(0x8C01_0000, 1);
        memory.WriteUInt32(0x8C01_0004, 1);
        memory.WriteUInt32(0x8C01_0008, 0x8C02_0000);
        memory.WriteUInt32(0x8C01_000C, 0);

        var status = memory.ExecuteGdromDmaReadCommand(0x8C01_0000);

        Assert.Equal(0u, status);
        Assert.Equal(0x20, memory.ReadByte(0x8C02_0000));
        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0360u, eventCode);
        Assert.Equal(11, level);
        var snapshot = memory.CreateAsicSnapshot();
        Assert.Equal("IRQB", snapshot.PendingInterrupt?.LevelName);
        Assert.Equal("A", snapshot.PendingInterrupt?.RegisterName);
        Assert.Equal(14, snapshot.PendingInterrupt?.Bit);
        var registerA = Assert.Single(snapshot.EventRegisters, register => register.Name == "A");
        Assert.Equal(1u << 14, registerA.Ack);
        Assert.Equal(1u << 14, registerA.PendingIrqB);

        memory.WriteUInt32(0xA05F_6900, 1u << 14);

        Assert.False(memory.TryGetPendingExternalInterrupt(out _, out _));
    }

    [Fact]
    public void GdromReadTranslatesRawCdFadToTrackRelativeSector()
    {
        var media = new RawSectorFromCdImage(CreateCdMediaData(
        [
            [0xA0, 0xA1],
            [0xB0, 0xB1]
        ]));
        var memory = new DreamcastMemory(media: media);
        memory.WriteUInt32(0x8C01_0000, 45_001);
        memory.WriteUInt32(0x8C01_0004, 1);
        memory.WriteUInt32(0x8C01_0008, 0x8C02_0000);

        var status = memory.ExecuteGdromPioReadCommand(0x8C01_0000);

        Assert.Equal(0u, status);
        Assert.Equal(0xB0, memory.ReadByte(0x8C02_0000));
        Assert.Equal(0xB1, memory.ReadByte(0x8C02_0001));
        var read = Assert.Single(memory.CreateGdromSnapshot().ReadCommands);
        Assert.Equal(45_001u, read.Sector);
        Assert.Equal("0x0000AFC9", read.SectorHex);
        Assert.True(read.Success);
    }

    [Fact]
    public void GdromReadTranslatesRawCdFilesystemFadToTrackRelativeSector()
    {
        var media = new RawSectorFromCdImage(CreateCdMediaData(
        [
            [0xA0, 0xA1],
            [0x01, 0x43, 0x44, 0x30, 0x30, 0x31]
        ]));
        var memory = new DreamcastMemory(media: media);
        memory.WriteUInt32(0x8C01_0000, 45_151);
        memory.WriteUInt32(0x8C01_0004, 1);
        memory.WriteUInt32(0x8C01_0008, 0x8C02_0000);

        var status = memory.ExecuteGdromPioReadCommand(0x8C01_0000);

        Assert.Equal(0u, status);
        Assert.Equal(0x01, memory.ReadByte(0x8C02_0000));
        Assert.Equal((byte)'C', memory.ReadByte(0x8C02_0001));
        Assert.Equal((byte)'D', memory.ReadByte(0x8C02_0002));
        Assert.Equal((byte)'0', memory.ReadByte(0x8C02_0003));
        Assert.Equal((byte)'0', memory.ReadByte(0x8C02_0004));
        Assert.Equal((byte)'1', memory.ReadByte(0x8C02_0005));
        var read = Assert.Single(memory.CreateGdromSnapshot().ReadCommands);
        Assert.Equal(45_151u, read.Sector);
        Assert.Equal("0x0000B05F", read.SectorHex);
        Assert.True(read.Success);
    }

    [Fact]
    public void GdromSnapshotReportsFailedReads()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0x8C01_0000, 0);
        memory.WriteUInt32(0x8C01_0004, 0x8C02_0000);
        memory.WriteUInt32(0x8C01_0008, 1);

        var status = memory.ExecuteGdromCommand(0x8C01_0000);
        var snapshot = memory.CreateGdromSnapshot();

        Assert.Equal(1u, status);
        Assert.False(snapshot.HasMedia);
        var read = Assert.Single(snapshot.ReadCommands);
        Assert.False(read.Success);
        Assert.Equal("no media image loaded", read.Status);
    }

    [Fact]
    public void MapsPvrVramThroughThirtyTwoBitAperture()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt16(0xA500_0000, 0xF800);
        memory.WriteUInt16(0x0500_0002, 0x07E0);

        Assert.Equal(0xF800, memory.ReadUInt16(0x0500_0000));
        Assert.Equal(0x07E0, memory.ReadUInt16(0xA500_0002));
    }

    [Fact]
    public void MapsPvrVramThroughTaTextureApertures()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt16(0x1100_0000, 0xF800);
        memory.WriteUInt16(0x1300_0002, 0x07E0);

        Assert.Equal(0xF800, memory.ReadUInt16(0x0400_0000));
        Assert.Equal(0x07E0, memory.ReadUInt16(0x0500_0002));
        Assert.True(memory.TryGetPvrVramOffset(0x1100_0000, 2, out var texture64Offset));
        Assert.True(memory.TryGetPvrVramOffset(0x1300_0002, 2, out var texture32Offset));
        Assert.Equal(0, texture64Offset);
        Assert.Equal(2, texture32Offset);
    }

    [Fact]
    public void CreatesBoundedMemorySnapshotsWithoutRecordingReads()
    {
        var memory = new DreamcastMemory(readWatch: new DreamcastMemoryReadWatch(0, uint.MaxValue));
        memory.WriteUInt32(0x8C20_C094, 0xA000_0009);
        memory.WriteUInt32(0x8C20_C098, 0x8000_0000);
        memory.WriteUInt16(0x1100_0000, 0xF800);

        var snapshot = memory.CreateMemorySnapshot(
            [
                new DreamcastMemoryAddressRange(0x8C20_C094, 0x8C20_C0A3),
                new DreamcastMemoryAddressRange(0x1100_0000, 0x1100_001F)
            ],
            maxBytesPerRange: 16);

        Assert.Empty(memory.WatchedReads);
        Assert.Collection(
            snapshot.Ranges,
            range =>
            {
                Assert.Equal("0x8C20C094", range.StartAddressHex);
                Assert.Equal(16, range.Bytes.Length);
                Assert.False(range.Truncated);
                Assert.True(range.Readable);
                Assert.Equal(new byte[] { 0x09, 0x00, 0x00, 0xA0 }, range.Bytes.Take(4));
            },
            range =>
            {
                Assert.Equal("0x11000000", range.StartAddressHex);
                Assert.Equal(16, range.Bytes.Length);
                Assert.True(range.Truncated);
                Assert.True(range.Readable);
                Assert.Equal(0x00, range.Bytes[0]);
                Assert.Equal(0xF8, range.Bytes[1]);
            });
    }

    [Fact]
    public void WritesMemorySnapshotLog()
    {
        var snapshot = new DreamcastMemorySnapshot(
            [
                new DreamcastMemorySnapshotRange(
                    0x8C20_C094,
                    0x8C20_C0A3,
                    0x8C20_C0A3,
                    16,
                    true,
                    false,
                    [0x09, 0x00, 0x00, 0xA0, 0x00, 0x00, 0x00, 0x80, 0xC0, 0x04, 0x88, 0x20, 0x00, 0x00, 0x00, 0x00])
            ]);
        using var writer = new StringWriter();

        DreamcastMemorySnapshotLogWriter.WriteText(writer, snapshot);

        var text = writer.ToString();
        Assert.Contains("# Dreamcast final memory snapshot", text);
        Assert.Contains("#0 start=0x8C20C094 end=0x8C20C0A3 capturedEnd=0x8C20C0A3 requestedBytes=16 capturedBytes=16 readable=True truncated=False nonZero=7 firstNonZero=0x8C20C094", text);
        Assert.Contains("0x8C20C094: bytes=09 00 00 A0 00 00 00 80 C0 04 88 20 00 00 00 00 words=0xA0000009,0x80000000,0x208804C0,0x00000000", text);
    }

    [Fact]
    public void PvrDmaCopiesSystemRamToTextureMemoryAndRaisesEvent()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0x8C01_0000, 0x1122_3344);
        memory.WriteUInt32(0x8C01_0004, 0x5566_7788);
        memory.WriteUInt32(0xFFA0_0020, 0x0C01_0000);
        memory.WriteUInt32(0xFFA0_0028, 1);
        memory.WriteUInt32(0xA05F_6800, 0x1100_0040);
        memory.WriteUInt32(0xA05F_6804, 8);
        memory.WriteUInt32(0xA05F_6930, 1u << 19);

        memory.WriteUInt32(0xA05F_6808, 1);

        Assert.Equal(0x1122_3344u, memory.ReadUInt32(0x0400_0040));
        Assert.Equal(0x5566_7788u, memory.ReadUInt32(0x0400_0044));
        Assert.Equal(0u, memory.ReadUInt32(0xA05F_6808));
        Assert.Equal(0u, memory.ReadUInt32(0xFFA0_0028));
        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0320u, eventCode);
        Assert.Equal(9, level);

        var transfer = Assert.Single(memory.CreateVideoSnapshot().PvrDmaTransfers);
        Assert.True(transfer.Completed);
        Assert.Equal("copied to PVR VRAM", transfer.Status);
        Assert.Equal("0x0C010000", transfer.SourceAddressHex);
        Assert.Equal("0x11000040", transfer.DestinationAddressHex);
        Assert.Equal(8u, transfer.ByteCount);
    }

    [Fact]
    public void PvrDmaRecordsFailedTransfersWithoutRaisingEvent()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0xFFA0_0020, 0x1800_0000);
        memory.WriteUInt32(0xA05F_6800, 0x1100_0040);
        memory.WriteUInt32(0xA05F_6804, 4);
        memory.WriteUInt32(0xA05F_6930, 1u << 19);

        memory.WriteUInt32(0xA05F_6808, 1);

        Assert.Equal(0u, memory.ReadUInt32(0xA05F_6808));
        Assert.False(memory.TryGetPendingExternalInterrupt(out _, out _));
        var transfer = Assert.Single(memory.CreateVideoSnapshot().PvrDmaTransfers);
        Assert.False(transfer.Completed);
        Assert.Equal("source outside system RAM", transfer.Status);
    }

    [Fact]
    public void G2AicaDmaCopiesSystemRamToAicaRamAndRaisesEvent()
    {
        var memory = new DreamcastMemory();
        for (uint offset = 0; offset < 32; offset += 4)
        {
            memory.WriteUInt32(0x8C01_0000 + offset, 0xA500_0000u + offset);
        }

        memory.WriteUInt32(0xA05F_6920, 1u << 15);
        memory.WriteUInt32(0xA05F_7800, 0x0080_0040);
        memory.WriteUInt32(0xA05F_7804, 0x0C01_0000);
        memory.WriteUInt32(0xA05F_7808, 1);
        memory.WriteUInt32(0xA05F_780C, 0);
        memory.WriteUInt32(0xA05F_7814, 1);

        memory.WriteUInt32(0xA05F_7818, 1);

        for (uint offset = 0; offset < 32; offset += 4)
        {
            Assert.Equal(0xA500_0000u + offset, memory.ReadUInt32(0x0080_0040 + offset));
        }

        Assert.Equal(0u, memory.ReadUInt32(0xA05F_7818));
        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0360u, eventCode);
        Assert.Equal(11, level);
        var registerA = Assert.Single(memory.CreateAsicSnapshot().EventRegisters, register => register.Name == "A");
        Assert.Equal(1u << 15, registerA.Ack);
        Assert.Equal(1u << 15, registerA.PendingIrqB);
    }

    [Fact]
    public void G2AicaDmaCopiesAicaRamToSystemRam()
    {
        var memory = new DreamcastMemory();
        for (uint offset = 0; offset < 32; offset += 4)
        {
            memory.WriteUInt32(0x0080_0100 + offset, 0x5A00_0000u + offset);
        }

        memory.WriteUInt32(0xA05F_7800, 0x0080_0100);
        memory.WriteUInt32(0xA05F_7804, 0x0C02_0000);
        memory.WriteUInt32(0xA05F_7808, 1);
        memory.WriteUInt32(0xA05F_780C, 1);
        memory.WriteUInt32(0xA05F_7814, 1);

        memory.WriteUInt32(0xA05F_7818, 1);

        for (uint offset = 0; offset < 32; offset += 4)
        {
            Assert.Equal(0x5A00_0000u + offset, memory.ReadUInt32(0x8C02_0000 + offset));
        }

        Assert.Equal(0u, memory.ReadUInt32(0xA05F_7818));
    }

    [Fact]
    public void AdvanceHardwareProcessesAicaCommandQueueChannelStart()
    {
        var memory = new DreamcastMemory();
        InitializeAicaCommandQueue(memory, head: 24 * 4);
        WriteAicaCommandHeader(memory, 0, sizeDwords: 24, command: 2, timestamp: 0, commandId: 3);

        var channelData = 0x0081_0018u + (8 * 4);
        memory.WriteUInt32(channelData, 1);
        memory.WriteUInt32(channelData + 4, 0x0003_0000);
        memory.WriteUInt32(channelData + 8, 0);
        memory.WriteUInt32(channelData + 12, 0x1234);
        memory.WriteUInt32(channelData + 16, 1);
        memory.WriteUInt32(channelData + 20, 0x10);
        memory.WriteUInt32(channelData + 24, 0x40);
        memory.WriteUInt32(channelData + 28, 44100);
        memory.WriteUInt32(channelData + 32, 0x80);
        memory.WriteUInt32(channelData + 36, 0x40);
        memory.WriteUInt32(channelData + 40, 0x55);

        memory.AdvanceHardware(1);

        Assert.Equal(24u * 4, memory.ReadUInt32(0x0081_0004));

        var channel = 0x0082_0000u + (3 * 16u * 4u);
        Assert.Equal(1u, memory.ReadUInt32(channel));
        Assert.Equal(0x0003_0000u, memory.ReadUInt32(channel + 4));
        Assert.Equal(0x1234u, memory.ReadUInt32(channel + 12));
        Assert.Equal(44100u, memory.ReadUInt32(channel + 28));
        Assert.Equal(0x80u, memory.ReadUInt32(channel + 32));
        Assert.Equal(0x40u, memory.ReadUInt32(channel + 36));
        Assert.Equal(0u, memory.ReadUInt32(channel + 40));

        var activity = Assert.Single(memory.CreateAudioSnapshot().CommandQueueActivities);
        Assert.Equal("ChannelStart", activity.Result);
        Assert.Equal("Channel", activity.CommandName);
        Assert.Equal(3u, activity.CommandId);
        Assert.Equal(24u, activity.SizeDwords);
        Assert.Equal(0u, activity.Tail);
        Assert.Equal(24u * 4, activity.NextTail);
    }

    [Fact]
    public void AdvanceHardwareDefersAicaCommandQueueTimestampUntilClockPasses()
    {
        var memory = new DreamcastMemory();
        InitializeAicaCommandQueue(memory, head: 8 * 4);
        WriteAicaCommandHeader(memory, 0, sizeDwords: 8, command: 3, timestamp: 10, commandId: 0);

        memory.AdvanceHardware(1);

        Assert.Equal(0u, memory.ReadUInt32(0x0081_0004));
        var deferred = Assert.Single(memory.CreateAudioSnapshot().CommandQueueActivities);
        Assert.Equal("DeferredTimestamp", deferred.Result);
        Assert.Equal("SyncClock", deferred.CommandName);
        Assert.Equal(0u, deferred.Tail);
        Assert.Equal(0u, deferred.NextTail);

        memory.WriteUInt32(0x0082_1000, 11);
        memory.AdvanceHardware(1);

        Assert.Equal(8u * 4, memory.ReadUInt32(0x0081_0004));
        Assert.Equal(0u, memory.ReadUInt32(0x0082_1000));
        var activities = memory.CreateAudioSnapshot().CommandQueueActivities;
        Assert.Equal(2, activities.Count);
        Assert.Equal("SyncClock", activities[1].Result);
        Assert.Equal(8u * 4, activities[1].NextTail);
    }

    [Fact]
    public void AdvanceHardwareAdvancesAicaClockForValidCommandQueue()
    {
        var memory = new DreamcastMemory();
        InitializeAicaCommandQueue(memory, head: 8 * 4);
        WriteAicaCommandHeader(memory, 0, sizeDwords: 8, command: 3, timestamp: 10, commandId: 0);

        memory.AdvanceHardware(200_000 * 10UL);

        Assert.Equal(10u, memory.ReadUInt32(0x0082_1000));
        Assert.Equal(0u, memory.ReadUInt32(0x0081_0004));
        var deferred = Assert.Single(memory.CreateAudioSnapshot().CommandQueueActivities);
        Assert.Equal("DeferredTimestamp", deferred.Result);

        memory.AdvanceHardware(200_000);

        Assert.Equal(8u * 4, memory.ReadUInt32(0x0081_0004));
        Assert.Equal(0u, memory.ReadUInt32(0x0082_1000));
        var activities = memory.CreateAudioSnapshot().CommandQueueActivities;
        Assert.Equal(2, activities.Count);
        Assert.Equal("SyncClock", activities[1].Result);
    }

    [Fact]
    public void AdvanceHardwareDoesNotAdvanceAicaClockBeforeQueueIsValid()
    {
        var memory = new DreamcastMemory();

        memory.AdvanceHardware(200_000 * 5UL);

        Assert.Equal(0u, memory.ReadUInt32(0x0082_1000));
    }

    [Fact]
    public void AdvanceHardwareReportsUnknownAicaCommandQueuePackets()
    {
        var memory = new DreamcastMemory();
        InitializeAicaCommandQueue(memory, head: 8 * 4);
        WriteAicaCommandHeader(memory, 0, sizeDwords: 8, command: 0xAA55_0001, timestamp: 0, commandId: 0x1234);

        memory.AdvanceHardware(1);

        Assert.Equal(8u * 4, memory.ReadUInt32(0x0081_0004));
        var activity = Assert.Single(memory.CreateAudioSnapshot().CommandQueueActivities);
        Assert.Equal("UnknownCommand", activity.Result);
        Assert.Equal("Command_AA550001", activity.CommandName);
        Assert.Equal(0xAA55_0001u, activity.Command);
        Assert.Equal(0x1234u, activity.CommandId);
        Assert.Equal(8u, activity.SizeDwords);
        Assert.Equal(8u * 4, activity.SizeBytes);
    }

    [Fact]
    public void AudioSnapshotReportsAicaCommandQueueCandidates()
    {
        var memory = new DreamcastMemory();
        InitializeAicaCommandQueue(memory, head: 0x20);

        var queue = Assert.Single(memory.CreateAudioSnapshot().CommandQueues);
        Assert.Equal(0x0001_0000u, queue.Offset);
        Assert.Equal(0x0001_0018u, queue.Data);
        Assert.Equal(0x100u, queue.Size);
        Assert.Equal(0x20u, queue.Head);
        Assert.Equal(0u, queue.Tail);
        Assert.True(queue.Valid);
        Assert.True(queue.ProcessOk);
        Assert.True(queue.Pending);
    }

    [Fact]
    public void AudioSnapshotReportsNonstandardAicaCommandQueueCandidates()
    {
        var memory = new DreamcastMemory();
        WriteAicaQueue(memory, offset: 0x0001_2000, head: 0x20, tail: 0, size: 0x80, valid: 1, processOk: 1, data: 0x0001_3000);

        var queue = Assert.Single(memory.CreateAudioSnapshot().CommandQueues);
        Assert.Equal(0x0001_2000u, queue.Offset);
        Assert.Equal(0x0001_3000u, queue.Data);
        Assert.Equal(0x80u, queue.Size);
        Assert.True(queue.Pending);
    }

    [Fact]
    public void AudioSnapshotIgnoresInvalidAicaCommandQueueCandidates()
    {
        var memory = new DreamcastMemory();
        WriteAicaQueue(memory, offset: 0x0001_2000, head: 0x20, tail: 0, size: 0x80, valid: 1, processOk: 1, data: 0x0020_0000);

        Assert.Empty(memory.CreateAudioSnapshot().CommandQueues);
    }

    [Fact]
    public void AudioSnapshotReportsAicaRamTextMarkers()
    {
        var memory = new DreamcastMemory();
        WriteAicaText(memory, 0x0000_0400, "AM2/AICA soundDrv 990902/Ver1.76");

        var marker = Assert.Single(memory.CreateAudioSnapshot().TextMarkers);
        Assert.Equal(0x0000_0400u, marker.Offset);
        Assert.Equal("0x000400", marker.OffsetHex);
        Assert.Equal(32, marker.Length);
        Assert.Equal("AM2/AICA soundDrv 990902/Ver1.76", marker.Text);
    }

    [Fact]
    public void AudioSnapshotIgnoresShortAicaRamTextRuns()
    {
        var memory = new DreamcastMemory();
        WriteAicaText(memory, 0x0000_0400, "AICA");
        WriteAicaText(memory, 0x0000_0500, "12345678");

        Assert.Empty(memory.CreateAudioSnapshot().TextMarkers);
    }

    [Fact]
    public void VideoSnapshotReportsVramChanges()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt16(0xA500_0000, 0xF800);
        memory.WriteUInt16(0xA500_0002, 0x07E0);

        var snapshot = memory.CreateVideoSnapshot();

        Assert.Equal(memory.PvrVramBytes, snapshot.VramBytes);
        Assert.Equal(3UL, snapshot.NonZeroBytes);
        Assert.Equal(1u, snapshot.FirstNonZeroOffset);
        Assert.Equal("0xF800", Assert.Single(snapshot.Samples, sample => sample.Name == "origin").Rgb565Hex);
        Assert.NotEqual("0x00000000", snapshot.Fnv1A32Hex);
    }

    [Fact]
    public void VideoSnapshotReportsNamedPvrRegisterAccesses()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xA05F_8044, 0x0080_0000);
        var readBack = memory.ReadUInt32(0xA05F_8044);

        var snapshot = memory.CreateVideoSnapshot();

        Assert.Equal(0x0080_0000u, readBack);
        Assert.Collection(
            snapshot.PvrRegisterAccesses,
            access =>
            {
                Assert.Equal(MemoryAccessKind.Write, access.Kind);
                Assert.Equal("PVR_FB_CFG_1", access.Name);
                Assert.Equal("0x0044", access.OffsetHex);
                Assert.Equal("0x00800000", access.ValueHex);
            },
            access =>
            {
                Assert.Equal(MemoryAccessKind.Read, access.Kind);
                Assert.Equal("PVR_FB_CFG_1", access.Name);
                Assert.Equal("0x00800000", access.ValueHex);
            });
    }

    [Fact]
    public void VideoSummaryDecodesPvrDisplayRegisters()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xA05F_8050, 0x0060_0000);
        memory.WriteUInt32(0xA05F_8054, 0x0068_0000);
        memory.WriteUInt32(0xA05F_8060, 0x0070_0000);
        memory.WriteUInt32(0xA05F_8064, 0x0078_0000);
        memory.WriteUInt32(0xA05F_8068, (639u << 16) | 0u);
        memory.WriteUInt32(0xA05F_806C, (479u << 16) | 0u);
        memory.WriteUInt32(0xA05F_805C, 0x013F_01DF);
        memory.WriteUInt32(0xA05F_80EC, 0x0000_0280);
        memory.WriteUInt32(0xA05F_80F0, 0x0000_01E0);
        memory.WriteUInt32(0xA05F_8044, 0x0080_0000);
        memory.WriteUInt32(0xA05F_8048, 0x0000_0001);
        memory.WriteUInt32(0xA05F_80E8, 0x0000_0003);
        memory.WriteUInt32(0xA05F_80F4, 0x0000_0400);
        memory.WriteUInt32(0xA05F_8108, 0x0000_0002);

        var display = DreamcastVideoSummary.FromSnapshot(memory.CreateVideoSnapshot()).PvrDisplay;

        Assert.True(display.HasConfiguredState);
        Assert.Equal("0x600000", display.FramebufferAddressHex);
        Assert.Equal("0x680000", display.InterlacedFramebufferAddressHex);
        Assert.Equal("0x700000", display.RenderAddressHex);
        Assert.Equal("0x780000", display.AlternateRenderAddressHex);
        Assert.Equal("0-639", display.PixelClipX?.Display);
        Assert.Equal("0-479", display.PixelClipY?.Display);
        Assert.Equal("0x013F01DF", display.FramebufferSizeHex);
        Assert.Equal("0x00000280", display.BitmapXHex);
        Assert.Equal("0x000001E0", display.BitmapYHex);
        Assert.Equal("0x00800000", display.FramebufferConfig1Hex);
        Assert.Equal("0x00000001", display.FramebufferConfig2Hex);
        Assert.Equal("0x00000003", display.VideoConfigHex);
        Assert.Equal("0x00000400", display.ScalerConfigHex);
        Assert.Equal("0x00000002", display.PaletteConfigHex);
    }

    [Fact]
    public void PvrIdentityRegistersReportDreamcastValues()
    {
        var memory = new DreamcastMemory();

        Assert.Equal(0x17FD_11DBu, memory.ReadUInt32(0xA05F_8000));
        Assert.Equal(0x0000_0011u, memory.ReadUInt32(0xA05F_8004));

        Assert.Collection(
            memory.CreateVideoSnapshot().PvrRegisterAccesses,
            access =>
            {
                Assert.Equal(MemoryAccessKind.Read, access.Kind);
                Assert.Equal("PVR_ID", access.Name);
                Assert.Equal("0x17FD11DB", access.ValueHex);
            },
            access =>
            {
                Assert.Equal(MemoryAccessKind.Read, access.Kind);
                Assert.Equal("PVR_REVISION", access.Name);
                Assert.Equal("0x00000011", access.ValueHex);
            });
    }

    [Fact]
    public void PvrSyncStatusReportsVBlankWindow()
    {
        var memory = new DreamcastMemory();

        Assert.Equal(0u, memory.ReadUInt32(0xA05F_810C) & 0x3FFu);
        Assert.Equal(0u, memory.ReadUInt32(0xA05F_810C) & 0x2000u);

        memory.RaiseVBlankBegin();

        Assert.NotEqual(0u, memory.ReadUInt32(0xA05F_810C) & 0x3FFu);
        Assert.NotEqual(0u, memory.ReadUInt32(0xA05F_810C) & 0x2000u);

        memory.AdvanceHardware(128);

        Assert.Equal(0u, memory.ReadUInt32(0xA05F_810C) & 0x3FFu);
        Assert.Equal(0u, memory.ReadUInt32(0xA05F_810C) & 0x2000u);
    }

    [Fact]
    public void VideoSnapshotReportsPvrTaCommandWrites()
    {
        var memory = new DreamcastMemory();

        memory.CurrentInstructionPc = 0x8C10_07D6;
        memory.WriteUInt32(0x1000_0000, 0x8084_0000);
        memory.CurrentInstructionPc = null;
        memory.WriteUInt32(0x1080_0000, 0x0000_0001);

        var snapshot = memory.CreateVideoSnapshot();

        Assert.Collection(
            snapshot.PvrTaCommandWrites,
            write =>
            {
                Assert.Equal("TA_INPUT", write.Region);
                Assert.Equal("PolygonHeader", write.Kind);
                Assert.Equal("OpaquePolygon", write.ListTypeName);
                Assert.Equal("0x10000000", write.AddressHex);
                Assert.Equal("0x80840000", write.ValueHex);
                Assert.Equal(0x8C10_07D6u, write.InstructionPc);
                Assert.Equal("0x8C1007D6", write.InstructionPcHex);
            },
            write =>
            {
                Assert.Equal("TA_YUV_CONV", write.Region);
                Assert.Equal("YuvConverterData", write.Kind);
                Assert.Equal("0x10800000", write.AddressHex);
                Assert.Null(write.InstructionPc);
                Assert.Null(write.InstructionPcHex);
            });
    }

    [Fact]
    public void StoreQueuePrefetchFlushesQacr0DestinationToPvrTa()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xFF00_0038, 0x10);
        memory.WriteUInt32(0xE000_0000, 0x8084_0000);
        memory.WriteUInt32(0xE000_0004, 0x0600_0000);

        memory.Prefetch(0xE000_0000);

        var snapshot = memory.CreateVideoSnapshot();
        Assert.Equal(8, snapshot.PvrTaCommandWrites.Count);
        Assert.Equal("0x10000000", snapshot.PvrTaCommandWrites[0].AddressHex);
        Assert.Equal("0x80840000", snapshot.PvrTaCommandWrites[0].ValueHex);
        Assert.Equal("PolygonHeader", snapshot.PvrTaCommandWrites[0].Kind);
        Assert.Equal("0x10000004", snapshot.PvrTaCommandWrites[1].AddressHex);
        Assert.Equal("0x06000000", snapshot.PvrTaCommandWrites[1].ValueHex);

        var flush = Assert.Single(snapshot.StoreQueueFlushes);
        Assert.Equal(0, flush.QueueIndex);
        Assert.Equal("0xE0000000", flush.SourceAddressHex);
        Assert.Equal("0x10000000", flush.DestinationAddressHex);
        Assert.Equal("0xFF000038", flush.QacrAddressHex);
        Assert.Equal("0x00000010", flush.QacrValueHex);
        Assert.Equal("0x80840000", flush.WordHex[0]);
        Assert.Equal("0x06000000", flush.WordHex[1]);
    }

    [Fact]
    public void StoreQueuePrefetchUsesQacr1ForSecondQueue()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xFF00_003C, 0x10);
        memory.WriteUInt32(0xE000_0020, 0xE000_0000);

        memory.Prefetch(0xE000_0020);

        var snapshot = memory.CreateVideoSnapshot();
        Assert.Equal("0x10000020", snapshot.PvrTaCommandWrites[0].AddressHex);
        Assert.Equal("Vertex", snapshot.PvrTaCommandWrites[0].Kind);
        Assert.Equal("0xFF00003C", Assert.Single(snapshot.StoreQueueFlushes).QacrAddressHex);
    }

    [Fact]
    public void StoreQueuePrefetchUsesTaFallbackWhenQacrAreaIsZero()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xFF00_0038, 0);
        memory.WriteUInt32(0xE000_3340, 0xF000_0000);

        memory.Prefetch(0xE000_3340);

        var write = memory.CreateVideoSnapshot().PvrTaCommandWrites[0];
        Assert.Equal("0x10003340", write.AddressHex);
        Assert.Equal("VertexEndOfStrip", write.Kind);
    }

    [Fact]
    public void VideoSummaryGroupsPvrTaWritesByRegionAndList()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0x1000_0000, 0x8084_0000);
        memory.WriteUInt32(0x1000_0000, 0xE000_0000);
        memory.WriteUInt32(0x1000_0000, 0xF000_0000);
        memory.WriteUInt32(0x1080_0000, 0x0000_0001);

        var summary = DreamcastVideoSummary.FromSnapshot(memory.CreateVideoSnapshot());

        Assert.Collection(
            summary.PvrTaLists,
            list =>
            {
                Assert.Equal("TA_INPUT", list.Region);
                Assert.Equal(0, list.ListType);
                Assert.Equal("OpaquePolygon", list.ListTypeName);
                Assert.Equal(3, list.CommandCount);
                Assert.Equal(1, list.PolygonHeaderCount);
                Assert.Equal(1, list.VertexCount);
                Assert.Equal(1, list.VertexEndOfStripCount);
            },
            list =>
            {
                Assert.Equal("TA_YUV_CONV", list.Region);
                Assert.Null(list.ListType);
                Assert.Null(list.ListTypeName);
                Assert.Equal(1, list.CommandCount);
                Assert.Equal(0, list.PolygonHeaderCount);
                Assert.Equal(0, list.VertexCount);
                Assert.Equal(0, list.VertexEndOfStripCount);
            });
        Assert.Collection(
            summary.RecentPvrTaStreamWrites,
            write =>
            {
                Assert.Equal("Control", write.Role);
                Assert.Equal("PolygonHeader", write.ControlKind);
                Assert.Equal(7, write.PayloadWordsRemaining);
            },
            write =>
            {
                Assert.Equal("Payload", write.Role);
                Assert.Equal("PolygonHeader", write.ControlKind);
                Assert.Equal(0, write.PayloadWordIndex);
                Assert.Equal(6, write.PayloadWordsRemaining);
                Assert.Equal("Mode1", write.PayloadWordName);
            },
            write =>
            {
                Assert.Equal("Payload", write.Role);
                Assert.Equal("PolygonHeader", write.ControlKind);
                Assert.Equal(1, write.PayloadWordIndex);
                Assert.Equal("Mode2", write.PayloadWordName);
            },
            write =>
            {
                Assert.Equal("Control", write.Role);
                Assert.Equal("YuvConverterData", write.ControlKind);
                Assert.Equal(0, write.PayloadWordsRemaining);
            });
        Assert.Collection(
            summary.RecentPvrTaParameterHeaders,
            header =>
            {
                Assert.Equal("PolygonHeader", header.Kind);
                Assert.Equal(4, header.ParameterType);
                Assert.Equal("OpaquePolygon", header.ListTypeName);
                Assert.Equal(7, header.ExpectedPayloadWords);
                Assert.True(header.HasKnownPayloadLength);
                Assert.Equal("ArgbPacked", header.PolygonHeaderCommand?.ColorFormatName);
                Assert.Equal("Strip2", header.PolygonHeaderCommand?.StripLengthName);
                Assert.True(header.PolygonHeaderCommand?.AutoStripLength);
            },
            header =>
            {
                Assert.Equal("Vertex", header.Kind);
                Assert.False(header.EndOfStrip);
            },
            header =>
            {
                Assert.Equal("VertexEndOfStrip", header.Kind);
                Assert.True(header.EndOfStrip);
            },
            header =>
            {
                Assert.Equal("YuvConverterData", header.Kind);
                Assert.Equal(0, header.ExpectedPayloadWords);
                Assert.True(header.HasKnownPayloadLength);
            });
    }

    [Fact]
    public void KnownOpaqueTaPolygonWritesVisiblePreviewPixels()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0x1000_0000, 0x8084_0000);
        WritePvrVertexPacket(memory, endOfStrip: false, x: 1, y: 1, color: 0xF800);
        WritePvrVertexPacket(memory, endOfStrip: false, x: 2, y: 1, color: 0xF800);
        WritePvrVertexPacket(memory, endOfStrip: true, x: 1, y: 2, color: 0xF800);

        var snapshot = memory.CreateVideoSnapshot();

        Assert.True(snapshot.NonZeroBytes >= 3);
        Assert.Equal(0x0000, snapshot.Samples.Single(sample => sample.Name == "origin").Rgb565);
        Assert.Equal(0x0000, snapshot.Samples.Single(sample => sample.Name == "pixel_1_0").Rgb565);
        Assert.Equal(0x0000, snapshot.Samples.Single(sample => sample.Name == "pixel_0_1_320x240").Rgb565);
        Assert.Equal(0xF800, ReadPreviewRgb565(snapshot.Vram, 1, 1));
        Assert.Equal(0xF800, ReadPreviewRgb565(snapshot.Vram, 2, 1));
        Assert.Equal(0xF800, ReadPreviewRgb565(snapshot.Vram, 1, 2));
        Assert.Equal(0x0000, snapshot.Samples.Single(sample => sample.Name == "pixel_1_1_320x240").Rgb565);
        var strip = Assert.Single(snapshot.PvrTaStrips);
        Assert.Equal("OpaquePolygon", strip.ListTypeName);
        Assert.Equal(3, strip.Vertices.Count);
        Assert.Equal(0xF800, strip.Rgb565);
        Assert.Collection(
            strip.Vertices,
            vertex =>
            {
                Assert.Equal(1, vertex.X);
                Assert.Equal(1, vertex.Y);
                Assert.Equal("0xE0000000", vertex.ControlValueHex);
                Assert.Equal("0x0000F800", vertex.ColorValueHex);
            },
            vertex =>
            {
                Assert.Equal(2, vertex.X);
                Assert.Equal(1, vertex.Y);
            },
            vertex =>
            {
                Assert.Equal(1, vertex.X);
                Assert.Equal(2, vertex.Y);
                Assert.True(vertex.EndOfStrip);
            });
    }

    [Fact]
    public void WiderOpaqueTaPolygonCoversSecondPreviewColumn()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0x1000_0000, 0x8084_0000);
        WritePvrVertexPacket(memory, endOfStrip: false, x: 1, y: 1, color: 0x07E0);
        WritePvrVertexPacket(memory, endOfStrip: false, x: 3, y: 1, color: 0x07E0);
        WritePvrVertexPacket(memory, endOfStrip: true, x: 1, y: 2, color: 0x07E0);

        var snapshot = memory.CreateVideoSnapshot();

        Assert.Equal(0x0000, snapshot.Samples.Single(sample => sample.Name == "origin").Rgb565);
        Assert.Equal(0x0000, snapshot.Samples.Single(sample => sample.Name == "pixel_1_0").Rgb565);
        Assert.Equal(0x0000, snapshot.Samples.Single(sample => sample.Name == "pixel_2_0").Rgb565);
        Assert.Equal(0x0000, snapshot.Samples.Single(sample => sample.Name == "pixel_0_1_320x240").Rgb565);
        Assert.Equal(0x07E0, ReadPreviewRgb565(snapshot.Vram, 1, 1));
        Assert.Equal(0x07E0, ReadPreviewRgb565(snapshot.Vram, 2, 1));
        Assert.Equal(0x07E0, ReadPreviewRgb565(snapshot.Vram, 3, 1));
        Assert.Equal(0x07E0, ReadPreviewRgb565(snapshot.Vram, 1, 2));
        Assert.Equal(0x0000, ReadPreviewRgb565(snapshot.Vram, 2, 2));
        var strip = Assert.Single(snapshot.PvrTaStrips);
        Assert.Equal(0x07E0, strip.Rgb565);
        Assert.Equal(3, strip.Vertices[1].X);
    }

    [Fact]
    public void KnownSpriteWritesVisiblePreviewPixels()
    {
        var memory = new DreamcastMemory();

        WritePvrSpritePacket(memory, instructionPcBase: 0x8C10_07D6);

        var snapshot = memory.CreateVideoSnapshot();

        Assert.True(snapshot.NonZeroBytes >= 9);
        Assert.Equal(0x0000, snapshot.Samples.Single(sample => sample.Name == "origin").Rgb565);
        Assert.Equal(0x0000, snapshot.Samples.Single(sample => sample.Name == "pixel_1_0").Rgb565);
        Assert.Equal(0x0000, snapshot.Samples.Single(sample => sample.Name == "pixel_2_0").Rgb565);
        Assert.Equal(0x0000, snapshot.Samples.Single(sample => sample.Name == "pixel_0_1_320x240").Rgb565);
        Assert.Equal(0x0000, snapshot.Samples.Single(sample => sample.Name == "pixel_1_1_320x240").Rgb565);
        Assert.Equal(0x07E0, snapshot.Samples.Single(sample => sample.Name == "pixel_2_2_320x240").Rgb565);
        var sprite = Assert.Single(snapshot.PvrTaSprites);
        Assert.Equal("OpaquePolygon", sprite.ListTypeName);
        Assert.Equal("0xFF00FF00", sprite.HeaderPayload.ArgbHex);
        Assert.Equal(0x07E0, sprite.Rgb565);
        Assert.Equal(4, sprite.Vertices.Count);
        Assert.Equal(3, sprite.Vertices[3].X);
        Assert.Equal(3, sprite.Vertices[3].Y);
        Assert.Equal(0x8C10_07D6u, sprite.HeaderInstructionPc);
        Assert.Equal("0x8C1007D6", sprite.HeaderInstructionPcHex);
        Assert.Equal(0x8C10_07E6u, sprite.ControlInstructionPc);
        Assert.Equal("0x8C1007E6", sprite.ControlInstructionPcHex);
        Assert.Equal(0x8C10_07E8u, sprite.FirstPayloadInstructionPc);
        Assert.Equal("0x8C1007E8", sprite.FirstPayloadInstructionPcHex);
        Assert.Equal(0x8C10_0804u, sprite.LastPayloadInstructionPc);
        Assert.Equal("0x8C100804", sprite.LastPayloadInstructionPcHex);
        Assert.Equal(1, snapshot.PvrPreviewRenderStats.SpriteCalls);
        Assert.True(snapshot.PvrPreviewRenderStats.PixelWriteAttempts >= snapshot.PvrPreviewRenderStats.PixelsWritten);
        Assert.True(snapshot.PvrPreviewRenderStats.PixelsWritten > 0);
        Assert.True(snapshot.PvrPreviewRenderStats.UniquePixelsWritten > 0);
        Assert.True(snapshot.PvrPreviewRenderStats.UniquePixelsWritten <= snapshot.PvrPreviewRenderStats.PixelsWritten);
        Assert.Equal(0, snapshot.PvrPreviewRenderStats.ZeroRgbWritePixels);
    }

    [Fact]
    public void PvrTaPreviewRendersIntoConfiguredRenderTarget()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xA05F_8060, 0x20);
        WritePvrSpritePacket(memory);

        var snapshot = memory.CreateVideoSnapshot();

        Assert.Equal(0x0000, ReadPreviewRgb565(snapshot.Vram, 2, 2));
        Assert.Equal(0x07E0, ReadPreviewRgb565(snapshot.Vram, 0x20, 2, 2));
    }

    private static void WritePvrVertexPacket(DreamcastMemory memory, bool endOfStrip, int x, int y, ushort color)
    {
        memory.WriteUInt32(0x1000_0000, endOfStrip ? 0xF000_0000 : 0xE000_0000);
        memory.WriteUInt32(0x1000_0000, (uint)x << 16);
        memory.WriteUInt32(0x1000_0000, (uint)y << 16);
        memory.WriteUInt32(0x1000_0000, color);
    }

    private static void WritePvrSpritePacket(DreamcastMemory memory, uint? instructionPcBase = null)
    {
        uint[] words =
        [
            0xA084_0000,
            0x0000_0000,
            0x0000_0000,
            0x0000_0000,
            0xFF00_FF00,
            0x0000_0000,
            0x0000_0000,
            0x0000_0000,
            0xF000_0000,
            0x3F80_0000,
            0x3F80_0000,
            0x3F80_0000,
            0x4040_0000,
            0x3F80_0000,
            0x3F80_0000,
            0x3F80_0000,
            0x4040_0000,
            0x3F80_0000,
            0x4040_0000,
            0x4040_0000,
            0x0000_0000,
            0x0000_0000,
            0x0000_0000,
            0x0000_0000
        ];

        for (var index = 0; index < words.Length; index++)
        {
            memory.CurrentInstructionPc = instructionPcBase + (uint)(index * 2);
            memory.WriteUInt32(0x1000_0000, words[index]);
        }

        memory.CurrentInstructionPc = null;
    }

    [Fact]
    public void AudioSnapshotReportsAicaRegisterAndChannelState()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xA070_0000, 0x0000_C000);
        memory.WriteUInt32(0xA070_0004, 0x0000_1234);
        memory.WriteUInt32(0xA070_0008, 0x0000_0008);
        memory.WriteUInt32(0xA070_000C, 0x0000_0040);
        memory.WriteUInt32(0xA070_0018, 0x0000_1AC0);
        memory.Write(0xA070_0024, [0x0F]);
        memory.Write(0xA070_0029, [0x40]);

        var snapshot = memory.CreateAudioSnapshot();

        Assert.True(snapshot.RegisterAccesses.Count >= 7);
        Assert.Contains(snapshot.Registers, register => register.Name == "AICA_CH0_CONTROL" && register.ValueHex == "0x0000C000" && register.Channel == 0);
        Assert.Contains(snapshot.RegisterAccesses, access => access.Name == "AICA_CH0_CONTROL" && access.ValueHex == "0x0000C000");
        var channel = Assert.Single(snapshot.Channels);
        Assert.Equal(0, channel.Channel);
        Assert.Equal("Pcm16", channel.SampleFormat);
        Assert.False(channel.Compressed);
        Assert.False(channel.Streamed);
        Assert.False(channel.LoopEnabled);
        Assert.Equal(0x1234u, channel.SampleAddress);
        Assert.True(channel.KeyOn);
        Assert.True(channel.KeyOnExecute);
        Assert.True(channel.Active);
        Assert.Equal(2, channel.SampleStrideBytes);
        Assert.Equal(0UL, channel.PlaybackPosition);
        Assert.Equal(0UL, channel.PlaybackBytePosition);
        Assert.Equal(0UL, channel.PlaybackSamplesAdvanced);
        Assert.Equal(0UL, channel.PlaybackBytesAdvanced);
        Assert.False(channel.PlaybackStoppedAtLoopEnd);
        Assert.Equal(0x1234u, channel.SampleAddressLow);
        Assert.Equal(0x1AC0u, channel.Pitch);
        Assert.Equal(0x0F, channel.Pan);
        Assert.Equal(0, channel.PanSendLevel);
        Assert.Equal(15, channel.PanPosition);
        Assert.Equal(0, channel.LeftBalance);
        Assert.Equal(15, channel.RightBalance);
        Assert.Equal(0x40, channel.Volume);
    }

    [Fact]
    public void AudioSnapshotDecodesAicaPanSendBalance()
    {
        var memory = new DreamcastMemory();

        memory.Write(0xA070_0024, [0x3A]);

        var channel = Assert.Single(memory.CreateAudioSnapshot().Channels);

        Assert.Equal(0x3A, channel.Pan);
        Assert.Equal(3, channel.PanSendLevel);
        Assert.Equal(10, channel.PanPosition);
        Assert.Equal(5, channel.LeftBalance);
        Assert.Equal(10, channel.RightBalance);
    }

    [Fact]
    public void AdvanceHardwareTracksAicaPlaybackPositionAndStopsAtLoopEnd()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xA070_0008, 0x0000_0004);
        memory.WriteUInt32(0xA070_000C, 0x0000_0008);
        memory.WriteUInt32(0xA070_0000, 0x0000_C000);

        memory.AdvanceHardware(200_000);

        var channel = Assert.Single(memory.CreateAudioSnapshot().Channels);
        Assert.False(channel.Active);
        Assert.Equal(8UL, channel.PlaybackPosition);
        Assert.Equal(16UL, channel.PlaybackBytePosition);
        Assert.Equal(8UL, channel.PlaybackSamplesAdvanced);
        Assert.Equal(16UL, channel.PlaybackBytesAdvanced);
        Assert.True(channel.PlaybackStoppedAtLoopEnd);
    }

    [Fact]
    public void AdvanceHardwareLoopsAicaPlaybackPosition()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xA070_0008, 0x0000_0004);
        memory.WriteUInt32(0xA070_000C, 0x0000_0008);
        memory.WriteUInt32(0xA070_0000, 0x0000_C200);

        memory.AdvanceHardware(200_000);

        var channel = Assert.Single(memory.CreateAudioSnapshot().Channels);
        Assert.True(channel.Active);
        Assert.Equal(4UL, channel.PlaybackPosition);
        Assert.Equal(8UL, channel.PlaybackBytePosition);
        Assert.Equal(44UL, channel.PlaybackSamplesAdvanced);
        Assert.Equal(88UL, channel.PlaybackBytesAdvanced);
        Assert.False(channel.PlaybackStoppedAtLoopEnd);
    }

    [Fact]
    public void AdvanceHardwareReportsPcm8PlaybackByteCounters()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xA070_000C, 0x0000_0008);
        memory.WriteUInt32(0xA070_0000, 0x0000_C080);

        memory.AdvanceHardware(200_000);
        memory.WriteUInt32(0xA070_0000, 0x0000_8000);

        var channel = Assert.Single(memory.CreateAudioSnapshot().Channels);
        Assert.Equal(0x0000_8000u, channel.Control);
        Assert.Equal("Pcm8", channel.SampleFormat);
        Assert.Equal(1, channel.SampleStrideBytes);
        Assert.False(channel.Active);
        Assert.Equal(8UL, channel.PlaybackPosition);
        Assert.Equal(8UL, channel.PlaybackBytePosition);
        Assert.Equal(8UL, channel.PlaybackSamplesAdvanced);
        Assert.Equal(8UL, channel.PlaybackBytesAdvanced);
        Assert.True(channel.PlaybackStoppedAtLoopEnd);
    }

    [Fact]
    public void AdvanceHardwareTracksMultipleAicaChannels()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xA070_000C, 0x0000_0008);
        memory.Write(0xA070_0024, [0x10]);
        memory.Write(0xA070_0029, [0x30]);
        memory.WriteUInt32(0xA070_0000, 0x0000_C000);
        memory.WriteUInt32(0xA070_0084, 0x0000_0040);
        memory.WriteUInt32(0xA070_008C, 0x0000_0004);
        memory.Write(0xA070_00A4, [0x2F]);
        memory.Write(0xA070_00A9, [0x60]);
        memory.WriteUInt32(0xA070_0080, 0x0000_C000);

        memory.AdvanceHardware(200_000);

        var channels = memory.CreateAudioSnapshot().Channels;
        var channel0 = channels.Single(channel => channel.Channel == 0);
        var channel1 = channels.Single(channel => channel.Channel == 1);
        Assert.False(channel0.Active);
        Assert.False(channel1.Active);
        Assert.Equal(8UL, channel0.PlaybackPosition);
        Assert.Equal(4UL, channel1.PlaybackPosition);
        Assert.Equal(16UL, channel0.PlaybackBytePosition);
        Assert.Equal(8UL, channel1.PlaybackBytePosition);
        Assert.Equal(1, channel0.PanSendLevel);
        Assert.Equal(0, channel0.PanPosition);
        Assert.Equal(15, channel0.LeftBalance);
        Assert.Equal(0, channel0.RightBalance);
        Assert.Equal(0x30, channel0.Volume);
        Assert.Equal(2, channel1.PanSendLevel);
        Assert.Equal(15, channel1.PanPosition);
        Assert.Equal(0, channel1.LeftBalance);
        Assert.Equal(15, channel1.RightBalance);
        Assert.Equal(0x60, channel1.Volume);
        Assert.Equal(0x40u, channel1.SampleAddress);
    }

    [Fact]
    public void AdvanceHardwareTracksAdpcmPlaybackByNibbleAndPackedByte()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xA070_0008, 0x0000_0004);
        memory.WriteUInt32(0xA070_000C, 0x0000_0008);
        memory.WriteUInt32(0xA070_0000, 0x0000_C100);

        memory.AdvanceHardware(200_000);
        memory.WriteUInt32(0xA070_0000, 0x0000_8000);

        var channel = Assert.Single(memory.CreateAudioSnapshot().Channels);
        Assert.Equal(0x0000_8000u, channel.Control);
        Assert.Equal("Adpcm", channel.SampleFormat);
        Assert.True(channel.Compressed);
        Assert.False(channel.Streamed);
        Assert.Equal(0, channel.SampleStrideBytes);
        Assert.False(channel.Active);
        Assert.Equal(8UL, channel.PlaybackPosition);
        Assert.Equal(4UL, channel.PlaybackBytePosition);
        Assert.Equal(8UL, channel.PlaybackSamplesAdvanced);
        Assert.Equal(4UL, channel.PlaybackBytesAdvanced);
        Assert.True(channel.PlaybackStoppedAtLoopEnd);
    }

    [Fact]
    public void AdvanceHardwareLoopsAdpcmPlaybackAtSampleBoundary()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xA070_0008, 0x0000_0003);
        memory.WriteUInt32(0xA070_000C, 0x0000_0007);
        memory.WriteUInt32(0xA070_0000, 0x0000_C300);

        memory.AdvanceHardware(200_000);

        var channel = Assert.Single(memory.CreateAudioSnapshot().Channels);
        Assert.True(channel.Active);
        Assert.Equal("Adpcm", channel.SampleFormat);
        Assert.Equal(4UL, channel.PlaybackPosition);
        Assert.Equal(2UL, channel.PlaybackBytePosition);
        Assert.Equal(44UL, channel.PlaybackSamplesAdvanced);
        Assert.Equal(22UL, channel.PlaybackBytesAdvanced);
        Assert.False(channel.PlaybackStoppedAtLoopEnd);
    }

    [Fact]
    public void AdvanceHardwareWrapsAicaPlaybackAtExactLoopBoundary()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xA070_0008, 0x0000_0004);
        memory.WriteUInt32(0xA070_000C, 0x0000_0008);
        memory.WriteUInt32(0xA070_0000, 0x0000_C200);

        memory.AdvanceHardware(36_282);

        var channel = Assert.Single(memory.CreateAudioSnapshot().Channels);
        Assert.True(channel.Active);
        Assert.Equal(4UL, channel.PlaybackPosition);
        Assert.Equal(8UL, channel.PlaybackBytePosition);
        Assert.Equal(8UL, channel.PlaybackSamplesAdvanced);
        Assert.Equal(16UL, channel.PlaybackBytesAdvanced);
        Assert.False(channel.PlaybackStoppedAtLoopEnd);
    }

    [Fact]
    public void AdvanceHardwareStopsAicaPlaybackWhenLoopRangeIsInvalid()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xA070_0008, 0x0000_0008);
        memory.WriteUInt32(0xA070_000C, 0x0000_0008);
        memory.WriteUInt32(0xA070_0000, 0x0000_C200);

        memory.AdvanceHardware(200_000);

        var channel = Assert.Single(memory.CreateAudioSnapshot().Channels);
        Assert.False(channel.Active);
        Assert.Equal(8UL, channel.PlaybackPosition);
        Assert.Equal(16UL, channel.PlaybackBytePosition);
        Assert.Equal(8UL, channel.PlaybackSamplesAdvanced);
        Assert.Equal(16UL, channel.PlaybackBytesAdvanced);
        Assert.True(channel.PlaybackStoppedAtLoopEnd);
    }

    [Theory]
    [InlineData(0x0000_0080u, "Pcm8", false)]
    [InlineData(0x0000_0100u, "Adpcm", false)]
    [InlineData(0x0000_0380u, "AdpcmLongStream", true)]
    public void AudioSnapshotDecodesAicaSampleFormatAndLoop(uint controlBits, string expectedFormat, bool expectedLoop)
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xA070_0000, controlBits);

        var channel = Assert.Single(memory.CreateAudioSnapshot().Channels);

        Assert.Equal(expectedFormat, channel.SampleFormat);
        Assert.Equal(expectedLoop, channel.LoopEnabled);
    }

    [Theory]
    [InlineData(0x0000_0000u, false, false, 2)]
    [InlineData(0x0000_0080u, false, false, 1)]
    [InlineData(0x0000_0100u, true, false, 0)]
    [InlineData(0x0000_0180u, true, true, 0)]
    public void AudioSnapshotReportsAicaCompressionMetadata(uint controlBits, bool expectedCompressed, bool expectedStreamed, int expectedStride)
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xA070_0000, controlBits);

        var channel = Assert.Single(memory.CreateAudioSnapshot().Channels);

        Assert.Equal(expectedCompressed, channel.Compressed);
        Assert.Equal(expectedStreamed, channel.Streamed);
        Assert.Equal(expectedStride, channel.SampleStrideBytes);
    }

    [Fact]
    public void MapsAicaSoundRam()
    {
        var memory = new DreamcastMemory();

        memory.Write(0xA080_0000, [0x12, 0x34, 0x56, 0x78]);

        Assert.Equal(0x7856_3412u, memory.ReadUInt32(0x0080_0000));
        Assert.Equal(4UL, memory.CreateAudioSnapshot().NonZeroBytes);
    }

    [Fact]
    public void TimerCounterUnderflowSetsControlFlag()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xFFD8_0014, 2);
        memory.WriteUInt32(0xFFD8_0018, 2);
        memory.WriteUInt16(0xFFD8_001C, 0);
        memory.Write(0xFFD8_0004, [0x02]);

        memory.AdvanceHardware(3);

        Assert.Equal(0x0100, memory.ReadUInt16(0xFFD8_001C));
    }

    [Fact]
    public void TimerUnderflowReportsPendingInternalInterruptWhenEnabled()
    {
        var memory = new DreamcastMemory();

        SetTimerPriority(memory, 0, 15);
        memory.WriteUInt32(0xFFD8_0008, 1);
        memory.WriteUInt32(0xFFD8_000C, 1);
        memory.WriteUInt16(0xFFD8_0010, 0x0020);
        memory.Write(0xFFD8_0004, [0x01]);

        memory.AdvanceHardware(2);

        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0400u, eventCode);
        Assert.Equal(15, level);
    }

    [Fact]
    public void TimerControlLowByteWritePreservesUnderflowFlag()
    {
        var memory = new DreamcastMemory();

        RaiseTimerUnderflow(memory, 0, 10);
        memory.Write(0xFFD8_0010, [0x20]);

        Assert.Equal(0x0120, memory.ReadUInt16(0xFFD8_0010));
        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0400u, eventCode);
        Assert.Equal(10, level);
    }

    [Fact]
    public void TimerControlLowByteWriteCanDisableInterruptWithoutClearingUnderflow()
    {
        var memory = new DreamcastMemory();

        RaiseTimerUnderflow(memory, 0, 10);
        memory.Write(0xFFD8_0010, [0x00]);

        Assert.Equal(0x0100, memory.ReadUInt16(0xFFD8_0010));
        Assert.False(memory.TryGetPendingExternalInterrupt(out _, out _));
    }

    [Fact]
    public void TimerControlHighByteWriteClearsUnderflowAndPreservesInterruptEnable()
    {
        var memory = new DreamcastMemory();

        RaiseTimerUnderflow(memory, 0, 10);
        memory.Write(0xFFD8_0011, [0x00]);

        Assert.Equal(0x0020, memory.ReadUInt16(0xFFD8_0010));
        Assert.False(memory.TryGetPendingExternalInterrupt(out _, out _));
    }

    [Fact]
    public void TimerControlWordWriteClearsUnderflowAndKeepsInterruptEnable()
    {
        var memory = new DreamcastMemory();

        RaiseTimerUnderflow(memory, 0, 10);
        memory.WriteUInt16(0xFFD8_0010, 0x0020);

        Assert.Equal(0x0020, memory.ReadUInt16(0xFFD8_0010));
        Assert.False(memory.TryGetPendingExternalInterrupt(out _, out _));
    }

    [Fact]
    public void TimerControlLongWriteUsesLowControlWordOnly()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xFFD8_0010, 0xFFFF_0020);

        var channel = Assert.Single(memory.CreateTimerSnapshot().Channels, channel => channel.Channel == 0);
        Assert.Equal(0x0020u, channel.Control);
        Assert.True(channel.InterruptEnabled);
        Assert.False(channel.UnderflowPending);
    }

    [Fact]
    public void TimerInterruptPriorityPrefersHighestPendingChannel()
    {
        var memory = new DreamcastMemory();

        RaiseTimerUnderflow(memory, 0, 4);
        RaiseTimerUnderflow(memory, 1, 12);
        RaiseTimerUnderflow(memory, 2, 8);

        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0420u, eventCode);
        Assert.Equal(12, level);
    }

    [Fact]
    public void TimerInterruptPriorityKeepsLowerChannelWhenPrioritiesTie()
    {
        var memory = new DreamcastMemory();

        RaiseTimerUnderflow(memory, 0, 7);
        RaiseTimerUnderflow(memory, 1, 7);

        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0400u, eventCode);
        Assert.Equal(7, level);
    }

    [Fact]
    public void TimerInterruptPriorityBeatsAsicWhenPriorityTies()
    {
        var memory = new DreamcastMemory();

        RaiseTimerUnderflow(memory, 0, 9);
        memory.WriteUInt32(0xA05F_6930, 1u << 3);
        memory.RaiseVBlankBegin();

        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0400u, eventCode);
        Assert.Equal(9, level);
    }

    [Fact]
    public void TimerSnapshotReportsChannelsAndPendingInterrupt()
    {
        var memory = new DreamcastMemory();

        RaiseTimerUnderflow(memory, 0, 10);
        memory.WriteUInt32(0xFFD8_0008, 0x1234);
        memory.WriteUInt32(0xFFD8_000C, 0x5678);
        memory.Write(0xFFD8_0004, [0x01]);

        var snapshot = memory.CreateTimerSnapshot();

        Assert.Equal(0x0400u, snapshot.PendingEventCode);
        Assert.Equal("0x0400", snapshot.PendingEventCodeHex);
        Assert.Equal(0, snapshot.PendingChannel);
        Assert.Equal(10, snapshot.PendingPriority);
        Assert.NotNull(snapshot.PendingInterrupt);
        Assert.Equal(0, snapshot.PendingInterrupt.Channel);
        Assert.Equal(10, snapshot.PendingInterrupt.Priority);

        var channel0 = Assert.Single(snapshot.Channels, channel => channel.Channel == 0);
        Assert.Equal(0x1234u, channel0.Constant);
        Assert.Equal("0x00001234", channel0.ConstantHex);
        Assert.Equal(0x5678u, channel0.Counter);
        Assert.Equal("0x00005678", channel0.CounterHex);
        Assert.Equal(0x0120u, channel0.Control);
        Assert.True(channel0.Running);
        Assert.True(channel0.UnderflowPending);
        Assert.True(channel0.InterruptEnabled);
        Assert.Equal(10, channel0.Priority);

        var channel1 = Assert.Single(snapshot.Channels, channel => channel.Channel == 1);
        Assert.Equal(0u, channel1.Control);
        Assert.False(channel1.Running);
    }

    [Fact]
    public void AsicInterruptPriorityBeatsLowerPriorityTimer()
    {
        var memory = new DreamcastMemory();

        RaiseTimerUnderflow(memory, 0, 8);
        memory.WriteUInt32(0xA05F_6930, 1u << 3);
        memory.RaiseVBlankBegin();

        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0320u, eventCode);
        Assert.Equal(9, level);
    }

    [Fact]
    public void RaisedVBlankReportsPendingIrq9WhenEnabled()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xA05F_6930, 1u << 3);
        memory.RaiseVBlankBegin();

        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0320u, eventCode);
        Assert.Equal(9, level);
    }

    [Fact]
    public void AsicSnapshotReportsEventRegistersAndPendingIrq()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xA05F_6930, 1u << 3);
        memory.WriteUInt32(0xA05F_6920, 1u << 12);
        memory.RaiseVBlankBegin();

        var snapshot = memory.CreateAsicSnapshot();

        Assert.Equal(0x0320u, snapshot.PendingEventCode);
        Assert.Equal("0x0320", snapshot.PendingEventCodeHex);
        Assert.Equal(9, snapshot.PendingLevel);
        Assert.NotNull(snapshot.PendingInterrupt);
        Assert.Equal("IRQ9", snapshot.PendingInterrupt.LevelName);
        Assert.Equal(0, snapshot.PendingInterrupt.RegisterIndex);
        Assert.Equal("A", snapshot.PendingInterrupt.RegisterName);
        Assert.Equal(3, snapshot.PendingInterrupt.Bit);
        Assert.Equal("0x00000008", snapshot.PendingInterrupt.BitMaskHex);
        var registerA = Assert.Single(snapshot.EventRegisters, register => register.Name == "A");
        Assert.Equal(1u << 3, registerA.Ack);
        Assert.Equal(1u << 3, registerA.Irq9Mask);
        Assert.Equal(1u << 12, registerA.IrqBMask);
        Assert.Equal(1u << 3, registerA.PendingIrq9);
        Assert.Equal(0u, registerA.PendingIrqB);
    }

    [Fact]
    public void AsicInterruptPriorityPrefersIrqDThenIrqBThenIrq9()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xA05F_6910, 1u << 3);
        memory.WriteUInt32(0xA05F_6920, 1u << 3);
        memory.WriteUInt32(0xA05F_6930, 1u << 3);
        memory.RaiseVBlankBegin();

        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x03A0u, eventCode);
        Assert.Equal(13, level);
        Assert.Equal("IRQD", memory.CreateAsicSnapshot().PendingInterrupt?.LevelName);
    }

    [Fact]
    public void AsicEventBanksReportAndClearIndependently()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xA05F_6924, 1u << 5);
        memory.WriteUInt32(0xA05F_6938, 1u << 7);
        memory.RaiseAsicEventForDiagnostics(0x0105);
        memory.RaiseAsicEventForDiagnostics(0x0207);

        var snapshot = memory.CreateAsicSnapshot();

        Assert.Equal("IRQB", snapshot.PendingInterrupt?.LevelName);
        Assert.Equal("B", snapshot.PendingInterrupt?.RegisterName);
        Assert.Equal(5, snapshot.PendingInterrupt?.Bit);
        Assert.Equal(0x0360u, snapshot.PendingEventCode);
        var registerA = Assert.Single(snapshot.EventRegisters, register => register.Name == "A");
        var registerB = Assert.Single(snapshot.EventRegisters, register => register.Name == "B");
        var registerC = Assert.Single(snapshot.EventRegisters, register => register.Name == "C");
        Assert.Equal(0u, registerA.Ack);
        Assert.Equal(1u << 5, registerB.Ack);
        Assert.Equal(1u << 5, registerB.PendingIrqB);
        Assert.Equal(1u << 7, registerC.Ack);
        Assert.Equal(1u << 7, registerC.PendingIrq9);

        memory.WriteUInt32(0xA05F_6904, 1u << 5);
        snapshot = memory.CreateAsicSnapshot();

        Assert.Equal("IRQ9", snapshot.PendingInterrupt?.LevelName);
        Assert.Equal("C", snapshot.PendingInterrupt?.RegisterName);
        Assert.Equal(7, snapshot.PendingInterrupt?.Bit);
        registerB = Assert.Single(snapshot.EventRegisters, register => register.Name == "B");
        registerC = Assert.Single(snapshot.EventRegisters, register => register.Name == "C");
        Assert.Equal(0u, registerB.Ack);
        Assert.Equal(1u << 7, registerC.Ack);

        memory.WriteUInt32(0xA05F_6908, 1u << 7);

        Assert.False(memory.TryGetPendingExternalInterrupt(out _, out _));
        Assert.Null(memory.CreateAsicSnapshot().PendingInterrupt);
    }

    [Fact]
    public void ExternalInterruptPriorityPrefersHigherOfTimerAndAsic()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0xA05F_6930, 1u << 3);
        memory.RaiseVBlankBegin();
        RaiseTimerUnderflow(memory, 0, 8);

        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0320u, eventCode);
        Assert.Equal(9, level);

        SetTimerPriority(memory, 0, 15);

        Assert.True(memory.TryGetPendingExternalInterrupt(out eventCode, out level));
        Assert.Equal(0x0400u, eventCode);
        Assert.Equal(15, level);
    }

    [Fact]
    public void ExternalInterruptPriorityPrefersTimerWhenTimerAndAsicAreEqual()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0xA05F_6930, 1u << 3);
        memory.RaiseVBlankBegin();
        RaiseTimerUnderflow(memory, 0, 9);

        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0400u, eventCode);
        Assert.Equal(9, level);
    }

    [Fact]
    public void ExternalInterruptPriorityIgnoresZeroPriorityTimer()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0xA05F_6930, 1u << 3);
        memory.RaiseVBlankBegin();
        RaiseTimerUnderflow(memory, 0, 0);

        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0320u, eventCode);
        Assert.Equal(9, level);
    }

    [Fact]
    public void MapleDmaDevInfoWritesControllerResponseAndRaisesEvent()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0x8C02_0000, 0x8000_0000);
        memory.WriteUInt32(0x8C02_0004, 0x0C03_0000);
        memory.WriteUInt32(0x8C02_0008, 0x0000_2001);
        memory.WriteUInt32(0xA05F_6930, 1u << 12);
        memory.WriteUInt32(0xA05F_6C04, 0x0C02_0000);

        memory.WriteUInt32(0xA05F_6C18, 1);

        Assert.Equal(5, memory.ReadByte(0x8C03_0000));
        Assert.Equal(28, memory.ReadByte(0x8C03_0003));
        Assert.Equal(0x0100_0000u, memory.ReadUInt32(0x8C03_0004));
        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0320u, eventCode);
        Assert.Equal(9, level);

        var snapshot = memory.CreateMapleSnapshot();
        var batch = Assert.Single(snapshot.DmaBatches);
        Assert.Equal("0x8C020000", batch.DescriptorAddressHex);
        Assert.Equal(1, batch.DescriptorsScanned);
        Assert.Equal(1, batch.TransferCount);
        Assert.True(batch.Completed);
        Assert.False(batch.HitDescriptorLimit);

        var transfer = Assert.Single(snapshot.Transfers);
        Assert.Equal("DeviceInfo", transfer.CommandName);
        Assert.Equal("DeviceInfo", transfer.ResponseName);
        Assert.Equal("A0", transfer.DestinationName);
        Assert.Equal("0x8C020000", transfer.DescriptorAddressHex);
        Assert.Equal("0x8C030000", transfer.ReceiveBufferAddressHex);
        Assert.Equal(116, transfer.ResponseBytes);
        Assert.Null(transfer.ControllerState);
    }

    [Fact]
    public void MapleDmaGetConditionWritesNeutralControllerState()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0x8C02_0000, 0x8000_0001);
        memory.WriteUInt32(0x8C02_0004, 0x0C03_0000);
        memory.WriteUInt32(0x8C02_0008, 0x0100_2009);
        memory.WriteUInt32(0x8C02_000C, 0x0100_0000);
        memory.WriteUInt32(0xA05F_6C04, 0x0C02_0000);

        memory.WriteUInt32(0xA05F_6C18, 1);

        Assert.Equal(8, memory.ReadByte(0x8C03_0000));
        Assert.Equal(3, memory.ReadByte(0x8C03_0003));
        Assert.Equal(0x0100_0000u, memory.ReadUInt32(0x8C03_0004));
        Assert.Equal(0x0000_FFFFu, memory.ReadUInt32(0x8C03_0008));
        Assert.Equal(0x8080_8080u, memory.ReadUInt32(0x8C03_000C));

        var transfer = Assert.Single(memory.CreateMapleSnapshot().Transfers);
        Assert.Equal("GetCondition", transfer.CommandName);
        Assert.Equal("DataTransfer", transfer.ResponseName);
        Assert.Equal("A0", transfer.DestinationName);
        Assert.Equal(16, transfer.ResponseBytes);
        Assert.Equal(DreamcastControllerButtons.None, transfer.ControllerState?.Buttons);
    }

    [Fact]
    public void MapleDmaGetConditionWritesConfiguredControllerState()
    {
        var memory = new DreamcastMemory(new DreamcastControllerState(
            Buttons: DreamcastControllerButtons.Start | DreamcastControllerButtons.A,
            LeftTrigger: 40,
            RightTrigger: 80,
            JoyX: -12,
            JoyY: 13,
            Joy2X: -2,
            Joy2Y: 3));
        memory.WriteUInt32(0x8C02_0000, 0x8000_0001);
        memory.WriteUInt32(0x8C02_0004, 0x0C03_0000);
        memory.WriteUInt32(0x8C02_0008, 0x0100_2009);
        memory.WriteUInt32(0x8C02_000C, 0x0100_0000);
        memory.WriteUInt32(0xA05F_6C04, 0x0C02_0000);

        memory.WriteUInt32(0xA05F_6C18, 1);

        Assert.Equal(8, memory.ReadByte(0x8C03_0000));
        Assert.Equal(0xFFF3, memory.ReadUInt16(0x8C03_0008));
        Assert.Equal(80, memory.ReadByte(0x8C03_000A));
        Assert.Equal(40, memory.ReadByte(0x8C03_000B));
        Assert.Equal(116, memory.ReadByte(0x8C03_000C));
        Assert.Equal(141, memory.ReadByte(0x8C03_000D));
        Assert.Equal(126, memory.ReadByte(0x8C03_000E));
        Assert.Equal(131, memory.ReadByte(0x8C03_000F));

        var transfer = Assert.Single(memory.CreateMapleSnapshot().Transfers);
        var state = Assert.IsType<DreamcastControllerState>(transfer.ControllerState);
        Assert.Equal(DreamcastControllerButtons.Start | DreamcastControllerButtons.A, state.Buttons);
        Assert.Equal(40, state.LeftTrigger);
        Assert.Equal(80, state.RightTrigger);
        Assert.Equal(-12, state.JoyX);
    }

    [Fact]
    public void MapleDmaGetConditionWritesConfiguredPortBControllerState()
    {
        var memory = new DreamcastMemory(
            controllerB: new DreamcastControllerState(
                Buttons: DreamcastControllerButtons.B,
                LeftTrigger: 7,
                RightTrigger: 9,
                JoyX: 12,
                JoyY: -13));
        memory.WriteUInt32(0x8C02_0000, 0x8000_0001);
        memory.WriteUInt32(0x8C02_0004, 0x0C03_0000);
        memory.WriteUInt32(0x8C02_0008, 0x0100_4009);
        memory.WriteUInt32(0x8C02_000C, 0x0100_0000);
        memory.WriteUInt32(0xA05F_6C04, 0x0C02_0000);

        memory.WriteUInt32(0xA05F_6C18, 1);

        Assert.Equal(8, memory.ReadByte(0x8C03_0000));
        Assert.Equal(0x40, memory.ReadByte(0x8C03_0002));
        Assert.Equal(0xFFFD, memory.ReadUInt16(0x8C03_0008));
        Assert.Equal(9, memory.ReadByte(0x8C03_000A));
        Assert.Equal(7, memory.ReadByte(0x8C03_000B));
        Assert.Equal(140, memory.ReadByte(0x8C03_000C));
        Assert.Equal(115, memory.ReadByte(0x8C03_000D));

        var transfer = Assert.Single(memory.CreateMapleSnapshot().Transfers);
        var state = Assert.IsType<DreamcastControllerState>(transfer.ControllerState);
        Assert.Equal("B0", transfer.DestinationName);
        Assert.Equal(DreamcastControllerButtons.B, state.Buttons);
    }

    [Fact]
    public void MapleDmaGetConditionUsesConfiguredControllerMap()
    {
        var memory = new DreamcastMemory(
            controllers: new Dictionary<byte, DreamcastControllerState>
            {
                [0x40] = new(Buttons: DreamcastControllerButtons.B, LeftTrigger: 7)
            });
        memory.WriteUInt32(0x8C02_0000, 0x8000_0001);
        memory.WriteUInt32(0x8C02_0004, 0x0C03_0000);
        memory.WriteUInt32(0x8C02_0008, 0x0100_4009);
        memory.WriteUInt32(0x8C02_000C, 0x0100_0000);
        memory.WriteUInt32(0xA05F_6C04, 0x0C02_0000);

        memory.WriteUInt32(0xA05F_6C18, 1);

        Assert.Equal(8, memory.ReadByte(0x8C03_0000));
        Assert.Equal(0xFFFD, memory.ReadUInt16(0x8C03_0008));
        Assert.Equal(7, memory.ReadByte(0x8C03_000B));
    }

    [Fact]
    public void TicksUntilNextTimerInterruptUsesRunningCounter()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0xFFD8_0008, 5);
        memory.WriteUInt32(0xFFD8_000C, 5);
        memory.WriteUInt16(0xFFD8_0010, 0x20);
        memory.WriteUInt16(0xFFD0_0004, 0xF000);
        memory.Write(0xFFD8_0004, [0x01]);

        Assert.Equal(6UL, memory.TicksUntilNextTimerInterrupt());

        memory.AdvanceHardware(6);

        Assert.Equal(0UL, memory.TicksUntilNextTimerInterrupt());
        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0400u, eventCode);
        Assert.Equal(15, level);
    }

    [Fact]
    public void MapleDmaGetConditionForUnconfiguredPortBWritesNoResponse()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0x8C02_0000, 0x8000_0001);
        memory.WriteUInt32(0x8C02_0004, 0x0C03_0000);
        memory.WriteUInt32(0x8C02_0008, 0x0100_4009);
        memory.WriteUInt32(0x8C02_000C, 0x0100_0000);
        memory.WriteUInt32(0xA05F_6C04, 0x0C02_0000);

        memory.WriteUInt32(0xA05F_6C18, 1);

        Assert.Equal(0xFF, memory.ReadByte(0x8C03_0000));
        var transfer = Assert.Single(memory.CreateMapleSnapshot().Transfers);
        Assert.Equal("B0", transfer.DestinationName);
        Assert.Equal("None", transfer.ResponseName);
        Assert.Null(transfer.ControllerState);
    }

    [Fact]
    public void MapleDmaUnknownDeviceWritesNoResponse()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0x8C02_0000, 0x8000_0000);
        memory.WriteUInt32(0x8C02_0004, 0x0C03_0000);
        memory.WriteUInt32(0x8C02_0008, 0x0000_6001);
        memory.WriteUInt32(0xA05F_6C04, 0x0C02_0000);

        memory.WriteUInt32(0xA05F_6C18, 1);

        Assert.Equal(0xFF, memory.ReadByte(0x8C03_0000));
        var transfer = Assert.Single(memory.CreateMapleSnapshot().Transfers);
        Assert.Equal("DeviceInfo", transfer.CommandName);
        Assert.Equal("None", transfer.ResponseName);
        Assert.Equal(1, transfer.ResponseBytes);
    }

    [Fact]
    public void MapleDmaRecordsDescriptorLimitWhenEndMarkerIsMissing()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(0xA05F_6930, 1u << 12);
        memory.WriteUInt32(0xA05F_6C04, 0x0C02_0000);

        memory.WriteUInt32(0xA05F_6C18, 1);

        Assert.Equal(0u, memory.ReadUInt32(0xA05F_6C18));
        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0320u, eventCode);
        Assert.Equal(9, level);

        var snapshot = memory.CreateMapleSnapshot();
        Assert.Empty(snapshot.Transfers);
        var batch = Assert.Single(snapshot.DmaBatches);
        Assert.Equal(0x8C02_0000u, batch.DescriptorAddress);
        Assert.Equal("0x8C020000", batch.DescriptorAddressHex);
        Assert.Equal(64, batch.DescriptorsScanned);
        Assert.Equal(0, batch.TransferCount);
        Assert.False(batch.Completed);
        Assert.True(batch.HitDescriptorLimit);
        Assert.Equal(0x8C02_02F4u, batch.LastDescriptorAddress);
        Assert.Equal("0x8C0202F4", batch.LastDescriptorAddressHex);

        var summary = DreamcastMapleSummary.FromSnapshot(snapshot);
        Assert.Equal(1, summary.DmaBatchCount);
        Assert.Equal(1, summary.DescriptorLimitHitCount);
        Assert.Empty(summary.RecentTransfers);
        var summaryBatch = Assert.Single(summary.RecentDmaBatches);
        Assert.True(summaryBatch.HitDescriptorLimit);
        Assert.False(summaryBatch.Completed);
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

    private static void InitializeAicaCommandQueue(DreamcastMemory memory, uint head)
    {
        memory.WriteUInt32(0x0081_0000, head);
        memory.WriteUInt32(0x0081_0004, 0);
        memory.WriteUInt32(0x0081_0008, 0x100);
        memory.WriteUInt32(0x0081_000C, 1);
        memory.WriteUInt32(0x0081_0010, 1);
        memory.WriteUInt32(0x0081_0014, 0x0001_0018);
    }

    private static void WriteAicaQueue(
        DreamcastMemory memory,
        uint offset,
        uint head,
        uint tail,
        uint size,
        uint valid,
        uint processOk,
        uint data)
    {
        memory.WriteUInt32(0x0080_0000 + offset, head);
        memory.WriteUInt32(0x0080_0000 + offset + 4, tail);
        memory.WriteUInt32(0x0080_0000 + offset + 8, size);
        memory.WriteUInt32(0x0080_0000 + offset + 12, valid);
        memory.WriteUInt32(0x0080_0000 + offset + 16, processOk);
        memory.WriteUInt32(0x0080_0000 + offset + 20, data);
    }

    private static void WriteAicaText(DreamcastMemory memory, uint offset, string text)
    {
        var index = 0;
        for (; index + 1 < text.Length; index += 2)
        {
            var value = (ushort)(text[index] | (text[index + 1] << 8));
            memory.WriteUInt16(0x0080_0000 + offset + (uint)index, value);
        }

        if (index < text.Length)
        {
            memory.WriteUInt16(0x0080_0000 + offset + (uint)index, text[index]);
        }
    }

    private static void WriteAicaCommandHeader(
        DreamcastMemory memory,
        uint queueOffset,
        uint sizeDwords,
        uint command,
        uint timestamp,
        uint commandId)
    {
        var address = 0x0081_0018 + queueOffset;
        memory.WriteUInt32(address, sizeDwords);
        memory.WriteUInt32(address + 4, command);
        memory.WriteUInt32(address + 8, timestamp);
        memory.WriteUInt32(address + 12, commandId);
    }

    private static byte[] CreateMediaData(int sectors)
    {
        var data = new byte[sectors * 2048];
        for (var sector = 0; sector < sectors; sector++)
        {
            data[sector * 2048] = (byte)(0x10 + (sector * 0x10));
        }

        return data;
    }

    private static byte[] CreateCdMediaData(byte[][] sectorPrefixes)
    {
        var data = new byte[sectorPrefixes.Length * 2352];
        for (var sector = 0; sector < sectorPrefixes.Length; sector++)
        {
            var prefix = sectorPrefixes[sector];
            Array.Copy(prefix, 0, data, (sector * 2352) + 16, prefix.Length);
        }

        return data;
    }

    private static ushort ReadPreviewRgb565(byte[] vram, int x, int y)
    {
        var offset = ((y * 640) + x) * 2;
        return (ushort)(vram[offset] | (vram[offset + 1] << 8));
    }

    private static ushort ReadPreviewRgb565(byte[] vram, int byteOffset, int x, int y)
    {
        var offset = byteOffset + (((y * 640) + x) * 2);
        return (ushort)(vram[offset] | (vram[offset + 1] << 8));
    }
}
