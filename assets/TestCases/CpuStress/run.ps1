param(
    [int]$DurationSec,
    [string]$Mode,
    [bool]$Fail = $false
)

Write-Output "CpuStress start DurationSec=$DurationSec Mode=$Mode Fail=$Fail"
Start-Sleep -Seconds $DurationSec

if ($Fail) {
    Write-Output "CpuStress requested failure"
    exit 1
}

Write-Output "CpuStress completed"
exit 0
