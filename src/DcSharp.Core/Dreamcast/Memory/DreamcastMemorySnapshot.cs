using System.Globalization;
using System.Text;

namespace DcSharp.Core.Dreamcast.Memory;

public sealed record DreamcastMemorySnapshot(IReadOnlyList<DreamcastMemorySnapshotRange> Ranges);

public sealed record DreamcastMemorySnapshotRange(
    uint StartAddress,
    uint EndAddress,
    uint CapturedEndAddress,
    ulong RequestedBytes,
    bool Readable,
    bool Truncated,
    byte[] Bytes)
{
    public string StartAddressHex => $"0x{StartAddress:X8}";
    public string EndAddressHex => $"0x{EndAddress:X8}";
    public string CapturedEndAddressHex => $"0x{CapturedEndAddress:X8}";
}

public static class DreamcastMemorySnapshotLogWriter
{
    public static void WriteText(TextWriter writer, DreamcastMemorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(snapshot);

        writer.WriteLine("# Dreamcast final memory snapshot");
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"# ranges={snapshot.Ranges.Count}"));
        writer.WriteLine("# columns: index start end capturedEnd requestedBytes capturedBytes readable truncated nonZero firstNonZero checksum");

        for (var index = 0; index < snapshot.Ranges.Count; index++)
        {
            var range = snapshot.Ranges[index];
            writer.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"#{index} start={range.StartAddressHex} end={range.EndAddressHex} capturedEnd={range.CapturedEndAddressHex} requestedBytes={range.RequestedBytes} capturedBytes={range.Bytes.Length} readable={range.Readable} truncated={range.Truncated} nonZero={CountNonZero(range.Bytes)} firstNonZero={FormatFirstNonZero(range)} checksum=0x{Fnv1A32(range.Bytes):X8}"));
            WriteRows(writer, range);
        }
    }

    private static void WriteRows(TextWriter writer, DreamcastMemorySnapshotRange range)
    {
        const int bytesPerRow = 16;
        for (var offset = 0; offset < range.Bytes.Length; offset += bytesPerRow)
        {
            var length = Math.Min(bytesPerRow, range.Bytes.Length - offset);
            var row = range.Bytes.AsSpan(offset, length);
            writer.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"  0x{range.StartAddress + (uint)offset:X8}: bytes={FormatBytes(row)} words={FormatWords(row)} ascii=\"{FormatAscii(row)}\""));
        }
    }

    private static int CountNonZero(ReadOnlySpan<byte> bytes)
    {
        var count = 0;
        foreach (var value in bytes)
        {
            if (value != 0)
            {
                count++;
            }
        }

        return count;
    }

    private static string FormatFirstNonZero(DreamcastMemorySnapshotRange range)
    {
        for (var index = 0; index < range.Bytes.Length; index++)
        {
            if (range.Bytes[index] != 0)
            {
                return $"0x{range.StartAddress + (uint)index:X8}";
            }
        }

        return "-";
    }

    private static uint Fnv1A32(ReadOnlySpan<byte> bytes)
    {
        var hash = 2166136261u;
        foreach (var value in bytes)
        {
            hash ^= value;
            hash *= 16777619u;
        }

        return hash;
    }

    private static string FormatBytes(ReadOnlySpan<byte> bytes) =>
        string.Join(" ", bytes.ToArray().Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));

    private static string FormatWords(ReadOnlySpan<byte> bytes)
    {
        var words = new List<string>();
        for (var offset = 0; offset + 4 <= bytes.Length; offset += 4)
        {
            var value = (uint)(bytes[offset]
                | (bytes[offset + 1] << 8)
                | (bytes[offset + 2] << 16)
                | (bytes[offset + 3] << 24));
            words.Add($"0x{value:X8}");
        }

        return words.Count == 0 ? "-" : string.Join(",", words);
    }

    private static string FormatAscii(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(bytes.Length);
        foreach (var value in bytes)
        {
            builder.Append(value is >= 0x20 and <= 0x7E ? (char)value : '.');
        }

        return builder.ToString();
    }
}
