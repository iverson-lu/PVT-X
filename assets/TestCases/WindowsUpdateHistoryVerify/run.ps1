param(
    [Parameter(Mandatory = $true)][int] $MinExpectedCount = 1
)

$updateHistory = @()

try {
    # Create Windows Update Session and Query History
    $updateSession = New-Object -ComObject Microsoft.Update.Session
    $updateSearcher = $updateSession.CreateUpdateSearcher()
    
    # Get all update history entries
    $historyCount = $updateSearcher.GetTotalHistoryCount()
    
    if ($historyCount -gt 0) {
        # Query all history entries
        $history = $updateSearcher.QueryHistory(0, $historyCount)
        
        # Extract relevant information from each update
        foreach ($update in $history) {
            $updateInfo = [PSCustomObject]@{
                Title = $update.Title
                Date = $update.Date
                Operation = switch ($update.Operation) {
                    1 { "Installation" }
                    2 { "Uninstallation" }
                    3 { "Other" }
                    default { "Unknown" }
                }
                ResultCode = switch ($update.ResultCode) {
                    0 { "Not Started" }
                    1 { "In Progress" }
                    2 { "Succeeded" }
                    3 { "Succeeded With Errors" }
                    4 { "Failed" }
                    5 { "Aborted" }
                    default { "Unknown" }
                }
            }
            $updateHistory += $updateInfo
        }
    }
} catch {
    Write-Host "Error querying Windows Update history: $($_.Exception.Message)"
    $updateHistory = @()
}

$detectedCount = $updateHistory.Count
$failures = @()

if ($MinExpectedCount -lt 0) {
    $failures += "MinExpectedCount must be >= 0."
} elseif ($detectedCount -lt $MinExpectedCount) {
    $failures += "Expected at least $MinExpectedCount update(s) in history but found $detectedCount."
}

Write-Host "Windows Update History ($detectedCount entries):"
if ($updateHistory.Count -gt 0) {
    # Display up to 10 most recent updates
    $displayCount = [Math]::Min(10, $updateHistory.Count)
    for ($i = 0; $i -lt $displayCount; $i++) {
        $update = $updateHistory[$i]
        Write-Host " - [$($update.Date)] $($update.Title) - $($update.Operation): $($update.ResultCode)"
    }
    if ($updateHistory.Count -gt 10) {
        Write-Host " ... and $($updateHistory.Count - 10) more entries"
    }
} else {
    Write-Host " (No update history found)"
}

New-Item -ItemType Directory -Force -Path "artifacts" | Out-Null
$report = [ordered]@{
    minExpectedCount = $MinExpectedCount
    detectedCount = $detectedCount
    updates = $updateHistory | Select-Object -First 50  # Limit output to first 50 entries
    failures = $failures
}
$report | ConvertTo-Json -Depth 4 | Set-Content -Path "artifacts/windows-update-history.json"

if ($failures.Count -gt 0) {
    Write-Host "Result: FAIL"
    exit 1
}

Write-Host "Result: PASS"
exit 0
