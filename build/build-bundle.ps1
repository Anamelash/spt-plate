# PLATE: builds the Unity bundle with the blood bag model.
# Requires Unity 2022.3.43f1 (EFT 0.16.9 engine version). The first build can take
# several minutes (importing a 28 MB FBX + textures).
# Usage:  pwsh -File build\build-bundle.ps1 [-UnityExe <path to Unity.exe>]
param(
    [string]$UnityExe = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$unityProj = "$repoRoot\unity"

if (-not $UnityExe) {
    $candidates = @("C:\Program Files\Unity\Hub\Editor\2022.3.43f1\Editor\Unity.exe")
    $hubDir = "C:\Program Files\Unity\Hub\Editor"
    if (Test-Path $hubDir) {
        $candidates += Get-ChildItem $hubDir -Directory |
            Where-Object { $_.Name -like "2022.3.*" } |
            ForEach-Object { "$($_.FullName)\Editor\Unity.exe" }
    }
    $UnityExe = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $UnityExe) {
        throw "Unity 2022.3.x not found. Install Unity Hub and the 2022.3.43f1 editor, " +
              "or pass the path: build-bundle.ps1 -UnityExe <...>\Unity.exe"
    }
}

Write-Host "=== Unity: $UnityExe" -ForegroundColor Cyan
$log = "$unityProj\build.log"
# Unity.exe is a GUI app: without Start-Process -Wait the shell would not wait for it to exit
$proc = Start-Process -FilePath $UnityExe -Wait -PassThru -ArgumentList @(
    "-batchmode", "-nographics", "-quit",
    "-projectPath", "`"$unityProj`"",
    "-executeMethod", "PlateBundleBuilder.Build",
    "-logFile", "`"$log`""
)
if ($proc.ExitCode -ne 0) {
    if (Test-Path $log) {
        Write-Host "--- tail of $log :" -ForegroundColor Yellow
        Get-Content $log -Tail 40
    }
    throw "Unity build failed (exit $($proc.ExitCode)), full log: $log"
}

$bundle = "$unityProj\BundleOutput\plate\blood_bag.bundle"
if (-not (Test-Path $bundle)) { throw "Bundle not found: $bundle" }

# into the repo: deploy.ps1 lays mod-files out into the game's mod folder
$dst = "$repoRoot\server\mod-files\bundles\plate"
New-Item -ItemType Directory -Force $dst | Out-Null
Copy-Item $bundle $dst -Force
$size = [math]::Round((Get-Item "$dst\blood_bag.bundle").Length / 1MB, 1)
Write-Host "Bundle -> $dst\blood_bag.bundle ($size MB)" -ForegroundColor Green
Write-Host "Next: pwsh -File build\deploy.ps1" -ForegroundColor Cyan
