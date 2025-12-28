param(
  [int]$DurationSec,
  [string]$Mode,
  [bool]$Fail
)

Write-Output "CpuStress starting. Duration=$DurationSec Mode=$Mode Fail=$Fail"
Start-Sleep -Seconds $DurationSec
if ($Fail) {
  Write-Output "CpuStress simulated failure."
  exit 1
}

Write-Output "CpuStress completed."
exit 0
