param(
    [Parameter(Mandatory = $false)]
    [string] $ExpectedUtcOffset = "+08:00"
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
$TestId = "TimezoneCheck"
$TsUtc  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# Normalize parameters
$ExpectedUtcOffset = Normalize-Text $ExpectedUtcOffset

# Artifacts
$ArtifactsRoot = Join-Path (Get-Location) "artifacts"
Ensure-Dir $ArtifactsRoot
$ReportPath = Join-Path $ArtifactsRoot "report.json"

# Defaults
$overallStatus = "FAIL"
$exitCode = 2

# Step definition
$step = @{
    id      = "check_timezone"
    index   = 1
    name    = "Check system timezone"
    status  = "FAIL"
    expected = @{
        utc_offset = if ([string]::IsNullOrWhiteSpace($ExpectedUtcOffset)) { $null } else { $ExpectedUtcOffset }
    }
    actual = @{
        timezone_id = $null
        display_name = $null
        current_utc_offset = $null
        base_utc_offset = $null
        supports_dst = $null
        is_dst_active = $null
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
    $swStep = [System.Diagnostics.Stopwatch]::StartNew()

    # Query timezone information
    $timezone = Get-TimeZone -ErrorAction Stop
    
    if (-not $timezone) {
        throw "Unable to retrieve timezone information"
    }

    # Calculate current UTC offset (considering DST if active)
    $currentUtcOffset = $timezone.BaseUtcOffset
    $currentUtcOffsetString = $currentUtcOffset.ToString("hh\:mm")
    
    # Add sign prefix
    if ($currentUtcOffset.TotalHours -ge 0) {
        $currentUtcOffsetString = "+$currentUtcOffsetString"
    } else {
        $currentUtcOffsetString = "-$($currentUtcOffset.ToString("hh\:mm").TrimStart('-'))"
    }

    $step.actual.timezone_id = $timezone.Id
    $step.actual.display_name = $timezone.DisplayName
    $step.actual.current_utc_offset = $currentUtcOffsetString
    $step.actual.base_utc_offset = $timezone.BaseUtcOffset.ToString()
    $step.actual.supports_dst = $timezone.SupportsDaylightSavingTime
    $step.actual.is_dst_active = $timezone.IsDaylightSavingTime([DateTime]::Now)

    # Metrics
    $step.metrics.timezone_id = $timezone.Id
    $step.metrics.utc_offset = $currentUtcOffsetString
    $step.metrics.supports_dst = $timezone.SupportsDaylightSavingTime
    $step.metrics.is_dst_active = $timezone.IsDaylightSavingTime([DateTime]::Now)

    # Validation
    if (-not [string]::IsNullOrWhiteSpace($ExpectedUtcOffset)) {
        if ($currentUtcOffsetString -eq $ExpectedUtcOffset) {
            $step.status = "PASS"
            $exitCode = 0
        }
        else {
            $step.status = "FAIL"
            $step.message = "Timezone UTC offset mismatch"
            $exitCode = 1
        }
    }
    else {
        # No validation required, just report timezone info
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
                expected_utc_offset = $ExpectedUtcOffset
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
        $d = "id='$($step.actual.timezone_id)' offset=$($step.actual.current_utc_offset)"
        $details.Add($d)
        $details.Add("display='$($step.actual.display_name)'")
        if (-not [string]::IsNullOrWhiteSpace($ExpectedUtcOffset)) {
            $details.Add("validation: offset matches '$ExpectedUtcOffset'")
        }
    }
    else {
        if ($step.message) { $details.Add("reason: $($step.message)") }
        $details.Add("expected: utc_offset='$ExpectedUtcOffset'")
        $details.Add("actual:   utc_offset='$($step.actual.current_utc_offset)' id='$($step.actual.timezone_id)'")
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
