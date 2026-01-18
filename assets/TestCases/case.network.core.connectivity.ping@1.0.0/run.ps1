param(
    [Parameter(Mandatory = $false)]
    [string] $TargetAddress = "www.microsoft.com",

    [Parameter(Mandatory = $false)]
    [int] $MaxReplyTimeMs = 0
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
# Metadata (case-level only)
# ----------------------------
$TestId = "NetworkPingConnectivity"
$Ts  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# Artifacts
$ArtifactsRoot = Join-Path (Get-Location) "artifacts"
Ensure-Dir $ArtifactsRoot
$ReportPath = Join-Path $ArtifactsRoot "report.json"

# Defaults
$overallStatus = "FAIL"
$exitCode = 2  # 2 = script/runtime error unless overridden

# Single step definition
$step = @{
    id      = "ping_target"
    index   = 1
    name    = "Ping target address"
    status  = "FAIL"
    expected = @{
        reachable = $true
        reply_time_ms_lte = $null  # set below if threshold enabled
    }
    actual = @{
        reachable = $false
        status = $null
        resolved_address = $null
        reply_time_ms = $null
    }
    metrics = @{}
    message = $null
    timing = @{ duration_ms = $null }
    error = $null
}

# timers
$swTotal = [System.Diagnostics.Stopwatch]::StartNew()
$swStep = $null

try {
    $TargetAddress = Normalize-Text $TargetAddress

    if ([string]::IsNullOrWhiteSpace($TargetAddress)) {
        throw "TargetAddress is empty."
    }
    if ($MaxReplyTimeMs -lt 0) {
        throw "MaxReplyTimeMs must be >= 0."
    }

    if ($MaxReplyTimeMs -gt 0) {
        $step.expected.reply_time_ms_lte = $MaxReplyTimeMs
    }

    $swStep = [System.Diagnostics.Stopwatch]::StartNew()

    # Do the ping (single probe). Note: ICMP can be blocked in some environments.
    $reply = Test-Connection -TargetName $TargetAddress -Count 1 -ErrorAction Stop |
             Select-Object -First 1

    if ($null -eq $reply) {
        $step.status = "FAIL"
        $step.message = "No ping reply received"
        $exitCode = 1
    }
    else {
        $status = ($reply.PSObject.Properties.Name -contains "Status") ? [string]$reply.Status : "Success"
        $step.actual.status = $status

        $resolved = $null
        if ($reply.PSObject.Properties.Name -contains "DisplayAddress") {
            $resolved = [string]$reply.DisplayAddress
        } elseif ($reply.PSObject.Properties.Name -contains "Address") {
            $resolved = [string]$reply.Address
        }
        if ($resolved) { $step.actual.resolved_address = $resolved }

        $replyTimeMs = $null
        if ($reply.PSObject.Properties.Name -contains "ResponseTime") {
            $replyTimeMs = [int]$reply.ResponseTime
        } elseif ($reply.PSObject.Properties.Name -contains "Latency") {
            $replyTimeMs = [int]$reply.Latency
        }
        if ($replyTimeMs -ne $null) { $step.actual.reply_time_ms = $replyTimeMs }

        $reachable = ($status -eq "Success")
        $step.actual.reachable = $reachable

        # metrics (structured, machine-friendly)
        if ($replyTimeMs -ne $null) { $step.metrics.reply_time_ms = $replyTimeMs }
        if ($resolved) { $step.metrics.resolved_address = $resolved }
        $step.metrics.target_address = $TargetAddress

        if (-not $reachable) {
            $step.status = "FAIL"
            $step.message = "Ping failed (status=$status)"
            $exitCode = 1
        }
        elseif ($MaxReplyTimeMs -gt 0 -and $replyTimeMs -ne $null -and $replyTimeMs -gt $MaxReplyTimeMs) {
            $step.status = "FAIL"
            $step.message = "Reply time exceeded threshold"
            $exitCode = 1
        }
        else {
            $step.status = "PASS"
            $step.message = $null
            $exitCode = 0
        }
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
    # report.json (lightweight schema v1.0)
    # ----------------------------
    $report = @{
        schema = @{ version = "1.0" }
        test = @{
            id = $TestId
            name = $TestId
            params = @{
                target_address = $TargetAddress
                max_reply_time_ms = $MaxReplyTimeMs
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
    # stdout (compact)
    # ----------------------------
    $dotCount = [Math]::Max(3, 30 - $step.name.Length)
    $stepLine = "[1/1] {0} {1} {2}" -f $step.name, ("." * $dotCount), $step.status

    $details = New-Object System.Collections.Generic.List[string]
    if ($step.status -eq "PASS") {
        $d = "target=$TargetAddress"
        if ($step.actual.resolved_address) { $d += " resolved=$($step.actual.resolved_address)" }
        if ($step.actual.reply_time_ms -ne $null) { $d += " reply_time_ms=$($step.actual.reply_time_ms)" }
        $details.Add($d)
    }
    else {
        if ($step.message) { $details.Add("reason: $($step.message)") }

        # show expected only when it matters
        if ($MaxReplyTimeMs -gt 0) {
            $details.Add("expected: reply_time_ms <= $MaxReplyTimeMs")
        } else {
            $details.Add("expected: reachable=true")
        }

        $act = @()
        if ($step.actual.reply_time_ms -ne $null) { $act += "reply_time_ms=$($step.actual.reply_time_ms)" }
        if ($step.actual.status) { $act += "status=$($step.actual.status)" }
        if ($step.actual.resolved_address) { $act += "resolved=$($step.actual.resolved_address)" }
        if ($act.Count -gt 0) { $details.Add(("actual:   " + ($act -join " "))) }
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
