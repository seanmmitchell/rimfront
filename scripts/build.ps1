[CmdletBinding()]
param(
    [ValidateSet('All','1.6','1.7')][string]$Version = 'All',
    [string]$Dotnet = 'dotnet',
    [string]$RimWorld17Dir = $env:RIMWORLD_17_DIR,
    [string]$Harmony17Dir = $env:HARMONY_17_DIR,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $projectRoot 'Source/RimFront.csproj'
$versions = if ($Version -eq 'All') { @('1.6','1.7') } else { @($Version) }
foreach ($target in $versions) {
    if ($target -eq '1.7') {
        if ([string]::IsNullOrWhiteSpace($RimWorld17Dir) -or [string]::IsNullOrWhiteSpace($Harmony17Dir)) { throw 'Pass -RimWorld17Dir and -Harmony17Dir for a 1.7 build.' }
        if (-not (Test-Path -LiteralPath (Join-Path $RimWorld17Dir 'Assembly-CSharp.dll'))) { throw 'Pass -RimWorld17Dir pointing to your RimWorld 1.7 install.' }
        if (-not (Test-Path -LiteralPath (Join-Path $Harmony17Dir '0Harmony.dll'))) { throw 'Pass -Harmony17Dir pointing to Harmony 1.7/Assemblies.' }
    }
    $intermediate = (Join-Path $projectRoot ".build/$target/") + [IO.Path]::DirectorySeparatorChar
    $arguments = @('build', $project, '-c', $Configuration, "-p:RestoreConfigFile=$(Join-Path $projectRoot 'NuGet.Config')", "-p:RimWorldVersion=$target", "-p:BaseIntermediateOutputPath=$intermediate", "-p:MSBuildProjectExtensionsPath=$intermediate")
    if ($target -eq '1.7') { $arguments += @("-p:RimWorld17Dir=$RimWorld17Dir", "-p:Harmony17Dir=$Harmony17Dir") }
    & $Dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "RimWorld $target build failed ($LASTEXITCODE)." }
}
Write-Host "Built: $($versions -join ', '). No files were installed into the game."
