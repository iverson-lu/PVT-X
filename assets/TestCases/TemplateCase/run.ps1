param(
    # --- Example parameters (align with manifest.parameters) ---
    [Parameter(Mandatory = $false)]
    [int] $MinExpectedCount = 1,

    [Parameter(Mandatory = $false)]
    [string] $NameContains
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------- Helpers ----------
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

# Always write artifacts under current working dir (Case Run Folder)
$ArtifactsRoot = Join-Path (Get-Location) "artifacts"
$RawDir        = Join-Path $ArtifactsRoot "raw"
$AttachDir     = Join-Path $ArtifactsRoot "attachments"
Ensure-Dir $ArtifactsRoot
Ensure-Dir $RawDir
Ensure-Dir $AttachDir

$TestId = "REPLACE_WITH_MANIFEST_ID"   # 可选：也可以不写死，只用于 report.json
$Outcome = "Fail"
$Summary = ""
$Details = @{}
$Metrics = @{}

try {
    Write-Log "INFO" "Start test."

    # ---------- Test logic ----------
    # TODO: do real checks, fill $Outcome/$Summary/$Details/$Metrics

    # Example:
    # if ($somethingOk) { $Outcome = "Pass" } else { $Outcome = "Fail" }

    if ($Outcome -eq "Pass") {
        Write-Log "INFO" "Test passed."
        $exitCode = 0
    } else {
        Write-Log "WARN" "Test failed."
        $exitCode = 1
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
        testId   = $TestId
        outcome  = $Outcome
        summary  = $Summary
        details  = $Details
        metrics  = $Metrics
    }

    Write-JsonFile (Join-Path $ArtifactsRoot "report.json") $report
    Write-Log "INFO" "Artifacts written: artifacts/report.json"
    Write-Output "[RESULT] $Outcome"
    exit $exitCode
}
