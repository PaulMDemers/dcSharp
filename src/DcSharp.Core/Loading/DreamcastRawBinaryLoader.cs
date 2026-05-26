using DcSharp.Core.Dreamcast.Memory;

namespace DcSharp.Core.Loading;

public sealed class DreamcastRawBinaryLoader
{
    public const uint DefaultLoadAddress = 0x8C01_0000;
    public const uint IpBinLoadAddress = 0x8C00_8000;

    public ElfLoadResult Load(ReadOnlySpan<byte> data, DreamcastMemory memory, uint loadAddress = DefaultLoadAddress, ReadOnlySpan<byte> ipBin = default, uint? entryPoint = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        if (data.Length == 0)
        {
            throw new InvalidDataException("Raw Dreamcast boot binary is empty.");
        }

        if (!ipBin.IsEmpty)
        {
            memory.Write(IpBinLoadAddress, ipBin);
        }

        memory.Write(loadAddress, data);
        var size = (uint)data.Length;
        return new ElfLoadResult(
            entryPoint ?? loadAddress,
            DreamcastMemory.TranslateAddress(entryPoint ?? loadAddress),
            [new LoadedSegment(0, loadAddress, DreamcastMemory.TranslateAddress(loadAddress), size, size, 0x5, 4)],
            []);
    }
}
