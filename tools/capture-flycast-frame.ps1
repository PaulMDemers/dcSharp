param(
    [ValidateSet('SA1', 'SA2', 'Shuffle')]
    [string]$Game = 'SA2',
    [int]$DelaySeconds = 12,
    [string]$OutputPath = '',
    [string[]]$Config = @(),
    [switch]$LeaveRunning,
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

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $artifactRoot = Join-Path $repoRoot 'artifacts\reference-frames'
    $OutputPath = Join-Path $artifactRoot "$($Game.ToLowerInvariant())-$timestamp.png"
}

$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $outputFullPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$arguments = @()
foreach ($entry in $Config) {
    $arguments += @('-config', $entry)
}

$arguments += $mediaPath
$workingDirectory = Split-Path -Parent $flycastExe

function Join-CommandArguments {
    param([Parameter(Mandatory = $true)][string[]]$ArgumentList)

    return ($ArgumentList | ForEach-Object {
        if ($_ -match '[\s"]') {
            '"' + ($_ -replace '"', '\"') + '"'
        }
        else {
            $_
        }
    }) -join ' '
}

$argumentText = Join-CommandArguments $arguments

Write-Host "Flycast: $flycastExe"
Write-Host "Game: $Game"
Write-Host "Media: $mediaPath"
Write-Host "Delay: $DelaySeconds seconds"
Write-Host "Output: $outputFullPath"

if ($PrintOnly) {
    Write-Host "Command: `"$flycastExe`" $argumentText"
    return
}

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class DcSharpWindowCapture {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();
}
'@

[DcSharpWindowCapture]::SetProcessDPIAware() | Out-Null

function Get-FlycastWindowHandle {
    param(
        [Parameter(Mandatory = $true)]$Process,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "Flycast exited before a window was available."
        }

        if ($Process.MainWindowHandle -ne [IntPtr]::Zero -and [DcSharpWindowCapture]::IsWindow($Process.MainWindowHandle)) {
            return $Process.MainWindowHandle
        }

        Start-Sleep -Milliseconds 250
    }

    throw "Flycast did not expose a main window within $TimeoutSeconds seconds."
}

$process = Start-Process -FilePath $flycastExe -ArgumentList $argumentText -WorkingDirectory $workingDirectory -PassThru
try {
    $handle = Get-FlycastWindowHandle $process 20
    [DcSharpWindowCapture]::ShowWindow($handle, 9) | Out-Null
    [DcSharpWindowCapture]::SetForegroundWindow($handle) | Out-Null
    Start-Sleep -Seconds $DelaySeconds

    $handle = Get-FlycastWindowHandle $process 5
    $rect = New-Object DcSharpWindowCapture+RECT
    if (-not [DcSharpWindowCapture]::GetWindowRect($handle, [ref]$rect)) {
        throw "Could not read Flycast window bounds."
    }

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) {
        throw "Flycast window bounds are empty: ${width}x${height}."
    }

    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
        $bitmap.Save($outputFullPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }

    Write-Host "Captured: $outputFullPath"
}
finally {
    if (-not $LeaveRunning -and $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
    }
}
