[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('menu', 'game', 'server', 'directory')]
    [string]$Mode = 'menu',

    [ValidateRange(0, 65535)]
    [int]$ServerPort = 27888,

    [ValidateRange(0, 65535)]
    [int]$DirectoryPort = 27889,

    [ValidateRange(2, 8)]
    [int]$Players = 8,

    [string]$ServerName = 'Project Prime',

    [string]$Master = '',

    [string]$HostPorts = 'none',

    [string]$PublicAddress = '',

    [switch]$NoUpdate,

    [switch]$ForcePaths
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$DataDirectory = Join-Path $Root 'AMHE1'
$GameProject = Join-Path (Join-Path (Join-Path $Root 'src') 'Client') 'Client.csproj'
$ServerProject = Join-Path (Join-Path (Join-Path $Root 'src') 'Server') 'Server.csproj'
$IsWindowsHost = $env:OS -eq 'Windows_NT'

function Assert-GameData {
    if (-not (Test-Path -LiteralPath $DataDirectory -PathType Container)) {
        throw "Missing $DataDirectory. Put the extracted AMHE1 folder beside this launcher."
    }

    $requiredPaths = @(
        [pscustomobject]@{ Path = (Join-Path (Join-Path $DataDirectory '_bin') 'arm9.bin'); Name = '_bin\arm9.bin' },
        [pscustomobject]@{ Path = (Join-Path $DataDirectory 'models'); Name = 'models' },
        [pscustomobject]@{ Path = (Join-Path $DataDirectory 'levels'); Name = 'levels' }
    )
    foreach ($required in $requiredPaths) {
        if (-not (Test-Path -LiteralPath $required.Path)) {
            throw "AMHE1 is missing $($required.Name). This must be the extracted game directory, not an .nds file."
        }
    }
}

function Get-DotnetPath {
    $command = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        return $null
    }
    return $command.Source
}

function Get-PublishedCandidates([string]$kind) {
    if ($kind -eq 'game') {
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

    if ($IsWindowsHost) {
        return @(
            (Join-Path $Root 'ProjectPrimeServer.exe'),
            (Join-Path (Join-Path (Join-Path $Root 'publish') 'win-x64-server') 'ProjectPrimeServer.exe')
        )
    }
    return @(
        (Join-Path $Root 'ProjectPrimeServer'),
        (Join-Path (Join-Path (Join-Path $Root 'publish') 'linux-x64-server') 'ProjectPrimeServer')
    )
}

function New-DevelopmentSpec([string]$kind) {
    $dotnet = Get-DotnetPath
    if ($null -eq $dotnet) {
        throw "No published $kind executable was found, and dotnet is not installed."
    }
    $project = if ($kind -eq 'game') { $GameProject } else { $ServerProject }
    if (-not (Test-Path -LiteralPath $project)) {
        throw "Could not find the project at $project."
    }

    $output = Join-Path $Root (Join-Path '.project-prime-launcher' $kind)
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    $runtimeIdentifier = if ($IsWindowsHost) { 'win-x64' } else { $null }
    $assemblyFileName = if ($kind -eq 'game') { 'ProjectPrime.dll' } else { 'ProjectPrimeServer.dll' }

    Write-Host "No published $kind binary found; building it into $output..." -ForegroundColor Yellow
    Push-Location $Root
    try {
        $buildArguments = @(
            'build', $project, '-c', 'Release', '-o', $output
        )
        if ($null -ne $runtimeIdentifier) {
            $buildArguments += @('-r', $runtimeIdentifier)
        }
        & $dotnet @buildArguments | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "The $kind build failed with exit code $LASTEXITCODE. Install/repair the .NET 10 SDK required by global.json, or place a published Project Prime binary beside this launcher."
        }
    }
    finally {
        Pop-Location
    }

    $binaryName = if ($kind -eq 'game') { 'ProjectPrime.exe' } else { 'ProjectPrimeServer.exe' }
    $assembly = Join-Path $output $assemblyFileName
    $nativeBinary = Join-Path $output $binaryName
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
        IsFallbackGui = $false
    }
}

