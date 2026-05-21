param(
    [switch]$KosFixtures
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $repoRoot
try {
    dotnet run --project src\DcSharp.Cli -- fixtures fixtures\kos.json --validate-only
    dotnet test dcSharp.slnx

    if ($KosFixtures) {
        dotnet run --project src\DcSharp.Cli -- fixtures fixtures\kos.json
    }
}
finally {
    Pop-Location
}
