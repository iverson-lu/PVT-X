param(
    [Parameter(Mandatory=$false)] [bool] $RestrictedMode = $false
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
$TestId = "DeviceHealthCheck"
$Ts  = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssZ")

# Artifacts
$ArtifactsRoot = Join-Path (Get-Location) "artifacts"
Ensure-Dir $ArtifactsRoot
$ReportPath = Join-Path $ArtifactsRoot "report.json"

# Defaults
$overallStatus = "FAIL"
$exitCode = 2

# Step definition
$step = @{
    id      = "check_device_health"
    index   = 1
    name    = "Check device health"
    status  = "FAIL"
    expected = @{
        restricted_mode = $RestrictedMode
        no_problem_devices = $true
        all_enabled = if ($RestrictedMode) { $true } else { $null }
    }
    actual = @{
        total_count = 0
        ok_count = 0
        disabled_count = 0
        problem_count = 0
        problem_devices = @()
        disabled_devices = @()
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

    # Query all PnP devices
    $devices = Get-CimInstance -ClassName Win32_PnPEntity -ErrorAction Stop
    
    $allDevices = @()
    $problemDevices = @()
    $disabledDevices = @()
    $okDevices = @()
    
    foreach ($device in $devices) {
        $deviceInfo = @{
            name = $device.Name
            device_id = $device.DeviceID
            status = $device.Status
            error_code = $device.ConfigManagerErrorCode
        }
        
        $allDevices += $deviceInfo
        
        # ConfigManagerErrorCode meanings:
        # 0  = Device is working properly
        # 22 = Device is disabled
        # Other non-zero values indicate problems
        
        if ($null -eq $device.ConfigManagerErrorCode) {
            $okDevices += $deviceInfo
        } 
        elseif ($device.ConfigManagerErrorCode -eq 0) {
            $okDevices += $deviceInfo
        } 
        elseif ($device.ConfigManagerErrorCode -eq 22) {
            $disabledDevices += $deviceInfo
        } 
        else {
            $problemDevices += $deviceInfo
        }
    }

    $totalCount = $allDevices.Count
    $okCount = $okDevices.Count
    $disabledCount = $disabledDevices.Count
    $problemCount = $problemDevices.Count

    $step.actual.total_count = $totalCount
    $step.actual.ok_count = $okCount
    $step.actual.disabled_count = $disabledCount
    $step.actual.problem_count = $problemCount
    $step.actual.problem_devices = $problemDevices | Select-Object -First 10
    $step.actual.disabled_devices = $disabledDevices | Select-Object -First 10

    # Metrics
    $step.metrics.total_count = $totalCount
    $step.metrics.ok_count = $okCount
    $step.metrics.disabled_count = $disabledCount
    $step.metrics.problem_count = $problemCount

    # Validation
    $failReasons = @()
    
    if ($RestrictedMode) {
        # Restricted mode: No disabled or problem devices allowed
        if ($disabledCount -gt 0) {
            $failReasons += "Found $disabledCount disabled device(s)"
        }
        if ($problemCount -gt 0) {
            $failReasons += "Found $problemCount device(s) with errors"
        }
    }
    else {
        # Normal mode: Only fail on problem devices
        if ($problemCount -gt 0) {
            $failReasons += "Found $problemCount device(s) with errors"
        }
    }

    if ($failReasons.Count -gt 0) {
        $step.status = "FAIL"
        $step.message = $failReasons -join "; "
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
                restricted_mode = $RestrictedMode
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
        $d = "total=$($step.actual.total_count) ok=$($step.actual.ok_count) disabled=$($step.actual.disabled_count) problem=$($step.actual.problem_count)"
        $details.Add($d)
        $details.Add("mode: " + $(if ($RestrictedMode) { "restricted (all must be enabled)" } else { "normal (problem devices only)" }))
    }
    else {
        if ($step.message) { $details.Add("reason: $($step.message)") }
        $details.Add("expected: no_problem_devices=true" + $(if ($RestrictedMode) { " all_enabled=true" } else { "" }))
        $details.Add("actual:   problem=$($step.actual.problem_count) disabled=$($step.actual.disabled_count)")
        if ($step.actual.problem_devices.Count -gt 0) {
            $names = ($step.actual.problem_devices | Select-Object -First 2 -ExpandProperty name) -join ", "
            if ($step.actual.problem_devices.Count -gt 2) { $names += " (+ $($step.actual.problem_devices.Count - 2) more)" }
            $details.Add("problems: $names")
        }
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
