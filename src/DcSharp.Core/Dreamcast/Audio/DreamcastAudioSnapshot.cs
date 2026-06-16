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
    IReadOnlyList<DreamcastAicaCommandQueueSnapshot> CommandQueues,
    IReadOnlyList<DreamcastAicaRamRegionSnapshot> RamRegions,
    IReadOnlyList<DreamcastAicaRamAccessHotspot> RamAccessHotspots,
    IReadOnlyList<DreamcastAicaRamDriverField> DriverFields,
    IReadOnlyList<DreamcastAicaRamFieldAccess> RamFieldAccesses,
    IReadOnlyList<DreamcastAicaRamTextMarker> TextMarkers,
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

public sealed record DreamcastAicaCommandQueueSnapshot(
    uint Offset,
    string OffsetHex,
    string Role,
    uint Head,
    string HeadHex,
    uint Tail,
    string TailHex,
    uint Size,
    string SizeHex,
    bool Valid,
    bool ProcessOk,
    bool Pending,
    uint Data,
    string DataHex);

public sealed record DreamcastAicaRamRegionSnapshot(
    uint StartOffset,
    string StartOffsetHex,
    uint EndOffsetExclusive,
    string EndOffsetExclusiveHex,
    uint Length,
    string LengthHex,
    ulong NonZeroBytes,
    double DensityPercent,
    uint Fnv1A32,
    string Fnv1A32Hex,
    string Area);

public sealed record DreamcastAicaRamAccessHotspot(
    MemoryAccessKind Kind,
    uint Offset,
    string OffsetHex,
    string Name,
    uint Address,
    string AddressHex,
    int Size,
    ulong Count,
    uint LastValue,
    string LastValueHex,
    uint? LastPc,
    string? LastPcHex,
    string Area);

public sealed record DreamcastAicaRamDriverField(
    uint Offset,
    string OffsetHex,
    string Name,
    uint Address,
    string AddressHex,
    uint Value,
    string ValueHex,
    ulong ReadCount,
    ulong WriteCount,
    uint? LastReadPc,
    string? LastReadPcHex,
    uint? LastWritePc,
    string? LastWritePcHex,
    string Area);

public sealed record DreamcastAicaRamFieldAccess(
    MemoryAccessKind Kind,
    uint Offset,
    string OffsetHex,
    string Name,
    uint Address,
    string AddressHex,
    int Size,
    uint Value,
    string ValueHex,
    uint? Pc,
    string? PcHex,
    string Area);

public sealed record DreamcastAicaRamTextMarker(
    uint Offset,
    string OffsetHex,
    int Length,
    string Text);
