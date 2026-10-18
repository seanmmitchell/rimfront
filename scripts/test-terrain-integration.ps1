[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [switch]$VerifyMutations)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $projectRoot 'tests/TerrainIntegrationTests/TerrainIntegrationTests.csproj'
$runRoot = Join-Path $projectRoot ('.build/terrain-regressions/' + [Guid]::NewGuid().ToString('N'))
$results = [Collections.Generic.List[object]]::new()
$utf8 = [Text.UTF8Encoding]::new($false)
function Invoke-Logged([string[]]$arguments, [string]$log) {
    $saved = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $global:LASTEXITCODE = $null
        $lines = @(& $Dotnet @arguments 2>&1)
        $code = $global:LASTEXITCODE
        $lines | Set-Content -LiteralPath $log -Encoding utf8
        return [pscustomobject]@{ Code = $code; Text = ($lines | Out-String) }
    }
    finally { $ErrorActionPreference = $saved }
}
function Run-Case([string]$name, [string]$source, [string]$scenario = '') {
    $caseRoot = Join-Path $runRoot $name
    New-Item -ItemType Directory -Force -Path $caseRoot | Out-Null
    $obj = (Join-Path $caseRoot 'obj') + [IO.Path]::DirectorySeparatorChar
    $output = (Join-Path $caseRoot 'bin') + [IO.Path]::DirectorySeparatorChar
    $build = Invoke-Logged @('build', $project, '-c', 'Release', '--nologo',
        "-p:RimFrontSource=$source", "-p:RestoreConfigFile=$(Join-Path $projectRoot 'NuGet.Config')",
        "-p:BaseIntermediateOutputPath=$obj", "-p:MSBuildProjectExtensionsPath=$obj", "-p:OutputPath=$output",
        '-p:AppendTargetFrameworkToOutputPath=false', '-p:UseAppHost=false') (Join-Path $caseRoot 'build.log')
    if ($null -eq $build.Code -or $build.Code -ne 0) { throw "Terrain $name did not compile. Build errors never count as detecting a regression. $($build.Text)" }
    $arguments = @((Join-Path $output 'TerrainIntegrationTests.dll'))
    if ($scenario) { $arguments += $scenario }
    $test = Invoke-Logged $arguments (Join-Path $caseRoot 'test.log')
    if (-not $scenario) {
        if ($test.Code -ne 0 -or ([regex]::Matches($test.Text, '(?m)^PASS: ')).Count -ne 5) { throw "Terrain baseline failed or omitted scenarios: $($test.Text)" }
        Write-Host $test.Text.Trim()
    }
    else {
        if ($test.Code -ne 1 -or -not $test.Text.Contains("FAIL: ${scenario}:")) { throw "Terrain regression $name was not detected by $scenario. $($test.Text)" }
        Write-Host "KILLED: $name compiled and failed scenario '$scenario'."
    }
    $results.Add([pscustomobject]@{ case = $name; scenario = $scenario; buildExit = $build.Code; testExit = $test.Code })
}
try {
    Run-Case 'baseline' (Join-Path $projectRoot 'Source')
    if ($VerifyMutations) {
        $mutations = @(
            @{ Name = 'unpublished-mountain-heights'; File = 'NativeMountains.cs'; Scenario = 'published-mountain-geometry';
                Anchor = 'snapshot = snapshot ?? surfaces.AtCell(x, z);'; Replacement = 'snapshot = snapshot ?? Capture(x / 12, z / 12);' },
            @{ Name = 'infinite-fog-column'; File = 'NativeAimTargeting.cs'; Scenario = 'fogged-mountain-exterior';
                Anchor = 'if (!cell.InBounds(map)) { boundary'; Replacement = 'if (!cell.InBounds(map) || cell.Fogged(map)) { boundary' },
            @{ Name = 'first-arrival-plant-budget'; File = 'NearestCandidates.cs'; Scenario = 'nearest-plant-admission';
                Anchor = 'var entry = new Entry'; Replacement = 'if (heap.Count == capacity) return false; var entry = new Entry' }
        )
        foreach ($mutation in $mutations) {
            $source = Join-Path $runRoot ($mutation.Name + '/source')
            New-Item -ItemType Directory -Force -Path $source | Out-Null
            foreach ($name in @('NativeAimTargeting', 'NativeMountains', 'MountainProfile', 'MountainSurfaceCache', 'NearestCandidates', 'AimGeometry')) {
                Copy-Item -LiteralPath (Join-Path $projectRoot "Source/$name.cs") -Destination $source
            }
            $path = Join-Path $source $mutation.File
            $text = [IO.File]::ReadAllText($path)
            $index = $text.IndexOf($mutation.Anchor, [StringComparison]::Ordinal)
            if ($index -lt 0 -or $text.IndexOf($mutation.Anchor, $index + $mutation.Anchor.Length, [StringComparison]::Ordinal) -ge 0) { throw "Mutation anchor changed: $($mutation.Name)" }
            [IO.File]::WriteAllText($path, $text.Replace($mutation.Anchor, $mutation.Replacement), $utf8)
            Run-Case $mutation.Name $source $mutation.Scenario
        }
    }
}
finally {
    New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
    $results.ToArray() | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runRoot 'results.json') -Encoding utf8
    Write-Host "Terrain regression evidence: $runRoot"
}
exit 0
