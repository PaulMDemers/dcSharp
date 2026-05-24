namespace DcSharp.Core.Dreamcast.Video;

public static class DreamcastPvrTaStreamDecoder
{
    public static IReadOnlyList<DreamcastPvrTaStreamWrite> Decode(IReadOnlyList<DreamcastPvrTaCommandWrite> writes)
    {
        ArgumentNullException.ThrowIfNull(writes);

        var decoded = new List<DreamcastPvrTaStreamWrite>(writes.Count);
        PayloadControl? payloadControl = null;
        PendingSpriteHeader? pendingSpriteHeader = null;
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
                    payloadWordsRemaining - 1,
                    PayloadWordName(payloadControl.Kind, payloadWordIndex, payloadControl.SpriteTextureEnabled)));
                payloadControl.AddPayloadWord(write.Value);
                payloadWordIndex++;
                payloadWordsRemaining--;
                if (payloadWordsRemaining == 0)
                {
                    if (string.Equals(payloadControl.Kind, "SpriteHeader", StringComparison.Ordinal))
                    {
                        pendingSpriteHeader = new PendingSpriteHeader(payloadControl.Region, SpriteHeaderTextureEnabled(payloadControl.PayloadWords));
                    }

                    payloadControl = null;
                    payloadWordIndex = 0;
                }

                continue;
            }

            if (pendingSpriteHeader is not null)
            {
                if (string.Equals(write.Region, pendingSpriteHeader.Region, StringComparison.Ordinal) && IsVertexControl(write))
                {
                    var controlKind = string.Equals(write.Kind, "VertexEndOfStrip", StringComparison.Ordinal)
                        ? "SpriteVertexEndOfStrip"
                        : "SpriteVertex";
                    decoded.Add(new DreamcastPvrTaStreamWrite(
                        write,
                        "Control",
                        controlKind,
                        write.Value,
                        write.ValueHex,
                        null,
                        15,
                        null));
                    payloadControl = new PayloadControl(write.Region, controlKind, write.Value, write.ValueHex, pendingSpriteHeader.TextureEnabled);
                    payloadWordsRemaining = 15;
                    payloadWordIndex = 0;
                    pendingSpriteHeader = null;
                    continue;
                }

                pendingSpriteHeader = null;
            }

            var header = DreamcastPvrTaParameterDecoder.Decode(write.Region, write.Value);
            decoded.Add(new DreamcastPvrTaStreamWrite(
                write,
                "Control",
                header.Kind,
                header.Value,
                header.ValueHex,
                null,
                ExpectedPayloadWords(header),
                null));
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

    private static bool IsVertexControl(DreamcastPvrTaCommandWrite write) =>
        string.Equals(write.Kind, "Vertex", StringComparison.Ordinal)
        || string.Equals(write.Kind, "VertexEndOfStrip", StringComparison.Ordinal);

    private static bool SpriteHeaderTextureEnabled(IReadOnlyList<uint> payloadWords) =>
        payloadWords.Count > 0 && (payloadWords[0] & 0x0200_0000u) != 0;

    private static string? PayloadWordName(string controlKind, int payloadWordIndex, bool? spriteTextureEnabled = null) =>
        controlKind switch
        {
            "PolygonHeader" => PolygonHeaderPayloadWordName(payloadWordIndex),
            "SpriteHeader" => SpriteHeaderPayloadWordName(payloadWordIndex),
            "SpriteVertex" or "SpriteVertexEndOfStrip" => SpriteVertexPayloadWordName(payloadWordIndex, spriteTextureEnabled == true),
            "ModifierVolume" => ModifierVolumePayloadWordName(payloadWordIndex),
            "UserClip" => UserClipPayloadWordName(payloadWordIndex),
            _ => null
        };

    private static string? PolygonHeaderPayloadWordName(int payloadWordIndex) =>
        payloadWordIndex switch
        {
            0 => "Mode1",
            1 => "Mode2",
            2 => "Mode3",
            3 => "Parameter0",
            4 => "Parameter1",
            5 => "Parameter2",
            6 => "Parameter3",
            _ => null
        };

    private static string? SpriteHeaderPayloadWordName(int payloadWordIndex) =>
        payloadWordIndex switch
        {
            0 => "Mode1",
            1 => "Mode2",
            2 => "Mode3",
            3 => "Argb",
            4 => "OffsetArgb",
            5 => "Dummy0",
            6 => "Dummy1",
            _ => null
        };

    private static string? SpriteVertexPayloadWordName(int payloadWordIndex, bool textureEnabled) =>
        payloadWordIndex switch
        {
            0 => "Ax",
            1 => "Ay",
            2 => "Az",
            3 => "Bx",
            4 => "By",
            5 => "Bz",
            6 => "Cx",
            7 => "Cy",
            8 => "Cz",
            9 => "Dx",
            10 => "Dy",
            11 => textureEnabled ? "Dummy" : "Dummy0",
            12 => textureEnabled ? "Auv" : "Dummy1",
            13 => textureEnabled ? "Buv" : "Dummy2",
            14 => textureEnabled ? "Cuv" : "Dummy3",
            _ => null
        };

    private static string? ModifierVolumePayloadWordName(int payloadWordIndex) =>
        payloadWordIndex switch
        {
            0 => "Mode1",
            1 => "Dummy0",
            2 => "Dummy1",
            3 => "Dummy2",
            4 => "Dummy3",
            5 => "Dummy4",
            6 => "Dummy5",
            _ => null
        };

    private static string? UserClipPayloadWordName(int payloadWordIndex) =>
        payloadWordIndex switch
        {
            0 => "Clip0",
            1 => "Clip1",
            2 => "Clip2",
            3 => "Clip3",
            4 => "Clip4",
            5 => "Clip5",
            6 => "Clip6",
            _ => null
        };
}

internal sealed record PayloadControl(
    string Region,
    string Kind,
    uint Value,
    string ValueHex,
    bool? SpriteTextureEnabled = null)
{
    private readonly List<uint> payloadWords = [];

    public IReadOnlyList<uint> PayloadWords => payloadWords;

    public void AddPayloadWord(uint value) => payloadWords.Add(value);
}

internal sealed record PendingSpriteHeader(string Region, bool TextureEnabled);

public sealed record DreamcastPvrTaStreamWrite(
    DreamcastPvrTaCommandWrite Write,
    string Role,
    string ControlKind,
    uint ControlValue,
    string ControlValueHex,
    int? PayloadWordIndex,
    int? PayloadWordsRemaining,
    string? PayloadWordName);
