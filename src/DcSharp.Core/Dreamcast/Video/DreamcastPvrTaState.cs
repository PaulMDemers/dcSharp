namespace DcSharp.Core.Dreamcast.Video;

public sealed class DreamcastPvrTaState
{
    private const int ParameterHeaderPayloadWords = DreamcastPvrTaPolygonHeaderPayloadDecoder.PayloadWordCount;
    private bool inRenderableList;
    private bool awaitingHeaderPayloadOrShortcut;
    private bool awaitingSpriteVertex;
    private bool inRealStream;
    private int headerPayloadWordsRemaining;
    private DreamcastPvrTaCommandWrite? currentHeader;
    private uint currentHeaderValue;
    private readonly uint[] currentHeaderPayloadWords = new uint[ParameterHeaderPayloadWords];
    private DreamcastPvrTaPolygonHeaderPayload? currentHeaderPayload;
    private DreamcastPvrTaSpriteHeaderPayload? currentSpriteHeaderPayload;
    private readonly DreamcastPvrTaDiagnosticVertexPacketDecoder diagnosticVertexDecoder = new();
    private readonly DreamcastPvrTaRealVertexPacketDecoder realVertexDecoder = new();
    private readonly DreamcastPvrTaSpritePacketDecoder spritePacketDecoder = new();
    private readonly List<DreamcastPvrTaVertex> currentVertices = [];
    private readonly List<DreamcastPvrTaStrip> completedStrips = [];
    private readonly List<DreamcastPvrTaSprite> completedSprites = [];
    private int droppedShortStripCount;
    private int droppedZeroColorPrimitiveCount;
    private int droppedMixedFlatColorStripCount;

    public IReadOnlyList<DreamcastPvrTaStrip> CompletedStrips => completedStrips;
    public IReadOnlyList<DreamcastPvrTaSprite> CompletedSprites => completedSprites;
    public DreamcastPvrTaAssemblyDiagnostics AssemblyDiagnostics =>
        new(droppedShortStripCount, droppedZeroColorPrimitiveCount, droppedMixedFlatColorStripCount);

    public DreamcastPvrTaRenderCommand? Accept(DreamcastPvrTaCommandWrite write)
    {
        if (!string.Equals(write.Region, "TA_INPUT", StringComparison.Ordinal))
        {
            ResetStrip();
            return null;
        }

        if (awaitingHeaderPayloadOrShortcut)
        {
            awaitingHeaderPayloadOrShortcut = false;
            if (!IsVertexControl(write))
            {
                inRealStream = true;
                currentHeaderPayloadWords[0] = write.Value;
                headerPayloadWordsRemaining = ParameterHeaderPayloadWords - 1;
                CompleteHeaderPayloadIfReady();
                return null;
            }
        }

        if (headerPayloadWordsRemaining > 0)
        {
            var wordIndex = ParameterHeaderPayloadWords - headerPayloadWordsRemaining;
            currentHeaderPayloadWords[wordIndex] = write.Value;
            headerPayloadWordsRemaining--;
            CompleteHeaderPayloadIfReady();
            return null;
        }

        if (spritePacketDecoder.HasPending)
        {
            return AcceptSpritePayload(write);
        }

        if (realVertexDecoder.HasPending)
        {
            return AcceptRealVertexPayload(write);
        }

        if (diagnosticVertexDecoder.HasPending)
        {
            return AcceptDiagnosticVertexPayload(write);
        }

        if (awaitingSpriteVertex)
        {
            if (IsTaInput(write) && IsVertexControl(write) && currentHeader is not null && currentSpriteHeaderPayload is not null)
            {
                awaitingSpriteVertex = false;
                spritePacketDecoder.Begin(
                    currentHeader,
                    currentSpriteHeaderPayload,
                    write,
                    string.Equals(write.Kind, "VertexEndOfStrip", StringComparison.Ordinal));
                return null;
            }

            ResetStrip();
            return null;
        }

        if (IsRenderableInput(write) && string.Equals(write.Kind, "PolygonHeader", StringComparison.Ordinal))
        {
            ResetStrip();
            inRenderableList = true;
            currentHeader = write;
            currentHeaderValue = write.Value;
            awaitingHeaderPayloadOrShortcut = true;
            return null;
        }

        if (IsRenderableInput(write) && string.Equals(write.Kind, "SpriteHeader", StringComparison.Ordinal))
        {
            ResetStrip();
            inRenderableList = true;
            inRealStream = true;
            currentHeader = write;
            currentHeaderValue = write.Value;
            headerPayloadWordsRemaining = ParameterHeaderPayloadWords;
            return null;
        }

        if (!inRenderableList)
        {
            return null;
        }

        if (IsTaInput(write) && IsVertexControl(write))
        {
            var endOfStrip = string.Equals(write.Kind, "VertexEndOfStrip", StringComparison.Ordinal);
            if (inRealStream)
            {
                realVertexDecoder.Begin(write, endOfStrip);
            }
            else
            {
                diagnosticVertexDecoder.Begin(write, endOfStrip);
            }

            return null;
        }

        ResetStrip();
        return null;
    }

