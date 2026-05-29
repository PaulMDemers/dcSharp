using DcSharp.Core.Dreamcast.Memory;

namespace DcSharp.Core.Execution;

internal static class FirmwareStubs
{
    private const uint SyscallSysinfoVector = 0x8C00_00B0;
    private const uint SyscallFlashromVector = 0x8C00_00B8;
    private const uint SyscallGdromVector = 0x8C00_00BC;
    private const uint SyscallSystemVector = 0x8C00_00E0;
    private const uint ReturnZeroStub = 0x8C00_00C0;
    private const uint GdromHleStub = 0x8C00_00D0;
    private const uint SystemHleStub = 0x8C00_00E8;
    private const uint BiosLanguageCodeAddress = 0x8C00_0074;
    private const uint BiosBootModeAddress = 0x8C00_80FC;
    private const uint BiosBootAreaModeAddress = 0x8C00_80FE;
    private const byte DefaultBiosLanguageCode = (byte)'1';
    private const byte DefaultBiosBootMode = 1;

    public static void Install(DreamcastMemory memory)
    {
        memory.WriteUInt32(SyscallSysinfoVector, ReturnZeroStub);
        memory.WriteUInt32(SyscallFlashromVector, ReturnZeroStub);
        memory.WriteUInt32(SyscallGdromVector, GdromHleStub);
        memory.WriteUInt32(SyscallSystemVector, SystemHleStub);
        memory.Write(BiosLanguageCodeAddress, [DefaultBiosLanguageCode]);
        memory.Write(BiosBootModeAddress, [DefaultBiosBootMode]);
        memory.Write(BiosBootAreaModeAddress, [DefaultBiosBootMode]);
        memory.Write(ReturnZeroStub,
        [
            0x00, 0xE0, // mov #0,r0
            0x0B, 0x00, // rts
            0x09, 0x00  // nop
        ]);
    }

    public static FirmwareTrapHandler CreateTrapHandler() => new();

    internal sealed class FirmwareTrapHandler
    {
        private const uint DefaultSoftResetEntryPoint = 0x8C01_0000;
        private const uint DefaultSoftResetStackPointer = 0x8D00_0000;
        private const uint SuperFunctionGdrom = 0;
        private const uint GdromFunctionSendCommand = 0;
        private const uint GdromFunctionCheckCommand = 1;
        private const uint GdromFunctionExecServer = 2;
        private const uint GdromFunctionInit = 3;
        private const uint GdromFunctionDriveStatus = 4;
        private const uint GdromFunctionDmaCallback = 5;
        private const uint GdromFunctionDmaTransfer = 6;
        private const uint GdromFunctionDmaCheck = 7;
        private const uint GdromFunctionAbortCommand = 8;
        private const uint GdromFunctionReset = 9;
        private const uint GdromFunctionSectorMode = 10;
        private const uint GdromFunctionPioCallback = 11;
        private const uint GdromFunctionPioTransfer = 12;
        private const uint GdromFunctionPioCheck = 13;
        private const uint GdromCommandPioRead = 16;
        private const uint GdromCommandDmaRead = 17;
        private const uint GdromCommandGetToc2 = 19;
        private const uint GdromCommandInit = 24;
        private const uint GdromCommandGetVersion = 40;
        private const int GdromTocWords = 102;
        private const int GdromFailed = -1;
        private const int GdromNoActive = 0;
        private const int GdromCompleted = 2;
        private const int GdromNoDiscStatus = 2;
        private const int CdStatusStandby = 2;
        private const int CdStatusNoDisc = 7;
        private const int CdCdda = 0x00;
        private const int CdGdrom = 0x80;

        private uint nextCommandId = 1;
        private readonly Dictionary<uint, GdromQueuedCommand> commands = [];

        public FirmwareTrapHandler(
            uint softResetEntryPoint = DefaultSoftResetEntryPoint,
            uint softResetStackPointer = DefaultSoftResetStackPointer)
        {
            SoftResetEntryPoint = softResetEntryPoint;
            SoftResetStackPointer = softResetStackPointer;
        }

        public uint SoftResetEntryPoint { get; }
        public uint SoftResetStackPointer { get; }

