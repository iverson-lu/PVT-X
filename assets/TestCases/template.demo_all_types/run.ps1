param(
    [Parameter(Mandatory=$true)]  [string]  $E_Mode,
    [Parameter(Mandatory=$false)] [string]  $S_Text = "hello",
    [Parameter(Mandatory=$false)] [int]     $N_Int = 42,
    [Parameter(Mandatory=$false)] [bool]    $B_Flag = $true,
    [Parameter(Mandatory=$false)] [double]  $N_Double = 3.14,
    [Parameter(Mandatory=$false)] [string]  $P_Path = "C:\\Windows",
    [Parameter(Mandatory=$false)] [string]  $ItemsJson = "[1, 2, 3]",
    [Parameter(Mandatory=$false)] [string]  $ConfigJson = "{`"timeout`": 30, `"retry`": true}"
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
$TestId = "TemplateCase"
$TsUtc  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# Normalize E_Mode
$E_Mode = Normalize-Text $E_Mode
$E_Mode = $E_Mode.ToLowerInvariant()

# Validate E_Mode
$allowedModes = @("pass", "fail", "timeout", "error")
if ($allowedModes -notcontains $E_Mode) {
    throw "Invalid E_Mode '$E_Mode'. Allowed: pass|fail|timeout|error"
}

# Artifacts
$ArtifactsRoot = Join-Path (Get-Location) "artifacts"
Ensure-Dir $ArtifactsRoot
$ReportPath = Join-Path $ArtifactsRoot "report.json"

# Defaults
$overallStatus = "FAIL"
$exitCode = 2  # 2 = script/runtime error unless overridden

# Step definition
$step = @{
    id      = "validate_params"
    index   = 1
    name    = "Validate all parameters"
    status  = "FAIL"
    expected = @{
        all_params_valid = $true
        mode_allowed = $true
    }
    actual = @{
        mode = $E_Mode
        text = $S_Text
        int_value = $N_Int
        flag = $B_Flag
        double_value = $N_Double
        path = $P_Path
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
    $swStep = [System.Diagnostics.Stopwatch]::StartNew()

    # Parse JSON parameters
    $parsedItems = $null
    $parsedConfig = $null
    try {
        $parsedItems = $ItemsJson | ConvertFrom-Json
        $parsedConfig = $ConfigJson | ConvertFrom-Json
        
        $step.actual.items_parsed = $parsedItems
        $step.actual.config_parsed = $parsedConfig
        $step.metrics.items_count = $parsedItems.Count
    }
    catch {
        throw "Failed to parse JSON parameters: $($_.Exception.Message)"
    }

    # Validate parameter types
    $validationErrors = @()
    if ($E_Mode -isnot [string]) { $validationErrors += "E_Mode: Expected string" }
    if ($S_Text -isnot [string]) { $validationErrors += "S_Text: Expected string" }
    if ($N_Int -isnot [int] -and $N_Int -isnot [int32]) { $validationErrors += "N_Int: Expected int" }
    if ($B_Flag -isnot [bool]) { $validationErrors += "B_Flag: Expected boolean" }
    if ($N_Double -isnot [double]) { $validationErrors += "N_Double: Expected double" }
    if ($P_Path -isnot [string]) { $validationErrors += "P_Path: Expected string" }
    
    if ($validationErrors.Count -gt 0) {
        throw "Parameter validation failed: " + ($validationErrors -join "; ")
    }

    # metrics
    $step.metrics.mode = $E_Mode
    $step.metrics.text_length = $S_Text.Length
    $step.metrics.int_value = $N_Int
    $step.metrics.flag = $B_Flag
    $step.metrics.double_value = $N_Double
    $step.metrics.path_exists = (Test-Path -LiteralPath $P_Path -ErrorAction SilentlyContinue)

    # Execute test based on mode
    switch ($E_Mode) {
        "pass" {
            $step.status = "PASS"
            $step.message = "All parameters validated successfully. Forced PASS by E_Mode."
            $exitCode = 0
            $overallStatus = "PASS"
        }
        "fail" {
            $step.status = "FAIL"
            $step.message = "Forced FAIL by E_Mode=fail"
            $exitCode = 1
            $overallStatus = "FAIL"
        }
        "timeout" {
            $step.status = "FAIL"
            $step.message = "Forcing timeout by sleeping longer than allowed (Runner should mark as Timeout)"
            $exitCode = 1
            $overallStatus = "FAIL"
            Start-Sleep -Seconds 150  # exceeds timeoutSec=120
        }
        "error" {
            throw "Forced error from E_Mode=error"
        }
    }
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
    # report.json (structured format like NetworkPingConnectivity)
    # ----------------------------
    $report = @{
        schema = @{ version = "1.0" }
        test = @{
            id = $TestId
            name = $TestId
            params = @{
                e_mode = $E_Mode
                s_text = $S_Text
                n_int = $N_Int
                b_flag = $B_Flag
                n_double = $N_Double
                p_path = $P_Path
                items_json = $ItemsJson
                config_json = $ConfigJson
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
    # stdout (compact format like NetworkPingConnectivity)
    # ----------------------------
    $dotCount = [Math]::Max(3, 30 - $step.name.Length)
    $stepLine = "[1/1] {0} {1} {2}" -f $step.name, ("." * $dotCount), $step.status

    $details = New-Object System.Collections.Generic.List[string]
    if ($step.status -eq "PASS") {
        $d = "mode=$E_Mode text='$S_Text' int=$N_Int flag=$B_Flag double=$N_Double"
        $details.Add($d)
        if ($step.metrics.items_count) { 
            $details.Add("items_count=$($step.metrics.items_count) path_exists=$($step.metrics.path_exists)")
        }
    }
    else {
        if ($step.message) { $details.Add("reason: $($step.message)") }
        $details.Add("expected: all_params_valid=true mode_allowed=true")
        
        $act = @()
        $act += "mode=$E_Mode"
        $act += "text_length=$($S_Text.Length)"
        $act += "int=$N_Int"
        if ($act.Count -gt 0) { $details.Add(("actual:   " + ($act -join " "))) }
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
