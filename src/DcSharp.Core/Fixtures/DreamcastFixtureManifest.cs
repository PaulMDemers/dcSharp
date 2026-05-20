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
    public string? ControllerAScript { get; set; }
    public DreamcastStopReason ExpectedStopReason { get; set; }
    public List<string> SerialContains { get; set; } = [];
    public bool RequireVideoNonZero { get; set; }
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
