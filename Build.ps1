$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw 'Windows .NET Framework C# compiler was not found.' }
$taskDist = Join-Path $taskRoot 'dist'
New-Item -ItemType Directory -Path $taskDist -Force | Out-Null
$taskSources = Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' -File | ForEach-Object FullName
$taskIcon = Join-Path $taskRoot 'AudioSwitch.ico'
if (-not ('TaskIconDestroy' -as [type])) {
    Add-Type -TypeDefinition 'using System.Runtime.InteropServices; public static class TaskIconDestroy { [DllImport("user32.dll")] public static extern bool DestroyIcon(System.IntPtr handle); }'
}
$taskIcoBytes = $null
if (-not (Test-Path -LiteralPath $taskIcon)) {
    Add-Type -AssemblyName System.Drawing
    $taskBitmap = [Drawing.Bitmap]::new(64, 64)
    try {
        $taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap)
        try {
            $taskGraphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $taskGraphics.Clear([Drawing.Color]::FromArgb(28, 65, 86))
            $taskBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(239, 180, 87))
            try {
                $taskGraphics.FillEllipse($taskBrush, 10, 18, 44, 14)
                $taskGraphics.FillEllipse($taskBrush, 10, 32, 44, 14)
                $taskGraphics.FillRectangle($taskBrush, 8, 25, 9, 16)
                $taskGraphics.FillRectangle($taskBrush, 47, 25, 9, 16)
            } finally { $taskBrush.Dispose() }
        } finally { $taskGraphics.Dispose() }
        $taskPng = [IO.MemoryStream]::new()
        try { $taskBitmap.Save($taskPng, [Drawing.Imaging.ImageFormat]::Png) } finally { }
        $taskPngBytes = $taskPng.ToArray(); $taskPng.Dispose()
        $taskIcoStream = [IO.MemoryStream]::new()
        $taskWriter = [IO.BinaryWriter]::new($taskIcoStream)
        try {
            $taskWriter.Write([UInt16]0); $taskWriter.Write([UInt16]1); $taskWriter.Write([UInt16]1)
            $taskWriter.Write([Byte]64); $taskWriter.Write([Byte]64); $taskWriter.Write([Byte]0); $taskWriter.Write([Byte]0)
            $taskWriter.Write([UInt16]1); $taskWriter.Write([UInt16]32)
            $taskWriter.Write([UInt32]$taskPngBytes.Length); $taskWriter.Write([UInt32]22)
            $taskWriter.Write($taskPngBytes); $taskWriter.Flush()
            $taskIcoBytes = $taskIcoStream.ToArray()
            [IO.File]::WriteAllBytes($taskIcon, $taskIcoBytes)
        } finally { $taskWriter.Dispose(); $taskIcoStream.Dispose() }
    } finally { $taskBitmap.Dispose() }
}
if ($null -eq $taskIcoBytes) { $taskIcoBytes = [IO.File]::ReadAllBytes($taskIcon) }
[IO.File]::WriteAllBytes((Join-Path $taskDist 'AudioSwitch.ico'), $taskIcoBytes)
$taskCommon = @('/nologo', '/optimize+', '/platform:x64', '/codepage:65001', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll', "/win32manifest:$taskRoot\app.manifest")
& $taskCompiler @taskCommon /target:winexe "/win32icon:$taskIcon" "/out:$taskDist\BarracudaSwitch.exe" @taskSources
if ($LASTEXITCODE -ne 0) { throw 'Tray application build failed.' }
& $taskCompiler @taskCommon /target:exe "/out:$taskDist\AudioSwitchCtl.exe" @taskSources
if ($LASTEXITCODE -ne 0) { throw 'Control application build failed.' }
$taskRuntimeConfig = '<?xml version="1.0" encoding="utf-8"?><configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup></configuration>'
foreach ($taskName in @('BarracudaSwitch.exe.config', 'AudioSwitchCtl.exe.config')) {
    [IO.File]::WriteAllText((Join-Path $taskDist $taskName), $taskRuntimeConfig, [Text.UTF8Encoding]::new($false))
}
foreach ($taskName in @('README.md', 'README.zh-CN.md', 'CHANGELOG.md', 'THIRD-PARTY-NOTICES.txt', 'Start.cmd', 'Enable-Startup.cmd', 'Disable-Startup.cmd', 'Stop.cmd', 'AudioSwitch.ico')) {
    $taskSource = Join-Path $taskRoot $taskName
    if (Test-Path -LiteralPath $taskSource) { Copy-Item -LiteralPath $taskSource -Destination $taskDist -Force }
}
if (Test-Path -LiteralPath (Join-Path $taskRoot 'docs')) {
    New-Item -ItemType Directory -Path (Join-Path $taskDist 'docs') -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $taskRoot 'docs') -File | Copy-Item -Destination (Join-Path $taskDist 'docs') -Force
}
$taskVersion = (Get-Item -LiteralPath "$taskDist\BarracudaSwitch.exe").VersionInfo.FileVersion
$taskScripts = @{
    'install.ps1' = @'
param([string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs\BarracudaSwitch'), [switch]$StartNow, [switch]$NoStartup)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path (Split-Path -Parent $PSCommandPath)).Path
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$source = (Resolve-Path -LiteralPath $source).Path
$Destination = (Resolve-Path -LiteralPath $Destination).Path
if ($source -eq $Destination) { throw 'Destination is the current program folder. Choose another destination.' }
$existing = Join-Path $Destination 'AudioSwitchCtl.exe'
if (Test-Path -LiteralPath $existing) {
    & $existing --status *> $null
    if ($LASTEXITCODE -eq 0) { & $existing --exit | Out-Null; Start-Sleep -Milliseconds 600 }
}
foreach ($name in @('BarracudaSwitch.exe','AudioSwitchCtl.exe','BarracudaSwitch.exe.config','AudioSwitchCtl.exe.config','README.md','README.zh-CN.md','CHANGELOG.md','THIRD-PARTY-NOTICES.txt','Start.cmd','Enable-Startup.cmd','Disable-Startup.cmd','Stop.cmd','AudioSwitch.ico','install.ps1','uninstall.ps1')) {
    $from = Join-Path $source $name
    if (Test-Path -LiteralPath $from) { Copy-Item -LiteralPath $from -Destination (Join-Path $Destination $name) -Force }
}
if (Test-Path -LiteralPath (Join-Path $source 'docs')) {
    New-Item -ItemType Directory -Path (Join-Path $Destination 'docs') -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $source 'docs') -File | Copy-Item -Destination (Join-Path $Destination 'docs') -Force
}
if (-not $NoStartup) { & (Join-Path $Destination 'AudioSwitchCtl.exe') --install-startup | Out-Null }
if ($StartNow) { Start-Process -FilePath (Join-Path $Destination 'BarracudaSwitch.exe') -ArgumentList '--background' -WindowStyle Hidden }
Write-Output "Installed to $Destination"
Write-Output "Startup=$([bool](-not $NoStartup)); Started=$([bool]$StartNow)"
exit 0
'@
    'uninstall.ps1' = @'
param([switch]$KeepSettings)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Split-Path -Parent $PSCommandPath)).Path
$control = Join-Path $root 'AudioSwitchCtl.exe'
if (Test-Path -LiteralPath $control) {
    & $control --status *> $null
    $wasRunning = $LASTEXITCODE -eq 0
    & $control --uninstall-startup | Out-Null
    if ($wasRunning) { & $control --exit | Out-Null; Start-Sleep -Milliseconds 600 }
}
if (-not $KeepSettings) {
    $data = Join-Path $env:LOCALAPPDATA 'BarracudaSwitch'
    if (Test-Path -LiteralPath $data) {
        $resolved = (Resolve-Path -LiteralPath $data).Path
        $allowed = (Join-Path $env:LOCALAPPDATA 'BarracudaSwitch')
        if ($resolved -ne $allowed) { throw "Refusing to remove unexpected data path: $resolved" }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
$parent = Split-Path -Parent $root
if ((Split-Path -Leaf $root) -eq 'BarracudaSwitch' -and (Split-Path -Leaf $parent) -eq 'Programs') {
    $resolvedRoot = (Resolve-Path -LiteralPath $root).Path
    $allowedRoot = (Join-Path $env:LOCALAPPDATA 'Programs\BarracudaSwitch')
    if ($resolvedRoot -ne $allowedRoot) { throw "Refusing to remove unexpected install path: $resolvedRoot" }
    Set-Location -LiteralPath $env:TEMP
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
    Write-Output "Removed $resolvedRoot"
} else {
    Write-Output "Startup disabled. Source folder was left in place: $root"
}
exit 0
'@
}
foreach ($taskPair in $taskScripts.GetEnumerator()) {
    $taskText = [regex]::Replace($taskPair.Value, '^[ \t]+(?=[^\r\n])', '', [Text.RegularExpressions.RegexOptions]::Multiline)
    [IO.File]::WriteAllText((Join-Path $taskDist $taskPair.Key), $taskText, [Text.UTF8Encoding]::new($false))
}
Get-Item -LiteralPath "$taskDist\BarracudaSwitch.exe","$taskDist\AudioSwitchCtl.exe" | Select-Object Name,Length,@{N='Version';E={$_.VersionInfo.FileVersion}}
