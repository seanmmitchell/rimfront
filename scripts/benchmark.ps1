[CmdletBinding()]
param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
& $Dotnet run --project (Join-Path $projectRoot 'tests/PerformanceBenchmark/PerformanceBenchmark.csproj') --configuration Release "-p:RestoreConfigFile=$(Join-Path $projectRoot 'NuGet.Config')"
if ($LASTEXITCODE -ne 0) { throw "Benchmark failed ($LASTEXITCODE)." }
