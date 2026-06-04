param(
    [int]$ScanSectors = 1024,
    [int]$VBlankInterval = 1000,
    [int]$BootstrapInstructions = 12000000,
    [int]$LegacyInstructions = 12000000,
    [switch]$Doa2SpriteProbe,
    [long]$Doa2SpriteProbeInstructions = 150000000,
    [switch]$LongDoa2,
    [long]$LongDoa2Instructions = 650000000
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$cliProject = Join-Path $repoRoot "src\DcSharp.Cli"
$cliDll = Join-Path $cliProject "bin\Debug\net10.0\DcSharp.Cli.dll"

function Invoke-BootSmoke {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][long]$Instructions
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Warning "Skipping $Name; media file not found: $Path"
        return $null
    }

    Write-Host "== $Name =="
    $commandArgs = if (Test-Path -LiteralPath $cliDll) {
        @($cliDll, 'media', 'boot-smoke', $Path)
    }
    else {
        @('run', '--no-build', '--project', $cliProject, '--', 'media', 'boot-smoke', $Path)
    }

    $commandArgs += @(
        '--scan-sectors', $ScanSectors,
        '--instructions', $Instructions,
        '--trace-tail', 12,
        '--stop-on-unmapped',
        '--vblank-interval', $VBlankInterval)

    $output = & dotnet @commandArgs

    if ($LASTEXITCODE -ne 0) {
        throw "$Name boot-smoke failed with exit code $LASTEXITCODE"
    }

    foreach ($line in ($output | Select-String -Pattern "^(Instructions|PC|SR|Stopped|Detail|GD-ROM):")) {
        Write-Host $line.Line
    }
    foreach ($line in ($output | Select-String -Pattern "^  GD-ROM (command|read|read sectors|status|TOC):")) {
        Write-Host $line.Line
    }

    return ($output -join "`n")
}

function Invoke-BootSmokeJson {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][long]$Instructions,
        [int]$TraceTail = 0
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Warning "Skipping $Name; media file not found: $Path"
        return $null
    }

    Write-Host "== $Name =="
    $commandArgs = if (Test-Path -LiteralPath $cliDll) {
        @($cliDll, 'media', 'boot-smoke', $Path)
    }
    else {
        @('run', '--no-build', '--project', $cliProject, '--', 'media', 'boot-smoke', $Path)
    }

    $commandArgs += @(
        '--scan-sectors', $ScanSectors,
        '--instructions', $Instructions,
        '--trace-tail', $TraceTail,
        '--json')

    $output = & dotnet @commandArgs

    if ($LASTEXITCODE -ne 0) {
        throw "$Name JSON boot-smoke failed with exit code $LASTEXITCODE"
    }

    $json = $output -join "`n"
    $report = $json | ConvertFrom-Json
    Write-Host "Instructions: $($report.summary.instructionsExecuted)"
    Write-Host "PC: $($report.summary.pcHex)"
    Write-Host "Stopped: $($report.summary.stopReason)"
    Write-Host "GD-ROM: reads=$($report.summary.gdrom.readCommandCount), ok=$($report.summary.gdrom.successfulReadCommandCount), failed=$($report.summary.gdrom.failedReadCommandCount)"
    Write-Host "PVR: taSprites=$($report.summary.video.pvrTaSprites.Count), sourceGroups=$($report.summary.video.pvrTaSpriteSourceGroups.Count)"

    return $report
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