        public bool TryHandle(DcSharp.Core.Cpu.Sh4State state, DreamcastMemory memory, out string trace)
        {
            if (state.Pc == SystemHleStub)
            {
                var systemR4 = state.R[4];
                var systemR5 = state.R[5];
                var systemR6 = state.R[6];
                var systemR7 = state.R[7];
                var systemPr = state.Pr;
                if (state.R[4] == 0)
                {
                    memory.Write(BiosBootAreaModeAddress, [0]);
                    state.Pc = SoftResetEntryPoint;
                    state.R[15] = SoftResetStackPointer;
                    trace = $"firmware system hle func=0 r4=0x{systemR4:X8}, r5=0x{systemR5:X8}, r6=0x{systemR6:X8}, r7=0x{systemR7:X8}, pr=0x{systemPr:X8} ; pc=0x{state.Pc:X8}, sp=0x{state.R[15]:X8}";
                    return true;
                }

                if (state.R[4] == 3)
                {
                    state.R[0] = 0;
                    state.Pc = state.Pr;
                    trace = $"firmware system hle func=3 r4=0x{systemR4:X8}, r5=0x{systemR5:X8}, r6=0x{systemR6:X8}, r7=0x{systemR7:X8}, pr=0x{systemPr:X8} ; r0=0x{state.R[0]:X8}";
                    return true;
                }

                throw new DreamcastFirmwareExitException(SystemCallMessage(state.R[4]));
            }

            if (state.Pc != GdromHleStub)
            {
                trace = string.Empty;
                return false;
            }

            var function = state.R[7];
            if (state.R[6] != SuperFunctionGdrom)
            {
                state.R[0] = 0;
                state.Pc = state.Pr;
                trace = $"firmware misc hle func={function} r4=0x{state.R[4]:X8}, r5=0x{state.R[5]:X8}, r6=0x{state.R[6]:X8}, r7=0x{state.R[7]:X8} ; r0=0x{state.R[0]:X8}";
                return true;
            }

            var r4 = state.R[4];
            var r5 = state.R[5];
            var r6 = state.R[6];
            var r7 = state.R[7];
            state.R[0] = HandleGdrom(function, state, memory);
            state.Pc = state.Pr;
            trace = $"firmware gdrom hle func={function} r4=0x{r4:X8}, r5=0x{r5:X8}, r6=0x{r6:X8}, r7=0x{r7:X8} ; r0=0x{state.R[0]:X8}";
            return true;
        }

        private uint HandleGdrom(uint function, DcSharp.Core.Cpu.Sh4State state, DreamcastMemory memory) =>
            function switch
            {
                GdromFunctionSendCommand => SendCommand(state, memory),
                GdromFunctionCheckCommand => CheckCommand(state, memory),
                GdromFunctionExecServer => 0,
                GdromFunctionInit => 0,
                GdromFunctionDriveStatus => CheckDrive(state, memory),
                GdromFunctionDmaCallback => 0,
                GdromFunctionDmaTransfer => 0,
                GdromFunctionDmaCheck => CheckTransfer(state, memory),
                GdromFunctionAbortCommand => AbortCommand(state, memory),
                GdromFunctionReset => 0,
                GdromFunctionSectorMode => SectorMode(state, memory),
                GdromFunctionPioCallback => 0,
                GdromFunctionPioTransfer => 0,
                GdromFunctionPioCheck => CheckTransfer(state, memory),
                _ => 0
            };

        private uint SendCommand(DcSharp.Core.Cpu.Sh4State state, DreamcastMemory memory)
        {
            var commandId = nextCommandId++;
            var command = ExecuteCommand(state.R[4], state.R[5], memory);
            commands[commandId] = command;
            memory.RecordGdromCommandActivity(
                "send",
                commandId,
                state.R[4],
                GdromCommandName(state.R[4]),
                state.R[5],
                null,
                command.Response,
                GdromResponseName(command.Response),
                command.Status0,
                command.Status1,
                command.TransferredBytes,
                command.AtaStatus,
                "command queued");
            return commandId;
        }

        private static GdromQueuedCommand ExecuteCommand(uint command, uint parameters, DreamcastMemory memory)
        {
            if (command is GdromCommandPioRead or GdromCommandDmaRead)
            {
                memory.ExecuteGdromPioReadCommand(parameters);
                var read = memory.CreateGdromSnapshot().ReadCommands.LastOrDefault();
                return read?.Success == true
                    ? GdromQueuedCommand.Completed(command, parameters, 0, 0, read.BytesRead, 0)
                    : GdromQueuedCommand.Failed(command, parameters, MapReadFailureStatus(read), 0, read?.BytesRead ?? 0, 0);
            }

            if (command == GdromCommandGetToc2)
            {
                return WriteToc2(parameters, memory);
            }

            if (command == GdromCommandInit)
            {
                return GdromQueuedCommand.Completed(command, parameters, 0, 0, 0, 0);
            }

            if (command == GdromCommandGetVersion)
            {
                WriteGetVersion(parameters, memory);
                return GdromQueuedCommand.Completed(command, parameters, 0, 0, 0, 0);
            }

            return GdromQueuedCommand.Completed(command, parameters, 0, 0, 0, 0);
        }

