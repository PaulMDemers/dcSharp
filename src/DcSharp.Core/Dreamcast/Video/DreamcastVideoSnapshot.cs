namespace DcSharp.Core.Dreamcast.Video;

public sealed record DreamcastVideoSnapshot(
    int VramBytes,
    ulong NonZeroBytes,
    uint Fnv1A32,
    string Fnv1A32Hex,
    uint? FirstNonZeroOffset,
    string? FirstNonZeroOffsetHex,
    IReadOnlyList<DreamcastVideoSample> Samples);

public sealed record DreamcastVideoSample(
    string Name,
    uint Offset,
    string OffsetHex,
    ushort Rgb565,
    string Rgb565Hex);
