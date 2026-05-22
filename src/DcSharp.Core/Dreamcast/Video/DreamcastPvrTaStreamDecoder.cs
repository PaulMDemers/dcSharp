namespace DcSharp.Core.Dreamcast.Video;

public static class DreamcastPvrTaStreamDecoder
{
    public static IReadOnlyList<DreamcastPvrTaStreamWrite> Decode(IReadOnlyList<DreamcastPvrTaCommandWrite> writes)
    {
        ArgumentNullException.ThrowIfNull(writes);

        var decoded = new List<DreamcastPvrTaStreamWrite>(writes.Count);
        DreamcastPvrTaParameterHeader? payloadHeader = null;
        var payloadWordsRemaining = 0;
        var payloadWordIndex = 0;

        foreach (var write in writes)
        {
            if (payloadHeader is not null && payloadWordsRemaining > 0 && string.Equals(write.Region, payloadHeader.Region, StringComparison.Ordinal))
            {
                decoded.Add(new DreamcastPvrTaStreamWrite(
                    write,
                    "Payload",
                    payloadHeader.Kind,
                    payloadHeader.Value,
                    payloadHeader.ValueHex,
                    payloadWordIndex,
                    payloadWordsRemaining - 1));
                payloadWordIndex++;
                payloadWordsRemaining--;
                if (payloadWordsRemaining == 0)
                {
                    payloadHeader = null;
                    payloadWordIndex = 0;
                }

                continue;
            }

            var header = DreamcastPvrTaParameterDecoder.Decode(write.Region, write.Value);
            decoded.Add(new DreamcastPvrTaStreamWrite(
                write,
                "Control",
                header.Kind,
                header.Value,
                header.ValueHex,
                null,
                header.ExpectedPayloadWords));
            if (header.ExpectedPayloadWords is > 0)
            {
                payloadHeader = header;
                payloadWordsRemaining = header.ExpectedPayloadWords.Value;
                payloadWordIndex = 0;
            }
            else
            {
                payloadHeader = null;
                payloadWordsRemaining = 0;
                payloadWordIndex = 0;
            }
        }

        return decoded;
    }
}

public sealed record DreamcastPvrTaStreamWrite(
    DreamcastPvrTaCommandWrite Write,
    string Role,
    string ControlKind,
    uint ControlValue,
    string ControlValueHex,
    int? PayloadWordIndex,
    int? PayloadWordsRemaining);
