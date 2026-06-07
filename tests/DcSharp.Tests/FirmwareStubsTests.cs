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
    private const uint GdromExecServer = 2;
    private const uint GdromDmaTransfer = 6;
    private const uint GdromDmaCheck = 7;
    private const uint GdromInit = 3;
    private const uint GdromAbortCommand = 8;
    private const uint GdromReset = 9;
    private const uint GdromCheckDrive = 4;
    private const uint GdromSectorMode = 10;
    private const uint GdromPioTransfer = 12;
    private const uint GdromPioCheck = 13;
    private const uint GdromCommandPioRead = 16;
    private const uint GdromCommandDmaRead = 17;
    private const uint GdromCommandGetToc2 = 19;
    private const uint GdromCommandInit = 24;
    private const uint GdromCommandDmaReadStream = 28;
    private const uint GdromCommandPioReadStream = 37;
    private const uint GdromCommandNop = 29;
    private const uint GdromCommandGetVersion = 40;
    private const uint GdromCompleted = 2;
    private const uint GdromNoActive = 0;
    private const uint GdromProcessing = 1;
    private const uint GdromStreaming = 3;
    private const uint Sector = 1;
    private const uint ParameterAddress = 0x8C01_0000;
    private const uint StatusAddress = 0x8C01_0100;
    private const uint TocAddress = 0x8C01_0200;
    private const uint DestinationAddress = 0x8C02_0000;
    private const uint TransferParameterAddress = 0x8C01_0300;
    private const uint TransferSizeAddress = 0x8C01_0400;

    [Fact]
    public void InstallSeedsBiosWorkAreaLanguageCode()
    {
        var memory = new DreamcastMemory();

        FirmwareStubs.Install(memory);

        Assert.Equal((byte)'1', memory.ReadByte(0x8C00_0074));
    }

    [Fact]
    public void InstallSeedsBootModeWorkAreaBytes()
    {
        var memory = new DreamcastMemory();
        memory.Write(0x8C00_80F0, "DEAD OR ALI "u8.ToArray());
        memory.Write(0x8C00_80FC, [(byte)' ']);
        memory.Write(0x8C00_80FE, [(byte)' ']);

        FirmwareStubs.Install(memory);

        Assert.Equal((byte)'.', memory.ReadByte(0x8C00_80F0));
        for (var offset = 0x8C00_80F1u; offset <= 0x8C00_80FB; offset++)
        {
            Assert.Equal((byte)' ', memory.ReadByte(offset));
        }

        Assert.Equal(1, memory.ReadByte(0x8C00_80FC));
        Assert.Equal((byte)' ', memory.ReadByte(0x8C00_80FD));
        Assert.Equal(1, memory.ReadByte(0x8C00_80FE));
    }

    [Fact]
    public void InstallSeedsBootDirectoryFromMatchingIsoRootDirectory()
    {
        var media = new RawSectorMediaImage(CreateIsoWithRootDirectory("USDC_DOA2", "DOA2"), 2048);
        var memory = new DreamcastMemory(media: media);

        FirmwareStubs.Install(memory);

        Assert.Equal("DOA2        ", ReadAscii(memory, 0x8C00_80F0, 12));
        Assert.Equal(1, memory.ReadByte(0x8C00_80FC));
        Assert.Equal((byte)' ', memory.ReadByte(0x8C00_80FD));
        Assert.Equal(1, memory.ReadByte(0x8C00_80FE));
    }

    [Fact]
    public void SystemBiosSoftResetContinuesAtLoadedBootEntry()
    {
        var handler = FirmwareStubs.CreateTrapHandler();
        var memory = new DreamcastMemory();
        FirmwareStubs.Install(memory);
        var state = new Sh4State { Pc = SystemHleStub };
        state.R[4] = 0;
        state.R[15] = 0x7E00_0FD0;

        Assert.True(handler.TryHandle(state, memory, out var trace));

        Assert.Equal(0x8C01_0000u, state.Pc);
        Assert.Equal(0x8D00_0000u, state.R[15]);
        Assert.Equal(1, memory.ReadByte(0x8C00_80FC));
        Assert.Equal(0, memory.ReadByte(0x8C00_80FE));
        Assert.Equal("firmware system hle func=0 r4=0x00000000, r5=0x00000000, r6=0x00000000, r7=0x00000000, pr=0x00000000 ; pc=0x8C010000, sp=0x8D000000", trace);
    }

    [Fact]
    public void SystemBiosCheckDiscReturnsToCaller()
    {
        var handler = FirmwareStubs.CreateTrapHandler();
        var state = new Sh4State { Pc = SystemHleStub, Pr = 0x8C01_2450 };
        state.R[4] = 3;
        state.R[5] = 0x1234_5678;

        Assert.True(handler.TryHandle(state, new DreamcastMemory(), out var trace));

        Assert.Equal(0x8C01_2450u, state.Pc);
        Assert.Equal(0u, state.R[0]);
        Assert.Equal("firmware system hle func=3 r4=0x00000003, r5=0x12345678, r6=0x00000000, r7=0x00000000, pr=0x8C012450 ; r0=0x00000000", trace);
    }

    [Fact]
    public void DefaultBiosCallbackReturnsToCaller()
    {
        var handler = FirmwareStubs.CreateTrapHandler();
        var state = new Sh4State { Pc = 0x8C00_0000, Pr = 0x8C01_6708 };
        state.R[0] = 0x1234_5678;

        Assert.True(handler.TryHandle(state, new DreamcastMemory(), out var trace));

        Assert.Equal(0x8C01_6708u, state.Pc);
        Assert.Equal(0u, state.R[0]);
        Assert.Equal("firmware default callback hle ; pc=0x8C016708, r0=0x00000000", trace);
    }

    [Theory]
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
    public void WindowsCePerformCallbackTransfersToCallbackFunction()
    {
        var handler = FirmwareStubs.CreateTrapHandler();
        var memory = new DreamcastMemory();
        var state = new Sh4State { Pc = 0xFFFF_FD1F, Pr = 0x8C02_2D7C };
        state.R[4] = 0x8C13_7538;
        state.R[5] = 0x8CEE_E654;
        state.R[6] = 1;
        state.R[7] = 0x8C13_7534;
        memory.WriteUInt32(0x8C13_7538, 0x0CEE_EFE2);
        memory.WriteUInt32(0x8C13_753C, 0x8C02_1FA0);
        memory.WriteUInt32(0x8C13_7540, 0x8C01_16E0);

        Assert.True(handler.TryHandle(state, memory, out var trace));

        Assert.Equal(0x8C02_1FA0u, state.Pc);
        Assert.Equal(0x8C02_2D7Cu, state.Pr);
        Assert.Equal(0x8C01_16E0u, state.R[4]);
        Assert.Equal(0x8CEE_E654u, state.R[5]);
        Assert.Equal(1u, state.R[6]);
        Assert.Equal(0x8C13_7534u, state.R[7]);
        Assert.Equal("firmware wince hle WIN32.PerformCallBack address=0xFFFFFD1F callback=0x8C137538, hproc=0x0CEEEFE2, pfn=0x8C021FA0, arg0=0x8C0116E0, r5=0x8CEEE654, r6=0x00000001, r7=0x8C137534 ; pc=0x8C021FA0, pr=0x8C022D7C", trace);
    }

    [Fact]
    public void WindowsCeSimpleSyscallReturnsZeroToCaller()
    {
        var handler = FirmwareStubs.CreateTrapHandler();
        var state = new Sh4State { Pc = 0xFFFF_FD5D, Pr = 0x8C02_0000 };
        state.R[4] = 42;

        Assert.True(handler.TryHandle(state, new DreamcastMemory(), out var trace));

        Assert.Equal(0x8C02_0000u, state.Pc);
        Assert.Equal(0u, state.R[0]);
        Assert.Equal("firmware wince hle WIN32.Sleep address=0xFFFFFD5D r4=0x0000002A, r5=0x00000000, r6=0x00000000, r7=0x00000000 ; r0=0x00000000, pc=0x8C020000", trace);
    }

    [Fact]
    public void WindowsCeSetProcPermissionsReturnsPreviousPermissions()
    {
        var handler = FirmwareStubs.CreateTrapHandler();
        var state = new Sh4State { Pc = 0xFFFF_F9F9, Pr = 0x01E3_24F6 };
        state.R[4] = 0x42;
        state.R[5] = 1;
        state.R[7] = 0x8CEE_E5F4;

        Assert.True(handler.TryHandle(state, new DreamcastMemory(), out var trace));

        Assert.Equal(0x01E3_24F6u, state.Pc);
        Assert.Equal(0xFFFF_FFFFu, state.R[0]);
        Assert.Equal("firmware wince hle CURPROC.SetProcPermissions address=0xFFFFF9F9 permissions=0x00000042, previous=0xFFFFFFFF, r5=0x00000001, r6=0x00000000, r7=0x8CEEE5F4 ; r0=0xFFFFFFFF, pc=0x01E324F6", trace);

        state.Pc = 0xFFFF_F9F9;
        state.Pr = 0x01E3_2500;
        state.R[4] = 0xFFFF_FFFF;

        Assert.True(handler.TryHandle(state, new DreamcastMemory(), out trace));

        Assert.Equal(0x01E3_2500u, state.Pc);
        Assert.Equal(0x42u, state.R[0]);
        Assert.Equal("firmware wince hle CURPROC.SetProcPermissions address=0xFFFFF9F9 permissions=0xFFFFFFFF, previous=0x00000042, r5=0x00000001, r6=0x00000000, r7=0x8CEEE5F4 ; r0=0x00000042, pc=0x01E32500", trace);
    }

    [Fact]
    public void WindowsCeWin32CreateCritReturnsStableHandle()
    {
        var handler = FirmwareStubs.CreateTrapHandler();
        var state = new Sh4State { Pc = 0xFFFF_FD65, Pr = 0x01E3_8A3E };
        state.R[4] = 0x01E4_C0C0;
        state.R[5] = 1;
        state.R[7] = 0x8CEE_E5F4;

        Assert.True(handler.TryHandle(state, new DreamcastMemory(), out var trace));

        Assert.Equal(0x01E3_8A3Eu, state.Pc);
        Assert.Equal(0x0CEE_C100u, state.R[0]);
        Assert.Equal("firmware wince hle WIN32.CreateCrit address=0xFFFFFD65 criticalSection=0x01E4C0C0, handle=0x0CEEC100, r5=0x00000001, r6=0x00000000, r7=0x8CEEE5F4 ; r0=0x0CEEC100, pc=0x01E38A3E", trace);

        state.Pc = 0xFFFF_FD65;
        state.R[0] = 0;

        Assert.True(handler.TryHandle(state, new DreamcastMemory(), out trace));

        Assert.Equal(0x0CEE_C100u, state.R[0]);
        Assert.Equal("firmware wince hle WIN32.CreateCrit address=0xFFFFFD65 criticalSection=0x01E4C0C0, handle=0x0CEEC100, r5=0x00000001, r6=0x00000000, r7=0x8CEEE5F4 ; r0=0x0CEEC100, pc=0x01E38A3E", trace);
    }

    [Fact]
    public void GdromCheckCommandReportsCompletedReadAndTransferredBytes()
    {
        var memory = new DreamcastMemory(media: new RawSectorMediaImage(CreateMediaData(2), 2048));
        WritePioReadParameters(memory);
        var handler = FirmwareStubs.CreateTrapHandler();

        var commandId = SendGdromCommand(handler, memory, GdromCommandPioRead, ParameterAddress);
        var processingResponse = CheckGdromCommand(handler, memory, commandId);
        Assert.Equal(GdromProcessing, processingResponse);
        Assert.Equal(0, memory.ReadByte(DestinationAddress));

        Assert.Equal(0u, ExecGdromServer(handler, memory));
        var response = CheckGdromCommand(handler, memory, commandId);

        Assert.Equal(1u, commandId);
        Assert.Equal(GdromCompleted, response);
        Assert.Equal(0u, memory.ReadUInt32(StatusAddress));
        Assert.Equal(0u, memory.ReadUInt32(StatusAddress + 4));
        Assert.Equal(2048u, memory.ReadUInt32(StatusAddress + 8));
        Assert.Equal(0u, memory.ReadUInt32(StatusAddress + 12));
        Assert.Equal(0x20, memory.ReadByte(DestinationAddress));
        var inactiveResponse = CheckGdromCommand(handler, memory, commandId);
        Assert.Equal(GdromNoActive, inactiveResponse);

        var activities = memory.CreateGdromSnapshot().CommandActivities;
        Assert.Collection(
            activities,
            send =>
            {
                Assert.Equal("send", send.Operation);
                Assert.Equal(1u, send.CommandId);
                Assert.Equal(GdromCommandPioRead, send.Command);
                Assert.Equal("PIO_READ", send.CommandName);
                Assert.Equal(ParameterAddress, send.ParameterAddress);
                Assert.Equal((int)GdromProcessing, send.Response);
                Assert.Equal("processing", send.ResponseName);
            },
            processing =>
            {
                Assert.Equal("check", processing.Operation);
                Assert.Equal((int)GdromProcessing, processing.Response);
                Assert.Equal("processing", processing.ResponseName);
            },
            exec =>
            {
                Assert.Equal("exec", exec.Operation);
                Assert.Equal(1u, exec.CommandId);
                Assert.Equal(GdromCommandPioRead, exec.Command);
                Assert.Equal((int)GdromCompleted, exec.Response);
                Assert.Equal(2048, exec.TransferredBytes);
            },
            check =>
            {
                Assert.Equal("check", check.Operation);
                Assert.Equal(1u, check.CommandId);
                Assert.Equal(GdromCommandPioRead, check.Command);
                Assert.Equal(StatusAddress, check.StatusAddress);
                Assert.Equal((int)GdromCompleted, check.Response);
                Assert.Equal("completed", check.ResponseName);
                Assert.Equal(2048, check.TransferredBytes);
                Assert.Equal("command status reported", check.Status);
            },
            inactive =>
            {
                Assert.Equal("check", inactive.Operation);
                Assert.Equal(1u, inactive.CommandId);
                Assert.Null(inactive.Command);
                Assert.Equal((int)GdromNoActive, inactive.Response);
                Assert.Equal("no active", inactive.ResponseName);
            });
    }

    [Fact]
    public void GdromDmaReadCommandRaisesDmaCompleteInterruptSource()
    {
        var memory = new DreamcastMemory(media: new RawSectorMediaImage(CreateMediaData(2), 2048));
        memory.WriteUInt32(0xA05F_6920, 1u << 14);
        WritePioReadParameters(memory);
        var handler = FirmwareStubs.CreateTrapHandler();

        var commandId = SendGdromCommand(handler, memory, GdromCommandDmaRead, ParameterAddress);
        var processingResponse = CheckGdromCommand(handler, memory, commandId);
        Assert.Equal(GdromProcessing, processingResponse);
        Assert.False(memory.TryGetPendingExternalInterrupt(out _, out _));

        Assert.Equal(0u, ExecGdromServer(handler, memory));
        var response = CheckGdromCommand(handler, memory, commandId);

        Assert.Equal(GdromCompleted, response);
        Assert.Equal(2048u, memory.ReadUInt32(StatusAddress + 8));
        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0360u, eventCode);
        Assert.Equal(11, level);
        var pending = memory.CreateAsicSnapshot().PendingInterrupt;
        Assert.Equal("IRQB", pending?.LevelName);
        Assert.Equal("A", pending?.RegisterName);
        Assert.Equal(14, pending?.Bit);
        var read = Assert.Single(memory.CreateGdromSnapshot().ReadCommands);
        Assert.True(read.Success);
    }

    [Fact]
    public void GdromCommandExecutionRaisesCommandStatusInterruptSource()
    {
        var memory = new DreamcastMemory(media: new RawSectorMediaImage(CreateMediaData(2), 2048));
        memory.WriteUInt32(0xA05F_6924, 1);
        WritePioReadParameters(memory);
        var handler = FirmwareStubs.CreateTrapHandler();

        var commandId = SendGdromCommand(handler, memory, GdromCommandPioRead, ParameterAddress);
        Assert.False(memory.TryGetPendingExternalInterrupt(out _, out _));

        Assert.Equal(0u, ExecGdromServer(handler, memory));

        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0360u, eventCode);
        Assert.Equal(11, level);
        var pending = memory.CreateAsicSnapshot().PendingInterrupt;
        Assert.Equal("IRQB", pending?.LevelName);
        Assert.Equal("B", pending?.RegisterName);
        Assert.Equal(0, pending?.Bit);
        Assert.Equal(GdromCompleted, CheckGdromCommand(handler, memory, commandId));
    }

    [Fact]
    public void GdromDmaStreamTransfersSectorsAndReportsCompletion()
    {
        var memory = new DreamcastMemory(media: new RawSectorMediaImage(CreateMediaData(3), 2048));
        memory.WriteUInt32(0xA05F_6920, 1u << 14);
        WriteStreamParameters(memory, 0, 2);
        var handler = FirmwareStubs.CreateTrapHandler();

        var commandId = SendGdromCommand(handler, memory, GdromCommandDmaReadStream, ParameterAddress);
        Assert.Equal(0u, ExecGdromServer(handler, memory));
        Assert.Equal(GdromStreaming, CheckGdromCommand(handler, memory, commandId));

        WriteTransferParameters(memory, DestinationAddress, 2048);
        Assert.Equal(0u, CallGdromTransfer(handler, memory, GdromDmaTransfer, commandId));
        Assert.Equal(0x10, memory.ReadByte(DestinationAddress));
        Assert.True(memory.TryGetPendingExternalInterrupt(out var eventCode, out var level));
        Assert.Equal(0x0360u, eventCode);
        Assert.Equal(11, level);
        Assert.Equal(0u, CheckGdromTransfer(handler, memory, GdromDmaCheck, commandId));
        Assert.Equal(0u, memory.ReadUInt32(TransferSizeAddress));
        Assert.Equal(GdromStreaming, CheckGdromCommand(handler, memory, commandId));

        memory.WriteUInt32(0xA05F_6900, 1u << 14);
        WriteTransferParameters(memory, DestinationAddress + 2048, 2048);
        Assert.Equal(0u, CallGdromTransfer(handler, memory, GdromDmaTransfer, commandId));
        Assert.Equal(0x20, memory.ReadByte(DestinationAddress + 2048));
        Assert.Equal(GdromCompleted, CheckGdromCommand(handler, memory, commandId));
        Assert.Equal(2048u, memory.ReadUInt32(StatusAddress + 8));
        Assert.Equal(GdromNoActive, CheckGdromCommand(handler, memory, commandId));

        var reads = memory.CreateGdromSnapshot().ReadCommands;
        Assert.Equal(2, reads.Count);
        Assert.Equal(0u, reads[0].Sector);
        Assert.Equal(1u, reads[1].Sector);
    }

    [Fact]
    public void GdromPioStreamTransferDoesNotRaiseDmaInterruptSource()
    {
        var memory = new DreamcastMemory(media: new RawSectorMediaImage(CreateMediaData(2), 2048));
        memory.WriteUInt32(0xA05F_6920, 1u << 14);
        WriteStreamParameters(memory, 1, 1);
        var handler = FirmwareStubs.CreateTrapHandler();

        var commandId = SendGdromCommand(handler, memory, GdromCommandPioReadStream, ParameterAddress);
        Assert.Equal(0u, ExecGdromServer(handler, memory));
        Assert.Equal(GdromStreaming, CheckGdromCommand(handler, memory, commandId));
        WriteTransferParameters(memory, DestinationAddress, 2048);

        Assert.Equal(0u, CallGdromTransfer(handler, memory, GdromPioTransfer, commandId));

        Assert.Equal(0x20, memory.ReadByte(DestinationAddress));
        Assert.False(memory.TryGetPendingExternalInterrupt(out _, out _));
        Assert.Equal(0u, CheckGdromTransfer(handler, memory, GdromPioCheck, commandId));
        Assert.Equal(GdromCompleted, CheckGdromCommand(handler, memory, commandId));
    }

    [Fact]
    public void GdromCheckCommandReportsFailedReadWhenNoMediaIsLoaded()
    {
        var memory = new DreamcastMemory();
        WritePioReadParameters(memory);
        var handler = FirmwareStubs.CreateTrapHandler();

        var commandId = SendGdromCommand(handler, memory, GdromCommandPioRead, ParameterAddress);
        Assert.Equal(0u, ExecGdromServer(handler, memory));
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
        Assert.Equal(0u, ExecGdromServer(handler, memory));
        var nopResponse = CheckGdromCommand(handler, memory, nopCommandId);
        var commandId = SendGdromCommand(handler, memory, GdromCommandNop, 0);
        AbortGdromCommand(handler, memory, commandId);
        var abortedResponse = CheckGdromCommand(handler, memory, commandId);

        Assert.Equal(GdromNoActive, unknownResponse);
        Assert.Equal(GdromCompleted, nopResponse);
        Assert.Equal(GdromNoActive, abortedResponse);
    }

    [Fact]
    public void GdromInitAndResetSyscallsDoNotAbortQueuedCommands()
    {
        var memory = new DreamcastMemory();
        var handler = FirmwareStubs.CreateTrapHandler();
        var commandId = SendGdromCommand(handler, memory, GdromCommandNop, 0);

        Assert.Equal(0u, CallGdromFunction(handler, memory, GdromInit));
        Assert.Equal(0u, CallGdromFunction(handler, memory, GdromReset));
        Assert.Equal(0u, ExecGdromServer(handler, memory));

        Assert.Equal(GdromCompleted, CheckGdromCommand(handler, memory, commandId));
    }

    [Fact]
    public void GdromTraceIncludesSyscallArguments()
    {
        var memory = new DreamcastMemory();
        var handler = FirmwareStubs.CreateTrapHandler();
        var state = CreateGdromState(GdromSendCommand);
        state.R[4] = GdromCommandInit;
        state.R[5] = 0x8CFF_FF7C;

        Assert.True(handler.TryHandle(state, memory, out var trace));

        Assert.Equal("firmware gdrom hle func=0 r4=0x00000018, r5=0x8CFFFF7C, r6=0x00000000, r7=0x00000000 ; r0=0x00000001", trace);
    }

    [Fact]
    public void GdromInitAndVersionCommandsCompleteWithoutTransfer()
    {
        var memory = new DreamcastMemory();
        var handler = FirmwareStubs.CreateTrapHandler();

        var initCommandId = SendGdromCommand(handler, memory, GdromCommandInit, 0);
        Assert.Equal(0u, ExecGdromServer(handler, memory));
        var initResponse = CheckGdromCommand(handler, memory, initCommandId);
        var versionCommandId = SendGdromCommand(handler, memory, GdromCommandGetVersion, 0);
        Assert.Equal(0u, ExecGdromServer(handler, memory));
        var versionResponse = CheckGdromCommand(handler, memory, versionCommandId);

        Assert.Equal(GdromCompleted, initResponse);
        Assert.Equal(0u, memory.ReadUInt32(StatusAddress + 8));
        Assert.Equal(GdromCompleted, versionResponse);
        Assert.Empty(memory.CreateGdromSnapshot().ReadCommands);
    }

    [Fact]
    public void GdromGetVersionWritesBiosVersionString()
    {
        var memory = new DreamcastMemory();
        memory.WriteUInt32(ParameterAddress, DestinationAddress);
        var handler = FirmwareStubs.CreateTrapHandler();

        var versionCommandId = SendGdromCommand(handler, memory, GdromCommandGetVersion, ParameterAddress);
        Assert.Equal(0u, ExecGdromServer(handler, memory));
        var versionResponse = CheckGdromCommand(handler, memory, versionCommandId);

        Assert.Equal(GdromCompleted, versionResponse);
        Assert.Equal((byte)'G', memory.ReadByte(DestinationAddress));
        Assert.Equal((byte)'D', memory.ReadByte(DestinationAddress + 1));
        Assert.Equal((byte)'C', memory.ReadByte(DestinationAddress + 2));
        Assert.Equal((byte)' ', memory.ReadByte(DestinationAddress + 3));
        Assert.Equal(0x02, memory.ReadByte(DestinationAddress + 27));
    }

    [Fact]
    public void GdromGetToc2WritesSingleDataTrackToc()
    {
        var memory = new DreamcastMemory(media: new RawSectorMediaImage(CreateMediaData(3), 2048));
        memory.WriteUInt32(ParameterAddress, 0);
        memory.WriteUInt32(ParameterAddress + 4, TocAddress);
        var handler = FirmwareStubs.CreateTrapHandler();

        var commandId = SendGdromCommand(handler, memory, GdromCommandGetToc2, ParameterAddress);
        Assert.Equal(0u, ExecGdromServer(handler, memory));
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
    public void GdromCommandUsesQueuedParameterSnapshot()
    {
        var memory = new DreamcastMemory(media: new RawSectorMediaImage(CreateMediaData(3), 2048));
        const uint laterStackValue = DestinationAddress;
        memory.WriteUInt32(ParameterAddress, 0);
        memory.WriteUInt32(ParameterAddress + 4, TocAddress);
        var handler = FirmwareStubs.CreateTrapHandler();

        var commandId = SendGdromCommand(handler, memory, GdromCommandGetToc2, ParameterAddress);
        memory.WriteUInt32(ParameterAddress + 4, laterStackValue);

        Assert.Equal(0u, ExecGdromServer(handler, memory));
        var response = CheckGdromCommand(handler, memory, commandId);
        var toc = Assert.Single(memory.CreateGdromSnapshot().TocCommands);

        Assert.Equal(GdromCompleted, response);
        Assert.Equal(0x4000_AFC8u, memory.ReadUInt32(TocAddress + 8));
        Assert.Equal(0u, memory.ReadUInt32(laterStackValue + 8));
        Assert.Equal(TocAddress, toc.BufferAddress);
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
        Assert.Equal(0u, ExecGdromServer(handler, memory));
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
        Assert.Equal(0u, ExecGdromServer(handler, memory));
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
        Assert.Equal(0x80u, memory.ReadUInt32(StatusAddress + 4));
        Assert.Equal(2, status.StatusCode);
        Assert.Equal("standby", status.StatusName);
        Assert.Equal(0x80, status.DiscType);
        Assert.Equal("GD-ROM", status.DiscTypeName);
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

    private static uint ExecGdromServer(
        FirmwareStubs.FirmwareTrapHandler handler,
        DreamcastMemory memory)
    {
        var state = CreateGdromState(GdromExecServer);
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

    private static uint CallGdromFunction(
        FirmwareStubs.FirmwareTrapHandler handler,
        DreamcastMemory memory,
        uint function)
    {
        var state = CreateGdromState(function);
        Assert.True(handler.TryHandle(state, memory, out _));
        return state.R[0];
    }

    private static uint CallGdromTransfer(
        FirmwareStubs.FirmwareTrapHandler handler,
        DreamcastMemory memory,
        uint function,
        uint commandId)
    {
        var state = CreateGdromState(function);
        state.R[4] = commandId;
        state.R[5] = TransferParameterAddress;
        Assert.True(handler.TryHandle(state, memory, out _));
        return state.R[0];
    }

    private static uint CheckGdromTransfer(
        FirmwareStubs.FirmwareTrapHandler handler,
        DreamcastMemory memory,
        uint function,
        uint commandId)
    {
        var state = CreateGdromState(function);
        state.R[4] = commandId;
        state.R[5] = TransferSizeAddress;
        Assert.True(handler.TryHandle(state, memory, out _));
        return state.R[0];
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

    private static void WriteStreamParameters(DreamcastMemory memory, uint sector, uint sectorCount)
    {
        memory.WriteUInt32(ParameterAddress, sector);
        memory.WriteUInt32(ParameterAddress + 4, sectorCount);
    }

    private static void WriteTransferParameters(DreamcastMemory memory, uint destination, uint byteCount)
    {
        memory.WriteUInt32(TransferParameterAddress, destination);
        memory.WriteUInt32(TransferParameterAddress + 4, byteCount);
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

    private static string ReadAscii(DreamcastMemory memory, uint address, int length)
    {
        var bytes = new byte[length];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = memory.ReadByte(address + (uint)index);
        }

        return System.Text.Encoding.ASCII.GetString(bytes);
    }

    private static byte[] CreateIsoWithRootDirectory(string volumeIdentifier, string directoryName)
    {
        var image = new byte[2048 * 24];
        var pvd = image.AsSpan(16 * 2048, 2048);
        pvd[0] = 1;
        System.Text.Encoding.ASCII.GetBytes("CD001").CopyTo(pvd[1..]);
        pvd[6] = 1;
        WriteAscii(image, (16 * 2048) + 40, 32, volumeIdentifier);
        WriteDirectoryRecord(pvd, 156, 20, 2048, 0x02, [0]);

        var directory = image.AsSpan(20 * 2048, 2048);
        var offset = 0;
        offset += WriteDirectoryRecord(directory, offset, 20, 2048, 0x02, [0]);
        offset += WriteDirectoryRecord(directory, offset, 20, 2048, 0x02, [1]);
        WriteDirectoryRecord(directory, offset, 21, 2048, 0x02, System.Text.Encoding.ASCII.GetBytes(directoryName));
        return image;
    }

    private static void WriteAscii(byte[] data, int offset, int length, string text)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(text);
        Array.Copy(bytes, 0, data, offset, Math.Min(bytes.Length, length));
        for (var index = bytes.Length; index < length; index++)
        {
            data[offset + index] = 0x20;
        }
    }

    private static int WriteDirectoryRecord(Span<byte> destination, int offset, uint extent, uint dataLength, byte flags, byte[] name)
    {
        var length = 33 + name.Length + (name.Length % 2 == 0 ? 1 : 0);
        var record = destination.Slice(offset, length);
        record.Clear();
        record[0] = (byte)length;
        WriteUInt32BothEndian(record, 2, extent);
        WriteUInt32BothEndian(record, 10, dataLength);
        record[25] = flags;
        record[28] = 1;
        record[30] = 1;
        record[32] = (byte)name.Length;
        name.CopyTo(record[33..]);
        return length;
    }

    private static void WriteUInt32BothEndian(Span<byte> destination, int offset, uint value)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset, 4), value);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(offset + 4, 4), value);
    }
}
