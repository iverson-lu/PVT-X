param(
    [Parameter(Mandatory = $false)][string] $ExpectedUtcOffset = "+08:00"
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

$ExpectedUtcOffset = Normalize-QuotedString -Value $ExpectedUtcOffset

$timezoneInfo = $null
$failures = @()

try {
    # Get current timezone information
    $timezone = Get-TimeZone -ErrorAction Stop
    
    $currentUtcOffset = $timezone.BaseUtcOffset
    $currentUtcOffsetString = $currentUtcOffset.ToString("hh\:mm")
    
    # Add sign prefix
    if ($currentUtcOffset.TotalHours -ge 0) {
        $currentUtcOffsetString = "+$currentUtcOffsetString"
    } else {
        $currentUtcOffsetString = "-$($currentUtcOffset.ToString("hh\:mm").TrimStart('-'))"
    }
    
    $timezoneInfo = [ordered]@{
        Id = $timezone.Id
        DisplayName = $timezone.DisplayName
        StandardName = $timezone.StandardName
        DaylightName = $timezone.DaylightName
        CurrentUtcOffset = $currentUtcOffsetString
        BaseUtcOffset = $timezone.BaseUtcOffset.ToString()
        SupportsDaylightSavingTime = $timezone.SupportsDaylightSavingTime
        IsDaylightSavingTime = $timezone.IsDaylightSavingTime([DateTime]::Now)
    }
    
    Write-Host "Timezone Information:"
    Write-Host "  ID: $($timezone.Id)"
    Write-Host "  Display Name: $($timezone.DisplayName)"
    Write-Host "  Current UTC Offset: $currentUtcOffsetString"
    Write-Host ""
    
    # Validate expected UTC offset
    if (-not [string]::IsNullOrWhiteSpace($ExpectedUtcOffset)) {
        if ($currentUtcOffsetString -eq $ExpectedUtcOffset) {
            Write-Host "✓ Timezone UTC offset matches expected value: $ExpectedUtcOffset"
        }
        else {
            $message = "Timezone UTC offset mismatch. Expected: $ExpectedUtcOffset, Actual: $currentUtcOffsetString"
            $failures += $message
        }
    }
    
} catch {
    $message = "Failed to retrieve timezone information: $($_.Exception.Message)"
    $failures += $message
    Write-Host "Error: $message"
}

New-Item -ItemType Directory -Force -Path "artifacts" | Out-Null
$report = @{
    ExpectedUtcOffset = $ExpectedUtcOffset
    TimezoneInfo = $timezoneInfo
    Failures = $failures
}
$report | ConvertTo-Json -Depth 4 | Set-Content -Path "artifacts/timezone-check.json"

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "Result: FAIL"
    foreach ($failure in $failures) {
        Write-Host "  ✗ $failure"
    }
    exit 1
}

Write-Host ""
Write-Host "Result: PASS"
Write-Host "  ✓ Timezone validated successfully."
exit 0
