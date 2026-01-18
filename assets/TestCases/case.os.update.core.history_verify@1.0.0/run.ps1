param(
    [Parameter(Mandatory = $false)]
    [int] $MinExpectedCount = 1
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
        [string]   $Ts,
        [string]   $StepLine,
        [string[]] $StepDetails,
        [int]      $Total,
        [int]      $Passed,
        [int]      $Failed,
        [int]      $Skipped
    )

    Write-Output "=================================================="
    Write-Output ("TEST: {0}  RESULT: {1}  EXIT: {2}" -f $TestName, $Overall, $ExitCode)
    Write-Output ("UTC:  {0}" -f $Ts)
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
$TestId = "WindowsUpdateHistoryVerify"
$Ts  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# Artifacts
$ArtifactsRoot = Join-Path (Get-Location) "artifacts"
Ensure-Dir $ArtifactsRoot
$ReportPath = Join-Path $ArtifactsRoot "report.json"

# Defaults
$overallStatus = "FAIL"
$exitCode = 2

# Step definition
$step = @{
    id      = "verify_update_history"
    index   = 1
    name    = "Verify Windows Update history count"
    status  = "FAIL"
    expected = @{
        min_update_count = $MinExpectedCount
    }
    actual = @{
        detected_count = 0
        updates = @()
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
    # Validate MinExpectedCount
    if ($MinExpectedCount -lt 0) {
        throw "MinExpectedCount must be >= 0"
    }

    $swStep = [System.Diagnostics.Stopwatch]::StartNew()

    # Query Windows Update history
    $updateHistory = @()
    
    $updateSession = New-Object -ComObject Microsoft.Update.Session -ErrorAction Stop
    $updateSearcher = $updateSession.CreateUpdateSearcher()
    
    $historyCount = $updateSearcher.GetTotalHistoryCount()
    
    if ($historyCount -gt 0) {
        $history = $updateSearcher.QueryHistory(0, $historyCount)
        
        foreach ($update in $history) {
            $updateInfo = @{
                title = $update.Title
                date = $update.Date.ToString("yyyy-MM-dd HH:mm:ss")
                operation = switch ($update.Operation) {
                    1 { "Installation" }
                    2 { "Uninstallation" }
                    3 { "Other" }
                    default { "Unknown" }
                }
                result_code = switch ($update.ResultCode) {
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

    $detectedCount = $updateHistory.Count
    
    $step.actual.detected_count = $detectedCount
    $step.actual.updates = $updateHistory

    # Metrics
    $step.metrics.detected_count = $detectedCount
    $step.metrics.min_expected = $MinExpectedCount
    
    # Count by result
    $succeeded = ($updateHistory | Where-Object { $_.result_code -eq "Succeeded" }).Count
    $failed = ($updateHistory | Where-Object { $_.result_code -eq "Failed" }).Count
    $step.metrics.succeeded_count = $succeeded
    $step.metrics.failed_count = $failed

    # Validation
    if ($detectedCount -ge $MinExpectedCount) {
        $step.status = "PASS"
        $exitCode = 0
    }
    else {
        $step.status = "FAIL"
        $step.message = "Insufficient update history entries"
        $exitCode = 1
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
                min_expected_count = $MinExpectedCount
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
        $d = "detected=$($step.actual.detected_count) min_expected=$MinExpectedCount"
        $details.Add($d)
        if ($step.metrics.succeeded_count -or $step.metrics.failed_count) {
            $details.Add("succeeded=$($step.metrics.succeeded_count) failed=$($step.metrics.failed_count)")
        }
        
        # Show sample of recent updates
        if ($step.actual.updates.Count -gt 0) {
            $displayCount = [Math]::Min(3, $step.actual.updates.Count)
            $details.Add("recent updates:")
            for ($i = 0; $i -lt $displayCount; $i++) {
                $upd = $step.actual.updates[$i]
                $details.Add("  [$($upd.date)] $($upd.operation): $($upd.result_code)")
            }
            if ($step.actual.updates.Count -gt $displayCount) {
                $details.Add("  ... and $($step.actual.updates.Count - $displayCount) more")
            }
        }
    }
    else {
        if ($step.message) { $details.Add("reason: $($step.message)") }
        $details.Add("expected: min_expected_count >= $MinExpectedCount")
        $details.Add("actual:   detected_count=$($step.actual.detected_count)")
    }

    Write-Stdout-Compact `
        -TestName $TestId `
        -Overall $overallStatus `
        -ExitCode $exitCode `
        -Ts $Ts `
        -StepLine $stepLine `
        -StepDetails $details.ToArray() `
        -Total 1 `
        -Passed $passCount `
        -Failed $failCount `
        -Skipped $skipCount

    exit $exitCode
}
