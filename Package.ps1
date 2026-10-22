param([string]$Output = (Join-Path $PSScriptRoot 'release'))
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Build.ps1')
& (Join-Path $PSScriptRoot 'Test.ps1')
$taskDist = Join-Path $PSScriptRoot 'dist\AudioFollow'
$taskVersion = (Get-Item -LiteralPath (Join-Path $taskDist 'AudioFollow.exe')).VersionInfo.ProductVersion
if ($taskVersion -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') { throw 'ProductVersion must be a release version.' }
$taskTag = "v$taskVersion"
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$taskZip = Join-Path $Output "AudioFollow-$taskTag-win-x64.zip"
Compress-Archive -Path (Join-Path $taskDist '*') -DestinationPath $taskZip -Force
$taskHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $taskZip).Hash
$taskSums = Join-Path $Output "SHA256SUMS-$taskTag.txt"
[IO.File]::WriteAllText($taskSums, "$taskHash  $([IO.Path]::GetFileName($taskZip))`n", [Text.Encoding]::ASCII)
Write-Output "Package: $taskZip"
Write-Output "Checksums: $taskSums"
