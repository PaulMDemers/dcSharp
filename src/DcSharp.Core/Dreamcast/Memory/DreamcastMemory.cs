using DcSharp.Core.Dreamcast;
using DcSharp.Core.Dreamcast.Asic;
using DcSharp.Core.Dreamcast.Audio;
using DcSharp.Core.Dreamcast.Input;
using DcSharp.Core.Dreamcast.Timer;
using DcSharp.Core.Dreamcast.Video;
using DcSharp.Core.Media;
using System.Numerics;
using System.Text;

namespace DcSharp.Core.Dreamcast.Memory;

public sealed class DreamcastMemory
{
    internal const uint BiosInterruptReturnHleStub = 0x8C00_00F0;

    private const uint AreaMask = 0xE000_0000;
    private const uint P1Base = 0x8000_0000;
    private const uint P2Base = 0xA000_0000;
    private const uint PhysicalMask = 0x1FFF_FFFF;
    private const uint BootRomPhysicalBase = 0x0000_0000;
    private const uint BootRomBytes = 2 * 1024 * 1024;
    private const uint BiosVectorTableBase = 0x0000_0180;
    private const uint BiosVectorTableBytes = 0x0000_0280;
    private const uint BiosInterruptHandlerTableBase = 0x0000_0200;
    private const uint SystemRamPhysicalBase = 0x0C00_0000;
    private const uint SystemRamMirrorBytes = 32 * 1024 * 1024;
    private const uint PvrVram64PhysicalBase = 0x0400_0000;
    private const uint PvrVram32PhysicalBase = 0x0500_0000;
    private const int PvrVramByteCount = 8 * 1024 * 1024;
    private const uint P4Base = 0xE000_0000;
    private const uint ExternalRegisterBase = 0x005F_0000;
    private const uint ExternalRegisterLimit = 0x0060_0000;
    private const uint PvrRegisterBase = 0x005F_8000;
    private const uint PvrRegisterLimit = 0x005F_A000;
    private const uint PvrId = 0x005F_8000;
    private const uint PvrIdValue = 0x17FD_11DB;
    private const uint PvrRevision = 0x005F_8004;
    private const uint PvrRevisionValue = 0x0000_0011;
    private const uint PvrSyncStatus = 0x005F_810C;
    private const uint PvrSyncStatusVBlank = 0x0000_0001;
    private const ulong PvrVBlankStatusTicks = 128;
    private const uint PvrTaInputBase = 0x1000_0000;
    private const uint PvrTaInputLimit = 0x1080_0000;
    private const uint PvrTaYuvBase = 0x1080_0000;
    private const uint PvrTaYuvLimit = 0x1100_0000;
    private const uint AicaRegisterBase = 0x0070_0000;
    private const uint AicaRegisterLimit = 0x0071_0000;
    private const uint AicaRamBase = 0x0080_0000;
    private const uint AicaRamBytes = 2 * 1024 * 1024;
    private const uint OperandCacheRamArea1Base = 0x7C00_0000;
    private const uint OperandCacheRamArea2Base = 0x7E00_0000;
    private const int OperandCacheRamAreaBytes = 4 * 1024;
    private const uint AicaOutputSampleRateHz = 44_100;
    private const uint ScifStatus = 0xFFE8_0010;
    private const uint ScifTransmitData = 0xFFE8_000C;
    private const uint InterruptPriorityA = 0xFFD0_0004;
    private const uint Sh4Tra = 0xFF00_0020;
    private const uint Sh4Expevt = 0xFF00_0024;
    private const uint Sh4Intevt = 0xFF00_0028;
    private const uint PortAData = 0xFF80_0030;
    private const uint DefaultPortAData = 0x0000_0300;
    private const uint TimerStart = 0xFFD8_0004;
    private const uint TimerConstant0 = 0xFFD8_0008;
    private const uint TimerCounter0 = 0xFFD8_000C;
    private const uint TimerControl0 = 0xFFD8_0010;
    private const uint TimerConstant1 = 0xFFD8_0014;
    private const uint TimerCounter1 = 0xFFD8_0018;
    private const uint TimerControl1 = 0xFFD8_001C;
    private const uint TimerConstant2 = 0xFFD8_0020;
    private const uint TimerCounter2 = 0xFFD8_0024;
    private const uint TimerControl2 = 0xFFD8_0028;
    private const uint TimerUnderflow = 0x0100;
    private const uint TimerUnderflowInterruptEnable = 0x0020;
    private const uint AsicAckA = 0x005F_6900;
    private const uint AsicIrqDA = 0x005F_6910;
    private const uint AsicIrqBA = 0x005F_6920;
    private const uint AsicIrq9A = 0x005F_6930;
    private const uint MapleDmaAddress = 0x005F_6C04;
    private const uint MapleState = 0x005F_6C18;
    private const uint MapleStateDma = 1;
    private const byte MapleResponseNone = 0xFF;
    private const byte MapleResponseDeviceInfo = 5;
    private const byte MapleResponseDataTransfer = 8;
    private const byte MapleCommandDeviceInfo = 1;
    private const byte MapleCommandGetCondition = 9;
    public const byte MaplePortAUnit0Address = 0x20;
    public const byte MaplePortBUnit0Address = 0x40;
    private const uint MapleFunctionController = 0x0100_0000;
    private const uint MapleStandardControllerCapabilities = 0xFE06_0F00;
    private const int MapleDmaDescriptorLimit = 64;
    private const ushort AsicEventPvrVBlankBegin = 0x0003;
    private const ushort AsicEventMapleDma = 0x000C;
    private const ushort AsicEventGdromCommand = 0x0100;
    private const ushort AsicEventGdromDma = 0x000E;

    private readonly byte[] systemRam = new byte[HardwareProfile.SystemRamBytes];
    private readonly byte[] biosVectorTable = new byte[BiosVectorTableBytes];
    private readonly byte[] pvrVram = new byte[PvrVramByteCount];
    private readonly float[] pvrPreviewDepth = new float[PvrVramByteCount / 2];
    private readonly byte[] aicaRam = new byte[HardwareProfile.AudioRamBytes];
    private readonly byte[] operandCacheRamArea1 = new byte[OperandCacheRamAreaBytes];
    private readonly byte[] operandCacheRamArea2 = new byte[OperandCacheRamAreaBytes];
    private readonly Dictionary<uint, uint> p4Registers = [];
    private readonly Dictionary<uint, uint> externalRegisters = [];
    private readonly Dictionary<uint, uint> aicaRegisters = [];
    private readonly DreamcastAicaPlaybackState[] aicaPlayback = CreateAicaPlaybackStates();
    private readonly List<MemoryAccess> deviceAccesses = [];
    private readonly List<MemoryAccess> watchedReads = [];
    private readonly List<MemoryAccess> watchedWrites = [];
    private readonly List<DreamcastPvrRegisterAccess> pvrRegisterAccesses = [];
    private readonly List<DreamcastPvrTaCommandWrite> pvrTaCommandWrites = [];
    private readonly DreamcastPvrTaState pvrTaState = new();
    private readonly List<DreamcastAicaRegisterAccess> aicaRegisterAccesses = [];
    private readonly List<DreamcastMapleDmaTransfer> mapleTransfers = [];
    private readonly List<DreamcastMapleDmaBatch> mapleDmaBatches = [];
    private readonly List<DreamcastGdromReadCommand> gdromReadCommands = [];
    private readonly List<DreamcastGdromTocCommand> gdromTocCommands = [];
    private readonly List<DreamcastGdromStatusCommand> gdromStatusCommands = [];
    private readonly List<DreamcastGdromSectorModeCommand> gdromSectorModeCommands = [];
    private readonly List<DreamcastGdromCommandActivity> gdromCommandActivities = [];
    private readonly Dictionary<byte, DreamcastControllerState> mapleControllers = [];
    private readonly IDreamcastMediaImage? mediaImage;
    private readonly DreamcastMemoryReadWatch? readWatch;
    private readonly DreamcastMemoryWriteWatch? writeWatch;
    private readonly List<byte> serialOutput = [];
    private ulong pvrVBlankStatusTicksRemaining;
    private readonly DreamcastMemoryRegionWriteCounter[] systemRamWriteCounters =
    [
        new("IP.BIN", 0x8C00_8000, 0x8000),
        new("Boot work", 0x8C00_C000, 0x4000),
        new("Boot binary", 0x8C01_0000, 0x200000),
        new("High RAM init", 0x8C1D_0000, 0x150000)
    ];

    public DreamcastMemory(
        DreamcastControllerState? controllerA = null,
        DreamcastControllerState? controllerB = null,
        IReadOnlyDictionary<byte, DreamcastControllerState>? controllers = null,
        IDreamcastMediaImage? media = null,
        DreamcastMemoryWriteWatch? writeWatch = null,
        DreamcastMemoryReadWatch? readWatch = null)
    {
        Array.Fill(pvrPreviewDepth, float.NaN);
        mediaImage = media;
        this.readWatch = readWatch;
        this.writeWatch = writeWatch;
        mapleControllers[MaplePortAUnit0Address] = controllerA ?? DreamcastControllerState.Neutral;
        if (controllerB is { } controllerBState)
        {
            mapleControllers[MaplePortBUnit0Address] = controllerBState;
        }

        if (controllers is not null)
        {
            foreach (var (address, state) in controllers)
            {
                mapleControllers[address] = state;
            }
        }
    }

    public int SystemRamBytes => systemRam.Length;
    public int PvrVramBytes => pvrVram.Length;
    public IDreamcastMediaImage? Media => mediaImage;
    public IReadOnlyList<MemoryAccess> DeviceAccesses => deviceAccesses;
    public IReadOnlyList<MemoryAccess> WatchedReads => watchedReads;
    public IReadOnlyList<MemoryAccess> WatchedWrites => watchedWrites;
    public IReadOnlyList<byte> SerialOutput => serialOutput;
    public uint? CurrentInstructionPc { get; set; }

    public void ResetSystemRamWriteCounters()
    {
        foreach (var counter in systemRamWriteCounters)
        {
            counter.Reset();
        }
    }

    public void ResetWatchedWrites() => watchedWrites.Clear();

    public void ResetWatchedReads() => watchedReads.Clear();

    public IReadOnlyList<DreamcastMemoryRegionWriteSummary> CreateSystemRamWriteSummary() =>
        systemRamWriteCounters
            .Select(counter => counter.CreateSummary())
            .ToArray();

    public DreamcastSh4EventRegistersSnapshot CreateSh4EventRegistersSnapshot() =>
        new(
            p4Registers.GetValueOrDefault(Sh4Tra),
            p4Registers.GetValueOrDefault(Sh4Expevt),
            p4Registers.GetValueOrDefault(Sh4Intevt));
    public DreamcastControllerState ControllerA
    {
        get => mapleControllers.GetValueOrDefault(MaplePortAUnit0Address, DreamcastControllerState.Neutral);
        set => mapleControllers[MaplePortAUnit0Address] = value;
    }

