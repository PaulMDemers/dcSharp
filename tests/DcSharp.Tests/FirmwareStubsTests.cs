using DcSharp.Core.Cpu;
using DcSharp.Core.Dreamcast.Memory;
using DcSharp.Core.Execution;
using DcSharp.Core.Media;

namespace DcSharp.Tests;

public class FirmwareStubsTests
{
    private const uint GdromHleStub = 0x8C00_00D0;
    private const uint SystemHleStub = 0x8C00_00E8;
    private const uint GdromSendCommand = 0;
    private const uint GdromCheckCommand = 1;
    private const uint GdromAbortCommand = 3;
    private const uint GdromCheckDrive = 4;
    private const uint GdromSectorMode = 10;
    private const uint GdromCommandPioRead = 16;
    private const uint GdromCommandGetToc2 = 19;
    private const uint GdromCommandNop = 29;
    private const uint GdromCompleted = 2;
    private const uint GdromNoActive = 0;
    private const uint Sector = 1;
    private const uint ParameterAddress = 0x8C01_0000;
    private const uint StatusAddress = 0x8C01_0100;
    private const uint TocAddress = 0x8C01_0200;
    private const uint DestinationAddress = 0x8C02_0000;

    [Fact]
    public void InstallSeedsBiosWorkAreaLanguageCode()
    {
        var memory = new DreamcastMemory();

        FirmwareStubs.Install(memory);

        Assert.Equal((byte)'1', memory.ReadByte(0x8C00_0074));
    }

    [Theory]
    [InlineData(0, "System BIOS soft reset requested: function=0")]
    [InlineData(1, "System BIOS menu requested: function=1")]
    [InlineData(2, "System BIOS CD menu requested: function=2")]
    [InlineData(7, "System BIOS call requested: function=7")]
    public void SystemBiosTrapReportsNamedTerminalCalls(uint function, string expectedMessage)
    {
        var handler = FirmwareStubs.CreateTrapHandler();
        var state = new Sh4State { Pc = SystemHleStub };
        state.R[4] = function;

        var exception = Assert.Throws<DreamcastFirmwareExitException>(() => handler.TryHandle(state, new DreamcastMemory(), out _));

        Assert.Equal(expectedMessage, exception.Message);
    }

    [Fact]
    public void GdromCheckCommandReportsCompletedReadAndTransferredBytes()
    {
        var memory = new DreamcastMemory(media: new RawSectorMediaImage(CreateMediaData(2), 2048));
        WritePioReadParameters(memory);
        var handler = FirmwareStubs.CreateTrapHandler();

        var commandId = SendGdromCommand(handler, memory, GdromCommandPioRead, ParameterAddress);
        var response = CheckGdromCommand(handler, memory, commandId);

        Assert.Equal(1u, commandId);
        Assert.Equal(GdromCompleted, response);
        Assert.Equal(0u, memory.ReadUInt32(StatusAddress));
        Assert.Equal(0u, memory.ReadUInt32(StatusAddress + 4));
        Assert.Equal(2048u, memory.ReadUInt32(StatusAddress + 8));
        Assert.Equal(0u, memory.ReadUInt32(StatusAddress + 12));
        Assert.Equal(0x20, memory.ReadByte(DestinationAddress));
    }

    [Fact]
    public void GdromCheckCommandReportsFailedReadWhenNoMediaIsLoaded()
    {
        var memory = new DreamcastMemory();
        WritePioReadParameters(memory);
        var handler = FirmwareStubs.CreateTrapHandler();

        var commandId = SendGdromCommand(handler, memory, GdromCommandPioRead, ParameterAddress);
        var response = CheckGdromCommand(handler, memory, commandId);

        Assert.Equal(unchecked((uint)-1), response);
        Assert.Equal(2u, memory.ReadUInt32(StatusAddress));
        Assert.Equal(0u, memory.ReadUInt32(StatusAddress + 8));
    }

    [Fact]
    public void GdromCheckCommandReportsNoActiveForUnknownOrAbortedCommand()
    {
        var memory = new DreamcastMemory();
        var handler = FirmwareStubs.CreateTrapHandler();

        var unknownResponse = CheckGdromCommand(handler, memory, 42);
        var nopCommandId = SendGdromCommand(handler, memory, GdromCommandNop, 0);
        var nopResponse = CheckGdromCommand(handler, memory, nopCommandId);
        var commandId = SendGdromCommand(handler, memory, GdromCommandNop, 0);
        AbortGdromCommand(handler, memory, commandId);
        var abortedResponse = CheckGdromCommand(handler, memory, commandId);

        Assert.Equal(GdromNoActive, unknownResponse);
        Assert.Equal(GdromCompleted, nopResponse);
        Assert.Equal(GdromNoActive, abortedResponse);
    }

