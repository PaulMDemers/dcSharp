namespace DcSharp.Core.Media;

public static class DreamcastBootScrambler
{
    private const int MaxChunkSize = 2048 * 1024;
    private const int SliceSize = 32;

    public static byte[] Descramble(ReadOnlySpan<byte> source)
    {
        var destination = new byte[source.Length];
        TransformReadOrder(source, destination);
        return destination;
    }

    public static byte[] Scramble(ReadOnlySpan<byte> source)
    {
        var destination = new byte[source.Length];
        TransformWriteOrder(source, destination);
        return destination;
    }

    private static void TransformReadOrder(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var random = new DreamcastScrambleRandom(source.Length);
        var sourceOffset = 0;
        var destinationOffset = 0;
        var remaining = source.Length;
        for (var chunkSize = MaxChunkSize; chunkSize >= SliceSize; chunkSize >>= 1)
        {
            while (remaining >= chunkSize)
            {
                foreach (var sliceIndex in SliceOrder(chunkSize, random))
                {
                    source.Slice(sourceOffset, SliceSize).CopyTo(destination.Slice(destinationOffset + (sliceIndex * SliceSize), SliceSize));
                    sourceOffset += SliceSize;
                }

                remaining -= chunkSize;
                destinationOffset += chunkSize;
            }
        }

        if (remaining > 0)
        {
            source[sourceOffset..].CopyTo(destination[destinationOffset..]);
        }
    }

    private static void TransformWriteOrder(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var random = new DreamcastScrambleRandom(source.Length);
        var sourceOffset = 0;
        var destinationOffset = 0;
        var remaining = source.Length;
        for (var chunkSize = MaxChunkSize; chunkSize >= SliceSize; chunkSize >>= 1)
        {
            while (remaining >= chunkSize)
            {
                foreach (var sliceIndex in SliceOrder(chunkSize, random))
                {
                    source.Slice(sourceOffset + (sliceIndex * SliceSize), SliceSize).CopyTo(destination.Slice(destinationOffset, SliceSize));
                    destinationOffset += SliceSize;
                }

                remaining -= chunkSize;
                sourceOffset += chunkSize;
            }
        }

        if (remaining > 0)
        {
            source[sourceOffset..].CopyTo(destination[destinationOffset..]);
        }
    }

    private static IReadOnlyList<int> SliceOrder(int chunkSize, DreamcastScrambleRandom random)
    {
        var sliceCount = chunkSize / SliceSize;
        var indexes = new int[sliceCount];
        for (var index = 0; index < indexes.Length; index++)
        {
            indexes[index] = index;
        }

        var order = new int[sliceCount];
        var orderIndex = 0;
        for (var index = sliceCount - 1; index >= 0; index--)
        {
            var replacement = (int)(((long)random.Next() * index) >> 16);
            (indexes[index], indexes[replacement]) = (indexes[replacement], indexes[index]);
            order[orderIndex++] = indexes[index];
        }

        return order;
    }

    private sealed class DreamcastScrambleRandom
    {
        private int seed;

        public DreamcastScrambleRandom(int fileSize)
        {
            seed = fileSize & 0xFFFF;
        }

        public int Next()
        {
            seed = (seed * 2109 + 9273) & 0x7FFF;
            return (seed + 0xC000) & 0xFFFF;
        }
    }
}
