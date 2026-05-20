namespace DcSharp.Core.Loading;

public sealed record ElfLoadResult(
    uint EntryPoint,
    uint TranslatedEntryPoint,
    IReadOnlyList<LoadedSegment> LoadedSegments)
{
    public uint LoadedBytes => (uint)LoadedSegments.Sum(segment => segment.FileSize);
    public uint ReservedBytes => (uint)LoadedSegments.Sum(segment => segment.MemorySize);
}

public sealed record LoadedSegment(
    int Index,
    uint VirtualAddress,
    uint PhysicalAddress,
    uint FileSize,
    uint MemorySize,
    uint Flags,
    uint Alignment);
