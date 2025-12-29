param(
    [Parameter(Mandatory = $false)][string] $Name = "World",
    [Parameter(Mandatory = $false)][int] $Repeat = 1,
    [Parameter(Mandatory = $false)][string[]] $Modes = @()
)

Write-Host "Hello $Name"
Write-Host "Repeat: $Repeat"
if ($Modes.Count -gt 0) {
    Write-Host "Modes: $($Modes -join ',')"
}

New-Item -ItemType Directory -Force -Path "artifacts" | Out-Null
Set-Content -Path "artifacts/output.txt" -Value "Hello $Name x$Repeat with modes: $($Modes -join ',')"

for ($i = 0; $i -lt $Repeat; $i++) {
    Write-Host "Iteration $i"
}

exit 0
