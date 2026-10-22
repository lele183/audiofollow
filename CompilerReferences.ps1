$taskFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$taskFacades = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
$taskMetadataRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\UnionMetadata'
$taskMetadata = Get-ChildItem -LiteralPath $taskMetadataRoot -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'Windows.winmd')) } |
    Sort-Object { [Version]$_.Name } -Descending | Select-Object -First 1
if (-not $taskMetadata) { throw 'Install the Windows 10/11 SDK (UnionMetadata) to build Bluetooth support.' }
$taskReferences = @(
    '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll',
    "/r:$taskFramework\System.Runtime.WindowsRuntime.dll",
    "/r:$taskFacades\System.Runtime.dll",
    "/r:$taskFacades\System.ObjectModel.dll",
    "/r:$taskFacades\System.Runtime.InteropServices.WindowsRuntime.dll",
    "/r:$($taskMetadata.FullName)\Windows.winmd"
)
