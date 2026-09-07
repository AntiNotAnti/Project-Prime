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

    [string]$ServerName = 'Fruity Prime',

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
$Project = Join-Path (Join-Path (Join-Path $Root 'src') 'MphRead') 'MphRead.csproj'
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

function Get-InstalledDotnetSdks([string]$dotnet) {
    # dotnet resolves global.json from the current directory and its parents.
    # Probe from the system temp directory so this diagnostic is not blocked by
    # this repository's .NET 9 pin.
    $probeDirectory = [IO.Path]::GetTempPath()
    Push-Location $probeDirectory
    try {
        $lines = @(& $dotnet '--list-sdks' 2>&1)
    }
    finally {
        Pop-Location
    }

    $entries = foreach ($line in $lines) {
        $text = [string]$line
        if ($text -match '^\s*(?<version>\d+\.\d+\.\d+)\s+\[(?<root>.+)\]\s*$') {
            $sdkDirectory = Join-Path $Matches.root $Matches.version
            $msbuild = Join-Path $sdkDirectory 'MSBuild.dll'
            if (Test-Path -LiteralPath $msbuild -PathType Leaf) {
                [pscustomobject]@{
                    Version = $Matches.version
                    Msbuild = $msbuild
                }
            }
        }
    }
    return @($entries | Sort-Object { [version]$_.Version } -Descending)
}

function Get-PublishedCandidates([string]$kind) {
    if ($kind -eq 'game') {
        if ($IsWindowsHost) {
            return @(
                (Join-Path $Root 'FruityPrime.exe'),
                (Join-Path (Join-Path (Join-Path $Root 'publish') 'win-x64') 'FruityPrime.exe')
            )
        }
        return @(
            (Join-Path $Root 'FruityPrime'),
            (Join-Path (Join-Path (Join-Path $Root 'publish') 'linux-x64') 'FruityPrime')
        )
    }

    if ($IsWindowsHost) {
        return @(
            (Join-Path $Root 'FruityPrimeServer.exe'),
            (Join-Path (Join-Path (Join-Path $Root 'publish') 'win-x64-server') 'FruityPrimeServer.exe')
        )
    }
    return @(
        (Join-Path $Root 'FruityPrime'),
        (Join-Path (Join-Path (Join-Path $Root 'publish') 'linux-x64-server') 'FruityPrime')
    )
}

