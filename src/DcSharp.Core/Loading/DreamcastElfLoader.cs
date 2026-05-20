using DcSharp.Core.Dreamcast.Memory;
using DcSharp.Core.Media;

namespace DcSharp.Core.Loading;

public sealed class DreamcastElfLoader
{
    public ElfLoadResult Load(ElfFile elf, DreamcastMemory memory)
    {
        ArgumentNullException.ThrowIfNull(elf);
        ArgumentNullException.ThrowIfNull(memory);

        if (!elf.IsDreamcastCandidate)
        {
            throw new InvalidDataException("ELF is not a little-endian SuperH executable.");
        }

        var loadedSegments = new List<LoadedSegment>();

        foreach (var segment in elf.ProgramHeaders.Where(header => header.IsLoadable))
        {
            if (segment.MemorySize == 0)
            {
                continue;
            }

            memory.Clear(segment.VirtualAddress, segment.MemorySize);
            memory.Write(segment.VirtualAddress, segment.Data);

            loadedSegments.Add(new LoadedSegment(
                segment.Index,
                segment.VirtualAddress,
                DreamcastMemory.TranslateAddress(segment.VirtualAddress),
                segment.FileSize,
                segment.MemorySize,
                segment.Flags,
                segment.Alignment));
        }

        if (loadedSegments.Count == 0)
        {
            throw new InvalidDataException("ELF does not contain any loadable program segments.");
        }

        return new ElfLoadResult(
            elf.EntryPoint,
            DreamcastMemory.TranslateAddress(elf.EntryPoint),
            loadedSegments,
            elf.Symbols);
    }
}
