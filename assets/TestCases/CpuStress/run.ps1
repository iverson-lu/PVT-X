# CpuStress Test Script
# Parameters are passed as named PowerShell parameters per spec section 9

param(
    [int]$DurationSec = 5,
    [string]$Mode = "Normal",
    [bool]$ShouldPass = $true
)

Write-Host "=== CpuStress Test ==="
Write-Host "Start Time: $(Get-Date -Format 'o')"
Write-Host "Duration: $DurationSec seconds"
Write-Host "Mode: $Mode"
Write-Host "ShouldPass: $ShouldPass"
Write-Host ""

# Simulate work
Write-Host "Running stress test..."
$elapsed = 0
while ($elapsed -lt $DurationSec) {
    $sleepTime = [Math]::Min(1, $DurationSec - $elapsed)
    Start-Sleep -Seconds $sleepTime
    $elapsed += $sleepTime
    Write-Host "  Progress: $elapsed / $DurationSec seconds"
}

Write-Host ""
Write-Host "Stress test completed."
Write-Host "End Time: $(Get-Date -Format 'o')"

# Exit based on ShouldPass parameter
if ($ShouldPass) {
    Write-Host "Test PASSED"
    exit 0
} else {
    Write-Host "Test FAILED (ShouldPass was false)"
    exit 1
}
