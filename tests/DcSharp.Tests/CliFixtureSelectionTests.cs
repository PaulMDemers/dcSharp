using DcSharp.Core.Fixtures;

namespace DcSharp.Tests;

public class CliFixtureSelectionTests
{
    [Fact]
    public void FilterFixturesReturnsExactSingleMatch()
    {
        var fixtures = CreateFixtures();

        var matched = CliFixtureSelection.FilterFixtures(fixtures, "input_idle");

        var fixture = Assert.Single(matched);
        Assert.Equal("input_idle", fixture.Name);
    }

    [Fact]
    public void FilterFixturesMatchesSubstringCaseInsensitively()
    {
        var fixtures = CreateFixtures();

        var matched = CliFixtureSelection.FilterFixtures(fixtures, "CONTROLLER");

        Assert.Equal(
            ["maple_controller_neutral", "maple_controller_script_transition"],
            matched.Select(fixture => fixture.Name).ToArray());
    }

    [Fact]
    public void FilterFixturesRejectsMissingMatch()
    {
        var fixtures = CreateFixtures();

        var exception = Assert.Throws<InvalidDataException>(
            () => CliFixtureSelection.FilterFixtures(fixtures, "no_such_fixture"));
        Assert.Equal("No fixtures match filter: no_such_fixture", exception.Message);
    }

    [Fact]
    public void CreateValidationReportUsesFilteredFixtureCountAndNames()
    {
        var fixtures = CliFixtureSelection.FilterFixtures(CreateFixtures(), "input");

        var report = CliFixtureSelection.CreateValidationReport(
            "fixtures/kos.json",
            "artifacts/kos",
            fixtures);

        Assert.Equal(1, report.FixtureCount);
        Assert.Equal(["input_idle"], report.FixtureNames);
    }

    private static IReadOnlyList<DreamcastFixtureDefinition> CreateFixtures() =>
    [
        new() { Name = "minimal", Artifact = "minimal.elf", Instructions = 1 },
        new() { Name = "input_idle", Artifact = "input.elf", Instructions = 1 },
        new() { Name = "maple_controller_neutral", Artifact = "controller.elf", Instructions = 1 },
        new() { Name = "maple_controller_script_transition", Artifact = "controller_script.elf", Instructions = 1 },
        new() { Name = "vblank_idle", Artifact = "vblank.elf", Instructions = 1 }
    ];
}