        private static int MapReadFailureStatus(DreamcastGdromReadCommand? read) =>
            read?.Status == "no media image loaded" ? GdromNoDiscStatus : 0;

        private static GdromQueuedCommand WriteToc2(uint parameters, DreamcastMemory memory)
        {
            var snapshot = memory.CreateGdromSnapshot();
            var buffer = parameters == 0 ? 0 : memory.ReadUInt32(parameters + 4);
            if (!snapshot.HasMedia)
            {
                memory.RecordGdromTocCommand(parameters, buffer == 0 ? null : buffer, null, null, null, null, false, "no media image loaded");
                return GdromQueuedCommand.Failed(GdromCommandGetToc2, parameters, GdromNoDiscStatus, 0, 0, 0);
            }

            if (buffer == 0)
            {
                memory.RecordGdromTocCommand(parameters, null, null, null, null, null, false, "missing TOC buffer");
                return GdromQueuedCommand.Failed(GdromCommandGetToc2, parameters, 0, 0, 0, 0);
            }

            for (var i = 0; i < GdromTocWords; i++)
            {
                memory.WriteUInt32(buffer + ((uint)i * 4), 0);
            }

            var firstTrack = snapshot.MediaTracks.Min(track => track.TrackNumber);
            var lastTrack = snapshot.MediaTracks.Max(track => track.TrackNumber);
            var dataTrackStartFad = snapshot.MediaTracks
                .Where(track => track.Control == 4)
                .OrderBy(track => track.TrackNumber)
                .Last()
                .StartFad;
            var leadoutFad = snapshot.LeadoutFad ?? 0;
            foreach (var track in snapshot.MediaTracks.Where(track => track.TrackNumber is >= 1 and <= 99))
            {
                memory.WriteUInt32(buffer + ((uint)(track.TrackNumber - 1) * 4), PackTocEntry((uint)track.Control, track.StartFad));
            }

            memory.WriteUInt32(buffer + 396, (uint)firstTrack << 16);
            memory.WriteUInt32(buffer + 400, (uint)lastTrack << 16);
            memory.WriteUInt32(buffer + 404, PackTocEntry(0, leadoutFad));
            memory.RecordGdromTocCommand(parameters, buffer, firstTrack, lastTrack, dataTrackStartFad, leadoutFad, true, "TOC written");
            return GdromQueuedCommand.Completed(GdromCommandGetToc2, parameters, 0, 0, 0, 0);
        }

        private static void WriteGetVersion(uint parameters, DreamcastMemory memory)
        {
            if (parameters == 0)
            {
                return;
            }

            var buffer = memory.ReadUInt32(parameters);
            if (buffer == 0)
            {
                return;
            }

            ReadOnlySpan<byte> version =
            [
                (byte)'G', (byte)'D', (byte)'C', (byte)' ',
                (byte)'V', (byte)'e', (byte)'r', (byte)'s', (byte)'i', (byte)'o', (byte)'n', (byte)' ',
                (byte)'1', (byte)'.', (byte)'1', (byte)'0', (byte)' ',
                (byte)'1', (byte)'9', (byte)'9', (byte)'9', (byte)'-', (byte)'0', (byte)'3', (byte)'-', (byte)'3', (byte)'1',
                0x02
            ];

            memory.Write(buffer, version);
        }

        private static uint PackTocEntry(uint control, uint fad) =>
            ((control & 0xFu) << 28) | (fad & 0x00FF_FFFFu);

        private uint CheckCommand(DcSharp.Core.Cpu.Sh4State state, DreamcastMemory memory)
        {
            if (!commands.TryGetValue(state.R[4], out var command))
            {
                command = new GdromQueuedCommand(null, null, GdromNoActive, 0, 0, 0, 0);
            }

            WriteWords(memory, state.R[5], command.Status0, command.Status1, command.TransferredBytes, command.AtaStatus);
            memory.RecordGdromCommandActivity(
                "check",
                state.R[4],
                command.Command,
                command.Command is { } commandValue ? GdromCommandName(commandValue) : null,
                command.ParameterAddress,
                state.R[5],
                command.Response,
                GdromResponseName(command.Response),
                command.Status0,
                command.Status1,
                command.TransferredBytes,
                command.AtaStatus,
                command.Response == GdromNoActive ? "no active command" : "command status reported");
            return unchecked((uint)command.Response);
        }

