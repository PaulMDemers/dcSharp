using DcSharp.Core.Dreamcast.Memory;

namespace DcSharp.Core.Dreamcast.Audio;

public sealed record DreamcastAudioSnapshot(
    int AudioRamBytes,
    ulong NonZeroBytes,
    uint Fnv1A32,
    string Fnv1A32Hex,
    IReadOnlyList<DreamcastAicaRegisterValue> Registers,
    IReadOnlyList<DreamcastAicaRegisterAccess> RegisterAccesses,
    IReadOnlyList<DreamcastAicaChannelSnapshot> Channels,
    IReadOnlyList<DreamcastAicaCommandQueueActivity> CommandQueueActivities,
    byte[] AudioRam);

public sealed record DreamcastAicaRegisterValue(
    uint Offset,
    string OffsetHex,
    string Name,
    int? Channel,
    uint Value,
    string ValueHex);

public sealed record DreamcastAicaRegisterAccess(
    MemoryAccessKind Kind,
    uint Address,
    string AddressHex,
    uint Offset,
    string OffsetHex,
    string Name,
    int? Channel,
    int Size,
    uint Value,
    string ValueHex);

public sealed record DreamcastAicaChannelSnapshot(
    int Channel,
    uint Control,
    string ControlHex,
    string SampleFormat,
    bool Compressed,
    bool Streamed,
    bool LoopEnabled,
    uint SampleAddress,
    string SampleAddressHex,
    uint SampleAddressLow,
    string SampleAddressLowHex,
    uint LoopStart,
    string LoopStartHex,
    uint LoopEnd,
    string LoopEndHex,
    uint Pitch,
    string PitchHex,
    byte Pan,
    byte PanSendLevel,
    byte PanPosition,
    byte LeftBalance,
    byte RightBalance,
    byte Volume,
    bool Active,
    bool KeyOn,
    bool KeyOnExecute,
    int SampleStrideBytes,
    ulong PlaybackPosition,
    string PlaybackPositionHex,
    ulong PlaybackBytePosition,
    string PlaybackBytePositionHex,
    ulong PlaybackSamplesAdvanced,
    ulong PlaybackBytesAdvanced,
    bool PlaybackStoppedAtLoopEnd);

public sealed record DreamcastAicaCommandQueueActivity(
    uint QueueOffset,
    string QueueOffsetHex,
    uint Head,
    string HeadHex,
    uint Tail,
    string TailHex,
    uint NextTail,
    string NextTailHex,
    uint SizeDwords,
    uint SizeBytes,
    uint Command,
    string CommandHex,
    string CommandName,
    uint CommandId,
    string CommandIdHex,
    uint Timestamp,
    string TimestampHex,
    string Result);
