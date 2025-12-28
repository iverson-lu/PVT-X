# MemoryCheck Test Script

param(
    [int]$MinMemoryMB = 100
)

Write-Host "=== Memory Check Test ==="
Write-Host "Start Time: $(Get-Date -Format 'o')"
Write-Host "Minimum Required: $MinMemoryMB MB"
Write-Host ""

# Get available memory
$os = Get-CimInstance Win32_OperatingSystem
$availableMB = [math]::Round($os.FreePhysicalMemory / 1024, 2)

Write-Host "Available Memory: $availableMB MB"

if ($availableMB -ge $MinMemoryMB) {
    Write-Host "PASSED: Sufficient memory available"
    exit 0
} else {
    Write-Host "FAILED: Insufficient memory (need $MinMemoryMB MB, have $availableMB MB)"
    exit 1
}
