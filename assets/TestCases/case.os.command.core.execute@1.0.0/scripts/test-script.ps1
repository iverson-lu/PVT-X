param([string]$Message = "default")

Write-Host "==================================="
Write-Host "PowerShell Test Script"
Write-Host "==================================="
Write-Host "Message parameter: $Message"
Write-Host "Current time: $(Get-Date)"
Write-Host "Current directory: $PWD"
Write-Host "==================================="

# Test logic
if ($Message -eq "fail") {
  Write-Host "Test result: FAIL (intentional)"
  exit 1
} elseif ($Message -eq "error") {
  throw "Intentional error for testing"
} else {
  Write-Host "Test result: PASS"
  exit 0
}
