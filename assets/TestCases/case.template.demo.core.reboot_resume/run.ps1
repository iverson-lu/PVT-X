param()

$phase = 0
if ($env:PVTX_PHASE) {
    $phase = [int]$env:PVTX_PHASE
}

$controlDir = $env:PVTX_CONTROL_DIR
if ([string]::IsNullOrWhiteSpace($controlDir)) {
    throw "PVTX_CONTROL_DIR is not set."
}

if ($phase -eq 0) {
    Write-Host "Phase 0: requesting reboot."

    $request = @{
        type = "control.reboot_required"
        nextPhase = 1
        reason = "Reboot required to validate resume flow."
        reboot = @{
            delaySec = 10
        }
    }

    $tmpPath = Join-Path $controlDir "reboot.tmp"
    $finalPath = Join-Path $controlDir "reboot.json"

    $json = $request | ConvertTo-Json -Depth 4
    Set-Content -Path $tmpPath -Value $json -Encoding utf8
    Move-Item -Path $tmpPath -Destination $finalPath -Force

    exit 0
}

Write-Host "Phase 1: resumed successfully."
