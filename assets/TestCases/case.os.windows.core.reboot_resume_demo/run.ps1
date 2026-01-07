param()

$phase = 0
if ($env:PVTX_PHASE) {
    $phase = [int]$env:PVTX_PHASE
}

Write-Output "PVTX_PHASE=$phase"

if ($phase -eq 0) {
    if (-not $env:PVTX_CONTROL_DIR) {
        Write-Output "PVTX_CONTROL_DIR is not set."
        exit 3
    }

    $payload = @{
        type = "control.reboot_required"
        nextPhase = 1
        reason = "Demo reboot-resume request from phase 0"
        reboot = @{
            delaySec = 10
        }
    } | ConvertTo-Json -Depth 4

    $tmpPath = Join-Path $env:PVTX_CONTROL_DIR "reboot.tmp"
    $finalPath = Join-Path $env:PVTX_CONTROL_DIR "reboot.json"

    $payload | Set-Content -Path $tmpPath -Encoding UTF8
    Move-Item -Path $tmpPath -Destination $finalPath -Force

    Write-Output "Reboot requested. Exiting for resume."
    exit 0
}

Write-Output "Phase 1 complete. Test finished successfully."

# 0 = Pass, 1 = Fail, 2 = Timeout, 3 = Error
exit 0
