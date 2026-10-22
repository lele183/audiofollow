param([int]$Seconds = 30, [string]$Control, [string]$Output)
$ErrorActionPreference = 'Stop'
if ($Seconds -lt 5 -or $Seconds -gt 60) { throw 'Use 5..60 seconds.' }
if (-not $Control) { $Control = Join-Path $env:LOCALAPPDATA 'Programs\BarracudaSwitch\AudioSwitchCtl.exe' }
$before = & $Control --status | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Start the installed switcher first.' }
$process = Get-Process -Id $before.ProcessId
$cpu = $process.TotalProcessorTime.TotalSeconds
$handles = $process.HandleCount
$memory = $process.WorkingSet64
$clock = [Diagnostics.Stopwatch]::StartNew()
Start-Sleep -Seconds $Seconds
$process.Refresh()
$elapsed = $clock.Elapsed.TotalSeconds
$cpuDelta = $process.TotalProcessorTime.TotalSeconds - $cpu
$after = & $Control --status | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $after.ProcessId -ne $before.ProcessId) { throw 'Process changed or became unavailable during measurement.' }
$result = [pscustomobject]@{
    Version = $after.Version
    Seconds = [math]::Round($elapsed, 3)
    CPUSeconds = $cpuDelta
    OneCoreCPUPercent = [math]::Round(100 * $cpuDelta / $elapsed, 4)
    StartMemoryMiB = [math]::Round($memory / 1MB, 2)
    EndMemoryMiB = [math]::Round($process.WorkingSet64 / 1MB, 2)
    StartHandles = $handles
    EndHandles = $process.HandleCount
    WorkerIterations = $after.WorkerIterations - $before.WorkerIterations
    AudioReads = $after.AudioReads - $before.AudioReads
    StatusWrites = $after.StatusWrites - $before.StatusWrites
    Mode = $after.Mode
    Link = $after.Link
    Error = $after.Error
}
$json = $result | ConvertTo-Json
if ($Output) { [IO.File]::WriteAllText($Output, $json, [Text.UTF8Encoding]::new($false)) }
$json
