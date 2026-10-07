$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-NativeRendererCheck.ps1')
$directory = Join-Path ([System.IO.Path]::GetTempPath()) ('prime-renderer-child-' + [guid]::NewGuid().ToString('N'))
[void] (New-Item -ItemType Directory -Path $directory)
$executable = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
$sentinel = $null
$checks = 0
function Check([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
    Write-Host "NATIVE CHILD FIXTURE PASS $Message"
}
try {
    $fixture = Join-Path $directory 'fixture.ps1'
    Set-Content -LiteralPath $fixture -Value 'Write-Output "fixture output"; [Console]::Error.WriteLine("fixture error"); exit 0'
    $stdout = Join-Path $directory 'positive.stdout.log'
    $stderr = Join-Path $directory 'positive.stderr.log'
    $result = Invoke-NativeRendererCheck -FilePath $executable -ArgumentList @('-NoLogo', '-NoProfile', '-File', $fixture) `
        -StdoutPath $stdout -StderrPath $stderr -TimeoutSeconds 15
    Check ($result.ExitCode -eq 0) 'actual zero child exit preserved'
    Check ((Get-Content $stdout -Raw).Contains('fixture output') -and (Get-Content $stderr -Raw).Contains('fixture error')) 'actual stdout and stderr retained'

    Set-Content -LiteralPath $fixture -Value 'exit 7'
    $result = Invoke-NativeRendererCheck -FilePath $executable -ArgumentList @('-NoLogo', '-NoProfile', '-File', $fixture) `
        -StdoutPath (Join-Path $directory 'negative.stdout.log') -StderrPath (Join-Path $directory 'negative.stderr.log') -TimeoutSeconds 15
    Check ($result.ExitCode -eq 7) 'actual nonzero child exit preserved'

    $sibling = Join-Path $directory 'sibling.ps1'
    Set-Content -LiteralPath $sibling -Value 'Start-Sleep -Seconds 60'
    $sentinel = Start-Process -FilePath $executable -ArgumentList @('-NoLogo', '-NoProfile', '-File', $sibling) -PassThru -NoNewWindow
    Set-Content -LiteralPath $fixture -Value 'Write-Output "before owned timeout"; Start-Sleep -Seconds 60'
    $caught = $null
    try {
        Invoke-NativeRendererCheck -FilePath $executable -ArgumentList @('-NoLogo', '-NoProfile', '-File', $fixture) `
            -StdoutPath (Join-Path $directory 'timeout.stdout.log') -StderrPath (Join-Path $directory 'timeout.stderr.log') -TimeoutSeconds 2 -ProgressSeconds 1
    } catch [System.TimeoutException] { $caught = $_.Exception }
    Check ($null -ne $caught -and $caught.Data['OwnedProcessExited'] -eq $true) 'deadline throws after owned child termination and reap'
    Check ((Get-Content (Join-Path $directory 'timeout.stdout.log') -Raw).Contains('before owned timeout')) 'timeout preserves actual pre-exit diagnostic file'
    Check (-not $sentinel.HasExited) 'timeout leaves separately owned sibling process untouched'

    # A real child must also be reaped if diagnostic output itself fails.
    $script:abnormalPid = 0
    function Write-Host([string] $Object) {
        if ($Object -match 'ownedPid=(\d+)') { $script:abnormalPid = [int] $Matches[1] }
        throw 'fixture deliberate diagnostic output failure'
    }
    $abnormalFailure = $null
    try {
        Invoke-NativeRendererCheck -FilePath $executable -ArgumentList @('-NoLogo', '-NoProfile', '-File', $fixture) `
            -StdoutPath (Join-Path $directory 'abnormal.stdout.log') -StderrPath (Join-Path $directory 'abnormal.stderr.log') -TimeoutSeconds 15
    } catch { $abnormalFailure = $_.Exception }
    finally { Remove-Item Function:\Write-Host }
    Check ($null -ne $abnormalFailure -and $abnormalFailure.Message -eq 'fixture deliberate diagnostic output failure') 'unexpected output failure is preserved'
    $remaining = if ($script:abnormalPid -gt 0) { Get-Process -Id $script:abnormalPid -ErrorAction SilentlyContinue } else { $null }
    Check ($script:abnormalPid -gt 0 -and $null -eq $remaining -and -not $sentinel.HasExited) 'unexpected output failure reaps only its owned child'
    Write-Host "NATIVE CHILD FIXTURE $checks checks passed. No GPU runtime acceptance is inferred."
} finally {
    if ($null -ne $sentinel) {
        if (-not $sentinel.HasExited) {
            $sentinel.Kill($true)
            if (-not $sentinel.WaitForExit(10000)) { throw 'Fixture sibling cleanup failed.' }
        }
        $sentinel.Dispose()
    }
    Remove-Item -LiteralPath $directory -Recurse -Force
}
