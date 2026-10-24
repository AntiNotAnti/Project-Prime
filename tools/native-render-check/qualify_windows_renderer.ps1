param(
    [Parameter(Mandatory)][string]$DotNet,
    [Parameter(Mandatory)][string]$FrozenBuild,
    [Parameter(Mandatory)][string]$UserData,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('Matrix', 'Soak', 'Scale')][string]$Mode = 'Matrix',
    [string]$Python = 'python',
    [string]$SourceSnapshot,
    [string]$BuildBinding
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
function Test-FiniteNumber($Value, [bool]$Positive = $false) {
    if ($null -eq $Value -or $Value -is [bool] -or $Value -is [string]) { return $false }
    try { $number = [double]$Value } catch { return $false }
    return [double]::IsFinite($number) -and $(if ($Positive) {$number -gt 0} else {$number -ge 0})
}
if (-not $IsWindows) { throw 'This qualification recipe executes only on Windows.' }
$binary = Join-Path (Resolve-Path -LiteralPath $FrozenBuild).Path 'native-render-check.dll'
$client = Join-Path (Split-Path -Parent $binary) 'ProjectPrime.dll'
if (-not (Test-Path -LiteralPath $binary)) { throw 'Frozen harness is missing.' }
if (-not (Test-Path -LiteralPath $client)) { throw 'Frozen client is missing.' }
$DotNet = (Get-Command $DotNet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$Python = (Get-Command $Python -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
foreach ($profile in @('PROJECT_PRIME_GL_PROFILE', 'PROJECT_PRIME_GL_GPU_TIMER')) {
    if ([Environment]::GetEnvironmentVariable($profile) -eq '1') { throw "Disable $profile for the declared unprofiled campaign." }
}
if ($BuildBinding -and -not $SourceSnapshot) { throw 'BuildBinding requires SourceSnapshot.' }
foreach ($metadata in @($SourceSnapshot, $BuildBinding)) {
    if ($metadata -and -not (Test-Path -LiteralPath $metadata -PathType Leaf)) { throw "Missing build metadata: $metadata" }
}
if (-not (Test-Path -LiteralPath (Join-Path $UserData 'paths.txt'))) { throw 'Fixture paths.txt is missing.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a fresh output directory; failed evidence must not be overwritten.' }
$null = New-Item -ItemType Directory -Path $OutputDirectory
$destination = (Resolve-Path -LiteralPath $OutputDirectory).Path
$hashes = @{client = (Get-FileHash -LiteralPath $client -Algorithm SHA256).Hash.ToLowerInvariant();
    harness = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant()}
$binding = if ($BuildBinding) { Get-Content -LiteralPath $BuildBinding -Raw | ConvertFrom-Json } else { $null }
if ($binding -and ($binding.clientSha256 -ne $hashes.client -or $binding.harnessSha256 -ne $hashes.harness `
    -or $binding.sourceSnapshotSha256 -ne (Get-FileHash -LiteralPath $SourceSnapshot -Algorithm SHA256).Hash)) {
    'Explicit source/build digest mismatch; no native run started.' | Set-Content -LiteralPath (Join-Path $destination 'preflight-error.log')
    throw 'Explicit source/build metadata does not match the frozen binary/source-snapshot digests.'
}
if ($binding -and $binding.PSObject.Properties.Name -contains 'buildInputMatch' `
    -and ($binding.buildInputMatch -isnot [bool] -or $binding.buildInputMatch -ne $true)) {
    'Explicit metadata reports changed/unverified build inputs; no native run started.' | Set-Content -LiteralPath (Join-Path $destination 'preflight-error.log')
    throw 'Build metadata does not confirm unchanged build inputs.'
}
@{schema = 1; mode = $Mode; frozenHashes = $hashes; dotnet = $DotNet; python = $Python;
    sourceSnapshot = $SourceSnapshot; buildBinding = $binding;
    sourceAttestation = $(if ($binding) {'Explicit supplied metadata digest-bound; no provenance manufactured'} else {'UNVERIFIED: no build binding supplied'});
    cadenceGate = 'Numeric mean within 2%; p99 <= 2x budget; zero simulation drops/stalls; complete, immutable, matching conditions';
    durationGate = 'Matrix/Soak: requested seconds minus 4 warmup seconds; 0.1 second endpoint tolerance; native interval sum agrees with measurement; raw Present intervals cover measurement and do not exceed wall duration. Scale capture has no wall-duration qualification.';
    contention = 'Operator must keep builds/tests idle; this script does not prove machine exclusivity'} |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'preflight.json') -Encoding utf8
& $DotNet --info *> (Join-Path $destination 'dotnet-info.log')
if ($LASTEXITCODE -ne 0) { throw 'The selected dotnet executable did not run; see dotnet-info.log.' }
& $Python --version *> (Join-Path $destination 'python-version.log')
if ($LASTEXITCODE -ne 0) { throw 'The selected Python executable did not run; see python-version.log.' }
$savedEnvironment = @{}
foreach ($name in @('PROJECT_PRIME_USER_DATA', 'PROJECT_PRIME_GL_FRAME_CAPTURE', 'PROJECT_PRIME_GL_FRAME_CAPACITY')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}
try {
$env:PROJECT_PRIME_USER_DATA = (Resolve-Path -LiteralPath $UserData).Path
$analyzer = Join-Path $PSScriptRoot '../native-fps/analyze.py'
$summary = [Collections.Generic.List[object]]::new()
$failed = $false
$runs = if ($Mode -eq 'Matrix') {
    @(60, 120, 144, 240, 0, -1) | ForEach-Object {
        @{name = "cap-$_"; cap = $_; seconds = 24; scale = 100; shadows = 'Off'}
    }
} elseif ($Mode -eq 'Soak') {
    @(@{name = 'combat-30min'; cap = 240; seconds = 1804; scale = 100; shadows = 'Off'})
} else {
    foreach ($scale in @(50, 100, 150, 200)) {
        foreach ($shadow in @('Off', 'Low', 'High')) {
            @{name = "scale-$scale-shadow-$shadow"; cap = 144; seconds = 8; scale = $scale; shadows = $shadow}
        }
    }
}
foreach ($run in $runs) {
    $prefix = Join-Path $destination $run.name
    $env:PROJECT_PRIME_GL_FRAME_CAPTURE = "$prefix-frames.json"
    # Include the first measured boundary and headroom; Unlimited uses the
    # harness's explicit 4096 returned-frames/sec storage envelope, not a cap.
    $required = [Math]::Ceiling(($run.seconds + 1) * $(if ($run.cap -gt 0) {$run.cap} else {4096}))
    $capacity = 1
    while ($capacity -lt $required) { $capacity *= 2 }
    if ($capacity -gt 1048576) { throw 'Requested run exceeds bounded raw storage; choose complete harness intervals instead.' }
    $env:PROJECT_PRIME_GL_FRAME_CAPACITY = "$capacity"
    $arguments = @($binary, '-room', 'MP3 PROVING GROUND', '-players', '8', '-size', '1280x720',
        '-seconds', "$($run.seconds)", '-hz', "$($run.cap)", '-scale', "$($run.scale)",
        '-shadows', $run.shadows, '-output', "$prefix-result.json")
    if ($Mode -eq 'Scale') { $arguments += @('-shots', "$prefix-shots") }
    $flags = @{}
    Get-ChildItem Env: | Where-Object Name -Like 'PROJECT_PRIME_GL_*' | ForEach-Object { $flags[$_.Name] = $_.Value }
    @{dotnet = $DotNet; arguments = $arguments; graphicsEnvironment = $flags;
        captureRun = $Mode -eq 'Scale'; benchmarkEligible = 'UNVERIFIED: inspect workload, contention and gates';
        contention = 'Operator must keep builds/tests idle during timing'} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$prefix-command.json" -Encoding utf8
    $nativeExit = $null; $analysisExit = $null; $receipt = $null; $report = $null; $runError = $null
    try {
        & $DotNet @arguments *> "$prefix.log"
        $nativeExit = $LASTEXITCODE
        if (Test-Path -LiteralPath $env:PROJECT_PRIME_GL_FRAME_CAPTURE) {
            & $Python $analyzer $env:PROJECT_PRIME_GL_FRAME_CAPTURE -o "$prefix-report.json" *> "$prefix-analysis.log"
            $analysisExit = $LASTEXITCODE
        }
        if (Test-Path -LiteralPath "$prefix-result.json") { $receipt = Get-Content -LiteralPath "$prefix-result.json" -Raw | ConvertFrom-Json }
        if (Test-Path -LiteralPath "$prefix-report.json") { $report = Get-Content -LiteralPath "$prefix-report.json" -Raw | ConvertFrom-Json }
    } catch { $runError = $_.Exception.Message; $_ | Out-String | Set-Content -LiteralPath "$prefix-error.log" }
    $immutable = (Get-FileHash -LiteralPath $client -Algorithm SHA256).Hash -eq $hashes.client `
        -and (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash -eq $hashes.harness
    $identityMatches = $receipt.observedWorkload.clientBinary.sha256 -eq $hashes.client `
        -and $receipt.observedWorkload.harnessBinary.sha256 -eq $hashes.harness `
        -and $report.loadedBinaryIdentity.assemblySha256 -eq $hashes.client `
        -and $report.loadedBinaryIdentity.assemblyMvid -eq $receipt.assemblyMvid
    $expectedVisibilityTransitions = if ($receipt.initialWindow.visible -eq $false -and $receipt.finalWindow.visible -eq $true) {1} else {0}
    $conditionsMatch = $receipt.effectiveCap -eq $run.cap -and $receipt.resolutionScale -eq $run.scale `
        -and $receipt.shadows -eq $run.shadows -and ($receipt.framebuffer -join ',') -eq '1280,720' `
        -and $receipt.focusTransitions -eq 0 -and $receipt.windowStateTransitions -eq 0 `
        -and $receipt.visibilityTransitions -eq $expectedVisibilityTransitions `
        -and $receipt.room -eq 'MP3 PROVING GROUND' -and $receipt.observedWorkload.startRoster.Count -eq 8 `
        -and $receipt.softwareFrequency -eq $(if ($run.cap -gt 0) {$run.cap} else {0}) `
        -and $report.activeCap -eq $run.cap `
        -and $report.activeVsync -eq $receipt.vsync `
        -and $receipt.vsync -eq $(if ($run.cap -eq 0) {'On'} else {'Off'})
    $numericFieldsValid = (Test-FiniteNumber $receipt.meanCompletedFrameFps $true) `
        -and (Test-FiniteNumber $receipt.frameIntervalP99Ms $true) `
        -and (Test-FiniteNumber $receipt.measurementSeconds $true) `
        -and (Test-FiniteNumber $receipt.wallSeconds $true) `
        -and (Test-FiniteNumber $report.meanCompletedFrameFps $true) `
        -and (Test-FiniteNumber $report.frameIntervalP99Ms $true)
    $nativeSeconds = 0.0; $nativeIntervalsValid = $receipt.frameIntervalsMs.Count -gt 0
    foreach ($interval in $receipt.frameIntervalsMs) {
        if (-not (Test-FiniteNumber $interval $true)) { $nativeIntervalsValid = $false; break }
        $nativeSeconds += [double]$interval / 1000.0
    }
    $rawSeconds = 0.0
    foreach ($interval in $report.chronologicalIntervalsMs) { $rawSeconds += [double]$interval / 1000.0 }
    $rawCoverageFraction = if (Test-FiniteNumber $receipt.measurementSeconds $true) { $rawSeconds / $receipt.measurementSeconds } else { $null }
    $requestedMeasuredSeconds = $run.seconds - 4
    $durationMatch = $numericFieldsValid -and $nativeIntervalsValid `
        -and $receipt.samples -eq $receipt.frameIntervalsMs.Count `
        -and [Math]::Abs($nativeSeconds - $receipt.measurementSeconds) -le 0.1 `
        -and $receipt.wallSeconds -ge $receipt.measurementSeconds
    if ($Mode -ne 'Scale') {
        # Present-return intervals and callback-return intervals have different
        # endpoints. Permit 100 ms total boundary discrepancy, never a fraction
        # of a long soak or an ignored missing part of the measured window.
        $durationMatch = $durationMatch -and $receipt.measurementSeconds -ge ($requestedMeasuredSeconds - 0.1) `
            -and $rawSeconds -ge ($receipt.measurementSeconds - 0.1) `
            -and $rawSeconds -le ($receipt.wallSeconds + 0.1)
    }
    $gate = $nativeExit -eq 0 -and $analysisExit -eq 0 -and $receipt.complete -eq $true `
        -and $receipt.timingEvidenceComplete -eq $true -and $receipt.launcherStartupScheduled -eq $false `
        -and $immutable -and $identityMatches -and $conditionsMatch -and $durationMatch -and -not $runError
    if ($Mode -ne 'Scale') {
        $gate = $gate -and $receipt.droppedSimulationSteps -eq 0 -and $receipt.stalls -eq 0
    }
    if ($run.cap -gt 0 -and $Mode -ne 'Scale') {
        $gate = $gate -and [Math]::Abs($receipt.meanCompletedFrameFps / $run.cap - 1) -le 0.02 `
            -and $receipt.frameIntervalP99Ms -le 2000.0 / $run.cap
    }
    $item = [ordered]@{name = $run.name; mode = $Mode; nativeExit = $nativeExit; analysisExit = $analysisExit;
        gate = $(if ($Mode -eq 'Scale' -and $gate) {'CAPTURE_PASS'} elseif ($gate) {'PASS'} else {'FAIL'});
        functionalComplete = $receipt.complete -eq $true; immutable = $immutable; identityMatches = $identityMatches;
        conditionsMatch = $conditionsMatch; error = $runError; meanFps = $receipt.meanCompletedFrameFps; p99Ms = $receipt.frameIntervalP99Ms;
        numericFieldsValid = $numericFieldsValid; durationMatch = $durationMatch; requestedMeasuredSeconds = $requestedMeasuredSeconds;
        nativeIntervalSeconds = $nativeSeconds; rawPresentSeconds = $rawSeconds; rawCoverageFraction = $rawCoverageFraction;
        drops = $receipt.droppedSimulationSteps; stalls = $receipt.stalls; samples = $receipt.samples;
        measurementSeconds = $receipt.measurementSeconds; wallSeconds = $receipt.wallSeconds;
        complete = $receipt.complete; storageComplete = $receipt.timingEvidenceComplete;
        launcherStartupScheduled = $receipt.launcherStartupScheduled; mvid = $receipt.assemblyMvid;
        framebuffer = $receipt.framebuffer; renderSize = $receipt.renderSize; requestedRenderSize = $receipt.requestedRenderSize}
    $summary.Add($item)
    if (-not $gate) { $failed = $true }
    $summary.ToArray() | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'summary.json') -Encoding utf8
    $item | ConvertTo-Json -Compress -Depth 5
}
if ($failed) { exit 1 }
exit 0
} finally {
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
}
