param(
    [Parameter(Mandatory = $false)]
    [string] $TargetAddress = "www.microsoft.com",

    [Parameter(Mandatory = $false)]
    [int] $MaxReplyTimeMs = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Log([string] $Level, [string] $Message) {
    $ts = (Get-Date).ToString("s")
    Write-Output "[$ts][$Level] $Message"
}

function Ensure-Dir([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Write-JsonFile([string] $Path, $Obj) {
    $Obj | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Normalize-Text([string] $s) {
    if ($null -eq $s) { return "" }
    $t = $s.Trim()

    # Remove wrapping quotes repeatedly (handles '"host"' / "'host'" / extra spaces)
    while (($t.StartsWith('"') -and $t.EndsWith('"')) -or ($t.StartsWith("'") -and $t.EndsWith("'"))) {
        if ($t.Length -lt 2) { break }
        $t = $t.Substring(1, $t.Length - 2).Trim()
    }

    return $t
}

# --- artifacts folders (must be under Case Run Folder) ---
$ArtifactsRoot = Join-Path (Get-Location) "artifacts"
Ensure-Dir $ArtifactsRoot

$TestId  = "NetworkPingConnectivity"
$Outcome = "Fail"
$Summary = ""
$Details = @{}
$Metrics = @{}

# ✅ Critical: initialize exitCode OUTSIDE try/catch so finally can always read it (StrictMode-safe)
$exitCode = 2  # default: script/runtime error unless overridden

try {
    $TargetAddress = Normalize-Text $TargetAddress

    if ([string]::IsNullOrWhiteSpace($TargetAddress)) {
        throw "TargetAddress is empty."
    }

    if ($MaxReplyTimeMs -lt 0) {
        throw "MaxReplyTimeMs must be >= 0."
    }

    Write-Log "INFO" "Start test: $TestId"
    Write-Log "INFO" "TargetAddress='$TargetAddress'"
    Write-Log "INFO" "MaxReplyTimeMs=$MaxReplyTimeMs"

    $Details["targetAddress"] = $TargetAddress
    $Details["maxReplyTimeMs"] = $MaxReplyTimeMs

    # Use Test-Connection for ICMP reachability.
    # We prefer a single probe (Count=1). Some environments may block ICMP.
    $replies = Test-Connection -TargetName $TargetAddress -Count 1 -ErrorAction Stop
    $reply = $replies | Select-Object -First 1

    if ($null -eq $reply) {
        $Outcome = "Fail"
        $Summary = "No ping reply received."
        $exitCode = 1
        Write-Log "WARN" $Summary
    }
    else {
        $status = $null
        if ($reply.PSObject.Properties.Name -contains "Status") {
            $status = [string]$reply.Status
        }
        else {
            # If Status is missing, assume success when we got an object back.
            $status = "Success"
        }

        $Details["status"] = $status

        $resolved = $null
        if ($reply.PSObject.Properties.Name -contains "DisplayAddress") {
            $resolved = [string]$reply.DisplayAddress
        }
        elseif ($reply.PSObject.Properties.Name -contains "Address") {
            $resolved = [string]$reply.Address
        }
        if (-not [string]::IsNullOrWhiteSpace($resolved)) {
            $Details["resolvedAddress"] = $resolved
        }

        $replyTimeMs = $null
        if ($reply.PSObject.Properties.Name -contains "ResponseTime") {
            $replyTimeMs = [int]$reply.ResponseTime
        }
        elseif ($reply.PSObject.Properties.Name -contains "Latency") {
            $replyTimeMs = [int]$reply.Latency
        }

        if ($null -ne $replyTimeMs) {
            $Metrics["replyTimeMs"] = $replyTimeMs
        }

        if ($status -ne "Success") {
            $Outcome = "Fail"
            $Summary = "Ping failed. Status='$status'."
            $exitCode = 1
            Write-Log "WARN" $Summary
        }
        elseif ($MaxReplyTimeMs -gt 0 -and $null -ne $replyTimeMs -and $replyTimeMs -gt $MaxReplyTimeMs) {
            $Outcome = "Fail"
            $Summary = "Ping succeeded but reply time exceeded threshold: ${replyTimeMs}ms > ${MaxReplyTimeMs}ms."
            $exitCode = 1
            Write-Log "WARN" $Summary
        }
        else {
            $Outcome = "Pass"
            if ($MaxReplyTimeMs -gt 0 -and $null -ne $replyTimeMs) {
                $Summary = "Ping succeeded within threshold (${replyTimeMs}ms <= ${MaxReplyTimeMs}ms)."
            }
            else {
                $Summary = "Ping succeeded."
            }
            $exitCode = 0
            Write-Log "INFO" $Summary
        }
    }
}
catch {
    Write-Log "ERROR" ("Unhandled exception: " + $_.Exception.Message)
    Write-Error $_

    $Outcome = "Fail"
    $Summary = "Script error: $($_.Exception.Message)"
    $Details["exceptionType"] = $_.Exception.GetType().FullName
    $Details["stack"] = $_.ScriptStackTrace
    $exitCode = 2
}
finally {
    if ([string]::IsNullOrWhiteSpace($Summary)) {
        $Summary = ($Outcome -eq "Pass") ? "OK" : "Check failed"
    }

    $report = @{
        testId  = $TestId
        outcome = $Outcome
        summary = $Summary
        details = $Details
        metrics = $Metrics
    }

    Write-JsonFile (Join-Path $ArtifactsRoot "report.json") $report
    Write-Log "INFO" "Artifacts written: artifacts/report.json"
    Write-Output "[RESULT] $Outcome"
    Write-Output "[DEBUG] exitCode=$exitCode"
    exit $exitCode
}
