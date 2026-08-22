# PLATE: builds a release archive ready to unpack into the SPT installation root.
# Usage:  pwsh -File build\package.ps1 [-Configuration Release] [-SkipBuild]
param(
    [string]$Configuration = "Release",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet   = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

# version comes from Directory.Build.props - single source of truth
[xml]$props = Get-Content "$repoRoot\Directory.Build.props"
$version = $props.Project.PropertyGroup.PlateVersion
if (-not $version) { throw "PlateVersion not found in Directory.Build.props" }

if (-not $SkipBuild) {
    foreach ($proj in "server\PLATE.Server\PLATE.Server.csproj", "client\PLATE.Client\PLATE.Client.csproj") {
        Write-Host "=== Building $proj ($Configuration)" -ForegroundColor Cyan
        & $dotnet build "$repoRoot\$proj" -c $Configuration --nologo
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $proj" }
    }
}

# staging mirrors the game root, so the user unpacks the archive over it
$stage = Join-Path $repoRoot "dist\stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$clientDst = New-Item -ItemType Directory -Force "$stage\BepInEx\plugins\PLATE"
$serverDst = New-Item -ItemType Directory -Force "$stage\SPT\user\mods\PLATE"

Copy-Item "$repoRoot\client\PLATE.Client\bin\$Configuration\PLATE.Client.dll" $clientDst -Force
Copy-Item "$repoRoot\server\PLATE.Server\bin\$Configuration\PLATE.Server.dll" $serverDst -Force

# bundles ship only when built (build-bundle.ps1); bundles.json without the
# bundle file would crash the client on load
$modFiles = "$repoRoot\server\mod-files"
if (Test-Path "$modFiles\bundles\plate\blood_bag.bundle") {
    Copy-Item "$modFiles\bundles.json" $serverDst -Force
    Copy-Item "$modFiles\bundles" $serverDst -Recurse -Force
} else {
    Write-Warning "Bundle not built - the blood bag will use the vanilla bloodset model"
}

Copy-Item "$repoRoot\README.md", "$repoRoot\CHANGELOG.md" $stage -Force

$zip = "$repoRoot\dist\PLATE-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force

$size = [math]::Round((Get-Item $zip).Length / 1MB, 2)
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Host "Release -> $zip ($size MB)" -ForegroundColor Green
Write-Host "SHA256: $hash" -ForegroundColor Green
