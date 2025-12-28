param(
  [int]$DurationSec = 1,
  [string]$Mode = "Pass"
)

Write-Output "CpuStress running for $DurationSec seconds with mode $Mode"
Start-Sleep -Seconds $DurationSec

if ($Mode -eq "Fail") {
  Write-Error "CpuStress requested failure."
  exit 1
}

exit 0