        private uint AbortCommand(DcSharp.Core.Cpu.Sh4State state, DreamcastMemory memory)
        {
            var removed = commands.Remove(state.R[4], out var command);
            memory.RecordGdromCommandActivity(
                "abort",
                state.R[4],
                command?.Command,
                command?.Command is { } commandValue ? GdromCommandName(commandValue) : null,
                command?.ParameterAddress,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                removed ? "command aborted" : "no active command");
            return 0;
        }

        private static uint CheckDrive(DcSharp.Core.Cpu.Sh4State state, DreamcastMemory memory)
        {
            var snapshot = memory.CreateGdromSnapshot();
            var statusCode = snapshot.HasMedia ? CdStatusStandby : CdStatusNoDisc;
            var discType = snapshot.HasMedia ? CdGdrom : CdCdda;
            var statusName = snapshot.HasMedia ? "standby" : "no disc";
            var discTypeName = snapshot.HasMedia ? "GD-ROM" : "CDDA/no disc";

            WriteWords(memory, state.R[4], statusCode, discType);
            memory.RecordGdromStatusCommand(state.R[4], statusCode, statusName, discType, discTypeName, true, "drive status reported");
            return 0;
        }

        private static uint CheckTransfer(DcSharp.Core.Cpu.Sh4State state, DreamcastMemory memory)
        {
            if (state.R[5] != 0)
            {
                memory.WriteUInt32(state.R[5], 0);
            }

            return 0;
        }

        private static uint SectorMode(DcSharp.Core.Cpu.Sh4State state, DreamcastMemory memory)
        {
            var parameters = state.R[4];
            if (parameters == 0)
            {
                memory.RecordGdromSectorModeCommand(0, -1, 0, 0, 0, false, "missing parameter block");
                return 0;
            }

            var request = unchecked((int)memory.ReadUInt32(parameters));
            if (request == 1)
            {
                WriteWords(memory, parameters, 1, 0x2000, 2048, 2048);
                memory.RecordGdromSectorModeCommand(parameters, request, 0x2000, 2048, 2048, true, "sector mode reported");
                return 0;
            }

            var sectorPart = unchecked((int)memory.ReadUInt32(parameters + 4));
            var cdXa = unchecked((int)memory.ReadUInt32(parameters + 8));
            var sectorSize = unchecked((int)memory.ReadUInt32(parameters + 12));
            memory.RecordGdromSectorModeCommand(parameters, request, sectorPart, cdXa, sectorSize, true, "sector mode set");
            return 0;
        }

        private static void WriteWords(DreamcastMemory memory, uint address, params int[] values)
        {
            if (address == 0)
            {
                return;
            }

            for (var i = 0; i < values.Length; i++)
            {
                memory.WriteUInt32(address + ((uint)i * 4), unchecked((uint)values[i]));
            }
        }

        private static string SystemCallMessage(uint function) =>
            function switch
            {
                0 => "System BIOS soft reset requested: function=0",
                1 => "System BIOS menu requested: function=1",
                2 => "System BIOS CD menu requested: function=2",
                _ => $"System BIOS call requested: function={function}"
            };

        private static string GdromCommandName(uint command) =>
            command switch
            {
                GdromCommandPioRead => "PIO_READ",
                GdromCommandDmaRead => "DMA_READ",
                GdromCommandGetToc2 => "GET_TOC2",
                GdromCommandInit => "INIT",
                29 => "NOP",
                GdromCommandGetVersion => "GET_VERSION",
                _ => "unknown"
            };

        private static string GdromResponseName(int response) =>
            response switch
            {
                GdromFailed => "failed",
                GdromNoActive => "no active",
                GdromCompleted => "completed",
                _ => "unknown"
            };

        private sealed record GdromQueuedCommand(
            uint? Command,
            uint? ParameterAddress,
            int Response,
            int Status0,
            int Status1,
            int TransferredBytes,
            int AtaStatus)
        {
            public static GdromQueuedCommand Completed(uint command, uint parameters, int status0, int status1, int transferredBytes, int ataStatus) =>
                new(command, parameters, GdromCompleted, status0, status1, transferredBytes, ataStatus);

            public static GdromQueuedCommand Failed(uint command, uint parameters, int status0, int status1, int transferredBytes, int ataStatus) =>
                new(command, parameters, GdromFailed, status0, status1, transferredBytes, ataStatus);
        }
    }
}

internal sealed class DreamcastFirmwareExitException(string message) : InvalidOperationException(message);
