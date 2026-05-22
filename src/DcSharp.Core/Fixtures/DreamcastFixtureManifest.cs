using DcSharp.Core.Execution;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DcSharp.Core.Fixtures;

public sealed class DreamcastFixtureManifest
{
    public string ArtifactDirectory { get; set; } = "artifacts/kos";
    public List<DreamcastFixtureDefinition> Fixtures { get; set; } = [];

    public static DreamcastFixtureManifest Read(Stream stream)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        var manifest = JsonSerializer.Deserialize<DreamcastFixtureManifest>(stream, options)
            ?? throw new InvalidDataException("Fixture manifest is empty.");

        if (manifest.Fixtures.Count == 0)
        {
            throw new InvalidDataException("Fixture manifest must contain at least one fixture.");
        }

        foreach (var fixture in manifest.Fixtures)
        {
            fixture.Validate();
        }

        return manifest;
    }
}

public sealed class DreamcastFixtureDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Sample { get; set; } = string.Empty;
    public string Artifact { get; set; } = string.Empty;
    public ulong Instructions { get; set; }
    public int TraceTail { get; set; } = 8;
    public ulong VblankInterval { get; set; } = 200_000;
    public string? ControllerA { get; set; }
    public string? ControllerB { get; set; }
    public Dictionary<string, string> Controllers { get; set; } = [];
    public string? ControllerAScript { get; set; }
    public Dictionary<string, string> ControllerScripts { get; set; } = [];
    public DreamcastStopReason ExpectedStopReason { get; set; }
    public List<string> SerialContains { get; set; } = [];
    public bool RequireVideoNonZero { get; set; }
    public bool RequireNoAsicPendingInterrupt { get; set; }
    public int? MinPvrRegisterAccesses { get; set; }
    public int? MinPvrTaCommandWrites { get; set; }
    public int? MinAicaRegisterAccesses { get; set; }
    public int? MinMapleTransfers { get; set; }
    public int? MinMapleDeviceInfoTransfers { get; set; }
    public int? MinMapleGetConditionTransfers { get; set; }
    public int? MinMapleDmaBatches { get; set; }
    public bool RequireNoMapleDescriptorLimitHits { get; set; }
    public ulong? MinVblankEvents { get; set; }
    public ulong? MinHardwareAdvanceTicks { get; set; }
    public ulong? MinHardwareAdvanceBatches { get; set; }
    public ulong? MaxHardwareAdvanceBatch { get; set; }
    public ulong? MinIdleAdvanceTicks { get; set; }
    public ulong? MinIdleAdvanceBatches { get; set; }
    public ulong? MaxIdleAdvanceBatch { get; set; }
    public ulong? MinIdleTimerWakes { get; set; }
    public ulong? MinIdleVBlankWakes { get; set; }
    public ulong? MinIdleInputWakes { get; set; }
    public ulong? MinCpuFastForwardInstructions { get; set; }
    public ulong? MinCpuFastForwardBatches { get; set; }
    public ulong? MaxCpuFastForwardBatch { get; set; }
    public ulong? MinControllerScriptChanges { get; set; }
    public Dictionary<string, int> MinDeviceAccessDomains { get; set; } = [];
    public Dictionary<string, string> PvrRegisters { get; set; } = [];
    public Dictionary<string, string> AicaRegisters { get; set; } = [];
    public DreamcastFixtureAsicPendingInterruptExpectation? AsicPendingInterrupt { get; set; }
    public List<DreamcastFixtureAsicEventRegisterExpectation> AsicEventRegisters { get; set; } = [];
    public List<DreamcastFixtureAicaChannelExpectation> AicaChannels { get; set; } = [];
    public List<DreamcastFixturePvrTaCommandExpectation> PvrTaCommands { get; set; } = [];
    public List<DreamcastFixturePvrTaStreamWriteExpectation> PvrTaStreamWrites { get; set; } = [];
    public List<DreamcastFixturePvrTaPolygonHeaderPayloadExpectation> PvrTaPolygonHeaderPayloads { get; set; } = [];
    public List<DreamcastFixturePvrTaParameterHeaderExpectation> PvrTaParameterHeaders { get; set; } = [];
    public List<DreamcastFixturePvrTaListExpectation> PvrTaLists { get; set; } = [];
    public List<DreamcastFixturePvrTaStripExpectation> PvrTaStrips { get; set; } = [];
    public List<DreamcastFixtureVideoSampleExpectation> VideoSamples { get; set; } = [];

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidDataException("Fixture manifest entry is missing a name.");
        }

        if (string.IsNullOrWhiteSpace(Artifact))
        {
            throw new InvalidDataException($"Fixture '{Name}' is missing an artifact.");
        }

        if (Instructions == 0)
        {
            throw new InvalidDataException($"Fixture '{Name}' must set a positive instruction budget.");
        }

        if (TraceTail < 0)
        {
            throw new InvalidDataException($"Fixture '{Name}' trace tail must be zero or greater.");
        }
    }
}

public sealed class DreamcastFixtureVideoSampleExpectation
{
    public string Name { get; set; } = string.Empty;
    public string Rgb565 { get; set; } = string.Empty;
}

