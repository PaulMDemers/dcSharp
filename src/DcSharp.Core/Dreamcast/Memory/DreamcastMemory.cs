using DcSharp.Core.Dreamcast;
using DcSharp.Core.Dreamcast.Asic;
using DcSharp.Core.Dreamcast.Audio;
using DcSharp.Core.Dreamcast.Input;
using DcSharp.Core.Dreamcast.Video;
using System.Numerics;
using System.Text;

namespace DcSharp.Core.Dreamcast.Memory;

public sealed class DreamcastMemory
{
    private const uint AreaMask = 0xE000_0000;
    private const uint P1Base = 0x8000_0000;
    private const uint P2Base = 0xA000_0000;
    private const uint PhysicalMask = 0x1FFF_FFFF;
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
    private const uint PvrTaInputBase = 0x1000_0000;
    private const uint PvrTaInputLimit = 0x1080_0000;
    private const uint PvrTaYuvBase = 0x1080_0000;
    private const uint PvrTaYuvLimit = 0x1100_0000;
    private const uint AicaRegisterBase = 0x0070_0000;
    private const uint AicaRegisterLimit = 0x0071_0000;
    private const uint AicaRamBase = 0x0080_0000;
    private const uint AicaRamBytes = 2 * 1024 * 1024;
    private const uint ScifStatus = 0xFFE8_0010;
    private const uint ScifTransmitData = 0xFFE8_000C;
    private const uint InterruptPriorityA = 0xFFD0_0004;
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

    private readonly byte[] systemRam = new byte[HardwareProfile.SystemRamBytes];
    private readonly byte[] pvrVram = new byte[PvrVramByteCount];
    private readonly byte[] aicaRam = new byte[HardwareProfile.AudioRamBytes];
    private readonly Dictionary<uint, uint> p4Registers = [];
    private readonly Dictionary<uint, uint> externalRegisters = [];
    private readonly Dictionary<uint, uint> aicaRegisters = [];
    private readonly List<MemoryAccess> deviceAccesses = [];
    private readonly List<DreamcastPvrRegisterAccess> pvrRegisterAccesses = [];
    private readonly List<DreamcastPvrTaCommandWrite> pvrTaCommandWrites = [];
    private readonly DreamcastPvrTaState pvrTaState = new();
    private readonly List<DreamcastAicaRegisterAccess> aicaRegisterAccesses = [];
    private readonly List<DreamcastMapleDmaTransfer> mapleTransfers = [];
    private readonly List<DreamcastMapleDmaBatch> mapleDmaBatches = [];
    private readonly Dictionary<byte, DreamcastControllerState> mapleControllers = [];
    private readonly List<byte> serialOutput = [];

    public DreamcastMemory(
        DreamcastControllerState? controllerA = null,
        DreamcastControllerState? controllerB = null,
        IReadOnlyDictionary<byte, DreamcastControllerState>? controllers = null)
    {
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
    public IReadOnlyList<MemoryAccess> DeviceAccesses => deviceAccesses;
    public IReadOnlyList<byte> SerialOutput => serialOutput;
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

    public static uint TranslateAddress(uint address)
    {
        var area = address & AreaMask;
        return area is P1Base or P2Base ? address & PhysicalMask : address;
    }

    public void AdvanceHardware(ulong instructions)
    {
        if (instructions == 0)
        {
            return;
        }

        for (var channel = 0; channel < 3; channel++)
        {
            AdvanceTimer(channel, instructions);
        }
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

    public void Write(uint address, ReadOnlySpan<byte> data)
    {
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

        if (!TryGetSystemRamOffset(address, data.Length, out var offset))
        {
            deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.UnmappedWrite, address, data.Length, ToValue(data)));
            return;
        }

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
            return value;
        }

        if (TryTranslateExternalRegister(address, out var externalAddress))
        {
            return (byte)(ReadExternal(address, externalAddress, 1) & 0xFF);
        }

        if (TryGetPvrVramOffset(address, 1, out var vramOffset))
        {
            return pvrVram[vramOffset];
        }

        if (TryTranslateAicaRegister(address, out var aicaAddress))
        {
            return (byte)(ReadAicaRegister(address, aicaAddress, 1) & 0xFF);
        }

        if (TryGetAicaRamOffset(address, 1, out var aicaOffset))
        {
            return aicaRam[aicaOffset];
        }