function New-DevelopmentSpec([string]$kind) {
    $dotnet = Get-DotnetPath
    if ($null -eq $dotnet) {
        throw "No published $kind executable was found, and dotnet is not installed."
    }
    if (-not (Test-Path -LiteralPath $Project)) {
        throw "Could not find the project at $Project."
    }

    $output = Join-Path $Root (Join-Path '.fruity-launcher' $kind)
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    $serverValue = if ($kind -eq 'game') {
        'false'
    }
    else {
        'true'
    }
    $runtimeIdentifier = if ($IsWindowsHost) { 'win-x64' } else { $null }
    $runtimeProperty = if ($null -eq $runtimeIdentifier) {
        $null
    }
    else {
        "/p:RuntimeIdentifier=$runtimeIdentifier"
    }
    $sdks = @(Get-InstalledDotnetSdks $dotnet)
    $sdk9 = @($sdks | Where-Object { ([version]$_.Version).Major -eq 9 } | Select-Object -First 1)
    $workingDirectory = $output
    $assemblyDirectory = $output
    $assemblyFileName = if ($IsWindowsHost -and $kind -ne 'game') {
        'FruityPrimeServer.dll'
    }
    else {
        'FruityPrime.dll'
    }

    Write-Host "No published $kind binary found; building it into $output..." -ForegroundColor Yellow
    Push-Location $Root
    try {
        if ($sdk9.Count -gt 0) {
            $buildArguments = @(
                'build', $Project, '-c', 'Release', '-o', $output,
                "-p:MphReadServer=$serverValue"
            )
            if ($null -ne $runtimeIdentifier) {
                $buildArguments += @('-r', $runtimeIdentifier)
            }
            & $dotnet @buildArguments | Out-Host
            if ($LASTEXITCODE -ne 0) {
                throw "The $kind build failed with exit code $LASTEXITCODE. Install/repair the .NET 9 SDK required by global.json, or place a published Fruity Prime binary beside this launcher."
            }
            $assemblyDirectory = $output
            $assembly = Join-Path $assemblyDirectory $assemblyFileName
        }
        else {
            $fallbackSdk = @(
                $sdks |
                    Where-Object { ([version]$_.Version).Major -gt 9 } |
                    Select-Object -First 1
            )
            if ($fallbackSdk.Count -eq 0) {
                throw "The .NET 9 SDK required by global.json is not installed. Install it, or place a published Fruity Prime binary beside this launcher."
            }

            Write-Warning "SDK 9 is not installed; using SDK $($fallbackSdk[0].Version) as a local build fallback. Install .NET 9 for reproducible builds."
            $msbuild = $fallbackSdk[0].Msbuild
            $property = "/p:MphReadServer=$serverValue"
            $restoreArguments = @(
                $msbuild, $Project, '/t:Restore',
                '/p:Configuration=Release', $property,
                '/p:RestoreIgnoreFailedSources=true', '/m:1', '/v:minimal'
            )
            if ($null -ne $runtimeProperty) {
                $restoreArguments += $runtimeProperty
            }
            & $dotnet @restoreArguments | Out-Host
            if ($LASTEXITCODE -ne 0) {
                throw "The .NET SDK fallback restore failed with exit code $LASTEXITCODE. Install the .NET 9 SDK required by global.json, or place a published Fruity Prime binary beside this launcher."
            }

            $baseOutput = "$output\"
            $buildArguments = @(
                $msbuild, $Project, '/t:Build',
                '/p:Configuration=Release', $property,
                "/p:BaseOutputPath=$baseOutput", '/m:1', '/v:minimal'
            )
            if ($null -ne $runtimeProperty) {
                $buildArguments += $runtimeProperty
            }
            & $dotnet @buildArguments | Out-Host
            if ($LASTEXITCODE -ne 0) {
                throw "The $kind build failed with exit code $LASTEXITCODE using SDK $($fallbackSdk[0].Version). Install the .NET 9 SDK required by global.json, or place a published Fruity Prime binary beside this launcher."
            }
            $assemblyDirectory = Join-Path (Join-Path $output 'Release') 'net9.0'
            if ($null -ne $runtimeIdentifier) {
                $assemblyDirectory = Join-Path $assemblyDirectory $runtimeIdentifier
            }
            $assembly = Join-Path $assemblyDirectory $assemblyFileName
            $workingDirectory = $assemblyDirectory
        }
    }
    finally {
        Pop-Location
    }

    $binaryName = if ($kind -eq 'game') { 'FruityPrime.exe' } else { 'FruityPrimeServer.exe' }
    $nativeBinary = Join-Path $assemblyDirectory $binaryName
    if ($IsWindowsHost -and (Test-Path -LiteralPath $nativeBinary -PathType Leaf)) {
        $launchFile = $nativeBinary
        $launchPrefix = @()
        $workingDirectory = $assemblyDirectory
    }
    else {
        $launchFile = $assembly
        $launchPrefix = @($assembly)
    }
    if (-not (Test-Path -LiteralPath $launchFile -PathType Leaf)) {
        throw "The build completed but did not produce $launchFile."
    }
    return [pscustomobject]@{
        FilePath = $launchFile
        Prefix = $launchPrefix
        WorkingDirectory = $workingDirectory
        RollForward = if ($sdk9.Count -gt 0) { $null } else { 'Major' }
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
                RollForward = $null
                IsFallbackGui = $false
            }
        }
    }

    # A GUI game binary can still dispatch -server and -masterserver, but it
    # does not provide a useful console/service lifetime on Windows. Prefer a
    # console server build; only use the GUI binary when dotnet is unavailable.
    if ($kind -ne 'game' -and $IsWindowsHost) {
        $guiCandidates = @(
            (Join-Path $Root 'FruityPrime.exe'),
            (Join-Path (Join-Path (Join-Path $Root 'publish') 'win-x64') 'FruityPrime.exe')
        )
        foreach ($candidate in $guiCandidates) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                if ($null -ne (Get-DotnetPath)) {
                    return New-DevelopmentSpec $kind
                }
                Write-Warning "Using GUI FruityPrime.exe for $kind because no console server binary or dotnet was found."
                return [pscustomobject]@{
                    FilePath = $candidate
                    Prefix = @()
                    WorkingDirectory = Split-Path -Parent $candidate
                    RollForward = $null
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

function Invoke-Fruity([pscustomobject]$spec, [string[]]$arguments) {
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
    $oldRollForward = $env:DOTNET_ROLL_FORWARD
    try {
        if (-not [string]::IsNullOrWhiteSpace($spec.RollForward)) {
            $env:DOTNET_ROLL_FORWARD = $spec.RollForward
            Write-Host "Using .NET runtime roll-forward: $($spec.RollForward)" -ForegroundColor DarkYellow
        }
        & $spec.FilePath @allArguments | Out-Host
        return $LASTEXITCODE
    }
    finally {
        if ($null -eq $oldRollForward) {
            Remove-Item Env:DOTNET_ROLL_FORWARD -ErrorAction SilentlyContinue
        }
        else {
            $env:DOTNET_ROLL_FORWARD = $oldRollForward
        }
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
    return Invoke-Fruity $spec $arguments
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
    return Invoke-Fruity $spec $arguments
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
    return Invoke-Fruity $spec $arguments
}

if ($Mode -eq 'menu') {
    Write-Host ''
    Write-Host 'Fruity Prime launcher' -ForegroundColor Green
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
