param(
    [long]$Instructions = 50000000,
    [int]$TraceTail = 16,
    [int]$ProfileLimit = 64,
    [switch]$AssertKnownFrontiers
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$cliProject = Join-Path $repoRoot "src\DcSharp.Cli"
$releaseCliDll = Join-Path $cliProject "bin\Release\net10.0\DcSharp.Cli.dll"
$debugCliDll = Join-Path $cliProject "bin\Debug\net10.0\DcSharp.Cli.dll"
$cliDll = if (Test-Path -LiteralPath $releaseCliDll) { $releaseCliDll } else { $debugCliDll }
$artifactDir = Join-Path $repoRoot "artifacts\tmp"
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

function Invoke-SonicBootSmoke {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ArtifactPrefix,
        [string]$ExpectedStop,
        [string]$ExpectedPc
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Warning "Skipping $Name; media file not found: $Path"
        return
    }

    Write-Host "== $Name =="
    $profilePath = Join-Path $artifactDir "$ArtifactPrefix-profile.txt"
    $commandArgs = if (Test-Path -LiteralPath $cliDll) {
        @($cliDll, 'media', 'boot-smoke', $Path)
    }
    else {
        @('run', '--no-build', '--project', $cliProject, '--', 'media', 'boot-smoke', $Path)
    }

    $commandArgs += @(
        '--instructions', $Instructions,
        '--trace-tail', $TraceTail,
        '--pc-profile-log', $profilePath,
        '--pc-profile-limit', $ProfileLimit)

    $output = & dotnet @commandArgs
    if ($LASTEXITCODE -ne 0) {
        throw "$Name boot-smoke failed with exit code $LASTEXITCODE"
    }

    $text = $output -join "`n"
    foreach ($line in ($output | Select-String -Pattern "^(Instructions|PC|Stopped|Detail|GD-ROM):")) {
        Write-Host $line.Line
    }
    foreach ($line in ($output | Select-String -Pattern "^(PVR:|PVR TA diag:)")) {
        Write-Host $line.Line
    }
    Write-Host "Profile: $profilePath"

    if ($AssertKnownFrontiers) {
        if ($ExpectedStop -and -not $text.Contains("Stopped: $ExpectedStop")) {
            throw "$Name did not stop at expected frontier '$ExpectedStop'."
        }

        if ($ExpectedPc -and -not $text.Contains("PC: $ExpectedPc")) {
            throw "$Name did not reach expected PC '$ExpectedPc'."
        }
    }
}

$sonicAdventure = Join-Path $repoRoot "retail_discs\Sonic Adventure (USA) (En,Ja,Fr,De,Es) (Rev A)\Sonic Adventure (USA) (En,Ja,Fr,De,Es) (Rev A)\Sonic Adventure (USA) (En,Ja,Fr,De,Es) (Rev A).gdi"
$sonicAdventure2 = Join-Path $repoRoot "retail_discs\Sonic Adventure 2 (USA) (EnJaFrDeEs)\Sonic Adventure 2 (USA) (En,Ja,Fr,De,Es).cue"
$sonicShuffle = Join-Path $repoRoot "retail_discs\Sonic Shuffle (USA)\Sonic Shuffle (USA)\Sonic Shuffle (USA).gdi"

Invoke-SonicBootSmoke "Sonic Adventure" $sonicAdventure "sonic-adventure-gdi" "FirmwareExit" "0x8C0000E8"
Invoke-SonicBootSmoke "Sonic Adventure 2" $sonicAdventure2 "sonic-adventure-2-cue" "InstructionLimit" "0x8C135C10"
Invoke-SonicBootSmoke "Sonic Shuffle" $sonicShuffle "sonic-shuffle-gdi" "UnsupportedInstruction" "0x8C008300"