    private DreamcastPvrTaRenderCommand? AcceptDiagnosticVertexPayload(DreamcastPvrTaCommandWrite write)
    {
        if (!diagnosticVertexDecoder.AcceptPayload(write.Value, out var vertex))
        {
            return null;
        }

        return AcceptVertex(write, vertex);
    }

    private DreamcastPvrTaRenderCommand? AcceptSpritePayload(DreamcastPvrTaCommandWrite write)
    {
        if (!spritePacketDecoder.AcceptPayload(write, out var sprite))
        {
            return null;
        }

        if (sprite is null || sprite.Rgb565 == 0)
        {
            ResetStrip();
            return null;
        }

        completedSprites.Add(sprite);
        var result = new DreamcastPvrTaRenderCommand(sprite);
        ResetStrip();
        return result;
    }

    private DreamcastPvrTaRenderCommand? AcceptRealVertexPayload(DreamcastPvrTaCommandWrite write)
    {
        if (!realVertexDecoder.AcceptPayload(write.Value, out var vertex))
        {
            return null;
        }

        return AcceptVertex(write, vertex);
    }

    private DreamcastPvrTaRenderCommand? AcceptVertex(DreamcastPvrTaCommandWrite write, DreamcastPvrTaVertex? vertex)
    {
        if (vertex is null || vertex.Rgb565 == 0)
        {
            droppedZeroColorPrimitiveCount++;
            ResetStrip();
            return null;
        }

        var color = vertex.Rgb565;
        if (!IsGouraudHeader(currentHeaderValue) && currentVertices.Count > 0 && currentVertices[0].Rgb565 != color)
        {
            droppedMixedFlatColorStripCount++;
            ResetStrip();
            return null;
        }

        currentVertices.Add(vertex);
        if (!vertex.EndOfStrip)
        {
            return null;
        }

        var canRender = currentVertices.Count >= 3;
        if (!canRender)
        {
            droppedShortStripCount++;
        }

        var stripColor = currentVertices[0].Rgb565;
        var strip = canRender
            ? new DreamcastPvrTaStrip(
                currentHeader?.Region ?? write.Region,
                currentHeader?.ListType ?? write.ListType,
                currentHeader?.ListTypeName ?? write.ListTypeName,
                currentHeaderValue,
                $"0x{currentHeaderValue:X8}",
                currentHeaderPayload,
                stripColor,
                $"0x{stripColor:X4}",
                currentVertices.ToArray())
            : null;
        if (strip is not null)
        {
            completedStrips.Add(strip);
        }

        var result = strip is not null ? new DreamcastPvrTaRenderCommand(strip) : null;
        ResetStrip();
        return result;
    }

    private void CompleteHeaderPayloadIfReady()
    {
        if (headerPayloadWordsRemaining == 0 && currentHeader is not null)
        {
            if (string.Equals(currentHeader.Kind, "PolygonHeader", StringComparison.Ordinal))
            {
                currentHeaderPayload = DreamcastPvrTaPolygonHeaderPayloadDecoder.DecodePayload(currentHeader, currentHeaderPayloadWords);
            }
            else if (string.Equals(currentHeader.Kind, "SpriteHeader", StringComparison.Ordinal))
            {
                currentSpriteHeaderPayload = DreamcastPvrTaSpriteHeaderPayload.FromPayload(currentHeader, currentHeaderPayloadWords);
                awaitingSpriteVertex = true;
            }
        }
    }

