param()

$phase = 0
if ($env:PVTX_PHASE) {
    [int]$phase = $env:PVTX_PHASE
}

Write-Output "PVTX_PHASE=$phase"

if ($phase -eq 0) {
    $controlDir = $env:PVTX_CONTROL_DIR
    if (-not $controlDir) {
        Write-Error "PVTX_CONTROL_DIR not set"
        exit 3
    }

    $reboot = @{
        type = "control.reboot_required"
        nextPhase = 1
        reason = "Reboot required to validate resume flow"
        reboot = @{
            delaySec = 10
        }
    }

    $tmpPath = Join-Path $controlDir "reboot.tmp"
    $finalPath = Join-Path $controlDir "reboot.json"
    $reboot | ConvertTo-Json -Depth 4 | Set-Content -Path $tmpPath -Encoding UTF8
    Move-Item -Path $tmpPath -Destination $finalPath -Force

    Write-Output "Reboot requested; exiting phase 0."
    exit 0
}

Write-Output "Phase 1 complete."
exit 0
