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
    
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "   Timezone Verification" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Timezone ID:       $($timezone.Id)" -ForegroundColor White
    Write-Host "Display Name:      $($timezone.DisplayName)" -ForegroundColor White
    Write-Host "Standard Name:     $($timezone.StandardName)" -ForegroundColor White
    Write-Host "Current UTC Offset: $currentUtcOffsetString" -ForegroundColor White
    Write-Host "Base UTC Offset:   $($timezone.BaseUtcOffset)" -ForegroundColor White
    Write-Host "Supports DST:      $($timezone.SupportsDaylightSavingTime)" -ForegroundColor White
    Write-Host "Is DST Active:     $($timezone.IsDaylightSavingTime([DateTime]::Now))" -ForegroundColor White
    Write-Host ""
    
    # Validate expected UTC offset
    if ([string]::IsNullOrWhiteSpace($ExpectedUtcOffset)) {
        Write-Host "✓ No expected UTC offset specified - verification skipped" -ForegroundColor Yellow
    }
    else {
        Write-Host "Expected UTC Offset: $ExpectedUtcOffset" -ForegroundColor White
        
        if ($currentUtcOffsetString -eq $ExpectedUtcOffset) {
            Write-Host "✓ Timezone UTC offset matches expected value" -ForegroundColor Green
        }
        else {
            $message = "Timezone UTC offset mismatch. Expected: $ExpectedUtcOffset, Actual: $currentUtcOffsetString"
            Write-Host "✗ $message" -ForegroundColor Red
            $failures += $message
        }
    }
    
} catch {
    $message = "Failed to retrieve timezone information: $($_.Exception.Message)"
    Write-Host "✗ $message" -ForegroundColor Red
    $failures += $message
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan

# Output result metadata
$result = @{
    TimezoneInfo = $timezoneInfo
    ExpectedUtcOffset = $ExpectedUtcOffset
    ValidationPassed = $failures.Count -eq 0
    Failures = $failures
}

$resultJson = $result | ConvertTo-Json -Depth 10
Write-Host ""
Write-Host "Result Metadata:" -ForegroundColor Gray
Write-Host $resultJson -ForegroundColor Gray
Write-Host ""

# Return appropriate exit code
if ($failures.Count -gt 0) {
    Write-Host "Test FAILED with $($failures.Count) error(s)" -ForegroundColor Red
    exit 1
} else {
    Write-Host "Test PASSED" -ForegroundColor Green
    exit 0
}
