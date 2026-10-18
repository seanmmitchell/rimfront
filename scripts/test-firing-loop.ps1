[CmdletBinding()]
param(
    [string]$Dotnet = 'dotnet',
    [switch]$VerifyMutations
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $projectRoot 'tests/FiringLoopTests/FiringLoopTests.csproj'
$gameplayPath = Join-Path $projectRoot 'Source/Gameplay.cs'
$runRoot = Join-Path $projectRoot ('.build/firing-loop/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
$temporarySources = [Collections.Generic.List[string]]::new()
$results = [Collections.Generic.List[object]]::new()
$utf8 = [Text.UTF8Encoding]::new($false)

function Replace-ExactlyOnce([string]$source, [string]$anchor, [string]$replacement) {
    $index = $source.IndexOf($anchor, [StringComparison]::Ordinal)
    if ($index -lt 0 -or $source.IndexOf($anchor, $index + $anchor.Length, [StringComparison]::Ordinal) -ge 0) {
        throw "Mutation anchor must occur exactly once; review the historical regression before updating it: $anchor"
    }
    return $source.Substring(0, $index) + $replacement + $source.Substring($index + $anchor.Length)
}

function Run-Case([string]$name, [string]$gameplaySource, [string]$expectedFailure = '') {
    $caseRoot = Join-Path $runRoot $name
    New-Item -ItemType Directory -Force -Path $caseRoot | Out-Null
    $intermediate = (Join-Path $caseRoot 'obj') + [IO.Path]::DirectorySeparatorChar
    $output = (Join-Path $caseRoot 'bin') + [IO.Path]::DirectorySeparatorChar
    $arguments = @('build', $project, '--configuration', 'Release', '--nologo',
        "-p:RestoreConfigFile=$(Join-Path $projectRoot 'NuGet.Config')", "-p:GameplaySource=$gameplaySource",
        "-p:BaseIntermediateOutputPath=$intermediate", "-p:MSBuildProjectExtensionsPath=$intermediate",
        "-p:OutputPath=$output", '-p:AppendTargetFrameworkToOutputPath=false', '-p:UseAppHost=false')
    $buildLog = Join-Path $caseRoot 'build.log'
    $savedPreference = $ErrorActionPreference
    try {
        # Windows PowerShell 5 represents redirected native stderr as error records.
        # Collect those here, then decide success using the child exit and results.
        $ErrorActionPreference = 'Continue'
        $global:LASTEXITCODE = $null
        & $Dotnet @arguments 2>&1 | Tee-Object -FilePath $buildLog | Out-Host
        $buildExit = $global:LASTEXITCODE
    }
    finally { $ErrorActionPreference = $savedPreference }
    if ($null -eq $buildExit -or $buildExit -ne 0) {
        $results.Add([pscustomobject]@{ case = $name; outcome = 'build-error'; buildExit = $buildExit; log = $buildLog })
        throw "Firing-loop $name failed to compile (exit $buildExit). A build error never counts as detecting a regression. See $buildLog"
    }
    $assembly = Join-Path $output 'FiringLoopTests.dll'
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw "Missing firing-loop assembly after successful build: $assembly" }
    try {
        $ErrorActionPreference = 'Continue'
        $global:LASTEXITCODE = $null
        $lines = @(& $Dotnet $assembly 2>&1)
        $testExit = $global:LASTEXITCODE
    }
    finally { $ErrorActionPreference = $savedPreference }
    $testLog = Join-Path $caseRoot 'scenarios.jsonl'
    $lines | Set-Content -LiteralPath $testLog -Encoding utf8
    $scenarios = [Collections.Generic.List[object]]::new()
    $summaries = [Collections.Generic.List[object]]::new()
    foreach ($line in $lines) {
        $value = [string]$line
        if (-not $value.TrimStart().StartsWith('{', [StringComparison]::Ordinal)) { continue }
        try { $record = $value | ConvertFrom-Json -ErrorAction Stop }
        catch { throw "Malformed structured result from $name; this is an infrastructure failure. See $testLog" }
        if ($record.summary -eq $true) { $summaries.Add($record) }
        elseif ($null -ne $record.scenario -and $record.passed -is [bool]) { $scenarios.Add($record) }
        else { throw "Unrecognized structured result from $name. See $testLog" }
    }
    $failed = @($scenarios | Where-Object { -not $_.passed })
    $ids = @($scenarios | ForEach-Object scenario)
    if ($summaries.Count -ne 1 -or $scenarios.Count -eq 0 -or ($ids | Sort-Object -Unique).Count -ne $scenarios.Count -or
        $summaries[0].scenarios -ne $scenarios.Count -or $summaries[0].failed -ne $failed.Count -or
        $summaries[0].passed -ne ($scenarios.Count - $failed.Count)) {
        throw "Incomplete or inconsistent scenario results from $name; not a detected regression. See $testLog"
    }
    $expectedExit = if ($failed.Count -eq 0) { 0 } else { 1 }
    if ($testExit -ne $expectedExit) { throw "Unexpected process exit $testExit from $name (expected $expectedExit). See $testLog" }
    if ([string]::IsNullOrEmpty($expectedFailure)) {
        $results.Add([pscustomobject]@{ case = $name; outcome = if ($failed.Count) { 'baseline-failed' } else { 'passed' }; scenarios = $scenarios.Count; failed = @($failed | ForEach-Object { $_.scenario }); log = $testLog })
        if ($failed.Count -ne 0) { throw "Unmodified firing-loop scenarios failed: $($failed.scenario -join ', '). See $testLog" }
        Write-Host "PASS: $($scenarios.Count) unmodified production firing-loop scenarios (engine boundaries simulated)."
    }
    else {
        $target = @($scenarios | Where-Object { $_.scenario -ceq $expectedFailure })
        if ($target.Count -ne 1) { throw "Required regression scenario '$expectedFailure' missing from $name. See $testLog" }
        if ($target[0].passed) {
            $results.Add([pscustomobject]@{ case = $name; outcome = 'survived'; expectedFailure = $expectedFailure; failed = @($failed | ForEach-Object { $_.scenario }); log = $testLog })
            throw "Historical regression $name survived its required scenario '$expectedFailure'. Other scenario failures do not substitute for this detection. See $testLog"
        }
        $results.Add([pscustomobject]@{ case = $name; outcome = 'killed'; expectedFailure = $expectedFailure; buildExit = $buildExit; testExit = $testExit; failed = @($failed | ForEach-Object { $_.scenario }); log = $testLog })
        Write-Host "KILLED: $name compiled and failed behavioral scenario '$expectedFailure'."
    }
}

try {
    Run-Case 'baseline' $gameplayPath
    if ($VerifyMutations) {
        $original = [IO.File]::ReadAllText($gameplayPath)
        $mutations = @(
            @{ Name = 'bipod-toggle-ignored'; Scenario = 'bipod-toggle-cancels-setup';
                Anchor = 'NativeBipods.TryToggle(pawn, out message)'; Replacement = 'false /* mutation: ignore G */' },
            @{ Name = 'stale-release-latch'; Scenario = 'stale-auto-recovery';
                Anchor = 'suppressNextFireEdge = true;'; Replacement = 'requireFireRelease = true;' },
            @{ Name = 'reload-resumes-stale-attack'; Scenario = 'reload-resume-ownership';
                Anchor = 'resumeCurrent = false;'; Replacement = 'resumeCurrent = true;' },
            @{ Name = 'successful-cleanup-resets-repeat'; Scenario = 'successful-repeat-cycle';
                Anchor = 'NativeAttackLifecycle.ShouldResetOnCleanup(condition == JobCondition.Succeeded,';
                Replacement = 'NativeAttackLifecycle.ShouldResetOnCleanup(false,' },
            @{ Name = 'auto-requires-another-press'; Scenario = 'held-auto-next-burst';
                Anchor = 'NativeTriggerPolicy.ShouldStart(input.fire, pendingFire, NativeFireModes.RequiresTriggerPress(pawn))';
                Replacement = 'pendingFire' },
            @{ Name = 'exit-leaves-native-burst-running'; Scenario = 'exit-mid-burst';
                Anchor = 'verb.Reset();'; Replacement = '; // mutation: leave active burst running' },
            @{ Name = 'movement-retains-reload-click'; Scenario = 'movement-during-reload-cancels-click';
                Anchor = 'if (movementBlocksFire || (moving && CombatAdapter.IsReloading(pawn))) pendingFire = false;'; Replacement = '; // mutation: retain queued click during reload movement' },
            @{ Name = 'ordered-attack-left-queued'; Scenario = 'deferred-attack-manual-reload';
                Anchor = 'bool deferred = sameAllocation && !started && actor.jobs.jobQueue.Contains(job);';
                Replacement = 'bool deferred = false; // mutation: abandon the accepted queued attack' }
        )
        foreach ($mutation in $mutations) {
            $mutated = Replace-ExactlyOnce $original $mutation.Anchor $mutation.Replacement
            $sourceFolder = Join-Path $runRoot ('temporary-source/' + $mutation.Name)
            New-Item -ItemType Directory -Force -Path $sourceFolder | Out-Null
            $sourcePath = Join-Path $sourceFolder 'Gameplay.cs'
            [IO.File]::WriteAllText($sourcePath, $mutated, $utf8)
            $temporarySources.Add($sourcePath)
            Run-Case $mutation.Name $sourcePath $mutation.Scenario
        }
    }
}
finally {
    $report = Join-Path $runRoot 'results.json'
    ConvertTo-Json -InputObject @($results.ToArray()) -Depth 5 | Set-Content -LiteralPath $report -Encoding utf8
    Write-Host "Firing-loop evidence: $report"
    # Only unique source copies created by this invocation are removed. Logs and
    # isolated build outputs remain available for diagnosing a failed scenario.
    $scope = [IO.Path]::GetFullPath($runRoot).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    foreach ($sourcePath in $temporarySources) {
        $absolute = [IO.Path]::GetFullPath($sourcePath)
        if (-not $absolute.StartsWith($scope, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe temporary source cleanup path.' }
        if (Test-Path -LiteralPath $absolute) { Remove-Item -LiteralPath $absolute -Force }
    }
}
# A killed mutant deliberately exits 1. Report the runner's success to callers
# instead of leaking the final child process exit code through LASTEXITCODE.
exit 0
