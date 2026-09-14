[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('menu', 'game')]
    [string]$Mode = 'menu',

    [switch]$NoUpdate,

    [switch]$ForcePaths
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$DataDirectory = Join-Path $Root 'AMHE1'
$GameProject = Join-Path (Join-Path (Join-Path $Root 'src') 'Client') 'Client.csproj'
$IsWindowsHost = $env:OS -eq 'Windows_NT'

function Get-DotnetPath {
    $command = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        return $null
    }
    return $command.Source
}

function Get-PublishedCandidates {
    if ($IsWindowsHost) {
        return @(
            (Join-Path $Root 'ProjectPrime.exe'),
            (Join-Path (Join-Path (Join-Path $Root 'publish') 'win-x64') 'ProjectPrime.exe')
        )
    }
    return @(
        (Join-Path $Root 'ProjectPrime'),
        (Join-Path (Join-Path (Join-Path $Root 'publish') 'linux-x64') 'ProjectPrime')
    )
}

function New-DevelopmentSpec {
    $dotnet = Get-DotnetPath
    if ($null -eq $dotnet) {
        throw 'No published game executable was found, and dotnet is not installed.'
    }
    if (-not (Test-Path -LiteralPath $GameProject)) {
        throw "Could not find the project at $GameProject."
    }

    $output = Join-Path $Root (Join-Path '.project-prime-launcher' 'game')
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    $runtimeIdentifier = if ($IsWindowsHost) { 'win-x64' } else { $null }

    Write-Host "No published game binary found; building it into $output..." -ForegroundColor Yellow
    Push-Location $Root
    try {
        $buildArguments = @('build', $GameProject, '-c', 'Release', '-o', $output)
        if ($null -ne $runtimeIdentifier) {
            $buildArguments += @('-r', $runtimeIdentifier)
        }
        & $dotnet @buildArguments | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "The game build failed with exit code $LASTEXITCODE. Install/repair the .NET 10 SDK required by global.json, or place a published Project Prime binary beside this launcher."
        }
    }
    finally {
        Pop-Location
    }

    $nativeBinary = Join-Path $output 'ProjectPrime.exe'
    $assembly = Join-Path $output 'ProjectPrime.dll'
    if ($IsWindowsHost -and (Test-Path -LiteralPath $nativeBinary -PathType Leaf)) {
        $launchFile = $nativeBinary
        $launchPrefix = @()
    }
    else {
        if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
            throw "The build completed but did not produce $assembly."
        }
        $launchFile = $dotnet
        $launchPrefix = @($assembly)
    }
    return [pscustomobject]@{
        FilePath = $launchFile
        Prefix = $launchPrefix
        WorkingDirectory = $output
    }
}

function Get-LaunchSpec {
    foreach ($candidate in (Get-PublishedCandidates)) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return [pscustomobject]@{
                FilePath = $candidate
                Prefix = @()
                WorkingDirectory = Split-Path -Parent $candidate
            }
        }
    }
    return New-DevelopmentSpec
}

function Ensure-GamePaths([string]$baseDirectory) {
    $pathsFile = Join-Path $baseDirectory 'paths.txt'
    $expected = [IO.Path]::GetFullPath($DataDirectory)
    if (Test-Path -LiteralPath $pathsFile -PathType Leaf) {
        $existing = Get-Content -LiteralPath $pathsFile -Raw
        $matchesExpected = $existing -match ('(?im)^AMHE1=' + [regex]::Escape($expected) + '\s*$')
        if ($matchesExpected) {
            return
        }
        if (-not $ForcePaths) {
            Write-Warning "$pathsFile already points somewhere other than AMHE1. Use -ForcePaths to replace it; starting the game as-is."
            return
        }
        $backup = "$pathsFile.before-launch-$(Get-Date -Format 'yyyyMMdd-HHmmss').bak"
        Copy-Item -LiteralPath $pathsFile -Destination $backup -Force
        Write-Host "Backed up the existing paths.txt to $backup" -ForegroundColor Yellow
    }

    # This is the same shape Extract.Setup writes. The version line only needs
    # to be at least the current minimum extraction version.
    $lines = @(
        '0.35.1.0',
        'AMFE0=',
        'AMFP0=',
        'A76E0=',
        'AMHE0=',
        "AMHE1=$expected",
        'AMHP0=',
        'AMHP1=',
        'AMHJ0=',
        'AMHJ1=',
        'AMHK0=',
        'Export='
    )
    [IO.File]::WriteAllLines($pathsFile, $lines)
    Write-Host "Configured game files: AMHE1 -> $expected" -ForegroundColor Green
}

function Invoke-ProjectPrime([pscustomobject]$spec, [string[]]$arguments) {
    $allArguments = @()
    if ($spec.Prefix.Count -gt 0) {
        $allArguments += $spec.Prefix
    }
    $allArguments += $arguments

    Write-Host "Starting $($spec.FilePath) $($allArguments -join ' ')" -ForegroundColor Cyan
    Push-Location $spec.WorkingDirectory
    try {
        & $spec.FilePath @allArguments | Out-Host
        return $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}

function Start-Game {
    $spec = Get-LaunchSpec
    Ensure-GamePaths $spec.WorkingDirectory
    $arguments = @('-launcher')
    if ($NoUpdate) {
        $arguments += '-noupdate'
    }
    return Invoke-ProjectPrime $spec $arguments
}

if ($Mode -eq 'menu') {
    Write-Host ''
    Write-Host 'Project Prime launcher' -ForegroundColor Green
    Write-Host '  1. Start game'
    Write-Host '  2. Exit'
    $choice = Read-Host 'Choose an option'
    if ($choice -eq '1') {
        $Mode = 'game'
    }
    else {
        exit 0
    }
}

try {
    $exitCode = switch ($Mode) {
        'game' { Start-Game; break }
        default { 0; break }
    }
    if ($null -eq $exitCode) {
        $exitCode = 0
    }
    exit ([int]$exitCode)
}
catch {
    Write-Error $_
    exit 1
}
