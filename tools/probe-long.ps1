param(
    [ValidateSet('SA1', 'SA2', 'Shuffle', 'All')]
    [string]$Game = 'SA2',
    [long]$Instructions = 1000000000,
    [int]$TraceTail = 16,
    [int]$ProfileLimit = 160,
    [int]$ScanSectors = 1024,
    [int]$VBlankInterval = 1000,
    [switch]$StopOnUnmapped,
    [switch]$Json,
    [string]$RunName = '',
    [string[]]$ExtraRunArgs = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$cliProject = Join-Path $repoRoot 'src\DcSharp.Cli'
$releaseCliDll = Join-Path $cliProject 'bin\Release\net10.0\DcSharp.Cli.dll'
$debugCliDll = Join-Path $cliProject 'bin\Debug\net10.0\DcSharp.Cli.dll'

function Get-CliArgsPrefix {
    if (Test-Path -LiteralPath $releaseCliDll) {
        return @($releaseCliDll)
    }

    if (Test-Path -LiteralPath $debugCliDll) {
        return @($debugCliDll)
    }

    return @('run', '--no-build', '--project', $cliProject, '--')
}

function Get-SafeName {
    param([Parameter(Mandatory = $true)][string]$Name)

    return ($Name.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-')
}

$games = [ordered]@{
    SA1 = @{
        Name = 'Sonic Adventure'
        Media = Join-Path $repoRoot 'retail_discs\Sonic Adventure (USA) (En,Ja,Fr,De,Es) (Rev A)\Sonic Adventure (USA) (En,Ja,Fr,De,Es) (Rev A)\Sonic Adventure (USA) (En,Ja,Fr,De,Es) (Rev A).gdi'
    }
    SA2 = @{
        Name = 'Sonic Adventure 2'
        Media = Join-Path $repoRoot 'retail_discs\Sonic Adventure 2 (USA) (EnJaFrDeEs)\Sonic Adventure 2 (USA) (En,Ja,Fr,De,Es).cue'
    }
    Shuffle = @{
        Name = 'Sonic Shuffle'
        Media = Join-Path $repoRoot 'retail_discs\Sonic Shuffle (USA)\Sonic Shuffle (USA)\Sonic Shuffle (USA).gdi'
    }
}

$selectedGames = if ($Game -eq 'All') { @('SA1', 'SA2', 'Shuffle') } else { @($Game) }
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runSlug = if ([string]::IsNullOrWhiteSpace($RunName)) { $timestamp } else { "$(Get-SafeName $RunName)-$timestamp" }
$artifactRoot = Join-Path $repoRoot "artifacts\long-probes\$runSlug"
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null

Write-Host "Long probe output: $artifactRoot"
Write-Host "Instruction budget: $Instructions"

foreach ($key in $selectedGames) {
    $entry = $games[$key]
    $name = $entry.Name
    $mediaPath = $entry.Media
    $slug = Get-SafeName $name

    if (-not (Test-Path -LiteralPath $mediaPath)) {
        Write-Warning "Skipping $name; media file not found: $mediaPath"
        continue
    }

    $profilePath = Join-Path $artifactRoot "$slug-profile.txt"
    $outputPath = Join-Path $artifactRoot "$slug-output.txt"
    $jsonPath = Join-Path $artifactRoot "$slug-summary.json"

    $commandArgs = @(Get-CliArgsPrefix)
    $commandArgs += @(
        'media',
        'boot-smoke',
        $mediaPath,
        '--scan-sectors',
        ([string]$ScanSectors),
        '--instructions',
        ([string]$Instructions),
        '--trace-tail',
        ([string]$TraceTail),
        '--vblank-interval',
        ([string]$VBlankInterval),
        '--pc-profile-log',
        $profilePath,
        '--pc-profile-limit',
        ([string]$ProfileLimit))

    if ($StopOnUnmapped) {
        $commandArgs += '--stop-on-unmapped'
    }

    if ($Json) {
        $commandArgs += '--json'
    }

    $commandArgs += $ExtraRunArgs

    Write-Host "== $name =="
    Write-Host "Media: $mediaPath"
    Write-Host "Profile: $profilePath"

    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    $output = & dotnet @commandArgs
    $exitCode = $LASTEXITCODE
    $timer.Stop()

    $output | Set-Content -LiteralPath $outputPath -Encoding UTF8
    if ($Json) {
        $output | Set-Content -LiteralPath $jsonPath -Encoding UTF8
    }

    if ($exitCode -ne 0) {
        throw "$name boot-smoke failed with exit code $exitCode. Full output: $outputPath"
    }

    $summaryPatterns = @(
        '^(Instructions|PC|SR|Stopped|Detail|Serial|GD-ROM|PVR|PVR TA diag|AICA|Maple|Scheduler):',
        '^  GD-ROM (command|read|read sectors|status|TOC):',
        '^  AICA (read|write|channel|active channels|recent register accesses):'
    )

    foreach ($pattern in $summaryPatterns) {
        foreach ($line in ($output | Select-String -Pattern $pattern)) {
            Write-Host $line.Line
        }
    }

    Write-Host ("Elapsed: {0}" -f $timer.Elapsed)
    Write-Host "Output: $outputPath"
}