public sealed class DreamcastFixturePvrTaCommandExpectation
{
    public string Kind { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string? ListTypeName { get; set; }
    public bool? EndOfStrip { get; set; }
    public string? Value { get; set; }
    public int MinCount { get; set; } = 1;
}

public sealed class DreamcastFixturePvrTaParameterHeaderExpectation
{
    public string? Kind { get; set; }
    public string? Region { get; set; }
    public int? ParameterType { get; set; }
    public string? ListTypeName { get; set; }
    public bool? EndOfStrip { get; set; }
    public string? Value { get; set; }
    public int? ExpectedPayloadWords { get; set; }
    public bool? HasKnownPayloadLength { get; set; }
    public bool? Uv16Bit { get; set; }
    public bool? Gouraud { get; set; }
    public bool? OffsetColorEnabled { get; set; }
    public bool? TextureEnabled { get; set; }
    public int? ColorFormat { get; set; }
    public string? ColorFormatName { get; set; }
    public bool? ModifierNormal { get; set; }
    public bool? ModifierEnabled { get; set; }
    public int? ClipMode { get; set; }
    public string? ClipModeName { get; set; }
    public int? StripLength { get; set; }
    public string? StripLengthName { get; set; }
    public bool? AutoStripLength { get; set; }
    public int MinCount { get; set; } = 1;
}

public sealed class DreamcastFixturePvrTaStreamWriteExpectation
{
    public string? Role { get; set; }
    public string? Region { get; set; }
    public string? Kind { get; set; }
    public string? Value { get; set; }
    public string? ControlKind { get; set; }
    public string? ControlValue { get; set; }
    public int? PayloadWordIndex { get; set; }
    public int? PayloadWordsRemaining { get; set; }
    public string? PayloadWordName { get; set; }
    public int MinCount { get; set; } = 1;
}

public sealed class DreamcastFixturePvrTaPolygonHeaderPayloadExpectation
{
    public string? Region { get; set; }
    public string? ListTypeName { get; set; }
    public string? HeaderValue { get; set; }
    public string? Mode1 { get; set; }
    public string? Mode2 { get; set; }
    public string? Mode3 { get; set; }
    public string? Parameter0 { get; set; }
    public string? Parameter1 { get; set; }
    public string? Parameter2 { get; set; }
    public string? Parameter3 { get; set; }
    public bool? TextureEnabled { get; set; }
    public bool? DepthWriteDisabled { get; set; }
    public int? Culling { get; set; }
    public string? CullingName { get; set; }
    public int? DepthCompare { get; set; }
    public string? DepthCompareName { get; set; }
    public string? BlendSrcName { get; set; }
    public string? BlendDstName { get; set; }
    public bool? AlphaEnabled { get; set; }
    public string? FogTypeName { get; set; }
    public string? TextureBase { get; set; }
    public string? PixelFormatName { get; set; }
    public bool? VqEnabled { get; set; }
    public bool? MipMapEnabled { get; set; }
    public int MinCount { get; set; } = 1;
}

public sealed class DreamcastFixturePvrTaListExpectation
{
    public string? Region { get; set; }
    public string? ListTypeName { get; set; }
    public int? MinCommands { get; set; }
    public int? MinPolygonHeaders { get; set; }
    public int? MinVertices { get; set; }
    public int? MinVertexEndOfStrip { get; set; }
}

public sealed class DreamcastFixturePvrTaStripExpectation
{
    public string? Region { get; set; }
    public string? ListTypeName { get; set; }
    public string? Rgb565 { get; set; }
    public int? MinVertices { get; set; }
    public List<DreamcastFixturePvrTaVertexExpectation> Vertices { get; set; } = [];
    public int MinCount { get; set; } = 1;
}

public sealed class DreamcastFixturePvrTaVertexExpectation
{
    public int X { get; set; }
    public int Y { get; set; }
}

public sealed class DreamcastFixtureAsicEventRegisterExpectation
{
    public string Name { get; set; } = string.Empty;
    public string? Ack { get; set; }
    public string? Irq9Mask { get; set; }
    public string? IrqBMask { get; set; }
    public string? IrqDMask { get; set; }
    public string? PendingIrq9 { get; set; }
    public string? PendingIrqB { get; set; }
    public string? PendingIrqD { get; set; }
}

public sealed class DreamcastFixtureAsicPendingInterruptExpectation
{
    public string? EventCode { get; set; }
    public int? Level { get; set; }
    public string? LevelName { get; set; }
    public string? RegisterName { get; set; }
    public int? Bit { get; set; }
    public string? BitMask { get; set; }
}

public sealed class DreamcastFixtureAicaChannelExpectation
{
    public int Channel { get; set; }
    public string? Control { get; set; }
    public string? SampleFormat { get; set; }
    public string? SampleAddress { get; set; }
    public string? LoopStart { get; set; }
    public string? LoopEnd { get; set; }
    public string? Pitch { get; set; }
    public byte? Pan { get; set; }
    public byte? Volume { get; set; }
    public bool? Active { get; set; }
    public bool? KeyOn { get; set; }
    public bool? KeyOnExecute { get; set; }
}
