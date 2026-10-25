[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [string]$RimWorld17Dir = $env:RIMWORLD_17_DIR, [string]$Harmony17Dir = $env:HARMONY_17_DIR, [string]$CombatExtendedAssembly = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
& $Dotnet run --project (Join-Path $projectRoot 'tests/MultiplayerBridgeTests/MultiplayerBridgeTests.csproj') --configuration Release
if ($LASTEXITCODE -ne 0) { throw "Multiplayer bridge checks failed ($LASTEXITCODE)." }
& $Dotnet run --project (Join-Path $projectRoot 'tests/MultiplayerTests/MultiplayerTests.csproj') --configuration Release
if ($LASTEXITCODE -ne 0) { throw "Multiplayer scenarios failed ($LASTEXITCODE)." }
& $Dotnet run --project (Join-Path $projectRoot 'tests/PawnSpriteTests/PawnSpriteTests.csproj') --configuration Release "-p:RestoreConfigFile=$(Join-Path $projectRoot 'NuGet.Config')"
if ($LASTEXITCODE -ne 0) { throw "Pawn sprite tests failed ($LASTEXITCODE)." }
& (Join-Path $PSScriptRoot 'test-firing-loop.ps1') -Dotnet $Dotnet -VerifyMutations
& (Join-Path $PSScriptRoot 'test-terrain-integration.ps1') -Dotnet $Dotnet -VerifyMutations
& $Dotnet run --project (Join-Path $projectRoot 'tests/TerrainTests/TerrainTests.csproj') --configuration Release
if ($LASTEXITCODE -ne 0) { throw "Terrain tests failed ($LASTEXITCODE)." }
& $Dotnet run --project (Join-Path $projectRoot 'tests/MinimapTests/MinimapTests.csproj') --configuration Release "-p:RestoreConfigFile=$(Join-Path $projectRoot 'NuGet.Config')"
if ($LASTEXITCODE -ne 0) { throw "Minimap tests failed ($LASTEXITCODE)." }
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
& $Dotnet run --project (Join-Path $projectRoot 'tests/InputTests/InputTests.csproj') --configuration Release "-p:RestoreConfigFile=$(Join-Path $projectRoot 'NuGet.Config')"
if ($LASTEXITCODE -ne 0) { throw "Native input tests failed ($LASTEXITCODE)." }
& $Dotnet run --project (Join-Path $projectRoot 'tests/WeaponControlTests/WeaponControlTests.csproj') --configuration Release
if ($LASTEXITCODE -ne 0) { throw "Weapon control tests failed ($LASTEXITCODE)." }
if (-not [string]::IsNullOrWhiteSpace($CombatExtendedAssembly)) {
    & $Dotnet run --project (Join-Path $projectRoot 'tests/CeContractTests/CeContractTests.csproj') --configuration Release -- $CombatExtendedAssembly
    if ($LASTEXITCODE -ne 0) { throw "Installed CE contract tests failed ($LASTEXITCODE)." }
} else { Write-Host 'Installed CE metadata checks skipped: pass -CombatExtendedAssembly.' }
& (Join-Path $projectRoot 'tests/PackageTests/PackageTests.ps1')
Write-Host 'Automated checks passed. These tests do not replace in-game validation.'

