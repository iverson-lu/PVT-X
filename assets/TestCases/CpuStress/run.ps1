param(
  [int]$DurationSec,
  [string]$Mode,
  [bool]$Verbose
)

Write-Output "CpuStress start: Mode=$Mode Duration=$DurationSec Verbose=$Verbose"
Start-Sleep -Seconds $DurationSec
if ($Mode -eq "B") {
  Write-Output "Simulated failure"
  exit 1
}
Write-Output "CpuStress completed"
exit 0
