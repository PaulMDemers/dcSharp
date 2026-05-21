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
    public ulong? MinVblankEvents { get; set; }
    public ulong? MinHardwareAdvanceTicks { get; set; }
    public ulong? MinHardwareAdvanceBatches { get; set; }
    public ulong? MaxHardwareAdvanceBatch { get; set; }
    public ulong? MinControllerScriptChanges { get; set; }
    public Dictionary<string, int> MinDeviceAccessDomains { get; set; } = [];
    public Dictionary<string, string> PvrRegisters { get; set; } = [];
    public Dictionary<string, string> AicaRegisters { get; set; } = [];
    public List<DreamcastFixtureAsicEventRegisterExpectation> AsicEventRegisters { get; set; } = [];
    public List<DreamcastFixtureAicaChannelExpectation> AicaChannels { get; set; } = [];
    public List<DreamcastFixturePvrTaCommandExpectation> PvrTaCommands { get; set; } = [];
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
    public int MinCount { get; set; } = 1;
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
