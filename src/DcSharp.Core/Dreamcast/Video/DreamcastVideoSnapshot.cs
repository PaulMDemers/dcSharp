using DcSharp.Core.Dreamcast.Memory;

namespace DcSharp.Core.Dreamcast.Video;

public sealed record DreamcastVideoSnapshot(
    int VramBytes,
    ulong NonZeroBytes,
    uint Fnv1A32,
    string Fnv1A32Hex,
    uint? FirstNonZeroOffset,
    string? FirstNonZeroOffsetHex,
    IReadOnlyList<DreamcastVideoSample> Samples,
    IReadOnlyList<DreamcastPvrRegisterValue> PvrRegisters,
    IReadOnlyList<DreamcastPvrRegisterAccess> PvrRegisterAccesses,
    IReadOnlyList<DreamcastPvrTaCommandWrite> PvrTaCommandWrites,
    IReadOnlyList<DreamcastPvrTaStrip> PvrTaStrips,
    byte[] Vram);

public sealed record DreamcastVideoSample(
    string Name,
    uint Offset,
    string OffsetHex,
    ushort Rgb565,
    string Rgb565Hex);

public sealed record DreamcastPvrRegisterValue(
    uint Offset,
    string OffsetHex,
    string Name,
    uint Value,
    string ValueHex);

public sealed record DreamcastPvrRegisterAccess(
    MemoryAccessKind Kind,
    uint Address,
    string AddressHex,
    uint Offset,
    string OffsetHex,
    string Name,
    int Size,
    uint Value,
    string ValueHex);

public sealed record DreamcastPvrTaCommandWrite(
    uint Address,
    string AddressHex,
    string Region,
    string Kind,
    int? ListType,
    string? ListTypeName,
    bool EndOfStrip,
    int Size,
    uint Value,
    string ValueHex);
