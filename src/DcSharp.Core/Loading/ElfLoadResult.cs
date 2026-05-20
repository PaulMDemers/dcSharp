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
        if (!TryGetComparableVirtualAddress(address, out var virtualAddress))
        {
            return null;
        }

        return Symbols
            .Where(symbol => symbol.IsFunction && symbol.Value <= virtualAddress)
            .OrderByDescending(symbol => symbol.Contains(virtualAddress))
            .ThenByDescending(symbol => symbol.Value)
            .FirstOrDefault();
    }

    private bool TryGetComparableVirtualAddress(uint address, out uint virtualAddress)
    {
        foreach (var segment in LoadedSegments)
        {
            if (address >= segment.PhysicalAddress && (ulong)address < (ulong)segment.PhysicalAddress + segment.MemorySize)
            {
                virtualAddress = segment.VirtualAddress + (address - segment.PhysicalAddress);
                return true;
            }

            if (address >= segment.VirtualAddress && (ulong)address < (ulong)segment.VirtualAddress + segment.MemorySize)
            {
                virtualAddress = address;
                return true;
            }
        }

        virtualAddress = 0;
        return false;
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
