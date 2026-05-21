using DcSharp.Core.Fixtures;
using System.Text.Json;

namespace DcSharp.Tests;

public class DreamcastFixtureManifestTests
{
    [Fact]
    public void KosManifestDeclaresLocalJsonSchema()
    {
        var repoRoot = FindRepoRoot();
        using var stream = File.OpenRead(Path.Combine(repoRoot, "fixtures", "kos.json"));
        using var document = JsonDocument.Parse(stream);

        Assert.True(document.RootElement.TryGetProperty("$schema", out var schema));
        Assert.Equal("./kos.schema.json", schema.GetString());
    }

    [Fact]
    public void KosManifestAndSchemaParseAsJson()
    {
        var repoRoot = FindRepoRoot();
        using var manifestStream = File.OpenRead(Path.Combine(repoRoot, "fixtures", "kos.json"));
        using var schemaStream = File.OpenRead(Path.Combine(repoRoot, "fixtures", "kos.schema.json"));

        using var manifestDocument = JsonDocument.Parse(manifestStream);
        using var schemaDocument = JsonDocument.Parse(schemaStream);

        Assert.Equal(JsonValueKind.Object, manifestDocument.RootElement.ValueKind);
        Assert.Equal(JsonValueKind.Object, schemaDocument.RootElement.ValueKind);
    }

    [Fact]
    public void KosManifestStillDeserializesThroughFixtureReader()
    {
        var repoRoot = FindRepoRoot();
        using var stream = File.OpenRead(Path.Combine(repoRoot, "fixtures", "kos.json"));

        var manifest = DreamcastFixtureManifest.Read(stream);

        Assert.NotEmpty(manifest.Fixtures);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dcSharp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
