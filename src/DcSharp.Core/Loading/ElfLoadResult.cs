using DcSharp.Core.Media;

namespace DcSharp.Core.Loading;

public sealed record ElfLoadResult(
    uint EntryPoint,
    uint TranslatedEntryPoint,
    IReadOnlyList<LoadedSegment> LoadedSegments,
    IReadOnlyList<ElfSymbol> Symbols)
{
    public uint LoadedBytes => (uint)LoadedSegments.Sum(segment => segment.FileSize);
    public uint ReservedBytes => (uint)LoadedSegments.Sum(segment => segment.MemorySize);

    public ElfSymbol? FindNearestSymbol(uint address)
    {
        var virtualAddress = ToComparableVirtualAddress(address);
        return Symbols
            .Where(symbol => symbol.IsFunction && symbol.Value <= virtualAddress)
            .OrderByDescending(symbol => symbol.Contains(virtualAddress))
            .ThenByDescending(symbol => symbol.Value)
            .FirstOrDefault();
    }

    private uint ToComparableVirtualAddress(uint address)
    {
        foreach (var segment in LoadedSegments)
        {
            if (address >= segment.PhysicalAddress && (ulong)address < (ulong)segment.PhysicalAddress + segment.MemorySize)
            {
                return segment.VirtualAddress + (address - segment.PhysicalAddress);
            }
        }

        return address;
    }
}

public sealed record LoadedSegment(
    int Index,
    uint VirtualAddress,
    uint PhysicalAddress,
    uint FileSize,
    uint MemorySize,
    uint Flags,
    uint Alignment);
