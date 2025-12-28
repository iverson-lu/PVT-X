param(
    [int]$DurationSec = 1,
    [string]$Mode = "A",
    [bool]$Fail = $false
)

Write-Output "CpuStress starting. Duration=$DurationSec Mode=$Mode"
Start-Sleep -Seconds $DurationSec
if ($Fail) {
    Write-Error "Forced failure"
    exit 1
}

Write-Output "CpuStress completed."
exit 0
