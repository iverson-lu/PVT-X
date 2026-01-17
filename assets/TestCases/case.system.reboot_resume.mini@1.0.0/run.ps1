$phase = 0
if ($env:PVTX_PHASE) {
    $phase = [int]$env:PVTX_PHASE
}

Write-Host "[RebootResume] Phase=$phase RunId=$env:PVTX_RUN_ID"

if ($phase -eq 0) {
    $controlDir = $env:PVTX_CONTROL_DIR
    if ([string]::IsNullOrWhiteSpace($controlDir)) {
        throw "PVTX_CONTROL_DIR is required for reboot control."
    }

    $payload = @{
        type = "control.reboot_required"
        nextPhase = 1
        reason = "Phase 0 completed; requesting reboot for resume validation."
        reboot = @{ delaySec = 10 }
    } | ConvertTo-Json -Depth 5

    $tmpPath = Join-Path $controlDir "reboot.tmp"
    $finalPath = Join-Path $controlDir "reboot.json"

    $payload | Set-Content -Path $tmpPath -Encoding UTF8
    Move-Item -Path $tmpPath -Destination $finalPath -Force

    Write-Host "[RebootResume] Reboot request written. Exiting for reboot."
    exit 0
}

Write-Host "[RebootResume] Phase 1 complete."
exit 0
