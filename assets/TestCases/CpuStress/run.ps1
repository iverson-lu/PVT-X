param(
  [int]$DurationSec,
  [string]$Mode,
  [string]$Password
)

Write-Output "CpuStress running for $DurationSec sec in mode $Mode"
Start-Sleep -Seconds $DurationSec

if ($Mode -eq "B") {
  Write-Error "Mode B indicates failure"
  exit 1
}

exit 0
