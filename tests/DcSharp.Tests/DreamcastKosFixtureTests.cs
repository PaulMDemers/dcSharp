using DcSharp.Core.Fixtures;

namespace DcSharp.Tests;

public class DreamcastKosFixtureTests
{
    public static IEnumerable<object[]> KosFixtures()
    {
        var repoRoot = FindRepoRoot();
        using var stream = File.OpenRead(Path.Combine(repoRoot, "fixtures", "kos.json"));
        var manifest = DreamcastFixtureManifest.Read(stream);

        foreach (var fixture in manifest.Fixtures)
        {
            yield return [manifest.ArtifactDirectory, fixture];
        }
    }

    [Theory]
    [MemberData(nameof(KosFixtures))]
    public void KosFixtureMatchesManifest(string artifactDirectory, DreamcastFixtureDefinition fixture)
    {
        if (!ShouldRunKosFixtures())
        {
            return;
        }

        var repoRoot = FindRepoRoot();
        var artifactPath = Path.Combine(repoRoot, artifactDirectory, fixture.Artifact);
        if (!File.Exists(artifactPath))
        {
            return;
        }

        var result = DreamcastFixtureRunner.Run(fixture, artifactPath);

        Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
    }

    private static bool ShouldRunKosFixtures() =>
        string.Equals(Environment.GetEnvironmentVariable("DCSHARP_RUN_KOS_FIXTURES"), "1", StringComparison.Ordinal);

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
