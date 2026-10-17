[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [string]$Node = 'node', [string]$RimWorld17Dir = $env:RIMWORLD_17_DIR, [string]$Harmony17Dir = $env:HARMONY_17_DIR)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
& $Dotnet run --project (Join-Path $projectRoot 'tests/JobPolicyTests/JobPolicyTests.csproj') --configuration Release "-p:RestoreConfigFile=$(Join-Path $projectRoot 'NuGet.Config')"
if ($LASTEXITCODE -ne 0) { throw "Job policy tests failed ($LASTEXITCODE)." }
& $Dotnet run --project (Join-Path $projectRoot 'tests/WindowIconTests/WindowIconTests.csproj') --configuration Release "-p:RestoreConfigFile=$(Join-Path $projectRoot 'NuGet.Config')"
if ($LASTEXITCODE -ne 0) { throw "Window icon tests failed ($LASTEXITCODE)." }
& $Dotnet run --project (Join-Path $projectRoot 'tests/MovementTests/MovementTests.csproj') --configuration Release "-p:RestoreConfigFile=$(Join-Path $projectRoot 'NuGet.Config')"
if ($LASTEXITCODE -ne 0) { throw "Movement tests failed ($LASTEXITCODE)." }
if (-not [string]::IsNullOrWhiteSpace($RimWorld17Dir)) {
    if ([string]::IsNullOrWhiteSpace($Harmony17Dir)) { throw 'Native reload tests also require -Harmony17Dir (or HARMONY_17_DIR).' }
    & $Dotnet run --project (Join-Path $projectRoot 'tests/NativePawnTests/NativePawnTests.csproj') --configuration Release "-p:RestoreConfigFile=$(Join-Path $projectRoot 'NuGet.Config')" -- $RimWorld17Dir (Join-Path $projectRoot 'mod/1.7/Assemblies/RimFront.dll') $Harmony17Dir
    if ($LASTEXITCODE -ne 0) { throw "Native pawn tests failed ($LASTEXITCODE)." }
    & $Dotnet run --project (Join-Path $projectRoot 'tests/NativeGeometryTests/NativeGeometryTests.csproj') --configuration Release "-p:RestoreConfigFile=$(Join-Path $projectRoot 'NuGet.Config')" -- $RimWorld17Dir (Join-Path $projectRoot 'mod/1.7/Assemblies/RimFront.dll') $Harmony17Dir
    if ($LASTEXITCODE -ne 0) { throw "Native geometry tests failed ($LASTEXITCODE)." }
} else { Write-Host 'Native geometry tests skipped: pass -RimWorld17Dir after building 1.7.' }
& $Dotnet run --project (Join-Path $projectRoot 'tests/BridgeTests/BridgeTests.csproj') --configuration Release "-p:RestoreConfigFile=$(Join-Path $projectRoot 'NuGet.Config')"
if ($LASTEXITCODE -ne 0) { throw "Bridge tests failed ($LASTEXITCODE)." }
& $Node --test (Join-Path $projectRoot 'Client/core.test.mjs')
if ($LASTEXITCODE -ne 0) { throw "Client tests failed ($LASTEXITCODE)." }
Write-Host 'Automated checks passed. These tests do not replace in-game validation.'
