$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskBuild = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $taskBuild -Force | Out-Null
$taskSources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File | ForEach-Object FullName
. (Join-Path $PSScriptRoot 'CompilerReferences.ps1')
& $taskCompiler /nologo /optimize+ /platform:x64 /codepage:65001 @taskReferences /target:exe /main:SwitchTests "/out:$taskBuild\SwitchTests.exe" @taskSources "$PSScriptRoot\tests\SwitchTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
& "$taskBuild\SwitchTests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
