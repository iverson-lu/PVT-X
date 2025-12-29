param(
    [Parameter(Mandatory = $false)][string] $EventCount = "100",
    [Parameter(Mandatory = $false)][string] $ProhibitedLevel = "Error",
    [Parameter(Mandatory = $false)][string] $LogName = "System"
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

# Normalize parameters
$EventCount = Normalize-QuotedString -Value $EventCount
$ProhibitedLevel = Normalize-QuotedString -Value $ProhibitedLevel
$LogName = Normalize-QuotedString -Value $LogName

# Convert EventCount to integer
try {
    $EventCountInt = [int]$EventCount
    if ($EventCountInt -le 0) {
        Write-Host "Error: EventCount must be a positive integer."
        exit 1
    }
} catch {
    Write-Host "Error: EventCount '$EventCount' is not a valid integer."
    exit 1
}

# Map level string to level integer
# https://docs.microsoft.com/en-us/windows/win32/eventlog/event-levels
$levelMap = @{
    "Critical" = 1
    "Error" = 2
    "Warning" = 3
    "Information" = 4
    "Verbose" = 5
}

$prohibitedLevelInt = $null
if ($levelMap.ContainsKey($ProhibitedLevel)) {
    $prohibitedLevelInt = $levelMap[$ProhibitedLevel]
} else {
    Write-Host "Error: Invalid ProhibitedLevel '$ProhibitedLevel'. Valid values are: Critical, Error, Warning, Information, Verbose"
    exit 1
}

Write-Host "Event Viewer Level Check"
Write-Host "  Log Name: $LogName"
Write-Host "  Event Count: $EventCountInt"
Write-Host "  Prohibited Level: $ProhibitedLevel (Level $prohibitedLevelInt)"
Write-Host ""

$allEvents = @()
$prohibitedEvents = @()

try {
    Write-Host "Retrieving the most recent $EventCountInt events from '$LogName' log..."
    
    # Get events from the specified log
    $events = Get-WinEvent -LogName $LogName -MaxEvents $EventCountInt -ErrorAction Stop
    
    Write-Host "Retrieved $($events.Count) events."
    Write-Host ""
    
    foreach ($event in $events) {
        $eventInfo = [ordered]@{
            TimeCreated = $event.TimeCreated.ToString("yyyy-MM-dd HH:mm:ss")
            Level = $event.Level
            LevelDisplayName = $event.LevelDisplayName
            Id = $event.Id
            ProviderName = $event.ProviderName
            Message = if ($event.Message) { $event.Message.Substring(0, [Math]::Min(200, $event.Message.Length)) } else { "" }
        }
        
        $allEvents += $eventInfo
        
        # Check if this event has the prohibited level
        if ($event.Level -eq $prohibitedLevelInt) {
            $prohibitedEvents += $eventInfo
        }
    }
} catch {
    Write-Host "Error querying Event Viewer: $_"
    Write-Host ""
    Write-Host "Common causes:"
    Write-Host "  - Log name '$LogName' does not exist"
    Write-Host "  - Insufficient permissions to access the log"
    Write-Host "  - The log is empty"
    exit 1
}

$totalCount = $allEvents.Count
$prohibitedCount = $prohibitedEvents.Count

Write-Host "Event Summary:"
Write-Host "  Total events checked: $totalCount"
Write-Host "  Events with '$ProhibitedLevel' level: $prohibitedCount"
Write-Host ""

$failures = @()

if ($prohibitedCount -gt 0) {
    $failures += "Found $prohibitedCount event(s) with prohibited level '$ProhibitedLevel' in the most recent $EventCountInt events."
    Write-Host "Prohibited level events:"
    $displayCount = [Math]::Min(10, $prohibitedCount)
    for ($i = 0; $i -lt $displayCount; $i++) {
        $evt = $prohibitedEvents[$i]
        Write-Host "  - [$($evt.TimeCreated)] $($evt.LevelDisplayName) - ID $($evt.Id) - $($evt.ProviderName)"
        if ($evt.Message) {
            $msgPreview = $evt.Message.Replace("`r", "").Replace("`n", " ")
            Write-Host "    $msgPreview"
        }
    }
    if ($prohibitedCount -gt $displayCount) {
        Write-Host "  ... and $($prohibitedCount - $displayCount) more event(s)"
    }
    Write-Host ""
}

# Create artifacts directory and save report
New-Item -ItemType Directory -Force -Path "artifacts" | Out-Null
$report = [ordered]@{
    logName = $LogName
    eventCount = $EventCountInt
    prohibitedLevel = $ProhibitedLevel
    prohibitedLevelInt = $prohibitedLevelInt
    totalCount = $totalCount
    prohibitedCount = $prohibitedCount
    prohibitedEvents = $prohibitedEvents
    failures = $failures
}
$report | ConvertTo-Json -Depth 4 | Set-Content -Path "artifacts/event-viewer-check.json"

if ($failures.Count -gt 0) {
    Write-Host "Result: FAIL"
    foreach ($failure in $failures) {
        Write-Host "  ✗ $failure"
    }
    exit 1
}

Write-Host "Result: PASS"
Write-Host "  ✓ No events with prohibited level '$ProhibitedLevel' found in the most recent $EventCountInt events."
exit 0
