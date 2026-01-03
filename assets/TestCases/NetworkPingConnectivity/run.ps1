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
    foreach ($d in $StepDetails) {
        Write-Output ("      " + $d)
    }
    Write-Output "--------------------------------------------------"
    Write-Output ("SUMMARY: total={0} passed={1} failed={2} skipped={3}" -f $Total, $Passed, $Failed, $Skipped)
    Write-Output "=================================================="
    Write-Output ("MACHINE: overall={0} exit_code={1}" -f $Overall, $ExitCode)
}

# ----------------------------
# Metadata (case-level only)
# ----------------------------
$TestId = "NetworkPingConnectivity"
$TsUtc  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# Artifacts
$ArtifactsRoot = Join-Path (Get-Location) "artifacts"
Ensure-Dir $ArtifactsRoot
$ReportPath = Join-Path $ArtifactsRoot "report.json"

# Defaults
$overallResult = "FAIL"
$exitCode = 2

# Single step definition
$stepIndex   = 1
$stepId      = "ping_target"
$stepName    = "Ping target address"
$stepStatus  = "FAIL"
$stepReason  = ""
$stepExpected = ""
$stepActual   = ""
$stepMetrics  = @{}
$stepEvidence = @{}
$stepError    = $null

try {
    $TargetAddress = Normalize-Text $TargetAddress

    if ([string]::IsNullOrWhiteSpace($TargetAddress)) {
        throw "TargetAddress is empty."
    }
    if ($MaxReplyTimeMs -lt 0) {
        throw "MaxReplyTimeMs must be >= 0."
    }

    $stepExpected = ($MaxReplyTimeMs -gt 0) ?
        "reply_time_ms <= $MaxReplyTimeMs" :
        "ping succeeds"

    $stepEvidence["target_address"] = $TargetAddress
    $stepEvidence["max_reply_time_ms"] = $MaxReplyTimeMs

    $reply = Test-Connection -TargetName $TargetAddress -Count 1 -ErrorAction Stop |
             Select-Object -First 1

    if ($null -eq $reply) {
        $stepReason = "No ping reply received"
        $exitCode = 1
    }
    else {
        $status = ($reply.PSObject.Properties.Name -contains "Status") ?
            [string]$reply.Status : "Success"

        $resolved = $null
        if ($reply.PSObject.Properties.Name -contains "DisplayAddress") {
            $resolved = [string]$reply.DisplayAddress
        } elseif ($reply.PSObject.Properties.Name -contains "Address") {
            $resolved = [string]$reply.Address
        }

        $replyTimeMs = $null
        if ($reply.PSObject.Properties.Name -contains "ResponseTime") {
            $replyTimeMs = [int]$reply.ResponseTime
        }

        $actualParts = @("status=$status")
        if ($resolved) { $actualParts += "resolved=$resolved" }
        if ($replyTimeMs -ne $null) { $actualParts += "reply_time_ms=$replyTimeMs" }
        $stepActual = $actualParts -join " "

        if ($replyTimeMs -ne $null) {
            $stepMetrics["reply_time_ms"] = $replyTimeMs
        }

        if ($status -ne "Success") {
            $stepReason = "Ping failed (status=$status)"
            $exitCode = 1
        }
        elseif ($MaxReplyTimeMs -gt 0 -and $replyTimeMs -ne $null -and $replyTimeMs -gt $MaxReplyTimeMs) {
            $stepReason = "Reply time exceeded threshold"
            $exitCode = 1
        }
        else {
            $stepStatus = "PASS"
            $exitCode = 0
        }
    }

    if ($exitCode -eq 0) {
        $stepStatus = "PASS"
        $overallResult = "PASS"
    } else {
        $stepStatus = "FAIL"
        $overallResult = "FAIL"
    }
}
catch {
    $stepStatus = "FAIL"
    $overallResult = "FAIL"
    $stepReason = "Script error: $($_.Exception.Message)"
    $stepError = @{
        code = "SCRIPT_ERROR"
        message = $_.Exception.Message
        exception_type = $_.Exception.GetType().FullName
        stack = $_.ScriptStackTrace
    }
    $exitCode = 2
}
finally {
    # ----------------------------
    # report.json (case truth)
    # ----------------------------
    $report = @{
        schema_version = "1.0"
        test = @{
            id = $TestId
            name = $TestId
        }
        steps = @(
            @{
                id = $stepId
                index = $stepIndex
                name = $stepName
                status = $stepStatus
                expected = $stepExpected
                actual = $stepActual
                reason = $stepReason
                metrics = $stepMetrics
                evidence = $stepEvidence
                error = $stepError
            }
        )
        summary = @{
            total_steps = 1
            passed = ($stepStatus -eq "PASS") ? 1 : 0
            failed = ($stepStatus -eq "FAIL") ? 1 : 0
            skipped = 0
            overall_result = $overallResult
            exit_code = $exitCode
        }
    }

    Write-JsonFile $ReportPath $report

    # ----------------------------
    # stdout (compact)
    # ----------------------------
    $stepLine = "[1/1] $stepName " +
                ("." * [Math]::Max(1, 30 - $stepName.Length)) +
                " $stepStatus"

    $details = @()
    if ($stepStatus -eq "PASS") {
        if ($stepActual) {
            $details += "target=$TargetAddress $stepActual"
        }
    }
    else {
        if ($stepReason)   { $details += "reason: $stepReason" }
        if ($stepExpected) { $details += "expected: $stepExpected" }
        if ($stepActual)   { $details += "actual:   $stepActual" }
    }

    Write-Stdout-Compact `
        -TestName $TestId `
        -Overall $overallResult `
        -ExitCode $exitCode `
        -TsUtc $TsUtc `
        -StepLine $stepLine `
        -StepDetails $details `
        -Total 1 `
        -Passed (($stepStatus -eq "PASS") ? 1 : 0) `
        -Failed (($stepStatus -eq "FAIL") ? 1 : 0) `
        -Skipped 0

    exit $exitCode
}