    [Fact]
    public void GdromGetToc2WritesSingleDataTrackToc()
    {
        var memory = new DreamcastMemory(media: new RawSectorMediaImage(CreateMediaData(3), 2048));
        memory.WriteUInt32(ParameterAddress, 0);
        memory.WriteUInt32(ParameterAddress + 4, TocAddress);
        var handler = FirmwareStubs.CreateTrapHandler();

        var commandId = SendGdromCommand(handler, memory, GdromCommandGetToc2, ParameterAddress);
        var response = CheckGdromCommand(handler, memory, commandId);
        var toc = Assert.Single(memory.CreateGdromSnapshot().TocCommands);

        Assert.Equal(GdromCompleted, response);
        Assert.Equal(0x4000_AFC8u, memory.ReadUInt32(TocAddress + 8));
        Assert.Equal(3u, (memory.ReadUInt32(TocAddress + 396) >> 16) & 0xFF);
        Assert.Equal(3u, (memory.ReadUInt32(TocAddress + 400) >> 16) & 0xFF);
        Assert.Equal(0x0000_AFCBu, memory.ReadUInt32(TocAddress + 404));
        Assert.True(toc.Success);
        Assert.Equal(0x0000_AFC8u, toc.DataTrackStartFad);
        Assert.Equal(0x0000_AFCBu, toc.LeadoutFad);
        Assert.Equal("TOC written", toc.Status);
    }

    [Fact]
    public void GdromGetToc2WritesMultipleDataTracks()
    {
        var media = new GdiMediaImage(
        [
            new GdiMediaTrack(3, 45_000, 2048, 0, CreateMediaData(2)),
            new GdiMediaTrack(4, 45_150, 2048, 0, CreateMediaData(3))
        ]);
        var memory = new DreamcastMemory(media: media);
        memory.WriteUInt32(ParameterAddress, 0);
        memory.WriteUInt32(ParameterAddress + 4, TocAddress);
        var handler = FirmwareStubs.CreateTrapHandler();

        var commandId = SendGdromCommand(handler, memory, GdromCommandGetToc2, ParameterAddress);
        var response = CheckGdromCommand(handler, memory, commandId);
        var toc = Assert.Single(memory.CreateGdromSnapshot().TocCommands);

        Assert.Equal(GdromCompleted, response);
        Assert.Equal(0x4000_AFC8u, memory.ReadUInt32(TocAddress + 8));
        Assert.Equal(0x4000_B05Eu, memory.ReadUInt32(TocAddress + 12));
        Assert.Equal(3u, (memory.ReadUInt32(TocAddress + 396) >> 16) & 0xFF);
        Assert.Equal(4u, (memory.ReadUInt32(TocAddress + 400) >> 16) & 0xFF);
        Assert.Equal(0x0000_B061u, memory.ReadUInt32(TocAddress + 404));
        Assert.True(toc.Success);
        Assert.Equal(3, toc.FirstTrack);
        Assert.Equal(4, toc.LastTrack);
        Assert.Equal(0x0000_B05Eu, toc.DataTrackStartFad);
        Assert.Equal(0x0000_B061u, toc.LeadoutFad);
        Assert.Equal("TOC written", toc.Status);
    }

    [Fact]
    public void GdromGetToc2FailsWhenNoMediaIsLoaded()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(ParameterAddress, 0);
        memory.WriteUInt32(ParameterAddress + 4, TocAddress);
        var handler = FirmwareStubs.CreateTrapHandler();

        var commandId = SendGdromCommand(handler, memory, GdromCommandGetToc2, ParameterAddress);
        var response = CheckGdromCommand(handler, memory, commandId);
        var toc = Assert.Single(memory.CreateGdromSnapshot().TocCommands);

