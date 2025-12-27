param(
    [int]$DurationSec,
    [string]$Mode,
    [string[]]$Flags,
    [string]$SecretToken
)

Write-Output "CpuStress running for $DurationSec seconds"
if ($SecretToken) {
    Write-Output "Secret=$SecretToken"
}
Start-Sleep -Seconds $DurationSec

if ($Mode -eq "Full") {
    exit 1
}
exit 0
