# Builds a local candidate only. This script never uploads or publishes a release.
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+([.-][0-9A-Za-z.-]+)?$')][string]$Version = '0.35.1',
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [switch]$SkipNative,
    [string]$DotnetPath = 'dotnet'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw 'Run this packager on Windows.' }
$root = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "OutputDirectory must be fresh: $output" }
$git = (Get-Command git.exe -ErrorAction Stop).Source
$gitRoot = Split-Path (Split-Path $git -Parent) -Parent
$bash = @((Join-Path $gitRoot 'bin/bash.exe'), (Join-Path $gitRoot 'usr/bin/bash.exe'),
    (Join-Path $env:ProgramFiles 'Git/bin/bash.exe')) |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $bash) { throw 'Git for Windows Bash is required.' }
$python = (Get-Command python -ErrorAction Stop).Source
$dotnet = (Get-Command $DotnetPath -ErrorAction Stop).Source

function Invoke-Checked([string]$Executable, [string[]]$Arguments) {
    # Windows PowerShell 5.1 can promote a successful native tool's stderr to
    # a terminating error under Stop (Git progress uses stderr routinely).
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $Executable @Arguments
        $nativeExitCode = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousPreference }
    if ($nativeExitCode -ne 0) { throw "$Executable failed with exit code $nativeExitCode" }
}
function Assert-GuiHead([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $reader = New-Object IO.BinaryReader($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5a4d) { throw "Invalid DOS header: $Path" }
        $stream.Position = 0x3c
        $pe = $reader.ReadInt32()
        $stream.Position = $pe
        if ($reader.ReadUInt32() -ne 0x00004550 -or $reader.ReadUInt16() -ne 0x8664) {
            throw "Expected x64 PE executable: $Path"
        }
        $stream.Position = $pe + 24
        if ($reader.ReadUInt16() -ne 0x20b) { throw "Expected PE32+: $Path" }
        $stream.Position = $pe + 24 + 68
        if ($reader.ReadUInt16() -ne 2) { throw "Expected Windows GUI subsystem: $Path" }
    } finally { $reader.Dispose() }
    $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path).ProductVersion
    if ($actual -cne $Version) { throw "ProductVersion mismatch in ${Path}: $actual != $Version" }
}

Push-Location $root
try {
    New-Item -ItemType Directory -Path $output | Out-Null
    if (-not $SkipNative) {
        Invoke-Checked $bash @((Join-Path $PSScriptRoot 'rmlui/build-native.sh'), 'win-x64', 'gl2')
    }
    # This validates the current source fingerprint even when reusing native output.
    Invoke-Checked $python @((Join-Path $PSScriptRoot 'rmlui/verify-runtime.py'),
        (Join-Path $root 'artifacts/rmlui-native/win-x64/ProjectPrime.RmlUi.Native.dll'), 'win-x64')
    $gamePublish = Join-Path $output 'game-publish'
    $studioPublish = Join-Path $output 'studio-publish'
    $package = Join-Path $output 'ProjectPrime-win-x64'
    $common = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false',
        '-p:PublishSingleFile=true', '-p:MphReadRmlUiGlSmoke=true',
        "-p:Version=$Version", "-p:InformationalVersion=$Version",
        '-p:IncludeSourceRevisionInInformationalVersion=false')
    Invoke-Checked $dotnet (@('publish', 'src/MphRead/MphRead.csproj') + $common + @('-o', $gamePublish))
    Invoke-Checked $dotnet (@('publish', 'src/ProjectPrime.Studio/ProjectPrime.Studio.csproj') + $common + @('-o', $studioPublish))
    New-Item -ItemType Directory -Path $package | Out-Null
    Get-ChildItem -LiteralPath $gamePublish -Force | Copy-Item -Destination $package -Recurse -Force
    foreach ($file in Get-ChildItem -LiteralPath $studioPublish -File -Recurse -Force) {
        $relative = $file.FullName.Substring($studioPublish.Length + 1)
        # A referenced executable project can contribute its apphost; the game
        # head already came from the game's own publish and must remain that one.
        if ($relative -ieq 'ProjectPrime.exe') { continue }
        $destination = Join-Path $package $relative
        if (Test-Path -LiteralPath $destination) {
            if ((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -cne
                (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash) {
                throw "Game/Studio shared publish file differs: $relative"
            }
        } else {
            New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $destination
        }
    }
    foreach ($head in @('ProjectPrime.exe', 'ProjectPrimeStudio.exe')) {
        Assert-GuiHead (Join-Path $package $head)
    }
    $encoding = New-Object Text.UTF8Encoding($false)
    $pair = @{ Version = 1; GameVersion = $Version; StudioVersion = $Version; IpcVersion = 1 } | ConvertTo-Json -Compress
    [IO.File]::WriteAllText((Join-Path $package '.project-prime-desktop.json'), $pair, $encoding)
    Invoke-Checked $bash @((Join-Path $PSScriptRoot 'rmlui/package-desktop.sh'), 'win-x64', $package)
    $readme = @"
Project Prime $Version - local Windows candidate

ProjectPrime.exe opens the game. ProjectPrimeStudio.exe opens Project Prime Studio.
Keep both applications and their shared runtime/resources together. Map Studio is Coming Soon.
To try an older standalone release, extract it into a separate folder; do not downgrade this folder.
This runtime package contains no ROM or extracted Nintendo game assets. Supply your own compatible game data.
Copy this package into a fresh runtime folder for testing; do not copy an existing user's data or credentials.
The package does not contain user credential storage. This candidate has not been uploaded or published.
"@
    [IO.File]::WriteAllText((Join-Path $package 'README.txt'), $readme, $encoding)
    if (@(Get-ChildItem -LiteralPath $package -File -Recurse -Force | Where-Object {
        $_.Extension -ieq '.nds' -or $_.Name -ieq 'paths.txt'
    }).Count -ne 0) { throw 'Unexpected ROM or user game-path configuration in runtime package.' }
    Invoke-Checked $bash @((Join-Path $PSScriptRoot 'check-no-game-assets.sh'), $package)
    Invoke-Checked $python @((Join-Path $PSScriptRoot 'write-release-manifest.py'), $package)
    $manifest = Get-Content -LiteralPath (Join-Path $package '.project-prime-files.json') -Raw | ConvertFrom-Json
    foreach ($required in @('ProjectPrime.exe', 'ProjectPrimeStudio.exe', '.project-prime-desktop.json')) {
        if ($manifest.Files -cnotcontains $required) { throw "Missing ownership entry: $required" }
    }
    foreach ($relative in $manifest.Files) {
        $expected = $manifest.Hashes.PSObject.Properties[$relative].Value
        $actual = (Get-FileHash -LiteralPath (Join-Path $package $relative) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -cne $expected) { throw "Ownership hash mismatch: $relative" }
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipPath = Join-Path $output "ProjectPrime-$Version-win-x64.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($package, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
    $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        foreach ($required in @('.project-prime-files.json', '.project-prime-desktop.json', 'ProjectPrime.exe', 'ProjectPrimeStudio.exe')) {
            if ($entries -cnotcontains $required) { throw "ZIP is missing required entry: $required" }
        }
        foreach ($relative in $manifest.Files) {
            if ($entries -cnotcontains $relative) { throw "ZIP is missing owned file: $relative" }
        }
    } finally { $zip.Dispose() }
    Write-Host "Verified local candidate: $zipPath"
} finally { Pop-Location }