    public DreamcastControllerState? ControllerB
    {
        get => mapleControllers.GetValueOrDefault(MaplePortBUnit0Address);
        set
        {
            if (value is { } controllerState)
            {
                mapleControllers[MaplePortBUnit0Address] = controllerState;
            }
            else
            {
                mapleControllers.Remove(MaplePortBUnit0Address);
            }
        }
    }

    public DreamcastControllerState? GetController(byte address) =>
        mapleControllers.GetValueOrDefault(address);

    public void SetController(byte address, DreamcastControllerState state) =>
        mapleControllers[address] = state;

    public static uint TranslateAddress(uint address) =>
        IsP4Address(address) ? address : address & PhysicalMask;

    public void AdvanceHardware(ulong instructions)
    {
        if (instructions == 0)
        {
            return;
        }

        if (pvrVBlankStatusTicksRemaining != 0)
        {
            pvrVBlankStatusTicksRemaining = instructions >= pvrVBlankStatusTicksRemaining
                ? 0
                : pvrVBlankStatusTicksRemaining - instructions;
        }

        for (var channel = 0; channel < 3; channel++)
        {
            AdvanceTimer(channel, instructions);
        }

        AdvanceAicaPlayback(instructions);
    }

    public bool TryGetSystemRamOffset(uint address, int length, out int offset)
    {
        offset = 0;

        if (length < 0)
        {
            return false;
        }

        var physical = TranslateAddress(address);
        if (physical < SystemRamPhysicalBase)
        {
            return false;
        }

        var relative = physical - SystemRamPhysicalBase;
        if (relative >= SystemRamMirrorBytes)
        {
            return false;
        }

        var mirrored = relative % (uint)systemRam.Length;

        if ((ulong)mirrored + (uint)length > (ulong)systemRam.Length)
        {
            return false;
        }

        offset = (int)mirrored;
        return true;
    }

    public bool TryGetPvrVramOffset(uint address, int length, out int offset)
    {
        offset = 0;

        if (length < 0)
        {
            return false;
        }

        var physical = TranslateAddress(address);
        if (physical >= PvrVram32PhysicalBase && physical < PvrVram32PhysicalBase + PvrVramByteCount)
        {
            offset = (int)(physical - PvrVram32PhysicalBase);
        }
        else if (physical >= PvrVram64PhysicalBase && physical < PvrVram64PhysicalBase + PvrVramByteCount)
        {
            offset = (int)(physical - PvrVram64PhysicalBase);
        }
        else
        {
            return false;
        }

        return offset + length <= pvrVram.Length;
    }

    private bool TryGetOperandCacheRamOffset(uint address, int length, out byte[] ram, out int offset)
    {
        ram = operandCacheRamArea1;
        offset = 0;

        if (length < 0)
        {
            return false;
        }

        if (address >= OperandCacheRamArea1Base && (ulong)address + (uint)length <= (ulong)OperandCacheRamArea1Base + OperandCacheRamAreaBytes)
        {
            ram = operandCacheRamArea1;
            offset = (int)(address - OperandCacheRamArea1Base);
            return true;
        }

        if (address >= OperandCacheRamArea2Base && (ulong)address + (uint)length <= (ulong)OperandCacheRamArea2Base + OperandCacheRamAreaBytes)
        {
            ram = operandCacheRamArea2;
            offset = (int)(address - OperandCacheRamArea2Base);
            return true;
        }

        return false;
    }

    private static bool IsBootRomAddress(uint address, int length)
    {
        if (length < 0)
        {
            return false;
        }

        var physical = TranslateAddress(address);
        return physical >= BootRomPhysicalBase
            && (ulong)physical + (uint)length <= (ulong)BootRomPhysicalBase + BootRomBytes;
    }

    private static bool TryGetBiosVectorTableOffset(uint address, int length, out int offset)
    {
        offset = 0;
        if (length < 0)
        {
            return false;
        }

        var physical = TranslateAddress(address);
        if (physical < BiosVectorTableBase
            || (ulong)physical + (uint)length > (ulong)BiosVectorTableBase + BiosVectorTableBytes)
        {
            return false;
        }

        offset = (int)(physical - BiosVectorTableBase);
        return true;
    }

