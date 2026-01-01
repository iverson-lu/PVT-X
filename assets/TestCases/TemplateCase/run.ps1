param(
    [Parameter(Mandatory = $false)]
    [string] $TestMode = "pass"
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

function Normalize-Mode([string] $s) {
    if ($null -eq $s) { return "pass" }
    $t = $s.Trim()

    # Remove wrapping quotes repeatedly (handles '"error"' / "'error'" / extra spaces)
    while (($t.StartsWith('"') -and $t.EndsWith('"')) -or ($t.StartsWith("'") -and $t.EndsWith("'"))) {
        if ($t.Length -ge 2) { $t = $t.Substring(1, $t.Length - 2).Trim() } else { break }
    }

    return $t.ToLowerInvariant()
}

# --- Normalize and validate TestMode ---
$rawMode = $TestMode
$TestMode = Normalize-Mode $TestMode
Write-Output "[DEBUG] Raw TestMode param = <$rawMode> ; Normalized = <$TestMode>"

$allowed = @("pass", "fail", "timeout", "error")
if ($allowed -notcontains $TestMode) {
    Write-Error "Invalid TestMode='$TestMode'. Allowed: pass|fail|timeout|error"
    exit 2
}

# --- artifacts folders (must be under Case Run Folder) ---
$ArtifactsRoot = Join-Path (Get-Location) "artifacts"
$RawDir        = Join-Path $ArtifactsRoot "raw"
$AttachDir     = Join-Path $ArtifactsRoot "attachments"
Ensure-Dir $ArtifactsRoot
Ensure-Dir $RawDir
Ensure-Dir $AttachDir

$TestId  = "TemplateCase"
$Outcome = "Fail"
$Summary = ""
$Details = @{}
$Metrics = @{}

# ✅ Critical: initialize exitCode OUTSIDE try/catch so finally can always read it (StrictMode-safe)
$exitCode = 2  # default: script/runtime error unless overridden

function Write-SampleArtifacts([string] $Mode) {
    $now = (Get-Date).ToString("o")

    $rawJson = @{
        generatedAt = $now
        testId      = $TestId
        mode        = $Mode
        note        = "This is a predictable raw JSON file for validation."
    }
    Write-JsonFile (Join-Path $RawDir "sample.json") $rawJson

    $txt = @(
        "TemplateCase raw text",
        "generatedAt=$now",
        "mode=$Mode",
        "line3=hello"
    ) -join "`r`n"
    Set-Content -LiteralPath (Join-Path $RawDir "sample.txt") -Value $txt -Encoding UTF8

    $log = @(
        "TemplateCase attachment log",
        "generatedAt=$now",
        "mode=$Mode",
        "this file is under artifacts/attachments"
    ) -join "`r`n"
    Set-Content -LiteralPath (Join-Path $AttachDir "sample.log") -Value $log -Encoding UTF8

    # Small binary blob (0..255)
    [byte[]] $bytes = 0..255
    [System.IO.File]::WriteAllBytes((Join-Path $AttachDir "sample.bin"), $bytes)
}

try {
    Write-Log "INFO" "Start test: $TestId"
    Write-Log "INFO" "TestMode='$TestMode'"

    Write-Log "INFO" "Writing sample artifacts..."
    Write-SampleArtifacts $TestMode
    Write-Log "INFO" "Sample artifacts written under artifacts/raw and artifacts/attachments."

    $Details["mode"] = $TestMode
    $Metrics["rawFilesWritten"] = 2
    $Metrics["attachmentsWritten"] = 2

    switch ($TestMode) {
        "pass" {
            $Outcome = "Pass"
            $Summary = "Forced PASS by TestMode."
            Write-Log "INFO" $Summary
            $exitCode = 0
        }
        "fail" {
            $Outcome = "Fail"
            $Summary = "Forced FAIL by TestMode."
            Write-Log "WARN" $Summary
            $exitCode = 1
        }
        "timeout" {
            # Runner should kill / mark timeout because manifest timeoutSeconds=2
            $Outcome = "Fail"
            $Summary = "Forced TIMEOUT by sleeping longer than timeoutSeconds (Runner should mark as Timeout)."
            Write-Log "WARN" $Summary
            $exitCode = 1

            Write-Log "INFO" "Sleeping 10 seconds to exceed timeout..."
            Start-Sleep -Seconds 10
        }
        "error" {
            Write-Log "ERROR" "Forced ERROR by throwing an exception."
            throw "Forced error from TestMode=error"
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
