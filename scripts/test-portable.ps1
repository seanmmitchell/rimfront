[CmdletBinding()]
param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
# This hosted gate never requires or distributes an installed game's assemblies.
& (Join-Path $PSScriptRoot 'build.ps1') -Dotnet $Dotnet -Version '1.6'
& (Join-Path $PSScriptRoot 'test-firing-loop.ps1') -Dotnet $Dotnet -VerifyMutations
foreach ($suite in @('JobPolicyTests', 'WindowIconTests', 'MovementTests', 'InputTests')) {
    $project = Join-Path $projectRoot "tests/$suite/$suite.csproj"
    & $Dotnet run --project $project --configuration Release "-p:RestoreConfigFile=$(Join-Path $projectRoot 'NuGet.Config')"
    if ($LASTEXITCODE -ne 0) { throw "$suite failed ($LASTEXITCODE)." }
}
# Package tests intentionally copy one mod DLL into both fixture slots. They test
# archive contracts, not cross-version compatibility or a playable release ZIP.
& (Join-Path $projectRoot 'tests/PackageTests/PackageTests.ps1') -ReferenceDll (Join-Path $projectRoot 'mod/1.6/Assemblies/RimFront.dll')
Write-Host 'Portable CI passed: 1.6 build, firing scenarios/mutations, policy, movement, window, input and package contracts.'
Write-Host 'Not covered: 1.7 build/native helper suites, live Harmony/CE, GPU, or in-game smoke tests. Run verify-release.ps1 locally before release.'
