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

        Assert.False(memory.TryPeekUInt32(0x3304_41F0, out var value));

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
    public void VideoSnapshotReportsPvrTaCommandWrites()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0x1000_0000, 0x8084_0000);
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
            },
            write =>
            {
                Assert.Equal("TA_YUV_CONV", write.Region);
                Assert.Equal("YuvConverterData", write.Kind);
                Assert.Equal("0x10800000", write.AddressHex);
            });
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
        Assert.Equal(0xF800, snapshot.Samples.Single(sample => sample.Name == "origin").Rgb565);
        Assert.Equal(0xF800, snapshot.Samples.Single(sample => sample.Name == "pixel_1_0").Rgb565);
        Assert.Equal(0xF800, snapshot.Samples.Single(sample => sample.Name == "pixel_0_1_320x240").Rgb565);
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

        Assert.Equal(0x07E0, snapshot.Samples.Single(sample => sample.Name == "origin").Rgb565);
        Assert.Equal(0x07E0, snapshot.Samples.Single(sample => sample.Name == "pixel_1_0").Rgb565);
        Assert.Equal(0x07E0, snapshot.Samples.Single(sample => sample.Name == "pixel_2_0").Rgb565);
        Assert.Equal(0x07E0, snapshot.Samples.Single(sample => sample.Name == "pixel_0_1_320x240").Rgb565);
        Assert.Equal(0x0000, snapshot.Samples.Single(sample => sample.Name == "pixel_1_1_320x240").Rgb565);
        var strip = Assert.Single(snapshot.PvrTaStrips);
        Assert.Equal(0x07E0, strip.Rgb565);
        Assert.Equal(3, strip.Vertices[1].X);
    }

    [Fact]
    public void KnownSpriteWritesVisiblePreviewPixels()
    {
        var memory = new DreamcastMemory();

        WritePvrSpritePacket(memory);

        var snapshot = memory.CreateVideoSnapshot();

        Assert.True(snapshot.NonZeroBytes >= 9);
        Assert.Equal(0x07E0, snapshot.Samples.Single(sample => sample.Name == "origin").Rgb565);
        Assert.Equal(0x07E0, snapshot.Samples.Single(sample => sample.Name == "pixel_1_0").Rgb565);
        Assert.Equal(0x07E0, snapshot.Samples.Single(sample => sample.Name == "pixel_2_0").Rgb565);
        Assert.Equal(0x07E0, snapshot.Samples.Single(sample => sample.Name == "pixel_0_1_320x240").Rgb565);
        Assert.Equal(0x07E0, snapshot.Samples.Single(sample => sample.Name == "pixel_1_1_320x240").Rgb565);
        var sprite = Assert.Single(snapshot.PvrTaSprites);
        Assert.Equal("OpaquePolygon", sprite.ListTypeName);
        Assert.Equal("0xFF00FF00", sprite.HeaderPayload.ArgbHex);
        Assert.Equal(0x07E0, sprite.Rgb565);
        Assert.Equal(4, sprite.Vertices.Count);
        Assert.Equal(3, sprite.Vertices[3].X);
        Assert.Equal(3, sprite.Vertices[3].Y);
    }

    private static void WritePvrVertexPacket(DreamcastMemory memory, bool endOfStrip, int x, int y, ushort color)
    {
        memory.WriteUInt32(0x1000_0000, endOfStrip ? 0xF000_0000 : 0xE000_0000);
        memory.WriteUInt32(0x1000_0000, (uint)x << 16);
        memory.WriteUInt32(0x1000_0000, (uint)y << 16);
        memory.WriteUInt32(0x1000_0000, color);
    }

    private static void WritePvrSpritePacket(DreamcastMemory memory)
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

        foreach (var word in words)
        {
            memory.WriteUInt32(0x1000_0000, word);
        }
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

    private static byte[] CreateMediaData(int sectors)
    {
        var data = new byte[sectors * 2048];
        for (var sector = 0; sector < sectors; sector++)
        {
            data[sector * 2048] = (byte)(0x10 + (sector * 0x10));
        }

        return data;
    }
}
