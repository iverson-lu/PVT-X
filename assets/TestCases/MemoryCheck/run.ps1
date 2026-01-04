param(
    [Parameter(Mandatory = $false)]
    [int] $MinMemoryMB = 100
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
$TestId = "MemoryCheck"
$TsUtc  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# Artifacts
$ArtifactsRoot = Join-Path (Get-Location) "artifacts"
Ensure-Dir $ArtifactsRoot
$ReportPath = Join-Path $ArtifactsRoot "report.json"

# Defaults
$overallStatus = "FAIL"
$exitCode = 2

# Step definition
$step = @{
    id      = "check_available_memory"
    index   = 1
    name    = "Check available physical memory"
    status  = "FAIL"
    expected = @{
        min_memory_mb = $MinMemoryMB
    }
    actual = @{
        available_memory_mb = $null
        total_memory_mb = $null
        free_memory_mb = $null
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
    # Validate MinMemoryMB
    if ($MinMemoryMB -lt 0) {
        throw "MinMemoryMB must be >= 0"
    }

    $swStep = [System.Diagnostics.Stopwatch]::StartNew()

    # Query OS for memory information
    $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
    
    if (-not $os) {
        throw "Unable to retrieve operating system information"
    }

    # FreePhysicalMemory is in KB, convert to MB
    $availableMB = [math]::Round($os.FreePhysicalMemory / 1024, 2)
    $totalMB = [math]::Round($os.TotalVisibleMemorySize / 1024, 2)
    
    $step.actual.available_memory_mb = $availableMB
    $step.actual.total_memory_mb = $totalMB
    $step.actual.free_memory_mb = $availableMB

    # Metrics
    $step.metrics.available_memory_mb = $availableMB
    $step.metrics.total_memory_mb = $totalMB
    $step.metrics.used_memory_mb = [math]::Round($totalMB - $availableMB, 2)
    $step.metrics.memory_usage_percent = [math]::Round((($totalMB - $availableMB) / $totalMB) * 100, 2)

    # Validation
    if ($availableMB -ge $MinMemoryMB) {
        $step.status = "PASS"
        $exitCode = 0
    }
    else {
        $step.status = "FAIL"
        $step.message = "Insufficient available memory"
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
                min_memory_mb = $MinMemoryMB
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
        $d = "available=$($step.actual.available_memory_mb)MB total=$($step.actual.total_memory_mb)MB"
        $details.Add($d)
        if ($step.metrics.memory_usage_percent) {
            $details.Add("usage=$($step.metrics.memory_usage_percent)% used=$($step.metrics.used_memory_mb)MB")
        }
    }
    else {
        if ($step.message) { $details.Add("reason: $($step.message)") }
        $details.Add("expected: min_memory_mb >= $MinMemoryMB")
        $details.Add("actual:   available_memory_mb=$($step.actual.available_memory_mb)")
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
