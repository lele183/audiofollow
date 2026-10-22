param([string]$Output = (Join-Path $PSScriptRoot 'build'))
$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskBuild = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $taskBuild -Force | Out-Null
$taskSources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File | ForEach-Object FullName
. (Join-Path $PSScriptRoot 'CompilerReferences.ps1')
& $taskCompiler /nologo /optimize+ /platform:x64 /codepage:65001 @taskReferences /target:exe /main:UiPreview "/out:$taskBuild\UiPreview.exe" @taskSources "$PSScriptRoot\tools\UiPreview.cs"
if ($LASTEXITCODE -ne 0) { throw 'Preview build failed.' }
& "$taskBuild\UiPreview.exe" $Output
if ($LASTEXITCODE -ne 0) { throw 'Preview checks failed.' }