    private static bool IsVertexControl(DreamcastPvrTaCommandWrite write) =>
        string.Equals(write.Kind, "Vertex", StringComparison.Ordinal)
        || string.Equals(write.Kind, "VertexEndOfStrip", StringComparison.Ordinal);

    private static bool IsTaInput(DreamcastPvrTaCommandWrite write) =>
        string.Equals(write.Region, "TA_INPUT", StringComparison.Ordinal);

    private static bool IsRenderableInput(DreamcastPvrTaCommandWrite write) =>
        IsTaInput(write)
        && write.ListTypeName is "OpaquePolygon" or "TranslucentPolygon" or "PunchThroughPolygon";

    private static bool IsGouraudHeader(uint value) =>
        (value & 0x0000_0002u) != 0;

    private void ResetStrip()
    {
        awaitingHeaderPayloadOrShortcut = false;
        awaitingSpriteVertex = false;
        inRealStream = false;
        headerPayloadWordsRemaining = 0;
        currentHeader = null;
        currentHeaderPayload = null;
        currentSpriteHeaderPayload = null;
        diagnosticVertexDecoder.Reset();
        realVertexDecoder.Reset();
        spritePacketDecoder.Reset();
        currentVertices.Clear();
    }
}

public sealed record DreamcastPvrTaAssemblyDiagnostics(
    int DroppedShortStripCount,
    int DroppedZeroColorPrimitiveCount,
    int DroppedMixedFlatColorStripCount);

public sealed record DreamcastPvrTaSpriteHeaderPayload(
    string Region,
    int? ListType,
    string? ListTypeName,
    uint HeaderValue,
    string HeaderValueHex,
    uint Mode1,
    string Mode1Hex,
    uint Mode2,
    string Mode2Hex,
    uint Mode3,
    string Mode3Hex,
    uint Argb,
    string ArgbHex,
    uint OffsetArgb,
    string OffsetArgbHex,
    uint Dummy0,
    string Dummy0Hex,
    uint Dummy1,
    string Dummy1Hex,
    DreamcastPvrTaPolygonHeaderMode1 Mode1Fields,
    DreamcastPvrTaPolygonHeaderMode2 Mode2Fields,
    DreamcastPvrTaPolygonHeaderMode3 Mode3Fields)
{
    public bool HasTexturePayload => (HeaderValue & 0x0000_0008u) != 0;

    public bool EffectiveTextureEnabled => Mode1Fields.TextureEnabled || HasTexturePayload;

    public static DreamcastPvrTaSpriteHeaderPayload FromPayload(DreamcastPvrTaCommandWrite header, IReadOnlyList<uint> words)
    {
        var decodedModes = DreamcastPvrTaPolygonHeaderPayloadDecoder.DecodePayload(header, words);
        return new(
            header.Region,
            header.ListType,
            header.ListTypeName,
            header.Value,
            header.ValueHex,
            words[0],
            $"0x{words[0]:X8}",
            words[1],
            $"0x{words[1]:X8}",
            words[2],
            $"0x{words[2]:X8}",
            words[3],
            $"0x{words[3]:X8}",
            words[4],
            $"0x{words[4]:X8}",
            words[5],
            $"0x{words[5]:X8}",
            words[6],
            $"0x{words[6]:X8}",
            decodedModes.Mode1Fields,
            decodedModes.Mode2Fields,
            decodedModes.Mode3Fields);
    }
}

public sealed record DreamcastPvrTaVertex(
    int X,
    int Y,
    float Z,
    uint ZValue,
    string ZValueHex,
    float U,
    uint UValue,
    string UValueHex,
    float V,
    uint VValue,
    string VValueHex,
    bool EndOfStrip,
    ushort Rgb565,
    string Rgb565Hex,
    uint ControlValue,
    string ControlValueHex,
    uint XValue,
    string XValueHex,
    uint YValue,
    string YValueHex,
    uint ColorValue,
    string ColorValueHex);

