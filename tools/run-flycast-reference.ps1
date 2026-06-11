param(
    [ValidateSet('SA1', 'SA2', 'Shuffle')]
    [string]$Game = 'SA2',
    [string[]]$Config = @(),
    [switch]$Wait,
    [switch]$PrintOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$currentPath = Join-Path $repoRoot 'dreamcast-downloads\flycast\current.txt'

if (-not (Test-Path -LiteralPath $currentPath)) {
    throw "Flycast is not set up yet. Run tools\setup-flycast-reference.ps1 first."
}

$flycastExe = (Get-Content -LiteralPath $currentPath -Raw).Trim()
if (-not (Test-Path -LiteralPath $flycastExe)) {
    throw "Flycast executable not found: $flycastExe"
}

$games = @{
    SA1 = Join-Path $repoRoot 'retail_discs\Sonic Adventure (USA) (En,Ja,Fr,De,Es) (Rev A)\Sonic Adventure (USA) (En,Ja,Fr,De,Es) (Rev A)\Sonic Adventure (USA) (En,Ja,Fr,De,Es) (Rev A).gdi'
    SA2 = Join-Path $repoRoot 'retail_discs\Sonic Adventure 2 (USA) (EnJaFrDeEs)\Sonic Adventure 2 (USA) (En,Ja,Fr,De,Es).cue'
    Shuffle = Join-Path $repoRoot 'retail_discs\Sonic Shuffle (USA)\Sonic Shuffle (USA)\Sonic Shuffle (USA).gdi'
}

$mediaPath = $games[$Game]
if (-not (Test-Path -LiteralPath $mediaPath)) {
    throw "Media file not found: $mediaPath"
}

$arguments = @()
foreach ($entry in $Config) {
    $arguments += @('-config', $entry)
}

$arguments += $mediaPath
$workingDirectory = Split-Path -Parent $flycastExe

Write-Host "Flycast: $flycastExe"
Write-Host "Game: $Game"
Write-Host "Media: $mediaPath"
if ($Config.Count -gt 0) {
    Write-Host "Config overrides: $($Config -join ', ')"
}

if ($PrintOnly) {
    $quotedArgs = ($arguments | ForEach-Object { '"' + ($_ -replace '"', '\"') + '"' }) -join ' '
    Write-Host "Command: `"$flycastExe`" $quotedArgs"
    return
}

Start-Process -FilePath $flycastExe -ArgumentList $arguments -WorkingDirectory $workingDirectory -Wait:$Wait
