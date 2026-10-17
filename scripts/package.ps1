[CmdletBinding()]
param([switch]$IncludeSource)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$modRoot = Join-Path $projectRoot 'mod'
foreach ($version in @('1.6','1.7')) {
    $assemblyDir = Join-Path $modRoot "$version/Assemblies"
    if (-not (Test-Path -LiteralPath (Join-Path $assemblyDir 'RimFront.dll'))) { throw "Build RimWorld $version first." }
    $unexpected = Get-ChildItem -LiteralPath $assemblyDir -Filter '*.dll' | Where-Object Name -ne 'RimFront.dll'
    if ($unexpected) { throw "Refusing to package dependency DLLs: $($unexpected.Name -join ', ')" }
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $modRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $modRoot -Force
New-Item -ItemType Directory -Force -Path (Join-Path $modRoot 'docs') | Out-Null
Copy-Item -Path (Join-Path $projectRoot 'docs/*') -Destination (Join-Path $modRoot 'docs') -Recurse -Force
$dist = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$archive = Join-Path $dist 'RimFront-0.5.8.zip'
$runtimeFiles = @('About','1.6','1.7','docs','README.md','LICENSE','LoadFolders.xml') | ForEach-Object { Join-Path $modRoot $_ }
Compress-Archive -Path $runtimeFiles -DestinationPath $archive -Force
Write-Host "Package: $archive"
if ($IncludeSource) {
    $sourceArchive = Join-Path $dist 'RimFront-0.5.8-source.zip'
    $sourceFiles = @()
    foreach ($folder in @('Source','Client','scripts','tests','docs','mod/About')) {
        $sourceFiles += Get-ChildItem -LiteralPath (Join-Path $projectRoot $folder) -File -Recurse |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.build|dist)[\\/]' -and $_.Extension -notin @('.dll','.pdb') }
    }
    foreach ($file in @('README.md','LICENSE','NuGet.Config','.gitignore','mod/LoadFolders.xml')) {
        $sourceFiles += Get-Item -LiteralPath (Join-Path $projectRoot $file)
    }
    $stream = [IO.File]::Open($sourceArchive, [IO.FileMode]::Create, [IO.FileAccess]::Write)
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in ($sourceFiles | Sort-Object FullName -Unique)) {
            $entry = $file.FullName.Substring($projectRoot.Length + 1).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally { $zip.Dispose(); $stream.Dispose() }
    Write-Host "Source archive: $sourceArchive"
}