    public void Write(uint address, ReadOnlySpan<byte> data)
    {
        RecordWatchedWrite(address, data);

        if (IsP4Address(address))
        {
            WriteP4(address, data);
            return;
        }

        if (TryTranslateExternalRegister(address, out var externalAddress))
        {
            WriteExternal(address, externalAddress, data);
            return;
        }

        if (TryWritePvrTa(address, data))
        {
            return;
        }

        if (TryTranslateAicaRegister(address, out var aicaAddress))
        {
            WriteAicaRegister(address, aicaAddress, data);
            return;
        }

        if (TryGetAicaRamOffset(address, data.Length, out var aicaOffset))
        {
            data.CopyTo(aicaRam.AsSpan(aicaOffset));
            return;
        }

        if (TryGetPvrVramOffset(address, data.Length, out var vramOffset))
        {
            data.CopyTo(pvrVram.AsSpan(vramOffset));
            return;
        }

        if (TryGetOperandCacheRamOffset(address, data.Length, out var operandCacheRam, out var operandCacheOffset))
        {
            data.CopyTo(operandCacheRam.AsSpan(operandCacheOffset));
            return;
        }

        if (TryGetBiosVectorTableOffset(address, data.Length, out var biosVectorOffset))
        {
            data.CopyTo(biosVectorTable.AsSpan(biosVectorOffset));
            deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.Write, address, data.Length, ToValue(data)));
            return;
        }

        if (IsBootRomAddress(address, data.Length))
        {
            deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.Write, address, data.Length, ToValue(data)));
            return;
        }

        if (!TryGetSystemRamOffset(address, data.Length, out var offset))
        {
            deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.UnmappedWrite, address, data.Length, ToValue(data)));
            return;
        }

        RecordSystemRamWrite(address, data.Length);
        data.CopyTo(systemRam.AsSpan(offset));
    }

    public void Clear(uint address, uint length)
    {
        if (length == 0)
        {
            return;
        }

        if (length > int.MaxValue)
        {
            throw new MemoryMapException($"Clear length is too large: {length}");
        }

        if (!TryGetSystemRamOffset(address, (int)length, out var offset))
        {
            throw new MemoryMapException($"Clear outside Dreamcast system RAM: address=0x{address:X8}, length={length}");
        }

        systemRam.AsSpan(offset, (int)length).Clear();
    }

    public byte ReadByte(uint address)
    {
        if (IsP4Address(address))
        {
            var value = (byte)(ReadP4(address, 1) & 0xFF);
            RecordWatchedRead(address, 1, value);
            return value;
        }

        if (TryTranslateExternalRegister(address, out var externalAddress))
        {
            var value = (byte)(ReadExternal(address, externalAddress, 1) & 0xFF);
            RecordWatchedRead(address, 1, value);
            return value;
        }

        if (TryGetPvrVramOffset(address, 1, out var vramOffset))
        {
            var value = pvrVram[vramOffset];
            RecordWatchedRead(address, 1, value);
            return value;
        }

        if (TryTranslateAicaRegister(address, out var aicaAddress))
        {
            var value = (byte)(ReadAicaRegister(address, aicaAddress, 1) & 0xFF);
            RecordWatchedRead(address, 1, value);
            return value;
        }

        if (TryGetAicaRamOffset(address, 1, out var aicaOffset))
        {
            var value = aicaRam[aicaOffset];
            RecordWatchedRead(address, 1, value);
            return value;
        }

        if (TryGetOperandCacheRamOffset(address, 1, out var operandCacheRam, out var operandCacheOffset))
        {
            var value = operandCacheRam[operandCacheOffset];
            RecordWatchedRead(address, 1, value);
            return value;
        }

        if (TryGetBiosVectorTableOffset(address, 1, out var biosVectorOffset))
        {
            var value = biosVectorTable[biosVectorOffset];
            deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.Read, address, 1, value));
            RecordWatchedRead(address, 1, value);
            return value;
        }

        if (IsBootRomAddress(address, 1))
        {
            deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.Read, address, 1, 0));
            RecordWatchedRead(address, 1, 0);
            return 0;
        }

        if (!TryGetSystemRamOffset(address, 1, out var offset))
        {
            deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.UnmappedRead, address, 1, 0));
            RecordWatchedRead(address, 1, 0);
            return 0;
        }

        var systemRamValue = systemRam[offset];
        RecordWatchedRead(address, 1, systemRamValue);
        return systemRamValue;
    }

    public ushort ReadUInt16(uint address)
    {
        if (IsP4Address(address))
        {
            var value = (ushort)(ReadP4(address, 2) & 0xFFFF);
            RecordWatchedRead(address, 2, value);
            return value;
        }

        if (TryTranslateExternalRegister(address, out var externalAddress))
        {
            var value = (ushort)(ReadExternal(address, externalAddress, 2) & 0xFFFF);
            RecordWatchedRead(address, 2, value);
            return value;
        }

        if (TryGetPvrVramOffset(address, 2, out var vramOffset))
        {
            var value = (ushort)(pvrVram[vramOffset] | (pvrVram[vramOffset + 1] << 8));
            RecordWatchedRead(address, 2, value);
            return value;
        }

        if (TryTranslateAicaRegister(address, out var aicaAddress))
        {
            var value = (ushort)(ReadAicaRegister(address, aicaAddress, 2) & 0xFFFF);
            RecordWatchedRead(address, 2, value);
            return value;
        }

        if (TryGetAicaRamOffset(address, 2, out var aicaOffset))
        {
            var value = (ushort)(aicaRam[aicaOffset] | (aicaRam[aicaOffset + 1] << 8));
            RecordWatchedRead(address, 2, value);
            return value;
        }

        if (TryGetOperandCacheRamOffset(address, 2, out var operandCacheRam, out var operandCacheOffset))
        {
            var value = (ushort)(operandCacheRam[operandCacheOffset] | (operandCacheRam[operandCacheOffset + 1] << 8));
            RecordWatchedRead(address, 2, value);
            return value;
        }

        if (TryGetBiosVectorTableOffset(address, 2, out var biosVectorOffset))
        {
            var value = (ushort)(biosVectorTable[biosVectorOffset] | (biosVectorTable[biosVectorOffset + 1] << 8));
            deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.Read, address, 2, value));
            RecordWatchedRead(address, 2, value);
            return value;
        }

        if (IsBootRomAddress(address, 2))
        {
            deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.Read, address, 2, 0));
            RecordWatchedRead(address, 2, 0);
            return 0;
        }

        if (!TryGetSystemRamOffset(address, 2, out var offset))
        {
            deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.UnmappedRead, address, 2, 0));
            RecordWatchedRead(address, 2, 0);
            return 0;
        }

        var systemRamValue = (ushort)(systemRam[offset] | (systemRam[offset + 1] << 8));
        RecordWatchedRead(address, 2, systemRamValue);
        return systemRamValue;
    }

    public uint ReadUInt32(uint address)
    {
        if (IsP4Address(address))
        {
            var value = ReadP4(address, 4);
            RecordWatchedRead(address, 4, value);
            return value;
        }

        if (TryTranslateExternalRegister(address, out var externalAddress))
        {
            var value = ReadExternal(address, externalAddress, 4);
            RecordWatchedRead(address, 4, value);
            return value;
        }

        if (TryGetPvrVramOffset(address, 4, out var vramOffset))
        {
            var value = (uint)(pvrVram[vramOffset]
                | (pvrVram[vramOffset + 1] << 8)
                | (pvrVram[vramOffset + 2] << 16)
                | (pvrVram[vramOffset + 3] << 24));
            RecordWatchedRead(address, 4, value);
            return value;
        }

        if (TryTranslateAicaRegister(address, out var aicaAddress))
        {
            var value = ReadAicaRegister(address, aicaAddress, 4);
            RecordWatchedRead(address, 4, value);
            return value;
        }

        if (TryGetAicaRamOffset(address, 4, out var aicaOffset))
        {
            var value = (uint)(aicaRam[aicaOffset]
                | (aicaRam[aicaOffset + 1] << 8)
                | (aicaRam[aicaOffset + 2] << 16)
                | (aicaRam[aicaOffset + 3] << 24));
            RecordWatchedRead(address, 4, value);
            return value;
        }

        if (TryGetOperandCacheRamOffset(address, 4, out var operandCacheRam, out var operandCacheOffset))
        {
            var value = (uint)(operandCacheRam[operandCacheOffset]
                | (operandCacheRam[operandCacheOffset + 1] << 8)
                | (operandCacheRam[operandCacheOffset + 2] << 16)
                | (operandCacheRam[operandCacheOffset + 3] << 24));
            RecordWatchedRead(address, 4, value);
            return value;
        }

        if (TryGetBiosVectorTableOffset(address, 4, out var biosVectorOffset))
        {
            var value = ReadUInt32From(biosVectorTable, biosVectorOffset);
            deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.Read, address, 4, value));
            RecordWatchedRead(address, 4, value);
            return value;
        }

        if (IsBootRomAddress(address, 4))
        {
            deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.Read, address, 4, 0));
            RecordWatchedRead(address, 4, 0);
            return 0;
        }

        if (!TryGetSystemRamOffset(address, 4, out var offset))
        {
            deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.UnmappedRead, address, 4, 0));
            RecordWatchedRead(address, 4, 0);
            return 0;
        }

        var systemRamValue = (uint)(systemRam[offset]
            | (systemRam[offset + 1] << 8)
            | (systemRam[offset + 2] << 16)
            | (systemRam[offset + 3] << 24));
        RecordWatchedRead(address, 4, systemRamValue);
        return systemRamValue;
    }

    public bool TryPeekUInt32(uint address, out uint value)
    {
        value = 0;
        if (TryGetPvrVramOffset(address, 4, out var vramOffset))
        {
            value = ReadUInt32From(pvrVram, vramOffset);
            return true;
        }

        if (TryGetAicaRamOffset(address, 4, out var aicaOffset))
        {
            value = ReadUInt32From(aicaRam, aicaOffset);
            return true;
        }

        if (TryGetOperandCacheRamOffset(address, 4, out var operandCacheRam, out var operandCacheOffset))
        {
            value = ReadUInt32From(operandCacheRam, operandCacheOffset);
            return true;
        }

        if (TryGetBiosVectorTableOffset(address, 4, out var biosVectorOffset))
        {
            value = ReadUInt32From(biosVectorTable, biosVectorOffset);
            return true;
        }

        if (TryGetSystemRamOffset(address, 4, out var systemRamOffset))
        {
            value = ReadUInt32From(systemRam, systemRamOffset);
            return true;
        }

        return false;
    }

    public bool TryGetBiosInterruptHandler(int interruptLevel, out uint vectorAddress, out uint handlerAddress)
    {
        vectorAddress = 0;
        handlerAddress = 0;
        if (interruptLevel is < 0 or > 15)
        {
            return false;
        }

        vectorAddress = BiosInterruptHandlerTableBase + ((uint)interruptLevel * 4);
        if (!TryGetBiosVectorTableOffset(vectorAddress, 4, out var offset))
        {
            return false;
        }

        handlerAddress = ReadUInt32From(biosVectorTable, offset);
        return handlerAddress != 0;
    }

    private static uint ReadUInt32From(byte[] bytes, int offset) =>
        (uint)(bytes[offset]
            | (bytes[offset + 1] << 8)
            | (bytes[offset + 2] << 16)
            | (bytes[offset + 3] << 24));

    public uint ExecuteGdromCommand(uint parameterAddress)
    {
        if (parameterAddress == 0)
        {
            RecordGdromRead(parameterAddress, null, null, null, 0, 0, false, "missing parameter block");
            return 1;
        }

        var sector = ReadUInt32(parameterAddress);
        var destination = ReadUInt32(parameterAddress + 4);
        var sectorCount = ReadUInt32(parameterAddress + 8);
        return ExecuteGdromRead(parameterAddress, sector, destination, sectorCount);
    }

    public uint ExecuteGdromPioReadCommand(uint parameterAddress)
    {
        if (parameterAddress == 0)
        {
            RecordGdromRead(parameterAddress, null, null, null, 0, 0, false, "missing parameter block");
            return 1;
        }

        var sector = ReadUInt32(parameterAddress);
        var sectorCount = ReadUInt32(parameterAddress + 4);
        var destination = ReadUInt32(parameterAddress + 8);
        return ExecuteGdromRead(parameterAddress, sector, destination, sectorCount);
    }

    public uint ExecuteGdromDmaReadCommand(uint parameterAddress)
    {
        if (parameterAddress == 0)
        {
            RecordGdromRead(parameterAddress, null, null, null, 0, 0, false, "missing parameter block");
            return 1;
        }

        var sector = ReadUInt32(parameterAddress);
        var sectorCount = ReadUInt32(parameterAddress + 4);
        var destination = ReadUInt32(parameterAddress + 8);
        return ExecuteGdromReadCommand(parameterAddress, sector, destination, sectorCount, raiseDmaComplete: true);
    }

    internal uint ExecuteGdromReadCommand(uint parameterAddress, uint sector, uint destination, uint sectorCount, bool raiseDmaComplete)
    {
        var status = ExecuteGdromRead(parameterAddress, sector, destination, sectorCount);
        if (status == 0)
        {
            if (raiseDmaComplete)
            {
                RaiseAsicEvent(AsicEventGdromDma);
            }
        }

        return status;
    }

    internal void RaiseGdromCommandStatus() => RaiseAsicEvent(AsicEventGdromCommand);

    internal void AcknowledgeBiosAsicInterrupt(uint eventCode)
    {
        if (!TryGetPendingAsicInterrupt(out var pendingInterrupt) || pendingInterrupt.EventCode != eventCode)
        {
            return;
        }

        var address = AsicAckA + ((uint)pendingInterrupt.RegisterIndex * 4);
        externalRegisters[address] = externalRegisters.GetValueOrDefault(address) & ~pendingInterrupt.BitMask;
    }

    private uint ExecuteGdromRead(uint parameterAddress, uint sector, uint destination, uint sectorCount)
    {
        if (mediaImage is null)
        {
            RecordGdromRead(parameterAddress, sector, destination, sectorCount, 0, 0, false, "no media image loaded");
            return 1;
        }

        if (sectorCount == 0)
        {
            sectorCount = 1;
        }

        var bytesRequested = GdromRequestedBytes(sectorCount);
        if (bytesRequested == 0)
        {
            RecordGdromRead(parameterAddress, sector, destination, sectorCount, 0, 0, false, "invalid media sector size/count");
            return 1;
        }

        if (destination == 0)
        {
            RecordGdromRead(parameterAddress, sector, destination, sectorCount, bytesRequested, 0, false, "missing destination");
            return 1;
        }

        var success = TryReadMediaSectors(sector, destination, sectorCount, out var bytesRead, out var status);
        RecordGdromRead(parameterAddress, sector, destination, sectorCount, bytesRequested, bytesRead, success, status);
        return success ? 0u : 1u;
    }

    public ushort ReadInstructionUInt16(uint address)
    {
        if (!TryGetSystemRamOffset(address, 2, out var offset))
        {
            throw new MemoryMapException($"Instruction fetch outside Dreamcast system RAM: address=0x{address:X8}");
        }

        return (ushort)(systemRam[offset] | (systemRam[offset + 1] << 8));
    }

    public void WriteUInt32(uint address, uint value)
    {
        Span<byte> bytes =
        [
            (byte)value,
            (byte)(value >> 8),
            (byte)(value >> 16),
            (byte)(value >> 24)
        ];

        Write(address, bytes);
    }

    public void WriteUInt16(uint address, ushort value)
    {
        Span<byte> bytes =
        [
            (byte)value,
            (byte)(value >> 8)
        ];

        Write(address, bytes);
    }

    public DreamcastVideoSnapshot CreateVideoSnapshot()
    {
        ulong nonZeroBytes = 0;
        uint? firstNonZeroOffset = null;
        const uint fnvPrime = 16_777_619;
        var hash = 2_166_136_261u;

        for (var index = 0; index < pvrVram.Length; index++)
        {
            var value = pvrVram[index];
            if (value != 0)
            {
                nonZeroBytes++;
                firstNonZeroOffset ??= (uint)index;
            }

            hash ^= value;
            hash *= fnvPrime;
        }

        return new DreamcastVideoSnapshot(
            pvrVram.Length,
            nonZeroBytes,
            hash,
            $"0x{hash:X8}",
            firstNonZeroOffset,
            firstNonZeroOffset is { } offset ? $"0x{offset:X8}" : null,
            CreateVideoSamples(),
            CreatePvrRegisterValues(),
            pvrRegisterAccesses.ToArray(),
            pvrTaCommandWrites.ToArray(),
            pvrTaState.CompletedStrips.ToArray(),
            pvrTaState.CompletedSprites.ToArray(),
            (byte[])pvrVram.Clone());
    }

    public DreamcastAudioSnapshot CreateAudioSnapshot()
    {
        ulong nonZeroBytes = 0;
        const uint fnvPrime = 16_777_619;
        var hash = 2_166_136_261u;

        foreach (var value in aicaRam)
        {
            if (value != 0)
            {
                nonZeroBytes++;
            }

            hash ^= value;
            hash *= fnvPrime;
        }

        return new DreamcastAudioSnapshot(
            aicaRam.Length,
            nonZeroBytes,
            hash,
            $"0x{hash:X8}",
            CreateAicaRegisterValues(),
            aicaRegisterAccesses.ToArray(),
            CreateAicaChannelSnapshots(),
            (byte[])aicaRam.Clone());
    }

    public DreamcastMapleSnapshot CreateMapleSnapshot() =>
        new(mapleTransfers.ToArray(), mapleDmaBatches.ToArray());

    public DreamcastGdromSnapshot CreateGdromSnapshot() =>
        new(
            mediaImage is not null,
            mediaImage?.SectorSize,
            mediaImage?.SectorCount,
            mediaImage?.LeadoutFad,
            mediaImage is null ? null : $"0x{mediaImage.LeadoutFad:X8}",
            mediaImage?.Tracks ?? [],
            gdromReadCommands.ToArray(),
            gdromTocCommands.ToArray(),
            gdromStatusCommands.ToArray(),
            gdromSectorModeCommands.ToArray(),
            gdromCommandActivities.ToArray());

    public void RecordGdromTocCommand(
        uint parameterAddress,
        uint? bufferAddress,
        int? firstTrack,
        int? lastTrack,
        uint? dataTrackStartFad,
        uint? leadoutFad,
        bool success,
        string status) =>
        gdromTocCommands.Add(new DreamcastGdromTocCommand(
            parameterAddress,
            $"0x{parameterAddress:X8}",
            bufferAddress,
            bufferAddress is { } bufferValue ? $"0x{bufferValue:X8}" : null,
            firstTrack,
            lastTrack,
            dataTrackStartFad,
            dataTrackStartFad is { } startValue ? $"0x{startValue:X8}" : null,
            leadoutFad,
            leadoutFad is { } leadoutValue ? $"0x{leadoutValue:X8}" : null,
            success,
            status));

    public void RecordGdromSectorModeCommand(
        uint parameterAddress,
        int request,
        int sectorPart,
        int cdXa,
        int sectorSize,
        bool success,
        string status) =>
        gdromSectorModeCommands.Add(new DreamcastGdromSectorModeCommand(
            parameterAddress,
            $"0x{parameterAddress:X8}",
            request,
            request == 0 ? "set" : request == 1 ? "get" : "unknown",
            sectorPart,
            $"0x{sectorPart:X8}",
            cdXa,
            sectorSize,
            success,
            status));

    public void RecordGdromStatusCommand(
        uint bufferAddress,
        int statusCode,
        string statusName,
        int discType,
        string discTypeName,
        bool success,
        string status) =>
        gdromStatusCommands.Add(new DreamcastGdromStatusCommand(
            bufferAddress,
            $"0x{bufferAddress:X8}",
            statusCode,
            statusName,
            discType,
            discTypeName,
            success,
            status));

    public void RecordGdromCommandActivity(
        string operation,
        uint? commandId,
        uint? command,
        string? commandName,
        uint? parameterAddress,
        uint? statusAddress,
        int? response,
        string? responseName,
        int? status0,
        int? status1,
        int? transferredBytes,
        int? ataStatus,
        string status) =>
        gdromCommandActivities.Add(new DreamcastGdromCommandActivity(
            operation,
            commandId,
            command,
            command is { } commandValue ? $"0x{commandValue:X8}" : null,
            commandName,
            parameterAddress,
            parameterAddress is { } parameterValue ? $"0x{parameterValue:X8}" : null,
            statusAddress,
            statusAddress is { } statusValue ? $"0x{statusValue:X8}" : null,
            response,
            responseName,
            status0,
            status1,
            transferredBytes,
            ataStatus,
            status));

    public DreamcastAsicSnapshot CreateAsicSnapshot()
    {
        var registers = Enumerable.Range(0, 3)
            .Select(index =>
            {
                var offset = (uint)index * 4u;
                var ack = externalRegisters.GetValueOrDefault(AsicAckA + offset);
                var irq9 = externalRegisters.GetValueOrDefault(AsicIrq9A + offset);
                var irqB = externalRegisters.GetValueOrDefault(AsicIrqBA + offset);
                var irqD = externalRegisters.GetValueOrDefault(AsicIrqDA + offset);
                var pendingIrq9 = ack & irq9;
                var pendingIrqB = ack & irqB;
                var pendingIrqD = ack & irqD;
                return new DreamcastAsicEventRegisterSnapshot(
                    index,
                    AsicEventRegisterName(index),
                    ack,
                    $"0x{ack:X8}",
                    irq9,
                    $"0x{irq9:X8}",
                    irqB,
                    $"0x{irqB:X8}",
                    irqD,
                    $"0x{irqD:X8}",
                    pendingIrq9,
                    $"0x{pendingIrq9:X8}",
                    pendingIrqB,
                    $"0x{pendingIrqB:X8}",
                    pendingIrqD,
                    $"0x{pendingIrqD:X8}");
            })
            .ToArray();

        var pendingInterrupt = TryGetPendingAsicInterrupt(out var pending) ? pending : null;
        return new DreamcastAsicSnapshot(
            registers,
            pendingInterrupt?.EventCode,
            pendingInterrupt?.EventCodeHex,
            pendingInterrupt?.Level,
            pendingInterrupt);
    }

    public DreamcastTimerSnapshot CreateTimerSnapshot()
    {
        var channels = Enumerable.Range(0, 3)
            .Select(channel =>
            {
                var constant = p4Registers.GetValueOrDefault(TimerConstantAddress(channel));
                var counter = p4Registers.GetValueOrDefault(TimerCounterAddress(channel));
                var control = p4Registers.GetValueOrDefault(TimerControlAddress(channel));
                var priority = TimerInterruptPriority(channel);
                return new DreamcastTimerChannelSnapshot(
                    channel,
                    constant,
                    $"0x{constant:X8}",
                    counter,
                    $"0x{counter:X8}",
                    control,
                    $"0x{control:X8}",
                    priority,
                    (p4Registers.GetValueOrDefault(TimerStart) & (1u << channel)) != 0,
                    (control & TimerUnderflow) != 0,
                    (control & TimerUnderflowInterruptEnable) != 0);
            })
            .ToArray();

        var pendingInterrupt = TryGetPendingTimerInterrupt(out var pendingEventCode, out var pendingPriority, out var pendingChannel)
            ? new DreamcastTimerPendingInterruptSnapshot(
                pendingEventCode,
                $"0x{pendingEventCode:X4}",
                pendingChannel,
                pendingPriority)
            : null;

        return new DreamcastTimerSnapshot(
            channels,
            pendingInterrupt?.EventCode,
            pendingInterrupt?.EventCodeHex,
            pendingInterrupt?.Channel,
            pendingInterrupt?.Priority,
            pendingInterrupt);
    }

    private IReadOnlyList<DreamcastVideoSample> CreateVideoSamples()
    {
        (string Name, uint Offset)[] offsets =
        [
            ("origin", 0),
            ("pixel_1_0", 2),
            ("pixel_2_0", 4),
            ("pixel_0_1_320x240", 320u * 2u),
            ("pixel_1_1_320x240", ((320u * 1u) + 1u) * 2u),
            ("pixel_2_2_320x240", ((320u * 2u) + 2u) * 2u),
            ("pixel_160_120_320x240", (120u * 320u + 160u) * 2u),
            ("pixel_319_239_320x240", (239u * 320u + 319u) * 2u),
            ("pixel_320_240_640x480", (240u * 640u + 320u) * 2u)
        ];

        return offsets
            .Where(sample => sample.Offset + 1 < pvrVram.Length)
            .Select(sample =>
            {
                var value = (ushort)(pvrVram[sample.Offset] | (pvrVram[sample.Offset + 1] << 8));
                return new DreamcastVideoSample(sample.Name, sample.Offset, $"0x{sample.Offset:X8}", value, $"0x{value:X4}");
            })
            .ToArray();
    }

    private IReadOnlyList<DreamcastPvrRegisterValue> CreatePvrRegisterValues() =>
        externalRegisters
            .Where(entry => entry.Key >= PvrRegisterBase && entry.Key < PvrRegisterLimit)
            .OrderBy(entry => entry.Key)
            .Select(entry =>
            {
                var offset = entry.Key - PvrRegisterBase;
                return new DreamcastPvrRegisterValue(
                    offset,
                    $"0x{offset:X4}",
                    PvrRegisterName(offset),
                    entry.Value,
                    $"0x{entry.Value:X8}");
            })
            .ToArray();

    private static bool IsP4Address(uint address) => address >= P4Base;

    private static bool TryTranslateExternalRegister(uint address, out uint externalAddress)
    {
        externalAddress = TranslateAddress(address);
        return externalAddress >= ExternalRegisterBase && externalAddress < ExternalRegisterLimit;
    }

    private static bool TryTranslateAicaRegister(uint address, out uint aicaAddress)
    {
        aicaAddress = TranslateAddress(address);
        return aicaAddress >= AicaRegisterBase && aicaAddress < AicaRegisterLimit;
    }

    private bool TryGetAicaRamOffset(uint address, int length, out int offset)
    {
        offset = 0;
        if (length < 0)
        {
            return false;
        }

        var physical = TranslateAddress(address);
        if (physical < AicaRamBase || physical >= AicaRamBase + AicaRamBytes)
        {
            return false;
        }

        offset = (int)(physical - AicaRamBase);
        return offset + length <= aicaRam.Length;
    }

    public void RaiseVBlankBegin()
    {
        pvrVBlankStatusTicksRemaining = PvrVBlankStatusTicks;
        RaiseAsicEvent(AsicEventPvrVBlankBegin);
    }

    internal void RaiseAsicEventForDiagnostics(ushort code) => RaiseAsicEvent(code);

    public bool IsVBlankBeginInterruptEnabled() =>
        (externalRegisters.GetValueOrDefault(AsicIrq9A) & (1u << AsicEventPvrVBlankBegin)) != 0;

    public bool TryGetPendingExternalInterrupt(out uint eventCode, out int level)
    {
        var hasTimer = TryGetPendingTimerInterrupt(out var timerEventCode, out var timerLevel, out _);
        var hasAsic = TryGetPendingAsicInterrupt(out var asicPending);
        if (hasTimer && (!hasAsic || timerLevel >= asicPending.Level))
        {
            eventCode = timerEventCode;
            level = timerLevel;
            return true;
        }

        if (hasAsic)
        {
            eventCode = asicPending.EventCode;
            level = asicPending.Level;
            return true;
        }

        eventCode = 0;
        level = 0;
        return false;
    }

    private bool TryGetPendingAsicInterrupt(out DreamcastAsicPendingInterruptSnapshot pendingInterrupt)
    {
        if (TryGetPendingAsicInterruptAtLevel(AsicIrqDA, 0x03A0, 13, "IRQD", out pendingInterrupt)
            || TryGetPendingAsicInterruptAtLevel(AsicIrqBA, 0x0360, 11, "IRQB", out pendingInterrupt)
            || TryGetPendingAsicInterruptAtLevel(AsicIrq9A, 0x0320, 9, "IRQ9", out pendingInterrupt))
        {
            return true;
        }

        pendingInterrupt = null!;
        return false;
    }

    private bool TryGetPendingAsicInterruptAtLevel(uint maskBase, uint eventCode, int level, string levelName, out DreamcastAsicPendingInterruptSnapshot pendingInterrupt)
    {
        for (var index = 0u; index < 3; index++)
        {
            var offset = index * 4u;
            var pending = externalRegisters.GetValueOrDefault(AsicAckA + offset) & externalRegisters.GetValueOrDefault(maskBase + offset);
            if (pending == 0)
            {
                continue;
            }

            var bit = BitOperations.TrailingZeroCount(pending);
            var bitMask = 1u << bit;
            pendingInterrupt = new DreamcastAsicPendingInterruptSnapshot(
                eventCode,
                $"0x{eventCode:X4}",
                level,
                levelName,
                (int)index,
                AsicEventRegisterName((int)index),
                bit,
                bitMask,
                $"0x{bitMask:X8}");
            return true;
        }

        pendingInterrupt = null!;
        return false;
    }

    public ulong? TicksUntilNextTimerInterrupt()
    {
        ulong? ticks = null;
        for (var channel = 0; channel < 3; channel++)
        {
            var control = p4Registers.GetValueOrDefault(TimerControlAddress(channel));
            if ((control & TimerUnderflowInterruptEnable) == 0 || TimerInterruptPriority(channel) == 0)
            {
                continue;
            }

            if ((control & TimerUnderflow) != 0)
            {
                return 0;
            }

            var startMask = 1u << channel;
            if ((p4Registers.GetValueOrDefault(TimerStart) & startMask) == 0)
            {
                continue;
            }

            var counter = p4Registers.GetValueOrDefault(TimerCounterAddress(channel));
            var channelTicks = (ulong)counter + 1;
            ticks = ticks is { } existing ? Math.Min(existing, channelTicks) : channelTicks;
        }

        return ticks;
    }

    private bool TryGetPendingTimerInterrupt(out uint eventCode, out int level) =>
        TryGetPendingTimerInterrupt(out eventCode, out level, out _);

    private bool TryGetPendingTimerInterrupt(out uint eventCode, out int level, out int pendingChannel)
    {
        eventCode = 0;
        level = 0;
        pendingChannel = -1;
        for (var channel = 0; channel < 3; channel++)
        {
            var control = p4Registers.GetValueOrDefault(TimerControlAddress(channel));
            if ((control & (TimerUnderflow | TimerUnderflowInterruptEnable)) != (TimerUnderflow | TimerUnderflowInterruptEnable))
            {
                continue;
            }

            var priority = TimerInterruptPriority(channel);
            if (priority == 0)
            {
                continue;
            }

            if (priority <= level)
            {
                continue;
            }

            eventCode = channel switch
            {
                0 => 0x0400,
                1 => 0x0420,
                _ => 0x0440
            };
            level = priority;
            pendingChannel = channel;
        }

        return level != 0;
    }

    private uint ReadExternal(uint originalAddress, uint externalAddress, int size)
    {
        var aligned = externalAddress & 0xFFFF_FFFCu;
        var value = ReadExternalRegisterValue(aligned);
        var shift = (int)((externalAddress & 0x3) * 8);
        var shifted = value >> shift;
        var masked = size switch
        {
            1 => shifted & 0xFF,
            2 => shifted & 0xFFFF,
            4 => value,
            _ => throw new MemoryMapException($"Unsupported external register read size: {size}")
        };

        deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.Read, originalAddress, size, masked));
        LogPvrRegisterAccess(MemoryAccessKind.Read, originalAddress, externalAddress, size, masked);
        return masked;
    }

    private uint ReadExternalRegisterValue(uint aligned) =>
        aligned switch
        {
            PvrId => PvrIdValue,
            PvrRevision => PvrRevisionValue,
            PvrSyncStatus when pvrVBlankStatusTicksRemaining != 0 => externalRegisters.GetValueOrDefault(aligned) | PvrSyncStatusVBlank,
            _ => externalRegisters.GetValueOrDefault(aligned)
        };

    private void WriteExternal(uint originalAddress, uint externalAddress, ReadOnlySpan<byte> data)
    {
        if (data.Length is not (1 or 2 or 4))
        {
            throw new MemoryMapException($"Unsupported external register write size: {data.Length}");
        }

        var aligned = externalAddress & 0xFFFF_FFFCu;
        var shift = (int)((externalAddress & 0x3) * 8);
        var mask = data.Length switch
        {
            1 => 0xFFu << shift,
            2 => 0xFFFFu << shift,
            4 => 0xFFFF_FFFFu,
            _ => 0u
        };

        var value = ToValue(data);
        var existing = externalRegisters.GetValueOrDefault(aligned);
        var stored = (existing & ~mask) | ((value << shift) & mask);

        if ((aligned is AsicAckA or AsicAckA + 4 or AsicAckA + 8) && data.Length == 4)
        {
            stored = existing & ~value;
        }

        externalRegisters[aligned] = stored;
        deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.Write, originalAddress, data.Length, value));
        LogPvrRegisterAccess(MemoryAccessKind.Write, originalAddress, aligned, data.Length, value);

        if (aligned == MapleState && data.Length == 4 && (value & MapleStateDma) != 0)
        {
            CompleteMapleDma();
        }
    }

    private void RaiseAsicEvent(ushort code)
    {
        var eventRegister = (uint)((code >> 8) & 0xFF);
        var eventBit = (int)(code & 0xFF);
        if (eventRegister > 2 || eventBit > 31)
        {
            return;
        }

        var address = AsicAckA + (eventRegister * 4);
        externalRegisters[address] = externalRegisters.GetValueOrDefault(address) | (1u << eventBit);
    }

    private static string AsicEventRegisterName(int index) => index switch
    {
        0 => "A",
        1 => "B",
        2 => "C",
        _ => $"#{index}"
    };

    private void CompleteMapleDma()
    {
        var dmaAddress = externalRegisters.GetValueOrDefault(MapleDmaAddress);
        if (dmaAddress != 0)
        {
            mapleDmaBatches.Add(WriteMapleResponses(dmaAddress));
        }

        externalRegisters[MapleState] = 0;
        RaiseAsicEvent(AsicEventMapleDma);
    }

    private DreamcastMapleDmaBatch WriteMapleResponses(uint dmaAddress)
    {
        var descriptor = dmaAddress | P1Base;
        var startDescriptor = descriptor;
        var lastDescriptor = descriptor;
        var startTransferCount = mapleTransfers.Count;

        for (var frames = 0; frames < MapleDmaDescriptorLimit; frames++)
        {
            lastDescriptor = descriptor;
            var header = ReadUInt32(descriptor);
            var receiveBuffer = ReadUInt32(descriptor + 4) | P1Base;
            var commandWord = ReadUInt32(descriptor + 8);
            var command = (byte)(commandWord & 0xFF);
            var destination = (byte)((commandWord >> 8) & 0xFF);
            var length = header & 0xFF;

            if (receiveBuffer != P1Base)
            {
                mapleTransfers.Add(WriteMapleResponse(descriptor, header, receiveBuffer, command, destination));
            }

            descriptor += 12 + (length * 4);
            if ((header & 0x8000_0000) != 0)
            {
                return CreateMapleDmaBatch(
                    startDescriptor,
                    frames + 1,
                    mapleTransfers.Count - startTransferCount,
                    completed: true,
                    hitDescriptorLimit: false,
                    lastDescriptor);
            }
        }

        return CreateMapleDmaBatch(
            startDescriptor,
            MapleDmaDescriptorLimit,
            mapleTransfers.Count - startTransferCount,
            completed: false,
            hitDescriptorLimit: true,
            lastDescriptor);
    }

    private static DreamcastMapleDmaBatch CreateMapleDmaBatch(
        uint startDescriptor,
        int descriptorsScanned,
        int transferCount,
        bool completed,
        bool hitDescriptorLimit,
        uint lastDescriptor) =>
        new(
            startDescriptor,
            $"0x{startDescriptor:X8}",
            descriptorsScanned,
            transferCount,
            completed,
            hitDescriptorLimit,
            lastDescriptor,
            $"0x{lastDescriptor:X8}");

    private DreamcastMapleDmaTransfer WriteMapleResponse(uint descriptor, uint header, uint receiveBuffer, byte command, byte destination)
    {
        byte[] response;
        DreamcastControllerState? responseControllerState = null;
        if (mapleControllers.ContainsKey(destination) && command == MapleCommandDeviceInfo)
        {
            response = CreateMapleControllerDeviceInfoResponse(destination);
        }
        else if (mapleControllers.TryGetValue(destination, out var controllerState) && command == MapleCommandGetCondition)
        {
            responseControllerState = controllerState;
            response = CreateMapleControllerConditionResponse(destination, controllerState);
        }
        else
        {
            response = [MapleResponseNone];
        }

        Write(receiveBuffer, response);
        return new DreamcastMapleDmaTransfer(
            descriptor,
            $"0x{descriptor:X8}",
            header,
            $"0x{header:X8}",
            receiveBuffer,
            $"0x{receiveBuffer:X8}",
            command,
            MapleCommandName(command),
            destination,
            $"0x{destination:X2}",
            MapleAddressName(destination),
            response[0],
            MapleResponseName(response[0]),
            response.Length,
            responseControllerState);
    }

    private byte[] CreateMapleControllerDeviceInfoResponse(byte destination)
    {
        var response = new byte[4 + 112];
        response[0] = MapleResponseDeviceInfo;
        response[2] = destination;
        response[3] = 28;
        WriteUInt32(response, 4, MapleFunctionController);
        WriteUInt32(response, 8, MapleStandardControllerCapabilities);
        response[20] = 0xFF;
        response[21] = 0;
        WriteFixedAscii(response.AsSpan(22, 30), "dcSharp Virtual Controller");
        WriteFixedAscii(response.AsSpan(52, 60), "Produced by or under license from dcSharp");
        WriteUInt16(response, 112, 0x01AE);
        WriteUInt16(response, 114, 0x01F4);
        return response;
    }

    private byte[] CreateMapleControllerConditionResponse(byte destination, DreamcastControllerState controllerState)
    {
        var response = new byte[16];
        response[0] = MapleResponseDataTransfer;
        response[2] = destination;
        response[3] = 3;
        WriteUInt32(response, 4, MapleFunctionController);
        WriteUInt16(response, 8, (ushort)~(ushort)controllerState.Buttons);
        response[10] = controllerState.RightTrigger;
        response[11] = controllerState.LeftTrigger;
        response[12] = ToUnsignedAxis(controllerState.JoyX);
        response[13] = ToUnsignedAxis(controllerState.JoyY);
        response[14] = ToUnsignedAxis(controllerState.Joy2X);
        response[15] = ToUnsignedAxis(controllerState.Joy2Y);
        return response;
    }

    private static string MapleAddressName(byte address) => address switch
    {
        MaplePortAUnit0Address => "A0",
        MaplePortBUnit0Address => "B0",
        _ => $"0x{address:X2}"
    };

    private static string MapleCommandName(byte command) => command switch
    {
        MapleCommandDeviceInfo => "DeviceInfo",
        MapleCommandGetCondition => "GetCondition",
        _ => $"Command_{command:X2}"
    };

    private static string MapleResponseName(byte response) => response switch
    {
        MapleResponseNone => "None",
        MapleResponseDeviceInfo => "DeviceInfo",
        MapleResponseDataTransfer => "DataTransfer",
        _ => $"Response_{response:X2}"
    };

    private uint ReadP4(uint address, int size)
    {
        var aligned = address & 0xFFFF_FFFCu;
        var value = p4Registers.GetValueOrDefault(aligned, DefaultP4RegisterValue(aligned));
        if (address == ScifStatus && size == 2)
        {
            value |= 0x60;
        }

        var shift = (int)((address & 0x3) * 8);
        var shifted = value >> shift;
        var masked = size switch
        {
            1 => shifted & 0xFF,
            2 => shifted & 0xFFFF,
            4 => value,
            _ => throw new MemoryMapException($"Unsupported P4 read size: {size}")
        };

        deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.Read, address, size, masked, CurrentInstructionPc));
        return masked;
    }

    private static uint DefaultP4RegisterValue(uint address) => address switch
    {
        PortAData => DefaultPortAData,
        _ => 0
    };

    private int GdromRequestedBytes(uint sectorCount)
    {
        if (mediaImage is null || sectorCount == 0)
        {
            return 0;
        }

        var sectorSize = mediaImage.SectorSize;
        return sectorSize <= 0 || sectorCount > int.MaxValue / (uint)sectorSize
            ? 0
            : (int)(sectorCount * (uint)sectorSize);
    }

    private bool TryReadMediaSectors(uint sector, uint destination, uint sectorCount, out int bytesRead, out string status)
    {
        bytesRead = 0;
        status = "media read completed";
        if (mediaImage is null || sectorCount == 0)
        {
            status = mediaImage is null ? "no media image loaded" : "zero sectors requested";
            return false;
        }

        var sectorSize = mediaImage.SectorSize;
        if (sectorSize <= 0 || sectorCount > int.MaxValue / (uint)sectorSize)
        {
            status = "invalid media sector size/count";
            return false;
        }

        var totalBytes = (int)(sectorCount * (uint)sectorSize);
        if (!TryGetSystemRamOffset(destination, totalBytes, out _))
        {
            status = "destination outside system RAM";
            return false;
        }

        var buffer = new byte[sectorSize];
        for (uint index = 0; index < sectorCount; index++)
        {
            var requestedSector = sector + index;
            var mediaSector = TranslateGdromSector(requestedSector);
            if (!mediaImage.TryReadSector(mediaSector, buffer, out var sectorBytesRead) || sectorBytesRead != sectorSize)
            {
                status = $"sector read failed at LBA {requestedSector}";
                return false;
            }

            Write(destination + (index * (uint)sectorSize), buffer);
            bytesRead += sectorSize;
        }

        return true;
    }

    private uint TranslateGdromSector(uint sector)
    {
        if (mediaImage is RawSectorFromCdImage)
        {
            var firstTrackStart = mediaImage.Tracks.FirstOrDefault()?.StartFad ?? 0;
            if (firstTrackStart == 0)
            {
                return sector;
            }

            var gdFilesystemStart = firstTrackStart + 150;
            if (sector >= gdFilesystemStart)
            {
                return sector - gdFilesystemStart;
            }

            if (sector >= firstTrackStart)
            {
                return sector - firstTrackStart;
            }
        }

        return sector;
    }

    private void RecordGdromRead(
        uint parameterAddress,
        uint? sector,
        uint? destination,
        uint? sectorCount,
        int bytesRequested,
        int bytesRead,
        bool success,
        string status) =>
        gdromReadCommands.Add(new DreamcastGdromReadCommand(
            parameterAddress,
            $"0x{parameterAddress:X8}",
            sector,
            sector is { } sectorValue ? $"0x{sectorValue:X8}" : null,
            destination,
            destination is { } destinationValue ? $"0x{destinationValue:X8}" : null,
            sectorCount,
            mediaImage?.SectorSize,
            bytesRequested,
            bytesRead,
            success,
            status));

    private static bool IsTimerControl(uint address) =>
        address is TimerControl0 or TimerControl1 or TimerControl2;

    private void AdvanceTimer(int channel, ulong instructions)
    {
        var startMask = 1u << channel;
        if ((p4Registers.GetValueOrDefault(TimerStart) & startMask) == 0)
        {
            return;
        }

        var counterAddress = TimerCounterAddress(channel);
        var constantAddress = TimerConstantAddress(channel);
        var controlAddress = TimerControlAddress(channel);
        var counter = p4Registers.GetValueOrDefault(counterAddress);
        var remaining = instructions;

        while (remaining > 0)
        {
            if (counter == 0)
            {
                p4Registers[controlAddress] = p4Registers.GetValueOrDefault(controlAddress) | TimerUnderflow;
                counter = p4Registers.GetValueOrDefault(constantAddress);
                if (counter == 0)
                {
                    break;
                }
            }

            var decrement = (uint)Math.Min(remaining, counter);
            counter -= decrement;
            remaining -= decrement;
        }

        p4Registers[counterAddress] = counter;
    }

    private int TimerInterruptPriority(int channel)
    {
        var source = channel switch
        {
            0 => 3,
            1 => 2,
            _ => 1
        };
        var priorityRegister = p4Registers.GetValueOrDefault(InterruptPriorityA);
        return (int)((priorityRegister >> (source * 4)) & 0xF);
    }

    private static uint TimerConstantAddress(int channel) => channel switch
    {
        0 => TimerConstant0,
        1 => TimerConstant1,
        _ => TimerConstant2
    };

    private static uint TimerCounterAddress(int channel) => channel switch
    {
        0 => TimerCounter0,
        1 => TimerCounter1,
        _ => TimerCounter2
    };

    private static uint TimerControlAddress(int channel) => channel switch
    {
        0 => TimerControl0,
        1 => TimerControl1,
        _ => TimerControl2
    };

    private void WriteP4(uint address, ReadOnlySpan<byte> data)
    {
        if (data.Length is not (1 or 2 or 4))
        {
            throw new MemoryMapException($"Unsupported P4 write size: {data.Length}");
        }

        var aligned = address & 0xFFFF_FFFCu;
        var shift = (int)((address & 0x3) * 8);
        var mask = data.Length switch
        {
            1 => 0xFFu << shift,
            2 => 0xFFFFu << shift,
            4 => 0xFFFF_FFFFu,
            _ => 0u
        };

        var value = ToValue(data);

        var existing = p4Registers.GetValueOrDefault(aligned);
        var written = (existing & ~mask) | ((value << shift) & mask);
        p4Registers[aligned] = IsTimerControl(aligned) ? written & 0xFFFFu : written;
        deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.Write, address, data.Length, value, CurrentInstructionPc));

        if (address == ScifTransmitData && data.Length == 1)
        {
            serialOutput.Add(data[0]);
        }
    }

    private static uint ToValue(ReadOnlySpan<byte> data)
    {
        uint value = 0;
        var count = Math.Min(data.Length, 4);
        for (var index = 0; index < count; index++)
        {
            value |= (uint)data[index] << (index * 8);
        }

        return value;
    }

    private void RecordWatchedWrite(uint address, ReadOnlySpan<byte> data)
    {
        if (writeWatch is null || !writeWatch.ShouldRecord(address, data.Length) || watchedWrites.Count >= writeWatch.Limit)
        {
            return;
        }

        watchedWrites.Add(new MemoryAccess(MemoryAccessKind.Write, address, data.Length, ToValue(data), CurrentInstructionPc));
    }

    private void RecordWatchedRead(uint address, int size, uint value)
    {
        if (readWatch is null || !readWatch.ShouldRecord(address, size) || watchedReads.Count >= readWatch.Limit)
        {
            return;
        }

        watchedReads.Add(new MemoryAccess(MemoryAccessKind.Read, address, size, value, CurrentInstructionPc));
    }

    private void RecordSystemRamWrite(uint address, int length)
    {
        if (length <= 0)
        {
            return;
        }

        foreach (var counter in systemRamWriteCounters)
        {
            counter.Record(address, length);
        }
    }

    private bool TryWritePvrTa(uint address, ReadOnlySpan<byte> data)
    {
        var physical = TranslateAddress(address);
        var region = physical switch
        {
            >= PvrTaInputBase and < PvrTaInputLimit => "TA_INPUT",
            >= PvrTaYuvBase and < PvrTaYuvLimit => "TA_YUV_CONV",
            _ => null
        };

        if (region is null)
        {
            return false;
        }

        if (data.Length is not (1 or 2 or 4))
        {
            throw new MemoryMapException($"Unsupported PVR TA write size: {data.Length}");
        }

        var value = ToValue(data);
        var command = DreamcastPvrTaCommandDecoder.Decode(region, value);
        var write = new DreamcastPvrTaCommandWrite(
            address,
            $"0x{address:X8}",
            region,
            command.Kind,
            command.ListType,
            command.ListTypeName,
            command.EndOfStrip,
            data.Length,
            value,
            $"0x{value:X8}");
        pvrTaCommandWrites.Add(write);
        if (pvrTaState.Accept(write) is { } renderCommand)
        {
            if (renderCommand.Strip is { } strip)
            {
                DreamcastPvrPreviewRenderer.RenderStrip(strip, pvrVram, pvrPreviewDepth);
            }
            else if (renderCommand.Sprite is { } sprite)
            {
                DreamcastPvrPreviewRenderer.RenderSprite(sprite, pvrVram);
            }
        }

        deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.Write, address, data.Length, value));
        return true;
    }

    private void LogPvrRegisterAccess(MemoryAccessKind kind, uint originalAddress, uint externalAddress, int size, uint value)
    {
        if (externalAddress < PvrRegisterBase || externalAddress >= PvrRegisterLimit)
        {
            return;
        }

        var offset = externalAddress - PvrRegisterBase;
        pvrRegisterAccesses.Add(new DreamcastPvrRegisterAccess(
            kind,
            originalAddress,
            $"0x{originalAddress:X8}",
            offset,
            $"0x{offset:X4}",
            PvrRegisterName(offset),
            size,
            value,
            $"0x{value:X8}"));
    }

    private static string PvrRegisterName(uint offset) => offset switch
    {
        0x0000 => "PVR_ID",
        0x0004 => "PVR_REVISION",
        0x0008 => "PVR_RESET",
        0x0014 => "PVR_ISP_START",
        0x0018 => "PVR_UNK_0018",
        0x0020 => "PVR_ISP_VERTBUF_ADDR",
        0x002C => "PVR_ISP_TILEMAT_ADDR",
        0x0030 => "PVR_SPANSORT_CFG",
        0x0040 => "PVR_BORDER_COLOR",
        0x0044 => "PVR_FB_CFG_1",
        0x0048 => "PVR_FB_CFG_2",
        0x004C => "PVR_RENDER_MODULO",
        0x0050 => "PVR_FB_ADDR",
        0x0054 => "PVR_FB_IL_ADDR",
        0x005C => "PVR_FB_SIZE",
        0x0060 => "PVR_RENDER_ADDR",
        0x0064 => "PVR_RENDER_ADDR_2",
        0x0068 => "PVR_PCLIP_X",
        0x006C => "PVR_PCLIP_Y",
        0x0074 => "PVR_CHEAP_SHADOW",
        0x0078 => "PVR_OBJECT_CLIP",
        0x007C => "PVR_UNK_007C",
        0x0080 => "PVR_UNK_0080",
        0x0084 => "PVR_TEXTURE_CLIP",
        0x0088 => "PVR_BGPLANE_Z",
        0x008C => "PVR_BGPLANE_CFG",
        0x0098 => "PVR_UNK_0098",
        0x00A0 => "PVR_UNK_00A0",
        0x00A8 => "PVR_UNK_00A8",
        0x00B0 => "PVR_FOG_TABLE_COLOR",
        0x00B4 => "PVR_FOG_VERTEX_COLOR",
        0x00B8 => "PVR_FOG_DENSITY",
        0x00BC => "PVR_COLOR_CLAMP_MAX",
        0x00C0 => "PVR_COLOR_CLAMP_MIN",
        0x00C4 => "PVR_GUN_POS",
        0x00C8 => "PVR_HPOS_IRQ",
        0x00CC => "PVR_VPOS_IRQ",
        0x00D0 => "PVR_IL_CFG",
        0x00D4 => "PVR_BORDER_X",
        0x00D8 => "PVR_SCAN_CLK",
        0x00DC => "PVR_BORDER_Y",
        0x00E4 => "PVR_TEXTURE_MODULO",
        0x00E8 => "PVR_VIDEO_CFG",
        0x00EC => "PVR_BITMAP_X",
        0x00F0 => "PVR_BITMAP_Y",
        0x00F4 => "PVR_SCALER_CFG",
        0x0108 => "PVR_PALETTE_CFG",
        0x010C => "PVR_SYNC_STATUS",
        0x0110 => "PVR_UNK_0110",
        0x0114 => "PVR_UNK_0114",
        0x0118 => "PVR_UNK_0118",
        0x0124 => "PVR_TA_OPB_START",
        0x0128 => "PVR_TA_VERTBUF_START",
        0x012C => "PVR_TA_OPB_END",
        0x0130 => "PVR_TA_VERTBUF_END",
        0x0134 => "PVR_TA_OPB_POS",
        0x0138 => "PVR_TA_VERTBUF_POS",
        0x013C => "PVR_TILEMAT_CFG",
        0x0140 => "PVR_OPB_CFG",
        0x0144 => "PVR_TA_INIT",
        0x0148 => "PVR_YUV_ADDR",
        0x014C => "PVR_YUV_CFG",
        0x0150 => "PVR_YUV_STAT",
        0x0160 => "PVR_UNK_0160",
        0x0164 => "PVR_TA_OPB_INIT",
        >= 0x0200 and < 0x0200 + 0x200 => "PVR_FOG_TABLE",
        >= 0x1000 and < 0x1000 + 0x400 => "PVR_PALETTE_TABLE",
        _ => $"PVR_REG_{offset:X4}"
    };

    private uint ReadAicaRegister(uint originalAddress, uint aicaAddress, int size)
    {
        var aligned = aicaAddress & 0xFFFF_FFFCu;
        var value = aicaRegisters.GetValueOrDefault(aligned);
        var shift = (int)((aicaAddress & 0x3) * 8);
        var shifted = value >> shift;
        var masked = size switch
        {
            1 => shifted & 0xFF,
            2 => shifted & 0xFFFF,
            4 => value,
            _ => throw new MemoryMapException($"Unsupported AICA register read size: {size}")
        };

        deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.Read, originalAddress, size, masked));
        LogAicaRegisterAccess(MemoryAccessKind.Read, originalAddress, aicaAddress, size, masked);
        return masked;
    }

    private void WriteAicaRegister(uint originalAddress, uint aicaAddress, ReadOnlySpan<byte> data)
    {
        if (data.Length is not (1 or 2 or 4))
        {
            throw new MemoryMapException($"Unsupported AICA register write size: {data.Length}");
        }

        var aligned = aicaAddress & 0xFFFF_FFFCu;
        var shift = (int)((aicaAddress & 0x3) * 8);
        var mask = data.Length switch
        {
            1 => 0xFFu << shift,
            2 => 0xFFFFu << shift,
            4 => 0xFFFF_FFFFu,
            _ => 0u
        };
        var value = ToValue(data);
        var existing = aicaRegisters.GetValueOrDefault(aligned);
        aicaRegisters[aligned] = (existing & ~mask) | ((value << shift) & mask);

        deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.Write, originalAddress, data.Length, value));
        LogAicaRegisterAccess(MemoryAccessKind.Write, originalAddress, aicaAddress, data.Length, value);

        var offset = aicaAddress - AicaRegisterBase;
        if (TryGetAicaChannel(offset, out var channel, out var channelOffset) && channelOffset < 4)
        {
            SyncAicaPlaybackKeyState(channel);
        }
    }

    private void LogAicaRegisterAccess(MemoryAccessKind kind, uint originalAddress, uint aicaAddress, int size, uint value)
    {
        var offset = aicaAddress - AicaRegisterBase;
        var channel = TryGetAicaChannel(offset, out var channelIndex, out var channelOffset) ? channelIndex : (int?)null;
        aicaRegisterAccesses.Add(new DreamcastAicaRegisterAccess(
            kind,
            originalAddress,
            $"0x{originalAddress:X8}",
            offset,
            $"0x{offset:X4}",
            AicaRegisterName(offset),
            channel,
            size,
            value,
            $"0x{value:X8}"));
    }

    private IReadOnlyList<DreamcastAicaRegisterValue> CreateAicaRegisterValues() =>
        aicaRegisters
            .Where(entry => entry.Key >= AicaRegisterBase && entry.Key < AicaRegisterLimit)
            .OrderBy(entry => entry.Key)
            .Select(entry =>
            {
                var offset = entry.Key - AicaRegisterBase;
                var channel = TryGetAicaChannel(offset, out var channelIndex, out var channelOffset) ? channelIndex : (int?)null;
                return new DreamcastAicaRegisterValue(
                    offset,
                    $"0x{offset:X4}",
                    AicaRegisterName(offset),
                    channel,
                    entry.Value,
                    $"0x{entry.Value:X8}");
            })
            .ToArray();

    private IReadOnlyList<DreamcastAicaChannelSnapshot> CreateAicaChannelSnapshots()
    {
        var channels = aicaRegisterAccesses
            .Select(access => access.Channel)
            .Where(channel => channel is not null)
            .Select(channel => channel!.Value)
            .Distinct()
            .Order()
            .ToArray();

        return channels.Select(channel =>
        {
            var control = ReadAicaChannelRegister(channel, 0x00);
            var sampleLow = ReadAicaChannelRegister(channel, 0x04);
            var loopStart = ReadAicaChannelRegister(channel, 0x08);
            var loopEnd = ReadAicaChannelRegister(channel, 0x0C);
            var pitch = ReadAicaChannelRegister(channel, 0x18);
            var pan = (byte)(ReadAicaChannelRegister(channel, 0x24) & 0xFF);
            var panSendLevel = (byte)((pan >> 4) & 0x0F);
            var panPosition = (byte)(pan & 0x0F);
            var volume = (byte)((ReadAicaChannelRegister(channel, 0x28) >> 8) & 0xFF);
            var sampleAddress = ((control & 0x7Fu) << 16) | (sampleLow & 0xFFFFu);
            var keyOn = (control & 0x4000) != 0;
            var keyOnExecute = (control & 0x8000) != 0;
            var playback = aicaPlayback[channel];
            var sampleFormatMode = playback.HasLatchedFormat ? playback.SampleFormatMode : (control >> 7) & 0x3;
            var sampleStrideBytes = AicaSampleStrideBytes(sampleFormatMode);
            var playbackBytePosition = AicaPlaybackBytesForSamples(playback.Position, sampleFormatMode);
            var playbackBytesAdvanced = AicaPlaybackBytesForSamples(playback.SamplesAdvanced, sampleFormatMode);
            return new DreamcastAicaChannelSnapshot(
                channel,
                control,
                $"0x{control:X8}",
                AicaSampleFormatName(sampleFormatMode),
                AicaSampleFormatIsCompressed(sampleFormatMode),
                AicaSampleFormatIsStreamed(sampleFormatMode),
                (control & 0x0200) != 0,
                sampleAddress,
                $"0x{sampleAddress:X8}",
                sampleLow,
                $"0x{sampleLow:X8}",
                loopStart,
                $"0x{loopStart:X8}",
                loopEnd,
                $"0x{loopEnd:X8}",
                pitch,
                $"0x{pitch:X8}",
                pan,
                panSendLevel,
                panPosition,
                (byte)(0x0F - panPosition),
                panPosition,
                volume,
                playback.Playing,
                keyOn,
                keyOnExecute,
                sampleStrideBytes,
                playback.Position,
                $"0x{playback.Position:X8}",
                playbackBytePosition,
                $"0x{playbackBytePosition:X8}",
                playback.SamplesAdvanced,
                playbackBytesAdvanced,
                playback.StoppedAtLoopEnd);
        }).ToArray();
    }

    private void SyncAicaPlaybackKeyState(int channel)
    {
        var control = ReadAicaChannelRegister(channel, 0x00);
        var keyOn = (control & 0x4000) != 0;
        var keyOnExecute = (control & 0x8000) != 0;
        var playback = aicaPlayback[channel];

        if (keyOn && keyOnExecute)
        {
            playback.Playing = true;
            playback.Position = 0;
            playback.SamplesAdvanced = 0;
            playback.CpuTickRemainder = 0;
            playback.SampleFormatMode = (control >> 7) & 0x3;
            playback.HasLatchedFormat = true;
            playback.StoppedAtLoopEnd = false;
            return;
        }

        if (!keyOn)
        {
            playback.Playing = false;
        }
    }

    private void AdvanceAicaPlayback(ulong ticks)
    {
        for (var channel = 0; channel < aicaPlayback.Length; channel++)
        {
            var playback = aicaPlayback[channel];
            if (!playback.Playing)
            {
                continue;
            }

            var control = ReadAicaChannelRegister(channel, 0x00);
            if ((control & 0xC000) != 0xC000)
            {
                playback.Playing = false;
                continue;
            }

            var wholeSeconds = ticks / HardwareProfile.CpuClockHz;
            var tickRemainder = ticks % HardwareProfile.CpuClockHz;
            var baseSamples = wholeSeconds > ulong.MaxValue / AicaOutputSampleRateHz
                ? ulong.MaxValue
                : wholeSeconds * AicaOutputSampleRateHz;
            var numerator = playback.CpuTickRemainder + (tickRemainder * AicaOutputSampleRateHz);
            var samples = SaturatingAdd(baseSamples, numerator / HardwareProfile.CpuClockHz);
            playback.CpuTickRemainder = numerator % HardwareProfile.CpuClockHz;
            if (samples == 0)
            {
                continue;
            }

            AdvanceAicaChannelSamples(channel, samples);
        }
    }

    private void AdvanceAicaChannelSamples(int channel, ulong samples)
    {
        var playback = aicaPlayback[channel];
        var control = ReadAicaChannelRegister(channel, 0x00);
        var loopEnabled = (control & 0x0200) != 0;
        var loopStart = ReadAicaChannelRegister(channel, 0x08);
        var loopEnd = ReadAicaChannelRegister(channel, 0x0C);
        if (loopEnd == 0)
        {
            playback.Position = SaturatingAdd(playback.Position, samples);
            playback.SamplesAdvanced = SaturatingAdd(playback.SamplesAdvanced, samples);
            return;
        }

        if (playback.Position >= loopEnd)
        {
            playback.Playing = false;
            playback.StoppedAtLoopEnd = true;
            return;
        }

        var remaining = loopEnd - playback.Position;
        if (samples < remaining)
        {
            playback.Position += samples;
            playback.SamplesAdvanced = SaturatingAdd(playback.SamplesAdvanced, samples);
            return;
        }

        if (!loopEnabled || loopStart >= loopEnd)
        {
            playback.Position = loopEnd;
            playback.SamplesAdvanced = SaturatingAdd(playback.SamplesAdvanced, remaining);
            playback.Playing = false;
            playback.StoppedAtLoopEnd = true;
            return;
        }

        var loopLength = loopEnd - loopStart;
        var loopSamples = samples - remaining;
        playback.Position = loopStart + (loopSamples % loopLength);
        playback.SamplesAdvanced = SaturatingAdd(playback.SamplesAdvanced, samples);
    }

    private static string AicaSampleFormatName(uint mode) => mode switch
    {
        0 => "Pcm16",
        1 => "Pcm8",
        2 => "Adpcm",
        3 => "AdpcmLongStream",
        _ => "Unknown"
    };

    private static int AicaSampleStrideBytes(uint mode) => mode switch
    {
        0 => 2,
        1 => 1,
        _ => 0
    };

    private static ulong AicaPlaybackBytesForSamples(ulong samples, uint mode)
    {
        if (AicaSampleFormatIsCompressed(mode))
        {
            return (samples / 2) + (samples % 2);
        }

        return SaturatingMultiply(samples, (ulong)AicaSampleStrideBytes(mode));
    }

    private static bool AicaSampleFormatIsCompressed(uint mode) => mode is 2 or 3;

    private static bool AicaSampleFormatIsStreamed(uint mode) => mode == 3;

    private uint ReadAicaChannelRegister(int channel, uint channelOffset) =>
        aicaRegisters.GetValueOrDefault(AicaRegisterBase + ((uint)channel * 0x80u) + (channelOffset & 0xFFFF_FFFCu));

    private static DreamcastAicaPlaybackState[] CreateAicaPlaybackStates()
    {
        var states = new DreamcastAicaPlaybackState[64];
        for (var index = 0; index < states.Length; index++)
        {
            states[index] = new DreamcastAicaPlaybackState();
        }

        return states;
    }

    private static ulong SaturatingAdd(ulong left, ulong right)
    {
        var result = left + right;
        return result < left ? ulong.MaxValue : result;
    }

    private static ulong SaturatingMultiply(ulong left, ulong right)
    {
        if (left == 0 || right == 0)
        {
            return 0;
        }

        return left > ulong.MaxValue / right ? ulong.MaxValue : left * right;
    }

    private static bool TryGetAicaChannel(uint offset, out int channel, out uint channelOffset)
    {
        if (offset < 0x2000)
        {
            channel = (int)(offset / 0x80);
            channelOffset = offset % 0x80;
            return channel is >= 0 and < 64;
        }

        channel = 0;
        channelOffset = 0;
        return false;
    }

    private static string AicaRegisterName(uint offset)
    {
        if (TryGetAicaChannel(offset, out var channel, out var channelOffset))
        {
            var field = channelOffset switch
            {
                0x00 => "CONTROL",
                0x04 => "SAMPLE_ADDR_LOW",
                0x08 => "LOOP_START",
                0x0C => "LOOP_END",
                0x10 => "ENVELOPE",
                0x14 => "ENVELOPE_RELATED",
                0x18 => "PITCH",
                0x24 => "PAN_SEND",
                0x28 => "VOLUME_LPF",
                _ => $"REG_{channelOffset:X2}"
            };
            return $"AICA_CH{channel}_{field}";
        }

        return offset switch
        {
            0x2800 => "AICA_MASTER_VOLUME",
            0x280D => "AICA_MONITOR_CHANNEL",
            0x2814 => "AICA_MONITOR_POSITION",
            _ => $"AICA_REG_{offset:X4}"
        };
    }

    private static void WriteUInt16(Span<byte> bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteUInt32(Span<byte> bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteFixedAscii(Span<byte> target, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        bytes.AsSpan(0, Math.Min(bytes.Length, target.Length)).CopyTo(target);
    }

    private static byte ToUnsignedAxis(sbyte value) => (byte)(value + 128);
}

internal sealed class DreamcastAicaPlaybackState
{
    public bool Playing { get; set; }
    public ulong Position { get; set; }
    public ulong SamplesAdvanced { get; set; }
    public ulong CpuTickRemainder { get; set; }
    public uint SampleFormatMode { get; set; }
    public bool HasLatchedFormat { get; set; }
    public bool StoppedAtLoopEnd { get; set; }
}

public sealed record DreamcastSh4EventRegistersSnapshot(
    uint Tra,
    uint Expevt,
    uint Intevt);

public sealed record DreamcastMemoryRegionWriteSummary(
    string Name,
    uint Start,
    string StartHex,
    uint End,
    string EndHex,
    ulong WriteCount,
    ulong BytesWritten,
    uint? FirstAddress,
    string? FirstAddressHex,
    uint? LastAddress,
    string? LastAddressHex);

internal sealed class DreamcastMemoryRegionWriteCounter(string name, uint start, uint length)
{
    private readonly uint end = start + length;

    public ulong WriteCount { get; private set; }
    public ulong BytesWritten { get; private set; }
    public uint? FirstAddress { get; private set; }
    public uint? LastAddress { get; private set; }

    public void Reset()
    {
        WriteCount = 0;
        BytesWritten = 0;
        FirstAddress = null;
        LastAddress = null;
    }

    public void Record(uint address, int length)
    {
        var translated = DreamcastMemory.TranslateAddress(address) | 0x8000_0000u;
        var writeStart = translated;
        var writeEnd = (ulong)translated + (uint)length;
        if (writeEnd <= start || writeStart >= end)
        {
            return;
        }

        var overlapStart = Math.Max(writeStart, start);
        var overlapEnd = Math.Min(writeEnd, end);
        WriteCount++;
        BytesWritten += overlapEnd - overlapStart;
        FirstAddress ??= overlapStart;
        LastAddress = (uint)(overlapEnd - 1);
    }

    public DreamcastMemoryRegionWriteSummary CreateSummary() =>
        new(
            name,
            start,
            $"0x{start:X8}",
            end,
            $"0x{end:X8}",
            WriteCount,
            BytesWritten,
            FirstAddress,
            FirstAddress is { } first ? $"0x{first:X8}" : null,
            LastAddress,
            LastAddress is { } last ? $"0x{last:X8}" : null);
}

public sealed class MemoryMapException(string message) : InvalidOperationException(message);

public sealed record MemoryAccess(MemoryAccessKind Kind, uint Address, int Size, uint Value, uint? Pc = null);

public sealed record DreamcastMemoryWriteWatch(
    uint StartAddress = 0,
    uint EndAddress = uint.MaxValue,
    int Limit = 4096,
    IReadOnlyList<DreamcastMemoryAddressRange>? Ranges = null)
{
    public bool ShouldRecord(uint address, int length)
    {
        if (Limit <= 0 || length <= 0)
        {
            return false;
        }

        if (Ranges is { Count: > 0 })
        {
            return Ranges.Any(range => range.Overlaps(address, length));
        }

        return new DreamcastMemoryAddressRange(StartAddress, EndAddress).Overlaps(address, length);
    }
}

public sealed record DreamcastMemoryReadWatch(
    uint StartAddress = 0,
    uint EndAddress = uint.MaxValue,
    int Limit = 4096,
    IReadOnlyList<DreamcastMemoryAddressRange>? Ranges = null)
{
    public bool ShouldRecord(uint address, int length)
    {
        if (Limit <= 0 || length <= 0)
        {
            return false;
        }

        if (Ranges is { Count: > 0 })
        {
            return Ranges.Any(range => range.Overlaps(address, length));
        }

        return new DreamcastMemoryAddressRange(StartAddress, EndAddress).Overlaps(address, length);
    }
}

public sealed record DreamcastMemoryAddressRange(uint StartAddress, uint EndAddress)
{
    public bool Overlaps(uint address, int length)
    {
        if (length <= 0)
        {
            return false;
        }

        var start = Math.Min(StartAddress, EndAddress);
        var end = Math.Max(StartAddress, EndAddress);
        var accessStart = (ulong)address;
        var accessEnd = accessStart + (uint)length - 1;

        return accessStart <= end && accessEnd >= start;
    }
}

public enum MemoryAccessKind
{
    Read,
    Write,
    UnmappedRead,
    UnmappedWrite
}
