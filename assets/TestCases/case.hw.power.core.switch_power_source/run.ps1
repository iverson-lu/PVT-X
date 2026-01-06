param(
    [Parameter(Mandatory=$false)] [string] $E_Action = "GetStatus",
    [Parameter(Mandatory=$false)] [string] $P_PrivateWmiScript = "private_wmi.ps1",
    [Parameter(Mandatory=$false)] [string] $E_ExpectedStatus = "any",
    [Parameter(Mandatory=$false)] [int]    $N_CommandTimeoutSec = 30
)

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
$TestId = $env:PVTX_TESTCASE_ID
$TsUtc  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# Default Test ID (fallback if runner did not inject PVTX_TESTCASE_ID)
$DefaultTestId = "case.hw.power.core.switch_power_source"
if ([string]::IsNullOrWhiteSpace($TestId)) {
    $TestId = $DefaultTestId
}

# Normalize and validate E_Action
$E_Action = Normalize-Text $E_Action
$E_Action = $E_Action.ToLowerInvariant()

$allowedActions = @("switchtodc", "switchtoac", "getstatus")
if ($allowedActions -notcontains $E_Action) {
    throw "E_Action must be one of: SwitchToDC | SwitchToAC | GetStatus."
}

# Normalize and validate E_ExpectedStatus
$E_ExpectedStatus = Normalize-Text $E_ExpectedStatus
$E_ExpectedStatus = $E_ExpectedStatus.ToUpperInvariant()

$allowedExpected = @("ANY", "AC", "DC")
if ($allowedExpected -notcontains $E_ExpectedStatus) {
    throw "E_ExpectedStatus must be one of: any | AC | DC."
}

# Validate timeout
if ($N_CommandTimeoutSec -lt 1 -or $N_CommandTimeoutSec -gt 600) {
    throw "N_CommandTimeoutSec must be between 1 and 600."
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
    id      = "power_source_control"
    index   = 1
    name    = "Switch power source / query power status"
    status  = "FAIL"
    expected = @{
        action_allowed = $true
        private_wmi_exists = $true
    }
    actual = @{
        action = $E_Action
        private_wmi_script = $P_PrivateWmiScript
        expected_status = $E_ExpectedStatus
        timeout_sec = $N_CommandTimeoutSec
    }
    metrics = @{}
    message = $null
    timing = @{ duration_ms = $null }
    error = $null
}


# Check for administrator privileges (only for switch operations, not for query)
if ($E_Action -in @("switchtodc", "switchtoac")) {
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    $isAdmin = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        [Console]::Error.WriteLine("ERROR: Switching power source requires administrator privileges. Please run as administrator.")
        exit 2
    }
}

# timers
$swTotal = [System.Diagnostics.Stopwatch]::StartNew()
$swStep = $null

