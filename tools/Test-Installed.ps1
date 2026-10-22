param([string]$Control, [int]$ReconnectCount = 2)
$ErrorActionPreference = 'Stop'
if (-not $Control) { $Control = Join-Path $env:LOCALAPPDATA 'Programs\AudioFollow\AudioSwitchCtl.exe' }
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
            $microphone = if ($target -eq 'monitor') { $s.FallbackMicrophoneId } else { $s.HeadsetMicrophoneId }
            $s.Mode -eq $target -and -not $s.Error -and -not $s.MicrophoneError -and
                ($s.OutputKeepCurrent -or ($id -and $s.DefaultIds.Count -eq 3 -and @($s.DefaultIds | Where-Object { $_ -ne $id }).Count -eq 0)) -and
                (-not $s.SwitchMicrophone -or $s.MicrophoneKeepCurrent -or ($microphone -and
                    $s.CaptureDefaultIds.Count -eq 3 -and @($s.CaptureDefaultIds | Where-Object { $_ -ne $microphone }).Count -eq 0))
        }
        $actual = & $Control --diagnose | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0 -or (Compare-Object $actual.Defaults $state.DefaultIds) -or
            (Compare-Object $actual.CaptureDefaults $state.CaptureDefaultIds)) { throw 'Independent playback/microphone readback failed.' }
        Write-Output "PASS manual ${target}: all three Windows playback AND microphone defaults independently verified"
    }
    & $Control --auto | Out-Null
    for ($index = 0; $index -lt $ReconnectCount; $index++) {
        $beforeRefresh = (Get-LiveStatus).WorkerIterations
        & $Control --refresh | Out-Null
        $state = Wait-State { param($s) $s.WorkerIterations -gt $beforeRefresh -and $s.Mode -eq 'Automatic' -and $s.Link -in @('Connected','Disconnected') -and -not $s.Error -and -not $s.MicrophoneError }
    }
    if ($state.ProcessId -ne $before.ProcessId) { throw 'Process restarted during test.' }
    Write-Output "PASS $ReconnectCount detector refreshes; process remained live with confirmed states"
    $state = Wait-State { param($s)
        $id = if ($s.Link -eq 'Connected') { $s.HeadsetEndpointId } else { $s.MonitorEndpointId }
        $microphone = if ($s.Link -eq 'Connected') { $s.HeadsetMicrophoneId } else { $s.FallbackMicrophoneId }
        $s.Mode -eq 'Automatic' -and $s.StableLink -in @('Connected','Disconnected','ReceiverMissing') -and
            ($s.OutputKeepCurrent -or ($id -and @($s.DefaultIds | Where-Object { $_ -ne $id }).Count -eq 0)) -and
            (-not $s.SwitchMicrophone -or $s.MicrophoneKeepCurrent -or ($microphone -and
                @($s.CaptureDefaultIds | Where-Object { $_ -ne $microphone }).Count -eq 0))
    }
    $actual = & $Control --diagnose | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or (Compare-Object $actual.Defaults $state.DefaultIds) -or
        (Compare-Object $actual.CaptureDefaults $state.CaptureDefaultIds)) { throw 'Automatic routing readback failed.' }
    Write-Output "PASS automatic mode restored: $($state.Link)"
}
finally { & $Control --auto | Out-Null }
