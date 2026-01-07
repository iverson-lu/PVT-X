param()

$phaseValue = $env:PVTX_PHASE
if ([string]::IsNullOrWhiteSpace($phaseValue)) {
    $phaseValue = "0"
}

$phase = [int]$phaseValue
$controlDir = $env:PVTX_CONTROL_DIR

if ($phase -eq 0) {
    $payload = @{
        type = "control.reboot_required"
        nextPhase = 1
        reason = "Reboot required for resume demo."
        reboot = @{
            delaySec = 10
        }
    } | ConvertTo-Json -Depth 4

    $tmpPath = Join-Path $controlDir "reboot.tmp"
    $finalPath = Join-Path $controlDir "reboot.json"
    $payload | Set-Content -Path $tmpPath -Encoding UTF8
    Move-Item -Path $tmpPath -Destination $finalPath -Force

    Write-Output "Phase 0 complete. Reboot requested."
    exit 0
}

Write-Output "Phase $phase complete."
exit 0
