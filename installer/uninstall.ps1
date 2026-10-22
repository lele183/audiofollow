param([switch]$KeepSettings)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$control = Join-Path $root 'AudioSwitchCtl.exe'
if (Test-Path -LiteralPath $control) {
    $query = New-Object Diagnostics.Process
    $query.StartInfo = New-Object Diagnostics.ProcessStartInfo
    $query.StartInfo.FileName = $control
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
        $wasRunning = $query.ExitCode -eq 0
    } finally { $query.Dispose() }
    $running = if ($wasRunning) { Get-Process -Id ($statusText | ConvertFrom-Json).ProcessId -ErrorAction SilentlyContinue }
    & $control --uninstall-startup | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not remove login startup.' }
    if ($wasRunning) {
        & $control --exit | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not exit AudioFollow.' }
        if ($running -and -not $running.WaitForExit(8000)) { throw 'AudioFollow is still shutting down. Retry after it exits.' }
    }
}
if (-not $KeepSettings) {
    $data = Join-Path $env:LOCALAPPDATA 'AudioFollow'
    if (Test-Path -LiteralPath $data) {
        $resolved = (Resolve-Path -LiteralPath $data).Path
        $allowed = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'AudioFollow'))
        if ($resolved -ne $allowed) { throw "Refusing to remove unexpected data path: $resolved" }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\AudioFollow'))
if ($root -eq $allowedRoot) {
    Set-Location -LiteralPath $env:TEMP
    Remove-Item -LiteralPath $root -Recurse -Force
    Write-Output "Removed $root"
} else {
    Write-Output "Startup disabled. Custom or portable folder left in place: $root"
}
Write-Output 'Legacy BarracudaSwitch files and settings were left in place.'
exit 0