try {
    $swStep = [System.Diagnostics.Stopwatch]::StartNew()

    # Resolve relative path using PVTX_TESTCASE_PATH (preferred), otherwise use script root.
    $caseRoot = if ($env:PVTX_TESTCASE_PATH) { $env:PVTX_TESTCASE_PATH } else { $PSScriptRoot }

    $privateWmiPath = $P_PrivateWmiScript
    if (-not [System.IO.Path]::IsPathRooted($privateWmiPath)) {
        $privateWmiPath = Join-Path $caseRoot $privateWmiPath
    }
    $privateWmiPath = [System.IO.Path]::GetFullPath($privateWmiPath)

    if (-not (Test-Path -LiteralPath $privateWmiPath)) {
        $step.expected.private_wmi_exists = $true
        $step.actual.private_wmi_exists = $false
        throw "private_wmi.ps1 not found at '$privateWmiPath'."
    }

    function Invoke-ChildPwsh {
        param(
            [Parameter(Mandatory=$true)] [string] $Command,
            [Parameter(Mandatory=$true)] [int] $TimeoutSec
        )

        # Use Windows PowerShell (powershell.exe) for WMI compatibility
        # PowerShell Core (pwsh) deserializes WMI objects and loses methods
        $pwshPath = $null
        $cmd = Get-Command powershell -ErrorAction SilentlyContinue
        if ($cmd -and $cmd.Source) {
            $pwshPath = $cmd.Source
        }
        if (-not $pwshPath) {
            $fallback = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
            if (Test-Path -LiteralPath $fallback) {
                $pwshPath = $fallback
            }
        }
        if (-not $pwshPath) {
            throw "powershell.exe executable not found."
        }

        $psi = [System.Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = $pwshPath

        # Quote -Command payload. Keep it close to the user's provided invocation.
        $escaped = $Command.Replace('"', '\"')
        $psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -Command `"$escaped`""

        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true

        $p = [System.Diagnostics.Process]::new()
        $p.StartInfo = $psi

        [void]$p.Start()

        if (-not $p.WaitForExit($TimeoutSec * 1000)) {
            try { $p.Kill($true) } catch {}
            throw "Child pwsh command timed out after $TimeoutSec seconds."
        }

        $stdout = $p.StandardOutput.ReadToEnd()
        $stderr = $p.StandardError.ReadToEnd()

        return [pscustomobject]@{
            ExitCode  = $p.ExitCode
            Stdout    = $stdout
            Stderr    = $stderr
            FileName  = $psi.FileName
            Arguments = $psi.Arguments
        }
    }

    $step.metrics.case_root = $caseRoot
    $step.metrics.private_wmi_path = $privateWmiPath

    # Compose the exact PowerShell payload: dot-source private_wmi.ps1, then invoke function.
    $escapedPath = $privateWmiPath.Replace("'", "''")

    $payload = $null
    switch ($E_Action) {
        "switchtodc" { $payload = ". '$escapedPath'; Switch-PowerToDC" }
        "switchtoac" { $payload = ". '$escapedPath'; Switch-PowerToAC" }
        "getstatus"  { $payload = ". '$escapedPath'; Get-PowerStatus" }
        default      { throw "Unexpected E_Action='$E_Action' (validation should have caught this)." }
    }

    $step.metrics.payload = $payload

    $child = Invoke-ChildPwsh -Command $payload -TimeoutSec $N_CommandTimeoutSec
    $step.metrics.child_exit_code = $child.ExitCode
    $step.metrics.child_stdout = if ($child.Stdout) { $child.Stdout.TrimEnd() } else { "" }
    $step.metrics.child_stderr = if ($child.Stderr) { $child.Stderr.TrimEnd() } else { "" }

    if ($E_Action -eq "getstatus") {
        $firstNonEmpty = ($child.Stdout -split "`r?`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -First 1
        if (-not $firstNonEmpty) {
            $step.status = "FAIL"
            $step.message = "Get-PowerStatus produced no output."
            $overallStatus = "FAIL"
            $exitCode = 1
        }
        else {
            $status = $firstNonEmpty.ToUpperInvariant()
            $step.metrics.power_status = $status

            if (@("AC","DC") -notcontains $status) {
                $step.status = "FAIL"
                $step.message = "Unexpected power status '$status' (expected AC or DC)."
                $overallStatus = "FAIL"
                $exitCode = 1
            }
            elseif ($E_ExpectedStatus -ne "ANY" -and $status -ne $E_ExpectedStatus) {
                $step.status = "FAIL"
                $step.message = "Power status mismatch. expected=$E_ExpectedStatus actual=$status"
                $overallStatus = "FAIL"
                $exitCode = 1
            }
            else {
                $step.status = "PASS"
                $step.message = "Power status is $status"
                $overallStatus = "PASS"
                $exitCode = 0
            }
        }
    }
    else {
        # SwitchToAC / SwitchToDC: use child process exit code for Pass/Fail.
        if ($child.ExitCode -eq 0) {
            $step.status = "PASS"
            $step.message = "Command succeeded (exit_code=0)."
            $overallStatus = "PASS"
            $exitCode = 0
        }
        else {
            $step.status = "FAIL"
            $step.message = "Command failed (exit_code=$($child.ExitCode))."
            $overallStatus = "FAIL"
            $exitCode = 1
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
                e_action = $E_Action
                p_private_wmi_script = $P_PrivateWmiScript
                e_expected_status = $E_ExpectedStatus
                n_command_timeout_sec = $N_CommandTimeoutSec
            }
            steps = @($step)
        }
    }

    Write-JsonFile $ReportPath $report

    # ----------------------------
    # stdout (compact format like NetworkPingConnectivity)
    # ----------------------------
    $dotCount = [Math]::Max(3, 30 - $step.name.Length)
    $stepLine = "[1/1] {0} {1} {2}" -f $step.name, ("." * $dotCount), $step.status

    $details = New-Object System.Collections.Generic.List[string]
if ($step.status -eq "PASS") {
    $d = "action=$E_Action private_wmi='$P_PrivateWmiScript' timeout_sec=$N_CommandTimeoutSec"
    $details.Add($d)

    if ($step.metrics.power_status) {
        $details.Add("power_status=$($step.metrics.power_status)")
    }
}
else {
    if ($step.message) { $details.Add("reason: $($step.message)") }
    $details.Add("expected: command_success=true")

    $act = @()
    $act += "action=$E_Action"
    $act += "private_wmi='$P_PrivateWmiScript'"
    $act += "timeout_sec=$N_CommandTimeoutSec"

    if ($step.metrics.child_exit_code -ne $null) { $act += "child_exit_code=$($step.metrics.child_exit_code)" }
    if ($step.metrics.power_status) { $act += "power_status=$($step.metrics.power_status)" }
    if ($E_Action -eq "getstatus" -and $E_ExpectedStatus -ne "ANY") { $act += "expected_status=$E_ExpectedStatus" }

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
