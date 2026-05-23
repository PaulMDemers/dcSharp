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
        private const int GdromCompleted = 2;
        private const int CdStatusStandby = 2;
        private const int CdRomXa = 0x20;

        private uint nextCommandId = 1;

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
                1 => CompleteCommand(state, memory),
                2 => 0,
                3 => 0,
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
            if (state.R[4] is GdromCommandPioRead or GdromCommandDmaRead)
            {
                memory.ExecuteGdromPioReadCommand(state.R[5]);
            }

            return commandId;
        }

        private static uint CompleteCommand(DcSharp.Core.Cpu.Sh4State state, DreamcastMemory memory)
        {
            WriteWords(memory, state.R[5], 0, 0, 0, 0);
            return GdromCompleted;
        }

        private static uint CheckDrive(DcSharp.Core.Cpu.Sh4State state, DreamcastMemory memory)
        {
            WriteWords(memory, state.R[4], CdStatusStandby, CdRomXa);
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
            if (parameters != 0 && memory.ReadUInt32(parameters) == 1)
            {
                WriteWords(memory, parameters, 1, 0x2000, 2048, 2048);
            }

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
    }
}

internal sealed class DreamcastFirmwareExitException(string message) : InvalidOperationException(message);
