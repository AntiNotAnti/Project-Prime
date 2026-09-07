[CmdletBinding()]
param([string]$Launcher = (Join-Path (Join-Path $PSScriptRoot '..\..') 'Start-FruityPrime.ps1'))

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Resolve-Path -LiteralPath $Launcher), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw ($parseErrors | Out-String) }
# Load definitions only. Never execute the launcher parameter defaults, menu, dispatch or exit statements.
foreach ($function in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($function.Extent.Text))
}
$checks = 0
function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:checks++
}
function Assert-Equal($expected, $actual, [string]$message) {
    Assert-True ((ConvertTo-Json -Compress -InputObject $expected) -ceq (ConvertTo-Json -Compress -InputObject $actual)) $message
}
function Assert-Throws([scriptblock]$action, [string]$text) {
    $caught = $false
    try { & $action | Out-Null }
    catch { $caught = $true; Assert-True ($_.Exception.Message.Contains($text)) "Unexpected error: $_" }
    Assert-True $caught "Expected error containing: $text"
}

$temporary = Join-Path ([IO.Path]::GetTempPath()) ('fruity launcher test ' + [guid]::NewGuid().ToString('N'))
$originalDirectory = (Get-Location).Path
$oldRollForward = $env:DOTNET_ROLL_FORWARD
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $Root = Join-Path $temporary "repo with spaces and ' quote"
    $Project = Join-Path $Root 'src/MphRead/MphRead.csproj'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Project) | Out-Null
    Set-Content -LiteralPath $Project -Value '<Project />'
    $DataDirectory = Join-Path $Root 'AMHE1'
    New-Item -ItemType Directory -Force -Path (Join-Path $DataDirectory '_bin'), (Join-Path $DataDirectory 'models'), (Join-Path $DataDirectory 'levels') | Out-Null
    Set-Content -LiteralPath (Join-Path $DataDirectory '_bin/arm9.bin') -Value 'test'
    $fake = Join-Path $temporary 'fake dotnet.ps1'
    @'
