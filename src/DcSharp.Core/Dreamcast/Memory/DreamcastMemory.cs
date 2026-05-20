using DcSharp.Core.Dreamcast;
using DcSharp.Core.Dreamcast.Input;
using DcSharp.Core.Dreamcast.Video;
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
    private const uint AsicIrq9A = 0x005F_6910;
    private const uint AsicIrqBA = 0x005F_6920;
    private const uint AsicIrqDA = 0x005F_6930;
    private const uint MapleDmaAddress = 0x005F_6C04;
    private const uint MapleState = 0x005F_6C18;
    private const uint MapleStateDma = 1;
    private const byte MapleResponseNone = 0xFF;
    private const byte MapleResponseDeviceInfo = 5;
    private const byte MapleResponseDataTransfer = 8;
    private const byte MapleCommandDeviceInfo = 1;
    private const byte MapleCommandGetCondition = 9;
    private const byte MaplePortAUnit0Address = 0x20;
    private const uint MapleFunctionController = 0x0100_0000;
    private const uint MapleStandardControllerCapabilities = 0xFE06_0F00;
    private const ushort AsicEventPvrVBlankBegin = 0x0003;
    private const ushort AsicEventMapleDma = 0x000C;

    private readonly byte[] systemRam = new byte[HardwareProfile.SystemRamBytes];
    private readonly byte[] pvrVram = new byte[PvrVramByteCount];
    private readonly Dictionary<uint, uint> p4Registers = [];
    private readonly Dictionary<uint, uint> externalRegisters = [];
    private readonly List<MemoryAccess> deviceAccesses = [];
    private readonly List<byte> serialOutput = [];
    private DreamcastControllerState controllerA;

    public DreamcastMemory(DreamcastControllerState? controllerA = null)
    {
        this.controllerA = controllerA ?? DreamcastControllerState.Neutral;
    }

    public int SystemRamBytes => systemRam.Length;
    public int PvrVramBytes => pvrVram.Length;
    public IReadOnlyList<MemoryAccess> DeviceAccesses => deviceAccesses;
    public IReadOnlyList<byte> SerialOutput => serialOutput;
    public DreamcastControllerState ControllerA
    {
        get => controllerA;
        set => controllerA = value;
    }

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
            CreateVideoSamples());
    }

    private IReadOnlyList<DreamcastVideoSample> CreateVideoSamples()
    {
        (string Name, uint Offset)[] offsets =
        [
            ("origin", 0),
            ("pixel_1_0", 2),
            ("pixel_2_0", 4),
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

    private static bool IsP4Address(uint address) => address >= P4Base;

    private static bool TryTranslateExternalRegister(uint address, out uint externalAddress)
    {
        externalAddress = TranslateAddress(address);
        return externalAddress >= ExternalRegisterBase && externalAddress < ExternalRegisterLimit;
    }

    public void RaiseVBlankBegin() => RaiseAsicEvent(AsicEventPvrVBlankBegin);

    public bool TryGetPendingExternalInterrupt(out uint eventCode, out int level)
    {
        if (TryGetPendingTimerInterrupt(out eventCode, out level))
        {
            return true;
        }

        var pendingA = externalRegisters.GetValueOrDefault(AsicAckA);
        if (pendingA != 0 && (pendingA & externalRegisters.GetValueOrDefault(AsicIrqDA)) != 0)
        {
            eventCode = 0x03A0;
            level = 13;
            return true;
        }

        if (pendingA != 0 && (pendingA & externalRegisters.GetValueOrDefault(AsicIrqBA)) != 0)
        {
            eventCode = 0x0360;
            level = 11;
            return true;
        }

        if (pendingA != 0 && (pendingA & externalRegisters.GetValueOrDefault(AsicIrq9A)) != 0)
        {
            eventCode = 0x0320;
            level = 9;
            return true;
        }

        eventCode = 0;
        level = 0;
        return false;
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

        if (aligned is AsicAckA or AsicAckA + 4 or AsicAckA + 8 && data.Length == 4)
        {
            stored = existing & ~value;
        }

        externalRegisters[aligned] = stored;
        deviceAccesses.Add(new MemoryAccess(MemoryAccessKind.Write, originalAddress, data.Length, value));

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

    private void CompleteMapleDma()
    {
        var dmaAddress = externalRegisters.GetValueOrDefault(MapleDmaAddress);
        if (dmaAddress != 0)
        {
            WriteMapleResponses(dmaAddress);
        }

        externalRegisters[MapleState] = 0;
        RaiseAsicEvent(AsicEventMapleDma);
    }

    private void WriteMapleResponses(uint dmaAddress)
    {
        var descriptor = dmaAddress | P1Base;
        for (var frames = 0; frames < 64; frames++)
        {
            var header = ReadUInt32(descriptor);
            var receiveBuffer = ReadUInt32(descriptor + 4) | P1Base;
            var commandWord = ReadUInt32(descriptor + 8);
            var command = (byte)(commandWord & 0xFF);
            var destination = (byte)((commandWord >> 8) & 0xFF);
            var length = header & 0xFF;

            if (receiveBuffer != P1Base)
            {
                WriteMapleResponse(receiveBuffer, command, destination);
            }

            descriptor += 12 + (length * 4);
            if ((header & 0x8000_0000) != 0)
            {
                return;
            }
        }
    }

    private void WriteMapleResponse(uint receiveBuffer, byte command, byte destination)
    {
        if (destination == MaplePortAUnit0Address && command == MapleCommandDeviceInfo)
        {
            WriteMapleControllerDeviceInfo(receiveBuffer);
            return;
        }

        if (destination == MaplePortAUnit0Address && command == MapleCommandGetCondition)
        {
            WriteMapleControllerCondition(receiveBuffer);
            return;
        }

        Write(receiveBuffer, [MapleResponseNone]);
    }

    private void WriteMapleControllerDeviceInfo(uint receiveBuffer)
    {
        var response = new byte[4 + 112];
        response[0] = MapleResponseDeviceInfo;
        response[2] = MaplePortAUnit0Address;
        response[3] = 28;
        WriteUInt32(response, 4, MapleFunctionController);
        WriteUInt32(response, 8, MapleStandardControllerCapabilities);
        response[20] = 0xFF;
        response[21] = 0;
        WriteFixedAscii(response.AsSpan(22, 30), "dcSharp Virtual Controller");
        WriteFixedAscii(response.AsSpan(52, 60), "Produced by or under license from dcSharp");
        WriteUInt16(response, 112, 0x01AE);
        WriteUInt16(response, 114, 0x01F4);
        Write(receiveBuffer, response);
    }

    private void WriteMapleControllerCondition(uint receiveBuffer)
    {
        var response = new byte[16];
        response[0] = MapleResponseDataTransfer;
        response[2] = MaplePortAUnit0Address;
        response[3] = 3;
        WriteUInt32(response, 4, MapleFunctionController);
        WriteUInt16(response, 8, (ushort)~(ushort)controllerA.Buttons);
        response[10] = controllerA.RightTrigger;
        response[11] = controllerA.LeftTrigger;
        response[12] = ToUnsignedAxis(controllerA.JoyX);
        response[13] = ToUnsignedAxis(controllerA.JoyY);
        response[14] = ToUnsignedAxis(controllerA.Joy2X);
        response[15] = ToUnsignedAxis(controllerA.Joy2Y);
        Write(receiveBuffer, response);
    }

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
