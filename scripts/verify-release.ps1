[CmdletBinding()]
param(
    [string]$Dotnet = 'dotnet',
    [string]$RimWorld17Dir = $env:RIMWORLD_17_DIR,
    [string]$Harmony17Dir = $env:HARMONY_17_DIR
)
$ErrorActionPreference = 'Stop'
# A failed build, scenario, mutation check or existing suite terminates before packaging.
& (Join-Path $PSScriptRoot 'build.ps1') -Dotnet $Dotnet -Version All -RimWorld17Dir $RimWorld17Dir -Harmony17Dir $Harmony17Dir
& (Join-Path $PSScriptRoot 'test.ps1') -Dotnet $Dotnet -RimWorld17Dir $RimWorld17Dir -Harmony17Dir $Harmony17Dir
& (Join-Path $PSScriptRoot 'package.ps1') -IncludeSource
Write-Host 'Release verification passed and packages created. Live game/CE smoke checks remain required before publication.'
