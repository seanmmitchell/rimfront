[CmdletBinding()]
param(
    [string]$Dotnet = 'dotnet',
    [string]$Python = 'python',
    [string]$RimWorld17Dir = $env:RIMWORLD_17_DIR,
    [string]$Harmony17Dir = $env:HARMONY_17_DIR
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RimWorld17Dir) -or [string]::IsNullOrWhiteSpace($Harmony17Dir)) { throw 'Pass the local game and Harmony assembly directories.' }
$projectRoot = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $projectRoot '.build/catalog'
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
$geometry = Join-Path $scratch 'pawn-models.json'
& $Dotnet run --project (Join-Path $projectRoot 'tests/NativePawnTests/NativePawnTests.csproj') --configuration Release "-p:RestoreConfigFile=$(Join-Path $projectRoot 'NuGet.Config')" -- $RimWorld17Dir (Join-Path $projectRoot 'mod/1.7/Assemblies/RimFront.dll') $Harmony17Dir $geometry
if ($LASTEXITCODE -ne 0) { throw 'Pawn geometry validation/export failed.' }
& $Python (Join-Path $PSScriptRoot 'render-model-catalog.py') $geometry (Join-Path $projectRoot 'docs/model-catalog.png')
if ($LASTEXITCODE -ne 0) { throw 'Catalog rendering failed. Python with Pillow and NumPy is required.' }
& $Python (Join-Path $PSScriptRoot 'render-model-catalog.py') ([IO.Path]::ChangeExtension($geometry,'objects.json')) (Join-Path $projectRoot 'docs/object-catalog.png') 'RimFront 0.5 / object geometry catalog'
if ($LASTEXITCODE -ne 0) { throw 'Object catalog rendering failed.' }
& $Python (Join-Path $PSScriptRoot 'render-model-catalog.py') ([IO.Path]::ChangeExtension($geometry,'actions.json')) (Join-Path $projectRoot 'docs/action-catalog.png') 'RimFront 0.5 / outfits and body poses'
if ($LASTEXITCODE -ne 0) { throw 'Action catalog rendering failed.' }

& $Python (Join-Path $PSScriptRoot 'render-model-catalog.py') ([IO.Path]::ChangeExtension($geometry,'weapons.json')) (Join-Path $projectRoot 'docs/weapon-catalog.png') 'RimFront / carried machine guns and bipods'
if ($LASTEXITCODE -ne 0) { throw 'Weapon catalog rendering failed.' }
