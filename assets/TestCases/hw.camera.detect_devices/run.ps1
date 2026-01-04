param(
    [Parameter(Mandatory=$true)] [int]    $MinExpectedCount = 1,
    [Parameter(Mandatory=$false)] [string] $NameContains = ""
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
$TestId = "CameraVerify"
$TsUtc  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# Normalize parameters
$NameContains = Normalize-Text $NameContains

# Artifacts
$ArtifactsRoot = Join-Path (Get-Location) "artifacts"
Ensure-Dir $ArtifactsRoot
$ReportPath = Join-Path $ArtifactsRoot "report.json"

# Defaults
$overallStatus = "FAIL"
$exitCode = 2

# Step definition
$step = @{
    id      = "detect_cameras"
    index   = 1
    name    = "Detect cameras"
    status  = "FAIL"
    expected = @{
        min_count = $MinExpectedCount
        name_contains = if ([string]::IsNullOrWhiteSpace($NameContains)) { $null } else { $NameContains }
    }
    actual = @{
        detected_count = 0
        camera_names = @()
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

    # Validate input
    if ($MinExpectedCount -lt 0) {
        throw "MinExpectedCount must be >= 0"
    }

    # Query camera devices
    $cameraNames = @()
    try {
        $devices = Get-CimInstance -ClassName Win32_PnPEntity -Filter "PNPClass = 'Camera' OR PNPClass = 'Image'" -ErrorAction Stop
        if ($devices) {
            $cameraNames = @($devices | Select-Object -ExpandProperty Name -ErrorAction SilentlyContinue)
        }
    }
    catch {
        $cameraNames = @()
    }

    # Fallback: search by name pattern
    if (-not $cameraNames -or $cameraNames.Count -eq 0) {
        try {
            $fallback = Get-CimInstance -ClassName Win32_PnPEntity -ErrorAction Stop | Where-Object { $_.Name -match "Camera" }
            if ($fallback) {
                $cameraNames = @($fallback | Select-Object -ExpandProperty Name -ErrorAction SilentlyContinue)
            }
        }
        catch {
            $cameraNames = @()
        }
    }

    $cameraNames = @($cameraNames | Where-Object { $_ } | Sort-Object -Unique)
    $detectedCount = $cameraNames.Count

    $step.actual.detected_count = $detectedCount
    $step.actual.camera_names = $cameraNames

    # Metrics
    $step.metrics.detected_count = $detectedCount
    $step.metrics.camera_names = $cameraNames

    # Validation: count check
    if ($detectedCount -lt $MinExpectedCount) {
        $step.status = "FAIL"
        $step.message = "Insufficient cameras detected"
        $exitCode = 1
    }
    # Validation: name check (if specified)
    elseif (-not [string]::IsNullOrWhiteSpace($NameContains)) {
        $escaped = [regex]::Escape($NameContains)
        $matching = $cameraNames | Where-Object { $_ -match $escaped }
        if (@($matching).Count -eq 0) {
            $step.status = "FAIL"
            $step.message = "No camera names contain required string"
            $exitCode = 1
        }
        else {
            $step.status = "PASS"
            $exitCode = 0
        }
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
                min_expected_count = $MinExpectedCount
                name_contains = $NameContains
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
        $d = "detected=$detectedCount min=$MinExpectedCount"
        $details.Add($d)
        if ($step.actual.camera_names.Count -gt 0) {
            $names = ($step.actual.camera_names | Select-Object -First 3) -join ", "
            if ($step.actual.camera_names.Count -gt 3) { $names += " (+ $($step.actual.camera_names.Count - 3) more)" }
            $details.Add("cameras: $names")
        }
    }
    else {
        if ($step.message) { $details.Add("reason: $($step.message)") }
        $details.Add("expected: min_count=$MinExpectedCount" + $(if (-not [string]::IsNullOrWhiteSpace($NameContains)) { " name_contains='$NameContains'" } else { "" }))
        $details.Add("actual:   detected_count=$($step.actual.detected_count)")
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