$global:FruityLauncherCalls.Add([pscustomobject]@{ Arguments = @($args); Directory = (Get-Location).Path })
if ($args[0] -eq 'build') {
    $global:LASTEXITCODE = $global:FruityBuildExit
    if ($global:FruityBuildExit -eq 0) {
        $output = $args[[array]::IndexOf($args, '-o') + 1]
        foreach ($file in $global:FruityBuildFiles) { Set-Content -LiteralPath (Join-Path $output $file) -Value 'test output' }
    }
}
else { $global:LASTEXITCODE = 23 }
'@ | Set-Content -LiteralPath $fake
    $global:FruityLauncherCalls = [Collections.Generic.List[object]]::new()
    $global:FruityBuildExit = 0
    $global:FruityBuildFiles = @('FruityPrime.dll')
    $script:DotnetAvailable = $true
    $script:DotnetLookups = 0
    function Get-DotnetPath {
        $script:DotnetLookups++
        if ($script:DotnetAvailable) { return $fake }
        return $null
    }

    $IsWindowsHost = $false
    $spec = New-DevelopmentSpec 'game'
    $output = Join-Path $Root '.fruity-launcher/game'
    Assert-Equal $fake $spec.FilePath 'Managed output must launch through dotnet.'
    Assert-Equal @((Join-Path $output 'FruityPrime.dll')) @($spec.Prefix) 'Managed assembly must be the first argument.'
    Assert-Equal $output $spec.WorkingDirectory 'Managed launch must run from its output folder.'
    Assert-Equal $Root $global:FruityLauncherCalls[0].Directory 'Build must resolve global.json from repository root.'
    Assert-Equal @('build', $Project, '-c', 'Release', '-o', $output, '-p:MphReadServer=false') $global:FruityLauncherCalls[0].Arguments 'Build must use normal pinned SDK selection.'
    Assert-Equal $originalDirectory (Get-Location).Path 'Build must restore current directory.'

    $env:DOTNET_ROLL_FORWARD = 'Minor'
    $result = Invoke-Fruity $spec @('-server', '-data', "path with ' quote", '-port', '27888')
    Assert-Equal 23 $result 'Launch must return the child exit code.'
    Assert-Equal @((Join-Path $output 'FruityPrime.dll'), '-server', '-data', "path with ' quote", '-port', '27888') $global:FruityLauncherCalls[1].Arguments 'Launch must preserve argument boundaries and prefix.'
    Assert-Equal $output $global:FruityLauncherCalls[1].Directory 'Child must run from output folder.'
    Assert-Equal 'Minor' $env:DOTNET_ROLL_FORWARD 'Launcher must not override runtime policy.'
    Assert-Equal $originalDirectory (Get-Location).Path 'Launch must restore current directory.'

    $global:FruityBuildExit = 17
    Assert-Throws { New-DevelopmentSpec 'server' } 'exit code 17'
    Assert-Equal $originalDirectory (Get-Location).Path 'Failed build must restore current directory.'
    $global:FruityBuildExit = 0
    $global:FruityBuildFiles = @()
    Assert-Throws { New-DevelopmentSpec 'server' } 'did not produce'
    $script:DotnetAvailable = $false
    Assert-Throws { New-DevelopmentSpec 'game' } 'dotnet is not installed'
    $script:DotnetAvailable = $true

    $IsWindowsHost = $true
    $global:FruityBuildFiles = @('FruityPrimeServer.exe', 'FruityPrimeServer.dll')
    $spec = New-DevelopmentSpec 'server'
    $serverOutput = Join-Path $Root '.fruity-launcher/server'
    Assert-Equal (Join-Path $serverOutput 'FruityPrimeServer.exe') $spec.FilePath 'Windows must prefer native server output.'
    Assert-Equal @() @($spec.Prefix) 'Native launch must not receive an assembly prefix.'
    Assert-Equal @('build', $Project, '-c', 'Release', '-o', $serverOutput, '-p:MphReadServer=true', '-r', 'win-x64') $global:FruityLauncherCalls[$global:FruityLauncherCalls.Count - 1].Arguments 'Windows server build flags must be preserved.'
    Remove-Item -LiteralPath (Join-Path $serverOutput 'FruityPrimeServer.exe')
    $global:FruityBuildFiles = @('FruityPrimeServer.dll')
    $spec = New-DevelopmentSpec 'server'
    Assert-Equal $fake $spec.FilePath 'Windows DLL fallback must also use dotnet.'
    Assert-Equal @((Join-Path $serverOutput 'FruityPrimeServer.dll')) @($spec.Prefix) 'Windows server DLL name must be preserved.'

    $published = Join-Path $Root 'publish/win-x64-server/FruityPrimeServer.exe'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $published) | Out-Null
    Set-Content -LiteralPath $published -Value 'published'
    $script:DotnetAvailable = $false
    $before = $script:DotnetLookups
    $spec = Get-LaunchSpec 'server'
    Assert-Equal $published $spec.FilePath 'Published server must run without any SDK.'
    Assert-Equal $before $script:DotnetLookups 'Published binary discovery must not probe dotnet.'
    Assert-Equal (Split-Path -Parent $published) $spec.WorkingDirectory 'Published working directory must remain beside binary.'

    # Exercise the unchanged game/server/directory argument builders with a fake child only.
    $script:LaunchSpec = [pscustomobject]@{ FilePath = $fake; Prefix = @(); WorkingDirectory = $temporary; IsFallbackGui = $false }
    function Get-LaunchSpec([string]$kind) { return $script:LaunchSpec }
    $NoUpdate = $true; $ForcePaths = $false
    $ServerPort = 28111; $DirectoryPort = 28112; $Players = 6
    $ServerName = "Test server ' name"; $Master = ''; $HostPorts = 'none'; $PublicAddress = ''
    Assert-Equal 23 (Start-Game) 'Game launch exit code must propagate.'
    Assert-Equal @('-launcher', '-noupdate') $global:FruityLauncherCalls[$global:FruityLauncherCalls.Count - 1].Arguments 'Game launch arguments changed.'
    Assert-Equal 23 (Start-Server) 'Server launch exit code must propagate.'
    Assert-Equal @('-server', '-data', $DataDirectory, '-dataversion', 'AMHE1', '-port', '28111', '-players', '6', '-servername', $ServerName, '-nomaster', '-noupdate') $global:FruityLauncherCalls[$global:FruityLauncherCalls.Count - 1].Arguments 'Server launch arguments changed.'
    $Master = 'directory.example:28112'
    Start-Server | Out-Null
    Assert-Equal @('-server', '-data', $DataDirectory, '-dataversion', 'AMHE1', '-port', '28111', '-players', '6', '-servername', $ServerName, '-master', $Master, '-noupdate') $global:FruityLauncherCalls[$global:FruityLauncherCalls.Count - 1].Arguments 'Explicit master address changed.'
    Assert-Equal 23 (Start-Directory) 'Directory launch exit code must propagate.'
    Assert-Equal @('-masterserver', '-port', '28112', '-hostports', 'none', '-noupdate') $global:FruityLauncherCalls[$global:FruityLauncherCalls.Count - 1].Arguments 'Directory-only arguments changed.'
    $HostPorts = '29000-29003'; $PublicAddress = 'server.example'
    Start-Directory | Out-Null
    Assert-Equal @('-masterserver', '-port', '28112', '-hostports', $HostPorts, '-data', $DataDirectory, '-dataversion', 'AMHE1', '-public', $PublicAddress, '-noupdate') $global:FruityLauncherCalls[$global:FruityLauncherCalls.Count - 1].Arguments 'Hosting directory arguments changed.'

    $bad = [pscustomobject]@{ FilePath = (Join-Path $temporary 'missing command'); Prefix = @(); WorkingDirectory = $Root; IsFallbackGui = $false }
    Assert-Throws { Invoke-Fruity $bad @() } 'missing command'
    Assert-Equal $originalDirectory (Get-Location).Path 'Failed launch must restore current directory.'
    Write-Host "LAUNCHER PASS checks=$checks (AST definitions only; fake builds and launch targets)"
}
finally {
    Set-Location -LiteralPath $originalDirectory
    if ($null -eq $oldRollForward) { Remove-Item Env:DOTNET_ROLL_FORWARD -ErrorAction SilentlyContinue }
    else { $env:DOTNET_ROLL_FORWARD = $oldRollForward }
    Remove-Variable FruityLauncherCalls, FruityBuildExit, FruityBuildFiles -Scope Global -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
