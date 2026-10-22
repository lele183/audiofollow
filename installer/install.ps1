param(
    [string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs\AudioFollow'),
    [switch]$StartNow,
    [switch]$NoStartup
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$Destination = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
if ($source -eq $Destination) { throw 'Destination is the current program folder. Choose another destination.' }
if (-not (Test-Path -LiteralPath (Join-Path $source 'AudioFollow.exe'))) { throw 'Run the installer from a built AudioFollow package.' }
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$Destination = (Resolve-Path -LiteralPath $Destination).Path
if ($source -eq $Destination) { throw 'Destination is the current program folder. Choose another destination.' }

function Stop-Switcher([string]$Control) {
    if (-not (Test-Path -LiteralPath $Control)) { return }
    $query = New-Object Diagnostics.Process
    $query.StartInfo = New-Object Diagnostics.ProcessStartInfo
    $query.StartInfo.FileName = $Control
    $query.StartInfo.Arguments = '--status'
    $query.StartInfo.UseShellExecute = $false
    $query.StartInfo.CreateNoWindow = $true
    $query.StartInfo.RedirectStandardOutput = $true
    $query.StartInfo.RedirectStandardError = $true
    try {
        $query.Start() | Out-Null
        $statusText = $query.StandardOutput.ReadToEnd()
        $query.StandardError.ReadToEnd() | Out-Null
        $query.WaitForExit()
        if ($query.ExitCode -ne 0) { return }
        $status = $statusText | ConvertFrom-Json
    } finally { $query.Dispose() }
    $running = Get-Process -Id $status.ProcessId -ErrorAction SilentlyContinue
    & $Control --exit | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not exit the running switcher: $Control" }
    if ($running -and -not $running.WaitForExit(8000)) { throw 'Switcher is still shutting down. Retry the installation after it exits.' }
}

$legacyInstall = Join-Path $env:LOCALAPPDATA 'Programs\BarracudaSwitch'
Stop-Switcher (Join-Path $legacyInstall 'AudioSwitchCtl.exe')
Stop-Switcher (Join-Path $Destination 'AudioSwitchCtl.exe')
Stop-Switcher (Join-Path $source 'AudioSwitchCtl.exe')

$data = Join-Path $env:LOCALAPPDATA 'AudioFollow'
$legacySettings = Join-Path $env:LOCALAPPDATA 'BarracudaSwitch\settings.json'
$settings = Join-Path $data 'settings.json'
if (-not (Test-Path -LiteralPath $settings) -and (Test-Path -LiteralPath $legacySettings)) {
    New-Item -ItemType Directory -Path $data -Force | Out-Null
    Copy-Item -LiteralPath $legacySettings -Destination $settings
    Write-Output 'Copied legacy device settings; originals remain in place.'
}

foreach ($name in @('AudioFollow.exe','AudioSwitchCtl.exe','AudioFollow.exe.config','AudioSwitchCtl.exe.config','README.md','README.zh-CN.md','CHANGELOG.md','THIRD-PARTY-NOTICES.txt','Start.cmd','Enable-Startup.cmd','Disable-Startup.cmd','Stop.cmd','AudioSwitch.ico','install.ps1','uninstall.ps1')) {
    $from = Join-Path $source $name
    if (Test-Path -LiteralPath $from) { Copy-Item -LiteralPath $from -Destination (Join-Path $Destination $name) -Force }
}
if (Test-Path -LiteralPath (Join-Path $source 'docs')) {
    New-Item -ItemType Directory -Path (Join-Path $Destination 'docs') -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $source 'docs') -File | Copy-Item -Destination (Join-Path $Destination 'docs') -Force
}

$startupKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$legacyCommand = '"' + (Join-Path $legacyInstall 'BarracudaSwitch.exe') + '" --background'
$oldStartup = Get-ItemProperty -LiteralPath $startupKey -Name 'BarracudaSwitch' -ErrorAction SilentlyContinue
if ($oldStartup -and $oldStartup.BarracudaSwitch -eq $legacyCommand) {
    Remove-ItemProperty -LiteralPath $startupKey -Name 'BarracudaSwitch'
    Write-Output 'Removed the known legacy login entry.'
} elseif ($oldStartup) {
    Write-Warning 'A legacy login entry points to another folder. Disable it there to avoid competing switchers.'
}
$installedControl = Join-Path $Destination 'AudioSwitchCtl.exe'
if ($NoStartup) { & $installedControl --uninstall-startup | Out-Null }
else { & $installedControl --install-startup | Out-Null }
if ($LASTEXITCODE -ne 0) { throw 'Could not update login startup.' }
if ($StartNow) { Start-Process -FilePath (Join-Path $Destination 'AudioFollow.exe') -ArgumentList '--background' -WindowStyle Hidden }
Write-Output "Installed to $Destination"
Write-Output "Startup=$([bool](-not $NoStartup)); Started=$([bool]$StartNow)"
exit 0
