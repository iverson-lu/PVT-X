param(
  [int]$DurationSec,
  [string]$Mode
)

Write-Output "CpuStress starting for $DurationSec seconds with mode $Mode"
Start-Sleep -Seconds $DurationSec

if ($Mode -eq "Fail") {
  Write-Output "CpuStress finished with failure"
  exit 1
}

Write-Output "CpuStress finished successfully"
exit 0
