param(
    [int]$ScanSectors = 1024,
    [int]$VBlankInterval = 1000,
    [int]$BootstrapInstructions = 12000000,
    [int]$LegacyInstructions = 12000000,
    [switch]$LongDoa2,
    [int]$LongDoa2Instructions = 120000000
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$cliProject = Join-Path $repoRoot "src\DcSharp.Cli"

function Invoke-BootSmoke {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int]$Instructions
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Warning "Skipping $Name; media file not found: $Path"
        return $null
    }

    Write-Host "== $Name =="
    $output = & dotnet run --no-build --project $cliProject -- media boot-smoke $Path `
        --scan-sectors $ScanSectors `
        --instructions $Instructions `
        --trace-tail 12 `
        --stop-on-unmapped `
        --vblank-interval $VBlankInterval

    if ($LASTEXITCODE -ne 0) {
        throw "$Name boot-smoke failed with exit code $LASTEXITCODE"
    }

    foreach ($line in ($output | Select-String -Pattern "^(Instructions|PC|SR|Stopped|Detail|GD-ROM):")) {
        Write-Host $line.Line
    }
    foreach ($line in ($output | Select-String -Pattern "^  GD-ROM (read|status|TOC):")) {
        Write-Host $line.Line
    }

    return ($output -join "`n")
}

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Output,
        [Parameter(Mandatory = $true)][string]$Expected
    )

    if (-not $Output.Contains($Expected)) {
        throw "$Name did not contain expected text: $Expected"
    }
}

function Assert-NotContains {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Output,
        [Parameter(Mandatory = $true)][string]$Unexpected
    )

    if ($Output.Contains($Unexpected)) {
        throw "$Name contained unexpected text: $Unexpected"
    }
}

$deadOrAlive = Join-Path $repoRoot "retail_discs\Dead or Alive 2 (USA)\Dead or Alive 2 (USA).cue"
$rayman = Join-Path $repoRoot "retail_discs\Rayman 2 - The Great Escape (USA) (EnFrDeEsIt)\Rayman 2 - The Great Escape (USA) (En,Fr,De,Es,It).cue"
$legacy = Join-Path $repoRoot "retail_discs\Legacy of Kain - Soul Reaver (USA)\Legacy of Kain - Soul Reaver (USA).cue"

$doaOutput = Invoke-BootSmoke "Dead or Alive 2" $deadOrAlive $BootstrapInstructions
if ($doaOutput) {
    Assert-Contains "Dead or Alive 2" $doaOutput "Stopped: InstructionLimit"
    Assert-Contains "Dead or Alive 2" $doaOutput "PC: 0x8C129E3E"
    Assert-Contains "Dead or Alive 2" $doaOutput "GD-ROM: media=True, reads="
    Assert-Contains "Dead or Alive 2" $doaOutput "Boot binary: writes="
    Assert-NotContains "Dead or Alive 2" $doaOutput "Stopped on Unmapped"
}

if ($LongDoa2) {
    $longDoaOutput = Invoke-BootSmoke "Dead or Alive 2 long" $deadOrAlive $LongDoa2Instructions
    if ($longDoaOutput) {
        Assert-Contains "Dead or Alive 2 long" $longDoaOutput "Stopped: InstructionLimit"
        Assert-Contains "Dead or Alive 2 long" $longDoaOutput "GD-ROM: media=True, reads=16, ok=16, failed=0"
        Assert-Contains "Dead or Alive 2 long" $longDoaOutput "GD-ROM status:"
        Assert-Contains "Dead or Alive 2 long" $longDoaOutput "disc=128/GD-ROM"
        Assert-Contains "Dead or Alive 2 long" $longDoaOutput "GD-ROM read:"
        Assert-Contains "Dead or Alive 2 long" $longDoaOutput "bytes=2048/2048, ok=True"
        Assert-NotContains "Dead or Alive 2 long" $longDoaOutput "Stopped on Unmapped"
    }
}

$raymanOutput = Invoke-BootSmoke "Rayman 2" $rayman $BootstrapInstructions
if ($raymanOutput) {
    Assert-Contains "Rayman 2" $raymanOutput "Stopped: InstructionLimit"
    Assert-Contains "Rayman 2" $raymanOutput "PC: 0x8C0DECDA"
    Assert-Contains "Rayman 2" $raymanOutput "GD-ROM: media=True, reads=0"
    Assert-Contains "Rayman 2" $raymanOutput "Boot binary: writes="
    Assert-NotContains "Rayman 2" $raymanOutput "Stopped on Unmapped"
}

$legacyOutput = Invoke-BootSmoke "Legacy of Kain" $legacy $LegacyInstructions
if ($legacyOutput) {
    Assert-Contains "Legacy of Kain" $legacyOutput "Stopped: InstructionLimit"
    Assert-Contains "Legacy of Kain" $legacyOutput "PC: 0x8C032164"
    Assert-Contains "Legacy of Kain" $legacyOutput "GD-ROM: media=True, reads=0"
    Assert-Contains "Legacy of Kain" $legacyOutput "Boot binary: writes="
}

Write-Host "Retail probes completed."
