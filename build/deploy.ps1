# PLATE build & deploy: builds both projects and lays them out into the game install.
# Usage:  pwsh -File build\deploy.ps1 [-Configuration Release] [-SkipBuild]
param(
    [string]$Configuration = "Release",
    [string]$GameDir = "",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

# game path: -GameDir wins, otherwise SptGameDir from Directory.Build.props,
# so the build and the deploy always agree on one value
if ($GameDir) {
    $gameDir = $GameDir
} else {
    [xml]$props = Get-Content "$repoRoot\Directory.Build.props"
    $gameDir = $props.Project.PropertyGroup.SptGameDir
    if (-not $gameDir) { throw "SptGameDir not set in Directory.Build.props" }
}
if (-not (Test-Path $gameDir)) { throw "Game directory not found: $gameDir" }

$dotnet   = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }  # fallback to the system one

# Warn if the game or the server is running (hot-swapping the dll will not work)
foreach ($proc in "EscapeFromTarkov", "SPT.Server") {
    if (Get-Process -Name $proc -ErrorAction SilentlyContinue) {
        Write-Warning "$proc is running - close it before deploying, otherwise files may be locked."
    }
}

if (-not $SkipBuild) {
    Write-Host "=== Building PLATE.Server ($Configuration)" -ForegroundColor Cyan
    & $dotnet build "$repoRoot\server\PLATE.Server\PLATE.Server.csproj" -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Server build failed" }

    Write-Host "=== Building PLATE.Client ($Configuration)" -ForegroundColor Cyan
    & $dotnet build "$repoRoot\client\PLATE.Client\PLATE.Client.csproj" -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Client build failed" }
}

# --- Server -> user/mods/PLATE ---
$serverOut = "$repoRoot\server\PLATE.Server\bin\$Configuration\PLATE.Server.dll"
$serverDst = "$gameDir\SPT\user\mods\PLATE"
New-Item -ItemType Directory -Force $serverDst | Out-Null
Copy-Item $serverOut $serverDst -Force
Write-Host "Server -> $serverDst\PLATE.Server.dll" -ForegroundColor Green

# --- Bundles (custom blood bag model) -> user/mods/PLATE ---
# copied only when the bundle is built (build-bundle.ps1); bundles.json without
# the bundle file would crash the client on load
$modFiles = "$repoRoot\server\mod-files"
if (Test-Path "$modFiles\bundles\plate\blood_bag.bundle") {
    Copy-Item "$modFiles\bundles.json" $serverDst -Force
    Copy-Item "$modFiles\bundles" $serverDst -Recurse -Force
    Write-Host "Bundles -> $serverDst\bundles\plate\blood_bag.bundle" -ForegroundColor Green
} else {
    Write-Host "Bundles: not built (build\build-bundle.ps1) - the item will use the vanilla bloodset model" -ForegroundColor Yellow
}

# --- Client -> BepInEx/plugins/PLATE ---
$clientOut = "$repoRoot\client\PLATE.Client\bin\$Configuration\PLATE.Client.dll"
$clientDst = "$gameDir\BepInEx\plugins\PLATE"
New-Item -ItemType Directory -Force $clientDst | Out-Null
Copy-Item $clientOut $clientDst -Force
Write-Host "Client -> $clientDst\PLATE.Client.dll" -ForegroundColor Green

Write-Host "Deploy OK" -ForegroundColor Green
