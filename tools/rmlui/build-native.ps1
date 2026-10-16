param(
    [ValidateSet('win-x64')][string]$Target = 'win-x64',
    [ValidateSet('gl2', 'draw-list')][string]$Renderer = 'gl2',
    [string]$BashPath
)
$ErrorActionPreference = 'Stop'
# Use Git for Windows' Bash, rather than the unrelated Windows/WSL launcher.
if (-not $BashPath) {
    $git = (Get-Command git.exe -ErrorAction Stop).Source
    $gitRoot = Split-Path (Split-Path $git -Parent) -Parent
    $candidates = @(
        (Join-Path $gitRoot 'bin/bash.exe'),
        (Join-Path $gitRoot 'usr/bin/bash.exe'),
        (Join-Path $env:ProgramFiles 'Git/bin/bash.exe')
    )
    $BashPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $BashPath -or -not (Test-Path $BashPath)) {
    throw 'Git for Windows Bash was not found. Install Git or pass -BashPath to its bash.exe.'
}
$script = Join-Path $PSScriptRoot 'build-native.sh'
& $BashPath $script $Target $Renderer
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
