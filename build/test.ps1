# PLATE: runs the test suite.
# Usage:  pwsh -File build\test.ps1 [-Configuration Debug]
#
# The patch integrity tests load the real game assemblies from SptGameDir. Without
# a game installation they skip rather than fail.
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet   = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

& $dotnet test "$repoRoot\tests\PLATE.Tests\PLATE.Tests.csproj" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Tests failed" }

Write-Host "Tests OK" -ForegroundColor Green
