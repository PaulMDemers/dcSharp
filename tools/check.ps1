param(
    [switch]$KosFixtures,
    [string]$FixtureFilter
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $repoRoot
try {
    git diff --check
    dotnet run --project src\DcSharp.Cli -- fixtures fixtures\kos.json --validate-only
    dotnet test dcSharp.slnx

    if ($KosFixtures) {
        $fixtureArgs = @('run', '--project', 'src\DcSharp.Cli', '--', 'fixtures', 'fixtures\kos.json')
        if (-not [string]::IsNullOrWhiteSpace($FixtureFilter)) {
            $fixtureArgs += @('--filter', $FixtureFilter)
        }

        dotnet @fixtureArgs
    }
}
finally {
    Pop-Location
}