function Get-LaunchSpec([string]$kind) {
    foreach ($candidate in (Get-PublishedCandidates $kind)) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return [pscustomobject]@{
                FilePath = $candidate
                Prefix = @()
                WorkingDirectory = Split-Path -Parent $candidate
                IsFallbackGui = $false
            }
        }
    }

    # A GUI game binary can still dispatch -server and -masterserver, but it
    # does not provide a useful console/service lifetime on Windows. Prefer a
    # console server build; only use the GUI binary when dotnet is unavailable.
    if ($kind -ne 'game' -and $IsWindowsHost) {
        $guiCandidates = @(
            (Join-Path $Root 'ProjectPrime.exe'),
            (Join-Path (Join-Path (Join-Path $Root 'publish') 'win-x64') 'ProjectPrime.exe')
        )
        foreach ($candidate in $guiCandidates) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                if ($null -ne (Get-DotnetPath)) {
                    return New-DevelopmentSpec $kind
                }
                Write-Warning "Using GUI ProjectPrime.exe for $kind because no console server binary or dotnet was found."
                return [pscustomobject]@{
                    FilePath = $candidate
                    Prefix = @()
                    WorkingDirectory = Split-Path -Parent $candidate
                    IsFallbackGui = $true
                }
            }
        }
    }

    return New-DevelopmentSpec $kind
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

    if ($spec.IsFallbackGui) {
        Write-Warning 'The selected executable is a GUI build; use a published server package for visible server logs.'
    }
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
    $spec = Get-LaunchSpec 'game'
    Ensure-GamePaths $spec.WorkingDirectory
    $arguments = @('-launcher')
    if ($NoUpdate) {
        $arguments += '-noupdate'
    }
    return Invoke-ProjectPrime $spec $arguments
}

function Start-Server {
    Assert-GameData
    $spec = Get-LaunchSpec 'server'
    $arguments = @(
        '-server',
        '-data', $DataDirectory,
        '-dataversion', 'AMHE1',
        '-port', $ServerPort.ToString(),
        '-players', $Players.ToString(),
        '-servername', $ServerName
    )
    if ([string]::IsNullOrWhiteSpace($Master)) {
        $arguments += '-nomaster'
    }
    else {
        $arguments += @('-master', $Master)
    }
    if ($NoUpdate) {
        $arguments += '-noupdate'
    }
    return Invoke-ProjectPrime $spec $arguments
}

function Start-Directory {
    $spec = Get-LaunchSpec 'server'
    $arguments = @(
        '-masterserver',
        '-port', $DirectoryPort.ToString(),
        '-hostports', $HostPorts
    )
    if (-not $HostPorts.Equals('none', [StringComparison]::OrdinalIgnoreCase)) {
        Assert-GameData
        $arguments += @('-data', $DataDirectory, '-dataversion', 'AMHE1')
        if (-not [string]::IsNullOrWhiteSpace($PublicAddress)) {
            $arguments += @('-public', $PublicAddress)
        }
    }
    if ($NoUpdate) {
        $arguments += '-noupdate'
    }
    return Invoke-ProjectPrime $spec $arguments
}

if ($Mode -eq 'menu') {
    Write-Host ''
    Write-Host 'Project Prime launcher' -ForegroundColor Green
    Write-Host '  1. Start game'
    Write-Host '  2. Start dedicated server'
    Write-Host '  3. Start server directory'
    Write-Host '  4. Exit'
    $choice = Read-Host 'Choose an option'
    if ($choice -eq '1') {
        $Mode = 'game'
    }
    elseif ($choice -eq '2') {
        $Mode = 'server'
    }
    elseif ($choice -eq '3') {
        $Mode = 'directory'
    }
    else {
        exit 0
    }
}

try {
    $exitCode = switch ($Mode) {
        'game' { Start-Game; break }
        'server' { Start-Server; break }
        'directory' { Start-Directory; break }
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
