# PLATE: runs the test suite.
# Usage:  pwsh -File build\test.ps1 [-Configuration Debug]
#
# Two projects, because the halves of the mod target different runtimes: the client
# tests run on net471 against the real game assemblies from SptGameDir (without a
# game installation they skip rather than fail), the server tests on net10.
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
# The server half targets net10, so the SDK has to be able to build it: the user-local
# install under ~\.dotnet is preferred when new enough, otherwise the system one.
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet) -or
    -not (& $dotnet --list-sdks | Where-Object { [int]($_ -split '\.')[0] -ge 10 })) {
    $dotnet = "dotnet"
}

& $dotnet test "$repoRoot\tests\PLATE.Tests\PLATE.Tests.csproj" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Client tests failed" }

& $dotnet test "$repoRoot\tests\PLATE.Server.Tests\PLATE.Server.Tests.csproj" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Server tests failed" }

Write-Host "Tests OK" -ForegroundColor Green
