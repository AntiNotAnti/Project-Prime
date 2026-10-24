param([string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$cameraRepository=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$cameraBaseline=Join-Path ([IO.Path]::GetTempPath()) ('prime-camera-baseline-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $cameraBaseline 'Source') | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Program.cs') -Destination $cameraBaseline
$cameraFiles=@('src/MphRead/Mods/Replay/ReplayCameraTrack.cs','src/MphRead/Mods/Replay/ReplayCameraTrack.Window.cs','src/MphRead/Mods/Replay/ReplayCameraTrack.State.cs','src/MphRead/Mods/Network/ReplayPathComparer.cs')
$cameraProject=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'replay-camera-benchmark.csproj')
foreach($cameraFile in $cameraFiles)
{
    $cameraOriginal=git -C $cameraRepository show ('HEAD:'+$cameraFile)
    if($LASTEXITCODE -ne 0){throw 'Could not read baseline camera source from HEAD.'}
    Set-Content -Encoding utf8 -LiteralPath (Join-Path $cameraBaseline ('Source/'+[IO.Path]::GetFileName($cameraFile))) -Value $cameraOriginal
    $cameraProject=$cameraProject.Replace('../../'+$cameraFile,'Source/'+[IO.Path]::GetFileName($cameraFile))
}
$cameraProject=$cameraProject.Replace('<Compile Include="../../src/MphRead/Mods/Replay/ReplayCameraTrack.Channels.cs"/>','')
$cameraProject=$cameraProject.Replace('<ItemGroup>','<ItemGroup><Compile Remove="Source/*.cs"/>')
Set-Content -LiteralPath (Join-Path $cameraBaseline 'baseline.csproj') -Value $cameraProject
& $Dotnet build (Join-Path $cameraBaseline 'baseline.csproj') -c Release --nologo -v:q --ignore-failed-sources
if($LASTEXITCODE -ne 0){throw 'Baseline build failed.'}
& $Dotnet build (Join-Path $PSScriptRoot 'replay-camera-benchmark.csproj') -c Release --nologo -v:q --ignore-failed-sources
if($LASTEXITCODE -ne 0){throw 'Current build failed.'}
# Run both as optimized code from startup: asynchronous tier promotion otherwise measures different JIT phases on subsecond loops.
$cameraPriorTiering=$env:DOTNET_TieredCompilation
try
{
    $env:DOTNET_TieredCompilation='0'
    Write-Output 'BASELINE HEAD (optimized JIT; synthetic CPU only)'
    & $Dotnet (Join-Path $cameraBaseline 'bin/Release/net10.0/baseline.dll')
    Write-Output 'CURRENT (same fixture and JIT policy)'
    & $Dotnet (Join-Path $PSScriptRoot 'bin/Release/net10.0/replay-camera-benchmark.dll')
}
finally{$env:DOTNET_TieredCompilation=$cameraPriorTiering}
Write-Output ('Baseline sources retained for inspection: '+$cameraBaseline)
