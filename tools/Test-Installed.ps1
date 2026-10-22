param([string]$Control)
$ErrorActionPreference = 'Stop'
if (-not $Control) { $Control = Join-Path $env:LOCALAPPDATA 'Programs\BarracudaSwitch\AudioSwitchCtl.exe' }
function Get-LiveStatus {
    $result = & $Control --status | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'Live IPC failed.' }
    return $result
}
function Wait-State([scriptblock]$Predicate) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $state = Get-LiveStatus
        if (& $Predicate $state) { return $state }
        Start-Sleep -Milliseconds 100
    } while ($clock.Elapsed.TotalSeconds -lt 8)
    throw ('State did not settle: ' + ($state | ConvertTo-Json -Compress))
}
$before = Get-LiveStatus
if ($before.Mode -ne 'Automatic') { throw 'Restore automatic mode before this test.' }
try {
    foreach ($target in @('monitor', 'headset')) {
        & $Control "--$target" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Mode command failed.' }
        $state = Wait-State { param($s)
            $id = if ($target -eq 'monitor') { $s.MonitorEndpointId } else { $s.HeadsetEndpointId }
            $s.Mode -eq $target -and $id -and $s.DefaultIds.Count -eq 3 -and @($s.DefaultIds | Where-Object { $_ -ne $id }).Count -eq 0
        }
        $actual = & $Control --diagnose | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0 -or (Compare-Object $actual.Defaults $state.DefaultIds)) { throw 'Independent default-device readback failed.' }
        Write-Output "PASS manual ${target}: all three Windows defaults independently verified"
    }
    & $Control --auto | Out-Null
    for ($index = 0; $index -lt 8; $index++) {
        $beforeRefresh = (Get-LiveStatus).WorkerIterations
        & $Control --refresh | Out-Null
        $state = Wait-State { param($s) $s.WorkerIterations -gt $beforeRefresh -and $s.Mode -eq 'Automatic' -and $s.Link -in @('Connected','Disconnected') -and -not $s.Error }
    }
    if ($state.ProcessId -ne $before.ProcessId) { throw 'Process restarted during test.' }
    Write-Output 'PASS eight HID reconnects; process remained live with verified link responses'
    $state = Wait-State { param($s)
        $id = if ($s.Link -eq 'Connected') { $s.HeadsetEndpointId } else { $s.MonitorEndpointId }
        $s.Mode -eq 'Automatic' -and $id -and @($s.DefaultIds | Where-Object { $_ -ne $id }).Count -eq 0
    }
    Write-Output "PASS automatic mode restored: $($state.Link)"
}
finally { & $Control --auto | Out-Null }
