# Keep CI's actual native process exit and full pixel assertions authoritative.
# Diagnostic tails are prefixed so they cannot be counted as completed phases.
function Invoke-NativeRendererCheck {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $ArgumentList,
        [Parameter(Mandatory)] [string] $StdoutPath,
        [Parameter(Mandatory)] [string] $StderrPath,
        [ValidateRange(1, 86400)] [int] $TimeoutSeconds = 1200,
        [ValidateRange(1, 1200)] [int] $ProgressSeconds = 30
    )

    function Write-DiagnosticTail([string] $Path, [string] $StreamName) {
        if (-not (Test-Path -LiteralPath $Path)) { return }
        try {
            foreach ($line in @(Get-Content -LiteralPath $Path -Tail 4 -ErrorAction Stop)) {
                $bounded = if ($line.Length -gt 1024) { $line.Substring(0, 1024) + ' [tail truncated; raw file retained]' } else { $line }
                Write-Host "DIAGNOSTIC native-child $StreamName tail: $bounded"
            }
        } catch {
            Write-Host "DIAGNOSTIC native-child $StreamName tail unavailable: $($_.Exception.Message)"
        }
    }

    function Write-CleanupDiagnostic([string] $Message) {
        # Host output can be the original failure; cleanup must still proceed.
        try { Write-Host $Message } catch { }
    }

    $child = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -PassThru -NoNewWindow `
        -RedirectStandardOutput $StdoutPath -RedirectStandardError $StderrPath
    try {
        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        $nextProgress = [double] $ProgressSeconds
        Write-Host "DIAGNOSTIC native-child started ownedPid=$($child.Id) deadlineSeconds=$TimeoutSeconds"
        while (-not $child.WaitForExit(200)) {
            if ($clock.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
                Write-Host "DIAGNOSTIC native-child deadline expired ownedPid=$($child.Id) elapsedSeconds=$([math]::Round($clock.Elapsed.TotalSeconds, 2))"
                # The Process handle belongs to this invocation. Never search
                # for or kill other Game/Studio/runner processes by name.
                if (-not $child.HasExited) {
                    try { $child.Kill($true) }
                    catch [System.InvalidOperationException] {
                        if (-not $child.HasExited) { throw }
                    }
                }
                if (-not $child.WaitForExit(10000)) {
                    throw [System.InvalidOperationException]::new("Owned native child $($child.Id) did not exit after timeout termination.")
                }
                Write-Host "DIAGNOSTIC native-child timed out ownedPid=$($child.Id) reaped=true actualExitCode=$($child.ExitCode)"
                Write-DiagnosticTail $StdoutPath 'stdout'
                Write-DiagnosticTail $StderrPath 'stderr'
                $deadlineFailure = [System.TimeoutException]::new("Native renderer deadline expired after $TimeoutSeconds seconds; owned PID $($child.Id) terminated and reaped; actual exit $($child.ExitCode). Raw output files are retained.")
                $deadlineFailure.Data['OwnedProcessId'] = $child.Id
                $deadlineFailure.Data['OwnedProcessExited'] = $child.HasExited
                $deadlineFailure.Data['ActualExitCode'] = $child.ExitCode
                throw $deadlineFailure
            }
            if ($clock.Elapsed.TotalSeconds -ge $nextProgress) {
                Write-Host "DIAGNOSTIC native-child running ownedPid=$($child.Id) elapsedSeconds=$([math]::Round($clock.Elapsed.TotalSeconds, 2))"
                Write-DiagnosticTail $StdoutPath 'stdout'
                Write-DiagnosticTail $StderrPath 'stderr'
                $nextProgress = $clock.Elapsed.TotalSeconds + $ProgressSeconds
            }
        }
        # Returning only an exit record keeps diagnostic text out of the
        # caller's result and releases our process handle deterministically.
        return [pscustomobject]@{
            Id = $child.Id
            ExitCode = $child.ExitCode
            ElapsedSeconds = $clock.Elapsed.TotalSeconds
        }
    } finally {
        # An unexpected wait/output exception must not leave this invocation's
        # native child alive. Cleanup diagnostics must preserve that exception.
        try {
            if (-not $child.HasExited) {
                try { $child.Kill($true) }
                catch [System.InvalidOperationException] {
                    if (-not $child.HasExited) { throw }
                }
                if (-not $child.WaitForExit(10000)) {
                    Write-CleanupDiagnostic "DIAGNOSTIC native-child abnormal cleanup did not reap ownedPid=$($child.Id) within 10 seconds"
                } else {
                    Write-CleanupDiagnostic "DIAGNOSTIC native-child abnormal cleanup reaped ownedPid=$($child.Id) actualExitCode=$($child.ExitCode)"
                }
            }
        } catch {
            Write-CleanupDiagnostic "DIAGNOSTIC native-child abnormal cleanup failed ownedPid=$($child.Id): $($_.Exception.Message)"
        } finally {
            $child.Dispose()
        }
    }
}
