param(
    [Parameter(Mandatory = $false)]
    [int] $EventCount = 100,

    [Parameter(Mandatory = $false)]
    [string] $ProhibitedLevel = "Error",

    [Parameter(Mandatory = $false)]
    [string] $LogName = "System"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ----------------------------
# Helpers
# ----------------------------
function Ensure-Dir([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Write-JsonFile([string] $Path, $Obj) {
    $Obj | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Normalize-Text([string] $s) {
    if ($null -eq $s) { return "" }
    $t = $s.Trim()
    while (($t.StartsWith('"') -and $t.EndsWith('"')) -or ($t.StartsWith("'") -and $t.EndsWith("'"))) {
        if ($t.Length -lt 2) { break }
        $t = $t.Substring(1, $t.Length - 2).Trim()
    }
    return $t
}

function Write-Stdout-Compact {
    param(
        [string]   $TestName,
        [string]   $Overall,
        [int]      $ExitCode,
        [string]   $TsUtc,
        [string]   $StepLine,
        [string[]] $StepDetails,
        [int]      $Total,
        [int]      $Passed,
        [int]      $Failed,
        [int]      $Skipped
    )

    Write-Output "=================================================="
    Write-Output ("TEST: {0}  RESULT: {1}  EXIT: {2}" -f $TestName, $Overall, $ExitCode)
    Write-Output ("UTC:  {0}" -f $TsUtc)
    Write-Output "--------------------------------------------------"
    Write-Output $StepLine
    foreach ($d in $StepDetails) { Write-Output ("      " + $d) }
    Write-Output "--------------------------------------------------"
    Write-Output ("SUMMARY: total={0} passed={1} failed={2} skipped={3}" -f $Total, $Passed, $Failed, $Skipped)
    Write-Output "=================================================="
    Write-Output ("MACHINE: overall={0} exit_code={1}" -f $Overall, $ExitCode)
}

# ----------------------------
# Metadata
# ----------------------------
$TestId = "EventViewerCheck"
$TsUtc  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# Normalize parameters
$ProhibitedLevel = Normalize-Text $ProhibitedLevel
$LogName = Normalize-Text $LogName

# Artifacts
$ArtifactsRoot = Join-Path (Get-Location) "artifacts"
Ensure-Dir $ArtifactsRoot
$ReportPath = Join-Path $ArtifactsRoot "report.json"

# Defaults
$overallStatus = "FAIL"
$exitCode = 2

# Map level string to level integer
$levelMap = @{
    "Critical" = 1
    "Error" = 2
    "Warning" = 3
    "Information" = 4
    "Verbose" = 5
}

# Step definition
$step = @{
    id      = "check_event_log"
    index   = 1
    name    = "Check Event Viewer for prohibited level"
    status  = "FAIL"
    expected = @{
        prohibited_count = 0
        log_accessible = $true
    }
    actual = @{
        log_name = $LogName
        total_checked = 0
        prohibited_count = 0
        prohibited_level = $ProhibitedLevel
        prohibited_events = @()
    }
    metrics = @{}
    message = $null
    timing = @{ duration_ms = $null }
    error = $null
}

# Timers
$swTotal = [System.Diagnostics.Stopwatch]::StartNew()
$swStep = $null

try {
    # Validate EventCount
    if ($EventCount -le 0) {
        throw "EventCount must be a positive integer"
    }

    # Validate ProhibitedLevel
    if (-not $levelMap.ContainsKey($ProhibitedLevel)) {
        $validLevels = $levelMap.Keys -join ", "
        throw "Invalid ProhibitedLevel '$ProhibitedLevel'. Valid values are: $validLevels"
    }

    $prohibitedLevelInt = $levelMap[$ProhibitedLevel]
    
    $swStep = [System.Diagnostics.Stopwatch]::StartNew()

    # Query Event Viewer
    $events = Get-WinEvent -LogName $LogName -MaxEvents $EventCount -ErrorAction Stop
    
    $allEvents = @()
    $prohibitedEvents = @()
    
    foreach ($event in $events) {
        $eventInfo = [ordered]@{
            time_created = $event.TimeCreated.ToString("yyyy-MM-dd HH:mm:ss")
            level = $event.Level
            level_name = $event.LevelDisplayName
            id = $event.Id
            provider = $event.ProviderName
            message = if ($event.Message) { $event.Message.Substring(0, [Math]::Min(200, $event.Message.Length)) } else { "" }
        }
        
        $allEvents += $eventInfo
        
        if ($event.Level -eq $prohibitedLevelInt) {
            $prohibitedEvents += $eventInfo
        }
    }

    $totalCount = $allEvents.Count
    $prohibitedCount = $prohibitedEvents.Count

    $step.actual.total_checked = $totalCount
    $step.actual.prohibited_count = $prohibitedCount
    $step.actual.prohibited_events = $prohibitedEvents

    # Metrics
    $step.metrics.log_name = $LogName
    $step.metrics.event_count_requested = $EventCount
    $step.metrics.total_checked = $totalCount
    $step.metrics.prohibited_count = $prohibitedCount
    $step.metrics.prohibited_level = $ProhibitedLevel

    # Validation
    if ($prohibitedCount -gt 0) {
        $step.status = "FAIL"
        $step.message = "Found $prohibitedCount event(s) with prohibited level '$ProhibitedLevel'"
        $exitCode = 1
    }
    else {
        $step.status = "PASS"
        $exitCode = 0
    }

    $overallStatus = ($exitCode -eq 0) ? "PASS" : "FAIL"
}
catch {
    $overallStatus = "FAIL"
    $step.status = "FAIL"
    $step.message = "Script error: $($_.Exception.Message)"
    $step.error = @{
        kind = "SCRIPT"
        code = "SCRIPT_ERROR"
        message = $_.Exception.Message
        exception_type = $_.Exception.GetType().FullName
        stack = $_.ScriptStackTrace
    }
    $exitCode = 2
}
finally {
    if ($swStep -ne $null) {
        $swStep.Stop()
        $step.timing.duration_ms = [int]$swStep.ElapsedMilliseconds
    }
    $swTotal.Stop()
    $totalMs = [int]$swTotal.ElapsedMilliseconds

    $passCount = ($step.status -eq "PASS") ? 1 : 0
    $failCount = ($step.status -eq "FAIL") ? 1 : 0
    $skipCount = ($step.status -eq "SKIP") ? 1 : 0

    # ----------------------------
    # report.json
    # ----------------------------
    $report = @{
        schema = @{ version = "1.0" }
        test = @{
            id = $TestId
            name = $TestId
            params = @{
                event_count = $EventCount
                prohibited_level = $ProhibitedLevel
                log_name = $LogName
            }
        }
        summary = @{
            status = $overallStatus
            exit_code = $exitCode
            counts = @{
                total = 1
                pass = $passCount
                fail = $failCount
                skip = $skipCount
            }
            duration_ms = $totalMs
        }
        steps = @($step)
    }

    Write-JsonFile $ReportPath $report

    # ----------------------------
    # stdout
    # ----------------------------
    $dotCount = [Math]::Max(3, 30 - $step.name.Length)
    $stepLine = "[1/1] {0} {1} {2}" -f $step.name, ("." * $dotCount), $step.status

    $details = New-Object System.Collections.Generic.List[string]
    if ($step.status -eq "PASS") {
        $d = "log='$LogName' checked=$($step.actual.total_checked) prohibited_level='$ProhibitedLevel' found=0"
        $details.Add($d)
    }
    else {
        if ($step.message) { $details.Add("reason: $($step.message)") }
        $details.Add("expected: prohibited_count=0")
        $details.Add("actual:   prohibited_count=$($step.actual.prohibited_count) total_checked=$($step.actual.total_checked)")
        
        # Show sample of prohibited events
        if ($step.actual.prohibited_events.Count -gt 0) {
            $displayCount = [Math]::Min(3, $step.actual.prohibited_events.Count)
            $details.Add("sample events:")
            for ($i = 0; $i -lt $displayCount; $i++) {
                $evt = $step.actual.prohibited_events[$i]
                $details.Add("  [$($evt.time_created)] $($evt.level_name) ID=$($evt.id) $($evt.provider)")
            }
            if ($step.actual.prohibited_events.Count -gt $displayCount) {
                $details.Add("  ... and $($step.actual.prohibited_events.Count - $displayCount) more")
            }
        }
    }

    Write-Stdout-Compact `
        -TestName $TestId `
        -Overall $overallStatus `
        -ExitCode $exitCode `
        -TsUtc $TsUtc `
        -StepLine $stepLine `
        -StepDetails $details.ToArray() `
        -Total 1 `
        -Passed $passCount `
        -Failed $failCount `
        -Skipped $skipCount

    exit $exitCode
}