        if (!TryGetSystemRamOffset(address, 1, out var offset))
        {
            deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.UnmappedRead, address, 1, 0));
            return 0;
        }

        return systemRam[offset];
    }

    public ushort ReadUInt16(uint address)
    {
        if (IsP4Address(address))
        {
            return (ushort)(ReadP4(address, 2) & 0xFFFF);
        }

        if (TryTranslateExternalRegister(address, out var externalAddress))
        {
            return (ushort)(ReadExternal(address, externalAddress, 2) & 0xFFFF);
        }

        if (TryGetPvrVramOffset(address, 2, out var vramOffset))
        {
            return (ushort)(pvrVram[vramOffset] | (pvrVram[vramOffset + 1] << 8));
        }

        if (TryTranslateAicaRegister(address, out var aicaAddress))
        {
            return (ushort)(ReadAicaRegister(address, aicaAddress, 2) & 0xFFFF);
        }

        if (TryGetAicaRamOffset(address, 2, out var aicaOffset))
        {
            return (ushort)(aicaRam[aicaOffset] | (aicaRam[aicaOffset + 1] << 8));
        }

        if (!TryGetSystemRamOffset(address, 2, out var offset))
        {
            deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.UnmappedRead, address, 2, 0));
            return 0;
        }

        return (ushort)(systemRam[offset] | (systemRam[offset + 1] << 8));
    }

    public uint ReadUInt32(uint address)
    {
        if (IsP4Address(address))
        {
            return ReadP4(address, 4);
        }

        if (TryTranslateExternalRegister(address, out var externalAddress))
        {
            return ReadExternal(address, externalAddress, 4);
        }

        if (TryGetPvrVramOffset(address, 4, out var vramOffset))
        {
            return (uint)(pvrVram[vramOffset]
                | (pvrVram[vramOffset + 1] << 8)
                | (pvrVram[vramOffset + 2] << 16)
                | (pvrVram[vramOffset + 3] << 24));
        }

        if (TryTranslateAicaRegister(address, out var aicaAddress))
        {
            return ReadAicaRegister(address, aicaAddress, 4);
        }

        if (TryGetAicaRamOffset(address, 4, out var aicaOffset))
        {
            return (uint)(aicaRam[aicaOffset]
                | (aicaRam[aicaOffset + 1] << 8)
                | (aicaRam[aicaOffset + 2] << 16)
                | (aicaRam[aicaOffset + 3] << 24));
        }

        if (!TryGetSystemRamOffset(address, 4, out var offset))
        {
            deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.UnmappedRead, address, 4, 0));
            return 0;
        }

        return (uint)(systemRam[offset]
            | (systemRam[offset + 1] << 8)
            | (systemRam[offset + 2] << 16)
            | (systemRam[offset + 3] << 24));
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
            CreateAicaChannelSnapshots());
    }

    public DreamcastMapleSnapshot CreateMapleSnapshot() =>
        new(mapleTransfers.ToArray(), mapleDmaBatches.ToArray());

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

    private IReadOnlyList<DreamcastVideoSample> CreateVideoSamples()
    {
        (string Name, uint Offset)[] offsets =
        [
            ("origin", 0),
            ("pixel_1_0", 2),
            ("pixel_2_0", 4),
            ("pixel_0_1_320x240", 320u * 2u),
            ("pixel_1_1_320x240", ((320u * 1u) + 1u) * 2u),
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

    public void RaiseVBlankBegin() => RaiseAsicEvent(AsicEventPvrVBlankBegin);

    internal void RaiseAsicEventForDiagnostics(ushort code) => RaiseAsicEvent(code);

    public bool IsVBlankBeginInterruptEnabled() =>
        (externalRegisters.GetValueOrDefault(AsicIrq9A) & (1u << AsicEventPvrVBlankBegin)) != 0;

    public bool TryGetPendingExternalInterrupt(out uint eventCode, out int level)
    {
        var hasTimer = TryGetPendingTimerInterrupt(out var timerEventCode, out var timerLevel);
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

    private bool TryGetPendingTimerInterrupt(out uint eventCode, out int level)
    {
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

            eventCode = channel switch
            {
                0 => 0x0400,
                1 => 0x0420,
                _ => 0x0440
            };
            level = priority;
            return true;
        }

        eventCode = 0;
        level = 0;
        return false;
    }

    private uint ReadExternal(uint originalAddress, uint externalAddress, int size)
    {
        var aligned = externalAddress & 0xFFFF_FFFCu;
        var value = externalRegisters.GetValueOrDefault(aligned);
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
        var value = p4Registers.GetValueOrDefault(aligned);
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

        deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.Read, address, size, masked));
        return masked;
    }

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
        p4Registers[aligned] = (existing & ~mask) | ((value << shift) & mask);
        deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.Write, address, data.Length, value));

        if (address == ScifTransmitData && data.Length == 1)
        {
            serialOutput.Add(data[0]);
        }
    }

    private static uint ToValue(ReadOnlySpan<byte> data) => data.Length switch
    {
        0 => 0,
        1 => data[0],
        2 => (uint)(data[0] | (data[1] << 8)),
        _ => (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24))
    };

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
            RenderPvrTaStripPreview(renderCommand);
        }

        deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.Write, address, data.Length, value));
        return true;
    }

    private void RenderPvrTaStripPreview(DreamcastPvrTaRenderCommand command)
    {
        var vertices = command.Strip.Vertices.Take(3).ToArray();
        if (vertices.Length < 3)
        {
            return;
        }

        var minX = vertices.Min(vertex => vertex.X);
        var minY = vertices.Min(vertex => vertex.Y);
        var a = new Vector2(vertices[0].X - minX, vertices[0].Y - minY);
        var b = new Vector2(vertices[1].X - minX, vertices[1].Y - minY);
        var c = new Vector2(vertices[2].X - minX, vertices[2].Y - minY);
        var maxX = (int)MathF.Max(a.X, MathF.Max(b.X, c.X));
        var maxY = (int)MathF.Max(a.Y, MathF.Max(b.Y, c.Y));

        for (var y = 0; y <= maxY; y++)
        {
            for (var x = 0; x <= maxX; x++)
            {
                if (IsInsideTriangle(new Vector2(x, y), a, b, c))
                {
                    WriteRgb565VramPixel(PvrPreviewPixelIndex(x, y), command.Strip.Rgb565);
                }
            }
        }
    }

    private static bool IsInsideTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        var edge0 = EdgeFunction(a, b, point);
        var edge1 = EdgeFunction(b, c, point);
        var edge2 = EdgeFunction(c, a, point);
        return (edge0 >= 0 && edge1 >= 0 && edge2 >= 0)
            || (edge0 <= 0 && edge1 <= 0 && edge2 <= 0);
    }

    private static float EdgeFunction(Vector2 a, Vector2 b, Vector2 point) =>
        ((point.X - a.X) * (b.Y - a.Y)) - ((point.Y - a.Y) * (b.X - a.X));

    private static int PvrPreviewPixelIndex(int x, int y)
    {
        const int width = 320;
        return (y * width) + x;
    }

    private void WriteRgb565VramPixel(int pixelIndex, ushort color)
    {
        var offset = pixelIndex * 2;
        if (offset + 1 >= pvrVram.Length)
        {
            return;
        }

        pvrVram[offset] = (byte)(color & 0xFF);
        pvrVram[offset + 1] = (byte)(color >> 8);
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
            var volume = (byte)((ReadAicaChannelRegister(channel, 0x28) >> 8) & 0xFF);
            var sampleAddress = ((control & 0x7Fu) << 16) | (sampleLow & 0xFFFFu);
            var keyOn = (control & 0x4000) != 0;
            var keyOnExecute = (control & 0x8000) != 0;
            return new DreamcastAicaChannelSnapshot(
                channel,
                control,
                $"0x{control:X8}",
                AicaSampleFormatName((control >> 7) & 0x3),
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
                volume,
                keyOn && keyOnExecute,
                keyOn,
                keyOnExecute);
        }).ToArray();
    }

    private static string AicaSampleFormatName(uint mode) => mode switch
    {
        0 => "Pcm16",
        1 => "Pcm8",
        2 => "Adpcm",
        3 => "AdpcmLongStream",
        _ => "Unknown"
    };

    private uint ReadAicaChannelRegister(int channel, uint channelOffset) =>
        aicaRegisters.GetValueOrDefault(AicaRegisterBase + ((uint)channel * 0x80u) + (channelOffset & 0xFFFF_FFFCu));

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

public sealed class MemoryMapException(string message) : InvalidOperationException(message);

public sealed record MemoryAccess(MemoryAccessKind Kind, uint Address, int Size, uint Value);

public enum MemoryAccessKind
{
    Read,
    Write,
    UnmappedRead,
    UnmappedWrite
}
