using DcSharp.Core.Fixtures;

internal static class CliFixtureSelection
{
    public static IReadOnlyList<DreamcastFixtureDefinition> FilterFixtures(
        IReadOnlyList<DreamcastFixtureDefinition> fixtures,
        string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return fixtures;
        }

        var matched = fixtures
            .Where(fixture => fixture.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matched.Length == 0)
        {
            throw new InvalidDataException($"No fixtures match filter: {filter}");
        }

        return matched;
    }

    public static FixtureManifestValidationReport CreateValidationReport(
        string manifestPath,
        string artifactDirectory,
        IReadOnlyList<DreamcastFixtureDefinition> fixtures) =>
        new(
            Path.GetFullPath(manifestPath),
            artifactDirectory,
            fixtures.Count,
            fixtures.Select(fixture => fixture.Name).ToArray());
}
