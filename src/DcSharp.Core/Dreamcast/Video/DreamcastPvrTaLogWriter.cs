using System.Globalization;

namespace DcSharp.Core.Dreamcast.Video;

public static class DreamcastPvrTaLogWriter
{
    public static void WriteText(TextWriter writer, IReadOnlyList<DreamcastPvrTaCommandWrite> writes, int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(writes);

        if (limit is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "--pvr-ta-log-limit must be zero or greater.");
        }

        var decoded = DreamcastPvrTaStreamDecoder.Decode(writes);
        var skipped = limit is { } requestedLimit && requestedLimit < decoded.Count
            ? decoded.Count - requestedLimit
            : 0;

        writer.WriteLine("# Dreamcast PVR TA stream");
        writer.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"# writes={writes.Count} decoded={decoded.Count} skipped={skipped} limit={FormatLimit(limit)}"));
        writer.WriteLine("# columns: index address region role controlKind rawKind list endOfStrip payloadIndex payloadRemaining payloadName controlValue value");

        for (var index = skipped; index < decoded.Count; index++)
        {
            var streamWrite = decoded[index];
            var write = streamWrite.Write;
            writer.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"#{index} addr={write.AddressHex} region={write.Region} role={streamWrite.Role} controlKind={streamWrite.ControlKind} rawKind={write.Kind} list={write.ListTypeName ?? "-"} endOfStrip={write.EndOfStrip} payloadIndex={FormatNullable(streamWrite.PayloadWordIndex)} payloadRemaining={FormatNullable(streamWrite.PayloadWordsRemaining)} payloadName={streamWrite.PayloadWordName ?? "-"} controlValue={streamWrite.ControlValueHex} value={write.ValueHex}"));
        }
    }

    private static string FormatNullable(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string FormatLimit(int? limit) =>
        limit?.ToString(CultureInfo.InvariantCulture) ?? "all";
}
