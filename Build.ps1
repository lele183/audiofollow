$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw 'Windows .NET Framework C# compiler was not found.' }
$taskDist = Join-Path $taskRoot 'dist\AudioFollow'
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
. (Join-Path $taskRoot 'CompilerReferences.ps1')
$taskCommon = @('/nologo', '/optimize+', '/platform:x64', '/codepage:65001', "/win32manifest:$taskRoot\app.manifest") + $taskReferences
& $taskCompiler @taskCommon /target:winexe "/win32icon:$taskIcon" "/out:$taskDist\AudioFollow.exe" @taskSources
if ($LASTEXITCODE -ne 0) { throw 'Tray application build failed.' }
& $taskCompiler @taskCommon /target:exe "/out:$taskDist\AudioSwitchCtl.exe" @taskSources
if ($LASTEXITCODE -ne 0) { throw 'Control application build failed.' }
$taskRuntimeConfig = '<?xml version="1.0" encoding="utf-8"?><configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup></configuration>'
foreach ($taskName in @('AudioFollow.exe.config', 'AudioSwitchCtl.exe.config')) {
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
$taskVersion = (Get-Item -LiteralPath "$taskDist\AudioFollow.exe").VersionInfo.FileVersion
foreach ($taskName in @('install.ps1', 'uninstall.ps1')) {
    Copy-Item -LiteralPath (Join-Path $taskRoot "installer\$taskName") -Destination $taskDist -Force
}
Get-Item -LiteralPath "$taskDist\AudioFollow.exe","$taskDist\AudioSwitchCtl.exe" | Select-Object Name,Length,@{N='Version';E={$_.VersionInfo.FileVersion}}
