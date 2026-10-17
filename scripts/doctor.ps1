[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [string]$RimWorld17Dir = $env:RIMWORLD_17_DIR, [string]$Harmony17Dir = $env:HARMONY_17_DIR)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
& $Dotnet --version
if ($LASTEXITCODE -ne 0) { throw 'A .NET 10 SDK is required.' }
foreach ($item in @(@('RimWorld 1.7', $RimWorld17Dir, 'Assembly-CSharp.dll'), @('Harmony 1.7', $Harmony17Dir, '0Harmony.dll'))) {
    $available = -not [string]::IsNullOrWhiteSpace($item[1]) -and (Test-Path -LiteralPath (Join-Path $item[1] $item[2]))
    Write-Host "$($item[0]): $available"
}
foreach ($version in @('1.6','1.7')) { Write-Host "Built $version assembly: $(Test-Path -LiteralPath (Join-Path $projectRoot "mod/$version/Assemblies/RimFront.dll"))" }
Write-Host 'RimWorld 1.6 reference assemblies restore from NuGet. Runtime and combat smoke tests require a game session.'