function Assert-PvrTaSpriteSourceGroup {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)]$Report,
        [Parameter(Mandatory = $true)][string]$PreviewStatus,
        [Parameter(Mandatory = $true)][int]$Count,
        [Parameter(Mandatory = $true)][string]$HeaderPc,
        [Parameter(Mandatory = $true)][string]$ControlPc,
        [Parameter(Mandatory = $true)][string]$PayloadPcRange
    )

    $groups = @($Report.summary.video.pvrTaSpriteSourceGroups)
    $group = $groups | Where-Object {
        $_.previewStatus -eq $PreviewStatus `
            -and $_.count -eq $Count `
            -and $_.headerInstructionPcHex -eq $HeaderPc `
            -and $_.controlInstructionPcHex -eq $ControlPc `
            -and $_.payloadInstructionPcRangeHex -eq $PayloadPcRange
    } | Select-Object -First 1

    if (-not $group) {
        $actual = ($groups | ForEach-Object {
            "$($_.previewStatus):$($_.count) pc=h:$($_.headerInstructionPcHex)/c:$($_.controlInstructionPcHex)/p:$($_.payloadInstructionPcRangeHex)"
        }) -join ", "
        throw "$Name did not contain expected PVR TA sprite source group ${PreviewStatus}:$Count pc=h:$HeaderPc/c:$ControlPc/p:$PayloadPcRange. Actual groups: $actual"
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

if ($Doa2SpriteProbe) {
    $doaSpriteReport = Invoke-BootSmokeJson "Dead or Alive 2 sprite source probe" $deadOrAlive $Doa2SpriteProbeInstructions
    if ($doaSpriteReport) {
        if ($doaSpriteReport.summary.stopReason -ne "InstructionLimit") {
            throw "Dead or Alive 2 sprite source probe stopped with $($doaSpriteReport.summary.stopReason), expected InstructionLimit"
        }

        if ($doaSpriteReport.summary.pcHex -ne "0x8C1007EE") {
            throw "Dead or Alive 2 sprite source probe reached $($doaSpriteReport.summary.pcHex), expected 0x8C1007EE"
        }

        Assert-PvrTaSpriteSourceGroup `
            "Dead or Alive 2 sprite source probe" `
            $doaSpriteReport `
            "renderable" `
            32901 `
            "0x8C1007FA" `
            "0x8C10084C" `
            "0x8C10084C-0x8C100850"
    }
}

if ($LongDoa2) {
    $longDoaOutput = Invoke-BootSmoke "Dead or Alive 2 long" $deadOrAlive $LongDoa2Instructions
    if ($longDoaOutput) {
        Assert-Contains "Dead or Alive 2 long" $longDoaOutput "Stopped: InstructionLimit"
        Assert-Contains "Dead or Alive 2 long" $longDoaOutput "PC: 0x8C12BF42"
        Assert-Contains "Dead or Alive 2 long" $longDoaOutput "ASIC: pending=0x0320"
        Assert-Contains "Dead or Alive 2 long" $longDoaOutput "GD-ROM: media=True, reads=17, ok=17, failed=0"
        Assert-Contains "Dead or Alive 2 long" $longDoaOutput "GD-ROM read sectors: unique=17, 45166x1, 45168x1, 45170x1, 45171x1"
        Assert-Contains "Dead or Alive 2 long" $longDoaOutput "GD-ROM status:"
        Assert-Contains "Dead or Alive 2 long" $longDoaOutput "disc=128/GD-ROM"
        Assert-Contains "Dead or Alive 2 long" $longDoaOutput "GD-ROM read:"
        Assert-Contains "Dead or Alive 2 long" $longDoaOutput "sector=412644, count=33"
        Assert-Contains "Dead or Alive 2 long" $longDoaOutput "GD-ROM command:"
        Assert-Contains "Dead or Alive 2 long" $longDoaOutput "cmd=0x00000011/DMA_READ"
        Assert-Contains "Dead or Alive 2 long" $longDoaOutput "response=2/completed"
        Assert-Contains "Dead or Alive 2 long" $longDoaOutput "bytes=2048/2048, ok=True"
        Assert-Contains "Dead or Alive 2 long" $longDoaOutput "bytes=67584/67584, ok=True"
        Assert-Contains "Dead or Alive 2 long" $longDoaOutput "Boot binary: writes=103289"
        Assert-NotContains "Dead or Alive 2 long" $longDoaOutput "PC: 0x8C00834A"
        Assert-NotContains "Dead or Alive 2 long" $longDoaOutput "PC: 0x8C113318"
        Assert-NotContains "Dead or Alive 2 long" $longDoaOutput "PC: 0x8C114200"
        Assert-NotContains "Dead or Alive 2 long" $longDoaOutput "PC: 0x8C10EDB8"
        Assert-NotContains "Dead or Alive 2 long" $longDoaOutput "PC: 0x8C014674"
        Assert-NotContains "Dead or Alive 2 long" $longDoaOutput "PC: 0x8C13048A"
        Assert-NotContains "Dead or Alive 2 long" $longDoaOutput "PC: 0x8C12FD2E"
        Assert-NotContains "Dead or Alive 2 long" $longDoaOutput "PC: 0x8C12FD36"
        Assert-NotContains "Dead or Alive 2 long" $longDoaOutput "ASIC: pending=0x0360"
        Assert-NotContains "Dead or Alive 2 long" $longDoaOutput "FirmwareExit"
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
