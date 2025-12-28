param(
  [int]$DurationSec,
  [string]$Mode,
  [int]$ExitCode = 0
)

Write-Output "CpuStress starting. DurationSec=$DurationSec Mode=$Mode"
Start-Sleep -Seconds $DurationSec
Write-Output "CpuStress completed."
exit $ExitCode
