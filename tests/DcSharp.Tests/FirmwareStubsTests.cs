using DcSharp.Core.Cpu;
using DcSharp.Core.Dreamcast.Memory;
using DcSharp.Core.Execution;
using DcSharp.Core.Media;

namespace DcSharp.Tests;

public class FirmwareStubsTests
{
    private const uint GdromHleStub = 0x8C00_00D0;
    private const uint GdromSendCommand = 0;
    private const uint GdromCheckCommand = 1;
    private const uint GdromAbortCommand = 3;
    private const uint GdromCommandPioRead = 16;
    private const uint GdromCommandNop = 29;
    private const uint GdromCompleted = 2;
    private const uint GdromNoActive = 0;
    private const uint Sector = 1;
    private const uint ParameterAddress = 0x8C01_0000;
    private const uint StatusAddress = 0x8C01_0100;
    private const uint DestinationAddress = 0x8C02_0000;

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
