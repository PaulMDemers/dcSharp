using DcSharp.Core.Dreamcast.Memory;

namespace DcSharp.Core.Dreamcast.Audio;

public sealed record DreamcastAudioSnapshot(
    int AudioRamBytes,
    ulong NonZeroBytes,
    uint Fnv1A32,
    string Fnv1A32Hex,
    IReadOnlyList<DreamcastAicaRegisterValue> Registers,
    IReadOnlyList<DreamcastAicaRegisterAccess> RegisterAccesses,
    IReadOnlyList<DreamcastAicaChannelSnapshot> Channels);

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
