using DcSharp.Core.Dreamcast.Input;
using DcSharp.Core.Dreamcast.Memory;

namespace DcSharp.Tests;

public class DreamcastMemoryTests
{
    [Theory]
    [InlineData(0x8C01_0000u, 0x0C01_0000u)]
    [InlineData(0xAC01_0000u, 0x0C01_0000u)]
    [InlineData(0x0C01_0000u, 0x0C01_0000u)]
    public void TranslatesDreamcastRamMirrors(uint address, uint expectedPhysical)
    {
        Assert.Equal(expectedPhysical, DreamcastMemory.TranslateAddress(address));
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
        Assert.Contains(snapshot.RegisterAccesses, access => access.Name == "AICA_CH0_CONTROL" && access.ValueHex == "0x0000C000");
        var channel = Assert.Single(snapshot.Channels);
        Assert.Equal(0, channel.Channel);
        Assert.Equal("Pcm16", channel.SampleFormat);
        Assert.False(channel.LoopEnabled);
        Assert.Equal(0x1234u, channel.SampleAddress);
        Assert.True(channel.KeyOn);
        Assert.True(channel.KeyOnExecute);
        Assert.True(channel.Active);
        Assert.Equal(0x1234u, channel.SampleAddressLow);
        Assert.Equal(0x1AC0u, channel.Pitch);
        Assert.Equal(0x0F, channel.Pan);
        Assert.Equal(0x40, channel.Volume);
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

        memory.WriteUInt16(0xFFD0_0004, 15 << 12);
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
    public void RaisedVBlankReportsPendingIrq9WhenEnabled()
    {
        var memory = new DreamcastMemory();

        memory.WriteUInt32(0xA05F_6910, 1u << 3);
        memory.RaiseVBlankBegin();

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
        memory.WriteUInt32(0xA05F_6910, 1u << 12);
        memory.WriteUInt32(0xA05F_6C04, 0x0C02_0000);

        memory.WriteUInt32(0xA05F_6C18, 1);

        Assert.Equal(5, memory.ReadByte(0x8C03_0000));
        Assert.Equal(28, memory.ReadByte(0x8C03_0003));
        Assert.Equal(0x0100_0000u, memory.ReadUInt32(0x8C03_0004));
        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0320u, eventCode);
        Assert.Equal(9, level);

        var transfer = Assert.Single(memory.CreateMapleSnapshot().Transfers);
        Assert.Equal("DeviceInfo", transfer.CommandName);
        Assert.Equal("DeviceInfo", transfer.ResponseName);
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
}
