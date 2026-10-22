param([switch]$Run)
$ErrorActionPreference = 'Stop'
if (-not $Run) { throw 'This opt-in test changes actual Windows playback/microphone defaults temporarily and restores them. Exit AudioFollow, then use -Run.' }
$taskRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $taskRoot 'CompilerReferences.ps1')
$taskSources = Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' -File | ForEach-Object FullName
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $taskCompiler /nologo /optimize+ /platform:x64 /codepage:65001 @taskReferences /target:exe /main:HardwareSmoke "/out:$taskRoot\build\HardwareSmoke.exe" @taskSources "$PSScriptRoot\HardwareSmoke.cs"
if ($LASTEXITCODE -ne 0) { throw 'Hardware smoke test build failed.' }
& "$taskRoot\build\HardwareSmoke.exe" "$taskRoot\build\hardware-smoke.json"
if ($LASTEXITCODE -ne 0) { throw 'Hardware smoke test failed.' }
