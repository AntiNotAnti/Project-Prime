# Opt-in diagnostics for a Windows Project Prime release.
# Full memory dumps can be large and can contain sensitive session information.
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Executable = '.\ProjectPrime.exe',
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$GameArguments = @()
)

$ErrorActionPreference = 'Stop'
$executablePath = (Resolve-Path -LiteralPath $Executable -ErrorAction Stop).ProviderPath
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Project Prime executable not found: $executablePath"
}
$installation = Split-Path -Parent $executablePath
$logDirectory = Join-Path $installation 'logs'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null

# These must be inherited by the game process before .NET starts.
$env:DOTNET_DbgEnableMiniDump = '1'
$env:DOTNET_DbgMiniDumpType = '4'
$env:DOTNET_DbgMiniDumpName = Join-Path $logDirectory 'crash-%p.dmp'

Write-Host "Crash dump capture enabled: $($env:DOTNET_DbgMiniDumpName)"
$start = @{
    FilePath = $executablePath
    WorkingDirectory = $installation
    PassThru = $true
    Wait = $true
}
if ($GameArguments.Count -gt 0) {
    $start.ArgumentList = $GameArguments
}
$game = Start-Process @start
Write-Host "Project Prime exited with code $($game.ExitCode)"
