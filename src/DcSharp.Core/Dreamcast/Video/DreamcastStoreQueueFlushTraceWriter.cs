using System.Globalization;

namespace DcSharp.Core.Dreamcast.Video;

public static class DreamcastStoreQueueFlushTraceWriter
{
    public static void WriteText(
        TextWriter writer,
        IReadOnlyList<DreamcastStoreQueueFlush> flushes,
        int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(flushes);

        if (limit is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "--store-queue-flush-log-limit must be zero or greater.");
        }

        var skipped = limit is { } requestedLimit && requestedLimit < flushes.Count
            ? flushes.Count - requestedLimit
            : 0;

        writer.WriteLine("# Dreamcast SH-4 store queue flush trace");
        writer.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"# flushes={flushes.Count} skipped={skipped} limit={FormatLimit(limit)}"));
        writer.WriteLine("# columns: index pc queue source qacr qacrValue destination words");

        for (var index = skipped; index < flushes.Count; index++)
        {
            var flush = flushes[index];
            writer.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"#{index} pc={flush.InstructionPcHex ?? "-"} queue={flush.QueueIndex} source={flush.SourceAddressHex} qacr={flush.QacrAddressHex} qacrValue={flush.QacrValueHex} destination={flush.DestinationAddressHex} words={string.Join(",", flush.WordHex)}"));
        }
    }

    private static string FormatLimit(int? limit) =>
        limit is null ? "all" : limit.Value.ToString(CultureInfo.InvariantCulture);
}
