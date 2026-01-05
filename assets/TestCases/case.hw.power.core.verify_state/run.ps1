param(
    [Parameter(Mandatory=$false)] [string] $E_ExpectedPowerSource = "any",
    [Parameter(Mandatory=$false)] [string] $E_ExpectedPowerScheme = "any",
    [Parameter(Mandatory=$false)] [bool]   $B_RequireBattery = $false,
    [Parameter(Mandatory=$false)] [bool]   $B_FailOnUnknownScheme = $false
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
if ([string]::IsNullOrWhiteSpace($TestId)) {
    $TestId = "case.hw.power.core.verify_state"
}
$TsUtc  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# Normalize expected enums
$E_ExpectedPowerSource = Normalize-Text $E_ExpectedPowerSource
$E_ExpectedPowerScheme = Normalize-Text $E_ExpectedPowerScheme
$E_ExpectedPowerSource = $E_ExpectedPowerSource.ToLowerInvariant()
$E_ExpectedPowerScheme = $E_ExpectedPowerScheme.ToLowerInvariant()

# Validate enums explicitly (manifest enums are UI hints only)
$allowedSources = @("any","ac","battery")
if (-not ($allowedSources -contains $E_ExpectedPowerSource)) {
    throw "Invalid E_ExpectedPowerSource '$E_ExpectedPowerSource'. Allowed: any|ac|battery"
}

$allowedSchemes = @("any","performance","balanced","saver")
if (-not ($allowedSchemes -contains $E_ExpectedPowerScheme)) {
    throw "Invalid E_ExpectedPowerScheme '$E_ExpectedPowerScheme'. Allowed: any|performance|balanced|saver"
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
    id      = "verify_power_state"
    index   = 1
    name    = "Verify current power source and scheme"
    status  = "FAIL"
    expected = @{
        power_source = $E_ExpectedPowerSource
        power_scheme = $E_ExpectedPowerScheme
    }
    actual = @{}
    metrics = @{}
    message = ""
    timing  = @{ duration_ms = 0 }
}

function Get-PowerSourceState {
    # Returns a hashtable: { source = 'ac'|'battery'|'unknown'; battery_present = bool; evidence = string }
    # Prefer root\wmi:BatteryStatus (PowerOnline) when available.
    try {
        $wmi = Get-CimInstance -Namespace "root\wmi" -ClassName "BatteryStatus" -ErrorAction Stop
        if ($null -ne $wmi) {
            $first = @($wmi)[0]
            if ($null -ne $first.PSObject.Properties["PowerOnline"]) {
                $online = [bool]$first.PowerOnline
                return @{
                    source = $(if ($online) { "ac" } else { "battery" })
                    battery_present = $true
                    evidence = ("root\wmi:BatteryStatus.PowerOnline={0}" -f $online)
                }
            }
        }
    } catch {
        # ignore and fall back
    }

    # Fallback: Win32_Battery BatteryStatus inference
    try {
        $b = Get-CimInstance -Namespace "root\cimv2" -ClassName "Win32_Battery" -ErrorAction Stop
        if ($null -ne $b -and @($b).Count -gt 0) {
            $first = @($b)[0]
            $code = $first.BatteryStatus
            $source = "unknown"
            if ($code -eq 2) { $source = "ac" } # On AC power
            elseif ($code -in 6,7,8,9) { $source = "ac" } # Charging implies AC
            elseif ($code -in 1,4,5) { $source = "battery" } # Discharging/low/critical implies battery
            else { $source = "unknown" }

            return @{
                source = $source
                battery_present = $true
                evidence = ("root\cimv2:Win32_Battery.BatteryStatus={0}" -f $code)
            }
        }
    } catch {
        # ignore
    }

    return @{
        source = "unknown"
        battery_present = $false
        evidence = "No WMI battery telemetry available"
    }
}

function Get-ActivePowerScheme {
    # Returns: { guid = string; name = string; category = 'balanced'|'performance'|'saver'|'unknown'; evidence = string }
    $guid = ""
    $name = ""
    $category = "unknown"
    $evidence = ""

    $output = & powercfg /getactivescheme 2>&1
    $evidence = ($output | Out-String).Trim()

    # Parse: "Power Scheme GUID: <GUID>  (<NAME>)"
    $m = [regex]::Match($evidence, "GUID:\s*([0-9a-fA-F\-]{36})\s*\((.*)\)")
    if ($m.Success) {
        $guid = $m.Groups[1].Value.ToLowerInvariant()
        $name = $m.Groups[2].Value.Trim()
    } else {
        # Some builds print without parentheses; attempt a simpler GUID parse
        $m2 = [regex]::Match($evidence, "GUID:\s*([0-9a-fA-F\-]{36})")
        if ($m2.Success) {
            $guid = $m2.Groups[1].Value.ToLowerInvariant()
        }
    }

    # Map well-known default GUIDs (locale-independent)
    switch ($guid) {
        "381b4222-f694-41f0-9685-ff5bb260df2e" { $category = "balanced" }     # Balanced
        "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" { $category = "performance" }  # High performance
        "a1841308-3541-4fab-bc81-f71556f20b4a" { $category = "saver" }        # Power saver
        "e9a42b02-d5df-448d-aa00-03f14749eb61" { $category = "performance" }  # Ultimate performance
        default { }
    }

    # Fallback: name matching (best-effort, may be localized)
    if ($category -eq "unknown" -and -not [string]::IsNullOrWhiteSpace($name)) {
        $n = $name.ToLowerInvariant()
        if ($n -match "balanced") { $category = "balanced" }
        elseif ($n -match "high performance|ultimate performance|performance") { $category = "performance" }
        elseif ($n -match "power saver|saver") { $category = "saver" }
    }

    return @{
        guid = $guid
        name = $name
        category = $category
        evidence = $evidence
    }
}

$swStep = $null

try {
    $swStep = [System.Diagnostics.Stopwatch]::StartNew()

    $ps = Get-PowerSourceState
    $scheme = Get-ActivePowerScheme

    $step.actual = @{
        power_source = $ps.source
        battery_present = $ps.battery_present
        power_source_evidence = $ps.evidence
        scheme_category = $scheme.category
        scheme_guid = $scheme.guid
        scheme_name = $scheme.name
    }

    $step.metrics = @{
        fail_on_unknown_scheme = $B_FailOnUnknownScheme
        require_battery = $B_RequireBattery
        powercfg_raw = $scheme.evidence
    }

    $reasons = @()

    # Enforce battery telemetry requirement
    if ($B_RequireBattery -and (-not $ps.battery_present)) {
        $reasons += "Battery telemetry not available (B_RequireBattery=true)."
    }

    # Validate power source expectation
    if ($E_ExpectedPowerSource -ne "any") {
        if ($ps.source -eq "unknown") {
            $reasons += ("Power source is unknown but expected '{0}'." -f $E_ExpectedPowerSource)
        } elseif ($ps.source -ne $E_ExpectedPowerSource) {
            $reasons += ("Power source mismatch: expected '{0}', actual '{1}'." -f $E_ExpectedPowerSource, $ps.source)
        }
    }

    # Validate scheme expectation
    if ($E_ExpectedPowerScheme -ne "any") {
        if ($scheme.category -eq "unknown") {
            $reasons += ("Power scheme is unknown but expected '{0}'." -f $E_ExpectedPowerScheme)
        } elseif ($scheme.category -ne $E_ExpectedPowerScheme) {
            $reasons += ("Power scheme mismatch: expected '{0}', actual '{1}'." -f $E_ExpectedPowerScheme, $scheme.category)
        }
    } else {
        # Optional strictness: fail if unknown scheme mapping
        if ($B_FailOnUnknownScheme -and $scheme.category -eq "unknown") {
            $reasons += "Power scheme category could not be determined (B_FailOnUnknownScheme=true)."
        }
    }

    if ($reasons.Count -eq 0) {
        $step.status = "PASS"
        $overallStatus = "PASS"
        $exitCode = 0
    } else {
        $step.status = "FAIL"
        $overallStatus = "FAIL"
        $exitCode = 1
        $step.message = ($reasons -join " ")
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

    $passCount = if ($step.status -eq "PASS") { 1 } else { 0 }
    $failCount = if ($step.status -eq "FAIL") { 1 } else { 0 }
    $skipCount = 0

    $totalMs = $step.timing.duration_ms

    $report = @{
        schema = @{ version = "1.0" }
        test = @{
            id = $TestId
            name = $TestId
            params = @{
                e_expected_power_source = $E_ExpectedPowerSource
                e_expected_power_scheme = $E_ExpectedPowerScheme
                b_require_battery = $B_RequireBattery
                b_fail_on_unknown_scheme = $B_FailOnUnknownScheme
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
    $details.Add(("power_source={0} battery_present={1}" -f $step.actual.power_source, $step.actual.battery_present))
    $details.Add(("scheme={0} guid={1}" -f $step.actual.scheme_category, $step.actual.scheme_guid))
    if (-not [string]::IsNullOrWhiteSpace($step.actual.scheme_name)) {
        $details.Add(("scheme_name='{0}'" -f $step.actual.scheme_name))
    }
    if (-not [string]::IsNullOrWhiteSpace($step.message)) {
        $details.Add(("reason: {0}" -f $step.message))
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
