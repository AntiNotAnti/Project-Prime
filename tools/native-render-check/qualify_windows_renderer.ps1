param(
    [Parameter(Mandatory)][string]$DotNet,
    [Parameter(Mandatory)][string]$FrozenBuild,
    [Parameter(Mandatory)][string]$UserData,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('Matrix', 'Soak', 'Scale')][string]$Mode = 'Matrix'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsWindows) { throw 'This qualification recipe executes only on Windows.' }
$binary = Join-Path (Resolve-Path -LiteralPath $FrozenBuild).Path 'native-render-check.dll'
if (-not (Test-Path -LiteralPath $binary)) { throw 'Frozen harness is missing.' }
if (-not (Test-Path -LiteralPath (Join-Path $UserData 'paths.txt'))) { throw 'Fixture paths.txt is missing.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a fresh output directory; failed evidence must not be overwritten.' }
$null = New-Item -ItemType Directory -Path $OutputDirectory
$destination = (Resolve-Path -LiteralPath $OutputDirectory).Path
$env:PROJECT_PRIME_USER_DATA = (Resolve-Path -LiteralPath $UserData).Path
$analyzer = Join-Path $PSScriptRoot '../native-fps/analyze.py'
$summary = [Collections.Generic.List[object]]::new()
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
    $env:PROJECT_PRIME_GL_FRAME_CAPACITY = if ($Mode -eq 'Soak') { '524288' } else { '65536' }
    $arguments = @($binary, '-room', 'MP3 PROVING GROUND', '-players', '8', '-size', '1280x720',
        '-seconds', "$($run.seconds)", '-hz', "$($run.cap)", '-scale', "$($run.scale)",
        '-shadows', $run.shadows, '-output', "$prefix-result.json")
    if ($Mode -eq 'Scale') { $arguments += @('-shots', "$prefix-shots") }
    $flags = @{}
    Get-ChildItem Env: | Where-Object Name -Like 'PROJECT_PRIME_GL_*' | ForEach-Object { $flags[$_.Name] = $_.Value }
    @{dotnet = $DotNet; arguments = $arguments; graphicsEnvironment = $flags;
        captureRun = $Mode -eq 'Scale'; benchmarkEligible = $Mode -ne 'Scale';
        contention = 'Operator must keep builds/tests idle during timing'} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$prefix-command.json" -Encoding utf8
    & $DotNet @arguments *> "$prefix.log"
    $nativeExit = $LASTEXITCODE
    $analysisExit = $null
    if (Test-Path -LiteralPath $env:PROJECT_PRIME_GL_FRAME_CAPTURE) {
        python $analyzer $env:PROJECT_PRIME_GL_FRAME_CAPTURE -o "$prefix-report.json" *> "$prefix-analysis.log"
        $analysisExit = $LASTEXITCODE
    }
    $receipt = if (Test-Path -LiteralPath "$prefix-result.json") {
        Get-Content -LiteralPath "$prefix-result.json" -Raw | ConvertFrom-Json
    } else { $null }
    $gate = $nativeExit -eq 0 -and $analysisExit -eq 0 -and $receipt.complete -eq $true `
        -and $receipt.timingEvidenceComplete -eq $true -and $receipt.launcherStartupScheduled -eq $false
    if ($Mode -ne 'Scale') {
        $gate = $gate -and $receipt.droppedSimulationSteps -eq 0 -and $receipt.stalls -eq 0
    }
    if ($run.cap -gt 0 -and $Mode -ne 'Scale') {
        $gate = $gate -and [Math]::Abs($receipt.meanCompletedFrameFps / $run.cap - 1) -le 0.02 `
            -and $receipt.frameIntervalP99Ms -le 2000.0 / $run.cap
    }
    $item = [ordered]@{name = $run.name; mode = $Mode; nativeExit = $nativeExit; analysisExit = $analysisExit;
        gate = $(if ($Mode -eq 'Scale' -and $gate) {'CAPTURE_PASS'} elseif ($gate) {'PASS'} else {'FAIL'});
        functionalComplete = $gate; meanFps = $receipt.meanCompletedFrameFps; p99Ms = $receipt.frameIntervalP99Ms;
        drops = $receipt.droppedSimulationSteps; stalls = $receipt.stalls; samples = $receipt.samples;
        measurementSeconds = $receipt.measurementSeconds; wallSeconds = $receipt.wallSeconds;
        complete = $receipt.complete; storageComplete = $receipt.timingEvidenceComplete;
        launcherStartupScheduled = $receipt.launcherStartupScheduled; mvid = $receipt.assemblyMvid;
        framebuffer = $receipt.framebuffer; renderSize = $receipt.renderSize; requestedRenderSize = $receipt.requestedRenderSize}
    $summary.Add($item)
    $summary.ToArray() | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'summary.json') -Encoding utf8
    $item | ConvertTo-Json -Compress -Depth 5
}
