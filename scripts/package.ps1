[CmdletBinding()]
param([switch]$IncludeSource)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$modRoot = Join-Path $projectRoot 'mod'
[xml]$project = Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'Source/RimFront.csproj')
$release = [string](@($project.Project.PropertyGroup.Version | Where-Object { $_ })[0])
if ($release -notmatch '^\d+\.\d+\.\d+$') { throw 'Source/RimFront.csproj must declare a numeric three-part release Version.' }
$expectedVersion = [Version]"$release.0"
foreach ($version in @('1.6','1.7')) {
    $assemblyDir = Join-Path $modRoot "$version/Assemblies"
    $assemblyPath = Join-Path $assemblyDir 'RimFront.dll'
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) { throw "Build RimWorld $version first." }
    $identity = [Reflection.AssemblyName]::GetAssemblyName($assemblyPath)
    $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyPath)
    if ($identity.Name -ne 'RimFront' -or $identity.Version -ne $expectedVersion -or [Version]$fileVersion.FileVersion -ne $expectedVersion) {
        throw "Stale or incorrect RimWorld $version assembly: expected RimFront $expectedVersion, found $($identity.Name) $($identity.Version), file $($fileVersion.FileVersion). Rebuild both versions."
    }
    $unexpected = Get-ChildItem -LiteralPath $assemblyDir -Filter '*.dll' | Where-Object Name -ne 'RimFront.dll'
    if ($unexpected) { throw "Refusing to package dependency DLLs: $($unexpected.Name -join ', ')" }
}
# Explicit current documents prevent obsolete browser docs left in mod/docs (or docs)
# from leaking into a release. Read originals directly; never mutate mod staging.
$documents = @('multiplayer.md','multiplayer-1.6-contracts.json','multiplayer-1.7-contracts.json','architecture.md','compatibility.md','performance.md','smoke-tests.md','model-catalog.png','object-catalog.png','action-catalog.png')
function Add-File($files, [string]$relative, [string]$entry) {
    $path = Join-Path $projectRoot $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing package input: $relative" }
    $files.Add([pscustomobject]@{ Path = $path; Entry = $entry.Replace('\','/') })
}
function Add-Folder($files, [string]$relative, [string]$prefix) {
    $folder = Join-Path $projectRoot $relative
    foreach ($file in (Get-ChildItem -LiteralPath $folder -File -Recurse | Sort-Object FullName)) {
        $suffix = $file.FullName.Substring($folder.Length + 1).Replace('\','/')
        if ($suffix -match '(^|/)(bin|obj|\.build|dist)(/|$)' -or $file.Extension -in @('.dll','.pdb')) { continue }
        $files.Add([pscustomobject]@{ Path = $file.FullName; Entry = "$prefix/$suffix" })
    }
}
function Write-Package($files, [string]$destination) {
    # Build a fresh archive under a unique temporary name; replace the prior archive
    # only after every entry has been written successfully.
    $temporary = "$destination.$([Guid]::NewGuid().ToString('N')).tmp"
    $stream = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in ($files | Sort-Object Entry)) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.Path, $file.Entry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally { $zip.Dispose(); $stream.Dispose() }
    Move-Item -LiteralPath $temporary -Destination $destination -Force
    Write-Host "Package: $destination"
}
$runtime = [Collections.Generic.List[object]]::new()
Add-Folder $runtime 'mod/About' 'About'
Add-File $runtime 'mod/LoadFolders.xml' 'LoadFolders.xml'
foreach ($version in @('1.6','1.7')) {
    Add-File $runtime "mod/$version/Assemblies/RimFront.dll" "$version/Assemblies/RimFront.dll"
    $pdb = "mod/$version/Assemblies/RimFront.pdb"
    if (Test-Path -LiteralPath (Join-Path $projectRoot $pdb)) { Add-File $runtime $pdb "$version/Assemblies/RimFront.pdb" }
}
foreach ($file in @('README.md','LICENSE')) { Add-File $runtime $file $file }
foreach ($file in $documents) { Add-File $runtime "docs/$file" "docs/$file" }
$dist = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
Write-Package $runtime (Join-Path $dist "RimFront-$release.zip")
if ($IncludeSource) {
    $source = [Collections.Generic.List[object]]::new()
    foreach ($folder in @('Source','scripts','tests','mod/About')) { Add-Folder $source $folder $folder }
    foreach ($file in $documents) { Add-File $source "docs/$file" "docs/$file" }
    foreach ($file in @('README.md','LICENSE','NuGet.Config','.gitignore','mod/LoadFolders.xml')) { Add-File $source $file $file }
    Write-Package $source (Join-Path $dist "RimFront-$release-source.zip")
}
