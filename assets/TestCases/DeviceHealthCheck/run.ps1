param(
    [Parameter(Mandatory = $false)][string] $RestrictedMode = "false"
)

# Removes the surrounding quotes injected by the runner so comparisons use the raw string.
function Normalize-QuotedString {
    param(
        [string] $Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $Value
    }

    $trimmed = $Value.Trim()

    if ($trimmed.Length -ge 2 -and $trimmed.StartsWith("'") -and $trimmed.EndsWith("'")) {
        $inner = $trimmed.Substring(1, $trimmed.Length - 2)
        return $inner.Replace("''", "'")
    }

    if ($trimmed.Length -ge 2 -and $trimmed.StartsWith('"') -and $trimmed.EndsWith('"')) {
        $inner = $trimmed.Substring(1, $trimmed.Length - 2)
        return $inner.Replace('`"', '"')
    }

    return $trimmed
}

# Convert string parameter to boolean
$RestrictedMode = Normalize-QuotedString -Value $RestrictedMode
$RestrictedModeBool = $false
if ($RestrictedMode -eq "true" -or $RestrictedMode -eq "True" -or $RestrictedMode -eq "1" -or $RestrictedMode -eq "TRUE" -or $RestrictedMode -eq "`$true" -or $RestrictedMode -eq "`$True" -or $RestrictedMode -eq $true) {
    $RestrictedModeBool = $true
}

Write-Host "Device Health Check - Restricted Mode: $RestrictedModeBool (input: '$RestrictedMode')"
Write-Host ""

$allDevices = @()
$problemDevices = @()
$disabledDevices = @()
$okDevices = @()

try {
    $devices = Get-CimInstance -ClassName Win32_PnPEntity -ErrorAction Stop
    
    foreach ($device in $devices) {
        $deviceInfo = [ordered]@{
            Name = $device.Name
            DeviceID = $device.DeviceID
            Status = $device.Status
            ConfigManagerErrorCode = $device.ConfigManagerErrorCode
            PNPDeviceID = $device.PNPDeviceID
        }
        
        $allDevices += $deviceInfo
        
        # ConfigManagerErrorCode meanings:
        # 0  = Device is working properly
        # 22 = Device is disabled
        # Other non-zero values indicate problems (yellow bang)
        
        if ($null -eq $device.ConfigManagerErrorCode) {
            # Some devices may not report error code, treat as OK
            $okDevices += $deviceInfo
        } elseif ($device.ConfigManagerErrorCode -eq 0) {
            $okDevices += $deviceInfo
        } elseif ($device.ConfigManagerErrorCode -eq 22) {
            $disabledDevices += $deviceInfo
        } else {
            $problemDevices += $deviceInfo
        }
    }
} catch {
    Write-Host "Error querying devices: $_"
    exit 1
}

$totalCount = $allDevices.Count
$okCount = $okDevices.Count
$disabledCount = $disabledDevices.Count
$problemCount = $problemDevices.Count

Write-Host "Device Summary:"
Write-Host "  Total devices: $totalCount"
Write-Host "  OK devices: $okCount"
Write-Host "  Disabled devices: $disabledCount"
Write-Host "  Problem devices (yellow bang): $problemCount"
Write-Host ""

$failures = @()

if ($RestrictedModeBool) {
    # Restricted mode: All devices must be OK (no disabled, no problem devices)
    if ($disabledCount -gt 0) {
        $failures += "Restricted mode requires all devices to be enabled, but found $disabledCount disabled device(s)."
        Write-Host "Disabled devices:"
        foreach ($dev in $disabledDevices) {
            Write-Host "  - $($dev.Name) (Error Code: $($dev.ConfigManagerErrorCode))"
        }
        Write-Host ""
    }
    
    if ($problemCount -gt 0) {
        $failures += "Restricted mode requires all devices to be working properly, but found $problemCount device(s) with errors."
        Write-Host "Problem devices:"
        foreach ($dev in $problemDevices) {
            Write-Host "  - $($dev.Name) (Error Code: $($dev.ConfigManagerErrorCode))"
        }
        Write-Host ""
    }
} else {
    # Normal mode: Only fail if there are problem devices (yellow bang)
    if ($problemCount -gt 0) {
        $failures += "Found $problemCount device(s) with errors (yellow bang in Device Manager)."
        Write-Host "Problem devices:"
        foreach ($dev in $problemDevices) {
            Write-Host "  - $($dev.Name) (Error Code: $($dev.ConfigManagerErrorCode))"
        }
        Write-Host ""
    }
}

New-Item -ItemType Directory -Force -Path "artifacts" | Out-Null
$report = [ordered]@{
    restrictedMode = $RestrictedModeBool
    totalCount = $totalCount
    okCount = $okCount
    disabledCount = $disabledCount
    problemCount = $problemCount
    problemDevices = $problemDevices
    disabledDevices = if ($RestrictedModeBool) { $disabledDevices } else { @() }
    failures = $failures
}
$report | ConvertTo-Json -Depth 4 | Set-Content -Path "artifacts/device-health-check.json"

if ($failures.Count -gt 0) {
    Write-Host "Result: FAIL"
    foreach ($failure in $failures) {
        Write-Host "  ✗ $failure"
    }
    exit 1
}

Write-Host "Result: PASS"
Write-Host "  ✓ All devices are in acceptable state."
exit 0
