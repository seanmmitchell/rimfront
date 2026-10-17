[CmdletBinding()]
param([string]$ReferenceDll)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$referenceDll = if ($ReferenceDll) { $ReferenceDll } else { Join-Path $projectRoot 'mod/1.7/Assemblies/RimFront.dll' }
if (-not (Test-Path -LiteralPath $referenceDll)) { throw 'Package contract tests require an existing RimFront build.' }
$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($referenceDll).Version
$release = "$($assemblyVersion.Major).$($assemblyVersion.Minor).$($assemblyVersion.Build)"
$fixtureRoot = Join-Path $projectRoot '.build/package-tests'
$fixture = Join-Path $fixtureRoot ('rimfront-package-test-' + [Guid]::NewGuid().ToString('N'))
$checks = 0
function Check([bool]$condition, [string]$label) {
    if (-not $condition) { throw "FAIL: $label" }
    $script:checks++
}
function Put([string]$relative, [string]$value = 'fixture') {
    $path = Join-Path $fixture $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $path -Parent) | Out-Null
    Set-Content -LiteralPath $path -Value $value
}
function Entries([string]$path) {
    $archive = [IO.Compression.ZipFile]::OpenRead($path)
    try { @($archive.Entries | ForEach-Object FullName) } finally { $archive.Dispose() }
}
function Rejects([scriptblock]$action, [string]$message) {
    try { & $action; throw 'Expected package rejection did not happen.' }
    catch { Check ($_.Exception.Message -like "*$message*") "Rejects $message" }
}
try {
    Put 'Source/RimFront.csproj' "<Project><PropertyGroup><Version>$release</Version></PropertyGroup></Project>"
    Put 'Source/NativeInput.cs'
    Put 'Source/obj/stale.cs'
    Put 'tests/InputTests/Program.cs'
    Put 'tests/InputTests/bin/stale.exe'
    Put 'Client/index.html' 'obsolete browser client'
    Put 'mod/Client/index.html' 'obsolete staged client'
    Put 'mod/docs/protocol.md' 'obsolete staged protocol'
    Put 'mod/docs/stale.txt' 'obsolete staged documentation'
    Put 'mod/README.md' 'obsolete staged readme'
    Put 'mod/About/About.xml'
    Put 'mod/LoadFolders.xml'
    Put 'README.md' 'current readme'
    Put 'LICENSE'
    Put 'NuGet.Config'
    Put '.gitignore'
    Put 'docs/protocol.md' 'obsolete original protocol'
    Put 'docs/runtime-session.md' 'obsolete browser session'
    foreach ($file in @('architecture.md','compatibility.md','performance.md','smoke-tests.md','model-catalog.png','object-catalog.png','action-catalog.png')) { Put "docs/$file" }
    foreach ($version in @('1.6','1.7')) {
        Put "mod/$version/Assemblies/stale.deps.json"
        Copy-Item -LiteralPath $referenceDll -Destination (Join-Path $fixture "mod/$version/Assemblies/RimFront.dll")
    }
    New-Item -ItemType Directory -Force -Path (Join-Path $fixture 'scripts') | Out-Null
    $scriptPath = Join-Path $fixture 'scripts/package.ps1'
    Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts/package.ps1') -Destination $scriptPath
    & $scriptPath -IncludeSource
    $runtimePath = Join-Path $fixture "dist/RimFront-$release.zip"
    $sourcePath = Join-Path $fixture "dist/RimFront-$release-source.zip"
    $runtime = Entries $runtimePath
    $source = Entries $sourcePath
    Check ($runtime -contains '1.6/Assemblies/RimFront.dll' -and $runtime -contains '1.7/Assemblies/RimFront.dll') 'both runtime assemblies included'
    Check (@($runtime | Where-Object { $_ -match '\.dll$' }).Count -eq 2) 'runtime contains only the two mod DLLs'
    Check (@($runtime | Where-Object { $_ -match 'Client|protocol|runtime-session|stale|Source/|scripts/|tests/' }).Count -eq 0) 'runtime excludes all stale/browser/development artifacts'
    Check ($source -contains 'Source/NativeInput.cs' -and $source -contains 'tests/InputTests/Program.cs') 'source contains native implementation and native checks'
    Check (@($source | Where-Object { $_ -match 'Client|protocol|runtime-session|/bin/|/obj/|\.dll$|dist/' }).Count -eq 0) 'source excludes browser, compiled outputs and recursive archives'
    $zip = [IO.Compression.ZipFile]::OpenRead($runtimePath)
    try {
        $reader = [IO.StreamReader]::new($zip.GetEntry('README.md').Open())
        try { Check ($reader.ReadToEnd().Trim() -eq 'current readme') 'runtime reads project README instead of stale staging copy' }
        finally { $reader.Dispose() }
    } finally { $zip.Dispose() }
    Check ((Get-Content -Raw -LiteralPath (Join-Path $fixture 'mod/README.md')).Trim() -eq 'obsolete staged readme') 'packaging does not modify mod staging'
    # Repackaging replaces the full archive rather than merging old entries.
    $zip = [IO.Compression.ZipFile]::Open($runtimePath, [IO.Compression.ZipArchiveMode]::Update)
    try { $null = $zip.CreateEntry('Client/stale.html') } finally { $zip.Dispose() }
    & $scriptPath
    Check ((Entries $runtimePath) -notcontains 'Client/stale.html') 'fresh archive removes preexisting obsolete entries'
    Put 'Source/RimFront.csproj' '<Project><PropertyGroup><Version>99.0.0</Version></PropertyGroup></Project>'
    Rejects { & $scriptPath } 'Stale or incorrect'
    Put 'Source/RimFront.csproj' "<Project><PropertyGroup><Version>$release</Version></PropertyGroup></Project>"
    Put 'mod/1.6/Assemblies/Assembly-CSharp.dll'
    Rejects { & $scriptPath } 'dependency DLLs'
    Write-Host "PASS: $checks native-only package contract checks"
}
finally {
    # The unique fixture is created above; verify its absolute scope before removal.
    $absoluteFixture = [IO.Path]::GetFullPath($fixture)
    $temporaryRoot = [IO.Path]::GetFullPath($fixtureRoot).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if (-not $absoluteFixture.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($absoluteFixture) -notlike 'rimfront-package-test-*') { throw 'Unsafe test fixture cleanup path.' }
    if (Test-Path -LiteralPath $absoluteFixture) { Remove-Item -LiteralPath $absoluteFixture -Recurse -Force }
}

