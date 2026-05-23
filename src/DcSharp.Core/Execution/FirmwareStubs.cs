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

    public static void Install(DreamcastMemory memory)
    {
        memory.WriteUInt32(SyscallSysinfoVector, ReturnZeroStub);
        memory.WriteUInt32(SyscallFlashromVector, ReturnZeroStub);
        memory.WriteUInt32(SyscallGdromVector, GdromHleStub);
        memory.WriteUInt32(SyscallSystemVector, SystemHleStub);
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
        private const uint SuperFunctionGdrom = 0;
        private const uint GdromCommandPioRead = 16;
        private const uint GdromCommandDmaRead = 17;
        private const uint GdromCommandGetToc2 = 19;
        private const uint GdromDataTrackStartFad = 45_000;
        private const int GdromTocWords = 102;
        private const int GdromFailed = -1;
        private const int GdromNoActive = 0;
        private const int GdromCompleted = 2;
        private const int GdromNoDiscStatus = 2;
        private const int CdStatusStandby = 2;
        private const int CdStatusNoDisc = 7;
        private const int CdCdda = 0x00;
        private const int CdRomXa = 0x20;

        private uint nextCommandId = 1;
        private readonly Dictionary<uint, GdromQueuedCommand> commands = [];

        public bool TryHandle(DcSharp.Core.Cpu.Sh4State state, DreamcastMemory memory, out string trace)
        {
            if (state.Pc == SystemHleStub)
            {
                throw new DreamcastFirmwareExitException($"System BIOS call requested: function={state.R[4]}");
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
                trace = $"firmware misc hle func={function} ; r0=0x{state.R[0]:X8}";
                return true;
            }

            state.R[0] = HandleGdrom(function, state, memory);
            state.Pc = state.Pr;
            trace = $"firmware gdrom hle func={function} ; r0=0x{state.R[0]:X8}";
            return true;
        }

        private uint HandleGdrom(uint function, DcSharp.Core.Cpu.Sh4State state, DreamcastMemory memory) =>
            function switch
            {
                0 => SendCommand(state, memory),
                1 => CheckCommand(state, memory),
                2 => 0,
                3 => AbortCommand(state),
                4 => CheckDrive(state, memory),
                5 => 0,
                6 => 0,
                7 => CheckTransfer(state, memory),
                8 => 0,
                9 => 0,
                10 => SectorMode(state, memory),
                11 => 0,
                12 => 0,
                13 => CheckTransfer(state, memory),
                14 => ReadSectors(state, memory),
                _ => 0
            };

        private uint SendCommand(DcSharp.Core.Cpu.Sh4State state, DreamcastMemory memory)
        {
            var commandId = nextCommandId++;
            commands[commandId] = ExecuteCommand(state.R[4], state.R[5], memory);
            return commandId;
        }

        private static GdromQueuedCommand ExecuteCommand(uint command, uint parameters, DreamcastMemory memory)
        {
            if (command is GdromCommandPioRead or GdromCommandDmaRead)
            {
                memory.ExecuteGdromPioReadCommand(parameters);
                var read = memory.CreateGdromSnapshot().ReadCommands.LastOrDefault();
                return read?.Success == true
                    ? GdromQueuedCommand.Completed(0, 0, read.BytesRead, 0)
                    : GdromQueuedCommand.Failed(MapReadFailureStatus(read), 0, read?.BytesRead ?? 0, 0);
            }

            if (command == GdromCommandGetToc2)
            {
                return WriteToc2(parameters, memory);
            }

            return GdromQueuedCommand.Completed(0, 0, 0, 0);
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
                return GdromQueuedCommand.Failed(GdromNoDiscStatus, 0, 0, 0);
            }

            if (buffer == 0)
            {
                memory.RecordGdromTocCommand(parameters, null, null, null, null, null, false, "missing TOC buffer");
                return GdromQueuedCommand.Failed(0, 0, 0, 0);
            }

            for (var i = 0; i < GdromTocWords; i++)
            {
                memory.WriteUInt32(buffer + ((uint)i * 4), 0);
            }

            var leadoutFad = CalculateLeadoutFad(snapshot.SectorCount ?? 0);
            memory.WriteUInt32(buffer + 8, PackTocEntry(4, GdromDataTrackStartFad));
            memory.WriteUInt32(buffer + 396, 3u << 16);
            memory.WriteUInt32(buffer + 400, 3u << 16);
            memory.WriteUInt32(buffer + 404, PackTocEntry(0, leadoutFad));
            memory.RecordGdromTocCommand(parameters, buffer, 3, 3, GdromDataTrackStartFad, leadoutFad, true, "TOC written");
            return GdromQueuedCommand.Completed(0, 0, 0, 0);
        }

        private static uint PackTocEntry(uint control, uint fad) =>
            ((control & 0xFu) << 28) | (fad & 0x00FF_FFFFu);

        private static uint CalculateLeadoutFad(ulong sectorCount) =>
            sectorCount > GdromDataTrackStartFad
                ? (uint)Math.Min(sectorCount, uint.MaxValue)
                : GdromDataTrackStartFad + (uint)Math.Min(sectorCount, uint.MaxValue - GdromDataTrackStartFad);

        private uint CheckCommand(DcSharp.Core.Cpu.Sh4State state, DreamcastMemory memory)
        {
            if (!commands.TryGetValue(state.R[4], out var command))
            {
                command = new GdromQueuedCommand(GdromNoActive, 0, 0, 0, 0);
            }

            WriteWords(memory, state.R[5], command.Status0, command.Status1, command.TransferredBytes, command.AtaStatus);
            return unchecked((uint)command.Response);
        }

        private uint AbortCommand(DcSharp.Core.Cpu.Sh4State state)
        {
            commands.Remove(state.R[4]);
            return 0;
        }

        private static uint CheckDrive(DcSharp.Core.Cpu.Sh4State state, DreamcastMemory memory)
        {
            var snapshot = memory.CreateGdromSnapshot();
            var statusCode = snapshot.HasMedia ? CdStatusStandby : CdStatusNoDisc;
            var discType = snapshot.HasMedia ? CdRomXa : CdCdda;
            var statusName = snapshot.HasMedia ? "standby" : "no disc";
            var discTypeName = snapshot.HasMedia ? "CD-ROM XA" : "CDDA/no disc";

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

        private static uint ReadSectors(DcSharp.Core.Cpu.Sh4State state, DreamcastMemory memory)
        {
            return memory.ExecuteGdromCommand(state.R[4]);
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

        private sealed record GdromQueuedCommand(
            int Response,
            int Status0,
            int Status1,
            int TransferredBytes,
            int AtaStatus)
        {
            public static GdromQueuedCommand Completed(int status0, int status1, int transferredBytes, int ataStatus) =>
                new(GdromCompleted, status0, status1, transferredBytes, ataStatus);

            public static GdromQueuedCommand Failed(int status0, int status1, int transferredBytes, int ataStatus) =>
                new(GdromFailed, status0, status1, transferredBytes, ataStatus);
        }
    }
}

internal sealed class DreamcastFirmwareExitException(string message) : InvalidOperationException(message);