public sealed record DreamcastPvrTaStrip(
    string Region,
    int? ListType,
    string? ListTypeName,
    uint HeaderValue,
    string HeaderValueHex,
    DreamcastPvrTaPolygonHeaderPayload? HeaderPayload,
    ushort Rgb565,
    string Rgb565Hex,
    IReadOnlyList<DreamcastPvrTaVertex> Vertices)
{
    public bool Gouraud => (HeaderValue & 0x0000_0002u) != 0;
}

public sealed record DreamcastPvrTaSpriteVertex(
    string Name,
    int X,
    int Y,
    float Z,
    uint ZValue,
    string ZValueHex,
    uint XValue,
    string XValueHex,
    uint YValue,
    string YValueHex,
    float U,
    float V,
    uint UvValue,
    string UvValueHex)
{
    public float RawX => BitConverter.UInt32BitsToSingle(XValue);
    public float RawY => BitConverter.UInt32BitsToSingle(YValue);
    public bool HasFinitePosition => float.IsFinite(RawX) && float.IsFinite(RawY) && float.IsFinite(Z);
    public float PreviewX => RawX;
    public float PreviewY => RawY;
}

public sealed record DreamcastPvrTaSpritePayloadWord(
    string Name,
    uint Value,
    string ValueHex);

public sealed record DreamcastPvrTaSprite(
    string Region,
    int? ListType,
    string? ListTypeName,
    uint HeaderValue,
    string HeaderValueHex,
    uint? HeaderInstructionPc,
    string? HeaderInstructionPcHex,
    DreamcastPvrTaSpriteHeaderPayload HeaderPayload,
    uint ControlValue,
    string ControlValueHex,
    uint? ControlInstructionPc,
    string? ControlInstructionPcHex,
    uint? FirstPayloadInstructionPc,
    string? FirstPayloadInstructionPcHex,
    uint? LastPayloadInstructionPc,
    string? LastPayloadInstructionPcHex,
    bool EndOfStrip,
    ushort Rgb565,
    string Rgb565Hex,
    IReadOnlyList<DreamcastPvrTaSpritePayloadWord> PayloadWords,
    IReadOnlyList<DreamcastPvrTaSpriteVertex> Vertices)
{
    public bool HasFinitePreviewCoordinates =>
        Vertices.Count >= 4 && Vertices.Take(4).All(vertex => vertex.HasFinitePosition);

    public bool HasRenderablePreviewArea =>
        Rgb565 != 0
        && HasFinitePreviewCoordinates
        && MathF.Abs(SignedPreviewArea(OrderPreviewVertices(Vertices.Take(4).ToArray()))) > 0.0001f;

    private static IReadOnlyList<DreamcastPvrTaSpriteVertex> OrderPreviewVertices(IReadOnlyList<DreamcastPvrTaSpriteVertex> vertices)
    {
        var centerX = (float)vertices.Average(vertex => vertex.PreviewX);
        var centerY = (float)vertices.Average(vertex => vertex.PreviewY);
        return vertices
            .OrderBy(vertex => MathF.Atan2(vertex.PreviewY - centerY, vertex.PreviewX - centerX))
            .ToArray();
    }

    private static float SignedPreviewArea(IReadOnlyList<DreamcastPvrTaSpriteVertex> vertices)
    {
        var area = 0.0f;
        for (var index = 0; index < vertices.Count; index++)
        {
            var current = vertices[index];
            var next = vertices[(index + 1) % vertices.Count];
            area += (current.PreviewX * next.PreviewY) - (next.PreviewX * current.PreviewY);
        }

        return area * 0.5f;
    }
}

public sealed record DreamcastPvrTaRenderCommand(DreamcastPvrTaStrip? Strip, DreamcastPvrTaSprite? Sprite)
{
    public DreamcastPvrTaRenderCommand(DreamcastPvrTaStrip strip)
        : this(strip, null)
    {
    }

    public DreamcastPvrTaRenderCommand(DreamcastPvrTaSprite sprite)
        : this(null, sprite)
    {
    }

    public ushort Rgb565 => Strip?.Rgb565 ?? Sprite?.Rgb565 ?? 0;
}