        Assert.Equal(unchecked((uint)-1), response);
        Assert.Equal(2u, memory.ReadUInt32(StatusAddress));
        Assert.False(toc.Success);
        Assert.Equal("no media image loaded", toc.Status);
    }

    [Fact]
    public void GdromCheckDriveReportsStandbyWhenMediaIsLoaded()
    {
        var memory = new DreamcastMemory(media: new RawSectorMediaImage(CreateMediaData(2), 2048));
        var handler = FirmwareStubs.CreateTrapHandler();

        var response = CheckDrive(handler, memory);
        var status = Assert.Single(memory.CreateGdromSnapshot().StatusCommands);

        Assert.Equal(0u, response);
        Assert.Equal(2u, memory.ReadUInt32(StatusAddress));
        Assert.Equal(0x20u, memory.ReadUInt32(StatusAddress + 4));
        Assert.Equal(2, status.StatusCode);
        Assert.Equal("standby", status.StatusName);
        Assert.Equal(0x20, status.DiscType);
        Assert.Equal("CD-ROM XA", status.DiscTypeName);
        Assert.True(status.Success);
    }

    [Fact]
    public void GdromCheckDriveReportsNoDiscWhenNoMediaIsLoaded()
    {
        var memory = new DreamcastMemory();
        var handler = FirmwareStubs.CreateTrapHandler();

        var response = CheckDrive(handler, memory);
        var status = Assert.Single(memory.CreateGdromSnapshot().StatusCommands);

        Assert.Equal(0u, response);
        Assert.Equal(7u, memory.ReadUInt32(StatusAddress));
        Assert.Equal(0u, memory.ReadUInt32(StatusAddress + 4));
        Assert.Equal(7, status.StatusCode);
        Assert.Equal("no disc", status.StatusName);
        Assert.Equal(0, status.DiscType);
        Assert.Equal("CDDA/no disc", status.DiscTypeName);
        Assert.True(status.Success);
    }

    [Fact]
    public void GdromSectorModeRecordsSetParameters()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(ParameterAddress, 0);
        memory.WriteUInt32(ParameterAddress + 4, 0x2000);
        memory.WriteUInt32(ParameterAddress + 8, 2048);
        memory.WriteUInt32(ParameterAddress + 12, 2048);
        var handler = FirmwareStubs.CreateTrapHandler();

        var response = SectorMode(handler, memory);
        var mode = Assert.Single(memory.CreateGdromSnapshot().SectorModeCommands);

        Assert.Equal(0u, response);
        Assert.Equal(0, mode.Request);
        Assert.Equal("set", mode.RequestName);
        Assert.Equal(0x2000, mode.SectorPart);
        Assert.Equal(2048, mode.CdXa);
        Assert.Equal(2048, mode.SectorSize);
        Assert.True(mode.Success);
        Assert.Equal("sector mode set", mode.Status);
    }

    [Fact]
    public void GdromSectorModeReportsCurrentDefaults()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(ParameterAddress, 1);
        var handler = FirmwareStubs.CreateTrapHandler();

        var response = SectorMode(handler, memory);
        var mode = Assert.Single(memory.CreateGdromSnapshot().SectorModeCommands);

        Assert.Equal(0u, response);
        Assert.Equal(1u, memory.ReadUInt32(ParameterAddress));
        Assert.Equal(0x2000u, memory.ReadUInt32(ParameterAddress + 4));
        Assert.Equal(2048u, memory.ReadUInt32(ParameterAddress + 8));
        Assert.Equal(2048u, memory.ReadUInt32(ParameterAddress + 12));
        Assert.Equal(1, mode.Request);
        Assert.Equal("get", mode.RequestName);
        Assert.Equal(0x2000, mode.SectorPart);
        Assert.Equal(2048, mode.CdXa);
        Assert.Equal(2048, mode.SectorSize);
        Assert.True(mode.Success);
        Assert.Equal("sector mode reported", mode.Status);
    }

    private static uint SendGdromCommand(
        FirmwareStubs.FirmwareTrapHandler handler,
        DreamcastMemory memory,
        uint command,
        uint parameters)
    {
        var state = CreateGdromState(GdromSendCommand);
        state.R[4] = command;
        state.R[5] = parameters;
        Assert.True(handler.TryHandle(state, memory, out _));
        return state.R[0];
    }

    private static uint CheckGdromCommand(
        FirmwareStubs.FirmwareTrapHandler handler,
        DreamcastMemory memory,
        uint commandId)
    {
        var state = CreateGdromState(GdromCheckCommand);
        state.R[4] = commandId;
        state.R[5] = StatusAddress;
        Assert.True(handler.TryHandle(state, memory, out _));
        return state.R[0];
    }

    private static void AbortGdromCommand(
        FirmwareStubs.FirmwareTrapHandler handler,
        DreamcastMemory memory,
        uint commandId)
    {
        var state = CreateGdromState(GdromAbortCommand);
        state.R[4] = commandId;
        Assert.True(handler.TryHandle(state, memory, out _));
    }

    private static uint CheckDrive(
        FirmwareStubs.FirmwareTrapHandler handler,
        DreamcastMemory memory)
    {
        var state = CreateGdromState(GdromCheckDrive);
        state.R[4] = StatusAddress;
        Assert.True(handler.TryHandle(state, memory, out _));
        return state.R[0];
    }

    private static uint SectorMode(
        FirmwareStubs.FirmwareTrapHandler handler,
        DreamcastMemory memory)
    {
        var state = CreateGdromState(GdromSectorMode);
        state.R[4] = ParameterAddress;
        Assert.True(handler.TryHandle(state, memory, out _));
        return state.R[0];
    }

    private static Sh4State CreateGdromState(uint function) =>
        new()
        {
            Pc = GdromHleStub,
            Pr = 0x8C01_FFFE,
            R =
            {
                [6] = 0,
                [7] = function
            }
        };

    private static void WritePioReadParameters(DreamcastMemory memory)
    {
        memory.WriteUInt32(ParameterAddress, Sector);
        memory.WriteUInt32(ParameterAddress + 4, 1);
        memory.WriteUInt32(ParameterAddress + 8, DestinationAddress);
        memory.WriteUInt32(ParameterAddress + 12, 0);
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
}
