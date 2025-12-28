param(
    [int]$DurationSec,
    [string]$Mode,
    [int]$ExitCode = 0
)

Write-Output "CpuStress running for $DurationSec seconds. Mode=$Mode"
Start-Sleep -Seconds $DurationSec
Write-Output "CpuStress complete."
exit $ExitCode
