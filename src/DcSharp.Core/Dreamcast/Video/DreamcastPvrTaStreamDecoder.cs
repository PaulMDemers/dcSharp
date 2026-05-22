namespace DcSharp.Core.Dreamcast.Video;

public static class DreamcastPvrTaStreamDecoder
{
    public static IReadOnlyList<DreamcastPvrTaStreamWrite> Decode(IReadOnlyList<DreamcastPvrTaCommandWrite> writes)
    {
        ArgumentNullException.ThrowIfNull(writes);

        var decoded = new List<DreamcastPvrTaStreamWrite>(writes.Count);
        PayloadControl? payloadControl = null;
        var payloadWordsRemaining = 0;
        var payloadWordIndex = 0;

        foreach (var write in writes)
        {
            if (payloadControl is not null && payloadWordsRemaining > 0 && string.Equals(write.Region, payloadControl.Region, StringComparison.Ordinal))
            {
                decoded.Add(new DreamcastPvrTaStreamWrite(
                    write,
                    "Payload",
                    payloadControl.Kind,
                    payloadControl.Value,
                    payloadControl.ValueHex,
                    payloadWordIndex,
                    payloadWordsRemaining - 1));
                payloadWordIndex++;
                payloadWordsRemaining--;
                if (payloadWordsRemaining == 0)
                {
                    payloadControl = null;
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
                ExpectedPayloadWords(header)));
            if (ExpectedPayloadWords(header) is > 0 and var expectedPayloadWords)
            {
                payloadControl = new PayloadControl(header.Region, header.Kind, header.Value, header.ValueHex);
                payloadWordsRemaining = expectedPayloadWords;
                payloadWordIndex = 0;
            }
            else
            {
                payloadControl = null;
                payloadWordsRemaining = 0;
                payloadWordIndex = 0;
            }
        }

        return decoded;
    }

    private static int? ExpectedPayloadWords(DreamcastPvrTaParameterHeader header) =>
        header.ExpectedPayloadWords
        ?? (header.Kind is "Vertex" or "VertexEndOfStrip" ? 7 : null);
}

internal sealed record PayloadControl(string Region, string Kind, uint Value, string ValueHex);

public sealed record DreamcastPvrTaStreamWrite(
    DreamcastPvrTaCommandWrite Write,
    string Role,
    string ControlKind,
    uint ControlValue,
    string ControlValueHex,
    int? PayloadWordIndex,
    int? PayloadWordsRemaining);
