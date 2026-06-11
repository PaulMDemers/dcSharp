param(
    [string]$DestinationRoot = '',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
    $DestinationRoot = Join-Path $repoRoot 'dreamcast-downloads\flycast'
}

$headers = @{
    'User-Agent' = 'dcSharp-reference-tooling'
}

$release = Invoke-RestMethod -Headers $headers -Uri 'https://api.github.com/repos/flyinghead/flycast/releases/latest'
$asset = $release.assets |
    Where-Object { $_.name -match '(?i)windows|win' -and $_.name -match '(?i)x64|64' -and $_.name -match '\.zip$' } |
    Select-Object -First 1

if (-not $asset) {
    $available = ($release.assets | ForEach-Object { $_.name }) -join ', '
    throw "Could not find a Windows x64 zip asset in Flycast release $($release.tag_name). Available assets: $available"
}

$tagRoot = Join-Path $DestinationRoot $release.tag_name
$archivePath = Join-Path $tagRoot $asset.name
$extractRoot = Join-Path $tagRoot 'extracted'
New-Item -ItemType Directory -Force -Path $tagRoot | Out-Null

if ($Force -or -not (Test-Path -LiteralPath $archivePath)) {
    Write-Host "Downloading Flycast $($release.tag_name): $($asset.name)"
    Invoke-WebRequest -Headers $headers -Uri $asset.browser_download_url -OutFile $archivePath
}
else {
    Write-Host "Using existing archive: $archivePath"
}

if ($Force -and (Test-Path -LiteralPath $extractRoot)) {
    Remove-Item -LiteralPath $extractRoot -Recurse -Force
}

if (-not (Test-Path -LiteralPath $extractRoot)) {
    New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot -Force
}

$flycastExe = Get-ChildItem -LiteralPath $extractRoot -Recurse -Filter 'flycast*.exe' |
    Select-Object -First 1

if (-not $flycastExe) {
    throw "Flycast extracted, but no flycast executable was found under $extractRoot"
}

$currentPath = Join-Path $DestinationRoot 'current.txt'
Set-Content -LiteralPath $currentPath -Value $flycastExe.FullName -Encoding UTF8

Write-Host "Flycast release: $($release.tag_name)"
Write-Host "Executable: $($flycastExe.FullName)"
Write-Host "Current pointer: $currentPath"
