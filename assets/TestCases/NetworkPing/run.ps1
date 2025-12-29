param(
    [Parameter(Mandatory = $false)][string] $TargetAddress = "www.google.com"
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

$TargetAddress = Normalize-QuotedString -Value $TargetAddress

Write-Host "Network Ping Test - Target: $TargetAddress"
Write-Host ""

$failures = @()
$pingResults = @()
$success = $false

try {
    # Perform ping test with 4 packets
    $pingTest = Test-Connection -ComputerName $TargetAddress -Count 4 -ErrorAction Stop
    
    foreach ($result in $pingTest) {
        # Check Status property (PowerShell 7+) or StatusCode property (Windows PowerShell)
        $isSuccess = $false
        if ($null -ne $result.Status) {
            $isSuccess = ($result.Status -eq "Success")
        } elseif ($null -ne $result.StatusCode) {
            $isSuccess = ($result.StatusCode -eq 0)
        } elseif ($null -ne $result.ResponseTime) {
            # If we got a response time, consider it successful
            $isSuccess = $true
        }
        
        $pingInfo = [ordered]@{
            Address = if ($result.Address) { $result.Address.ToString() } else { $TargetAddress }
            ResponseTime = if ($result.ResponseTime) { $result.ResponseTime } else { 0 }
            Status = if ($isSuccess) { "Success" } else { "Failed" }
        }
        $pingResults += $pingInfo
    }
    
    # Check if all pings succeeded
    $successCount = ($pingResults | Where-Object { $_.Status -eq "Success" }).Count
    $totalCount = $pingResults.Count
    
    if ($successCount -eq $totalCount -and $totalCount -gt 0) {
        $success = $true
        $validResponseTimes = $pingResults | Where-Object { $_.ResponseTime -gt 0 } | Select-Object -ExpandProperty ResponseTime
        if ($validResponseTimes.Count -gt 0) {
            $avgResponseTime = ($validResponseTimes | Measure-Object -Average).Average
            Write-Host "Ping successful: $successCount/$totalCount packets received"
            Write-Host "Average response time: $([math]::Round($avgResponseTime, 2)) ms"
        } else {
            Write-Host "Ping successful: $successCount/$totalCount packets received"
        }
    } else {
        $failures += "Ping failed: only $successCount out of $totalCount packets received."
        Write-Host "Ping failed: $successCount/$totalCount packets received"
    }
    
} catch {
    $failures += "Unable to ping target '$TargetAddress': $($_.Exception.Message)"
    Write-Host "Error: $($_.Exception.Message)"
}

Write-Host ""
Write-Host "Ping Results:"
foreach ($result in $pingResults) {
    Write-Host "  Address: $($result.Address), Response Time: $($result.ResponseTime) ms, Status: $($result.Status)"
}

New-Item -ItemType Directory -Force -Path "artifacts" | Out-Null
$report = [ordered]@{
    targetAddress = $TargetAddress
    success = $success
    pingResults = $pingResults
    failures = $failures
}
$report | ConvertTo-Json -Depth 4 | Set-Content -Path "artifacts/network-ping.json"

if ($failures.Count -gt 0 -or -not $success) {
    Write-Host ""
    Write-Host "Result: FAIL"
    foreach ($failure in $failures) {
        Write-Host "  ✗ $failure"
    }
    exit 1
}

Write-Host ""
Write-Host "Result: PASS"
Write-Host "  ✓ Network connectivity confirmed to $TargetAddress"
exit 0
