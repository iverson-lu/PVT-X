param(
    [Parameter(Mandatory=$false)] [int]    $N_MinTotalGB = 0,
    [Parameter(Mandatory=$false)] [int]    $N_MinModuleCount = 1,
    [Parameter(Mandatory=$false)] [string] $E_ExpectedDdrType = "any",
    [Parameter(Mandatory=$false)] [int]    $N_MinConfiguredClockMHz = 0,
    [Parameter(Mandatory=$false)] [bool]   $B_AllowMixedDdrTypes = $false,
    [Parameter(Mandatory=$false)] [bool]   $B_ExportModulesJson = $true
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
        [string]   $Ts,
        [string]   $StepLine,
        [string[]] $StepDetails,
        [int]      $Total,
        [int]      $Passed,
        [int]      $Failed,
        [int]      $Skipped
    )

    Write-Output ("TEST: name={0} ts_utc={1}" -f $TestName, $Ts)
    Write-Output ("STEPS: total={0} pass={1} fail={2} skip={3}" -f $Total, $Passed, $Failed, $Skipped)
    Write-Output ("STEP: {0}" -f $StepLine)
    foreach ($d in $StepDetails) {
        Write-Output ("  - {0}" -f $d)
    }
    Write-Output ("MACHINE: overall={0} exit_code={1}" -f $Overall, $ExitCode)
}

function Convert-SmbiosMemoryTypeToDdrType([int] $SmbiosMemoryType) {
    switch ($SmbiosMemoryType) {
        26 { return "DDR4" }
        34 { return "DDR5" }
        default { return [string]$SmbiosMemoryType }
    }
}

# ----------------------------
# Metadata
# ----------------------------
$TestId = $env:PVTX_TESTCASE_ID
if ([string]::IsNullOrWhiteSpace($TestId)) { $TestId = "case.hw.memory.core.inventory_check" }
$Ts  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# Normalize E_ExpectedDdrType
$E_ExpectedDdrType = Normalize-Text $E_ExpectedDdrType
$E_ExpectedDdrType = $E_ExpectedDdrType.ToLowerInvariant()

# Validate parameters
$allowedDdr = @("any", "ddr4", "ddr5")
if ($allowedDdr -notcontains $E_ExpectedDdrType) {
    throw "Invalid E_ExpectedDdrType '$E_ExpectedDdrType'. Allowed: any|ddr4|ddr5"
}
if ($N_MinTotalGB -lt 0) { throw "N_MinTotalGB must be >= 0." }
if ($N_MinModuleCount -lt 0) { throw "N_MinModuleCount must be >= 0." }
if ($N_MinConfiguredClockMHz -lt 0) { throw "N_MinConfiguredClockMHz must be >= 0." }

# Artifacts
$ArtifactsRoot = Join-Path (Get-Location) "artifacts"
Ensure-Dir $ArtifactsRoot
$ReportPath = Join-Path $ArtifactsRoot "report.json"
$ModulesPath = Join-Path $ArtifactsRoot "memory_modules.json"

# Defaults
$overallStatus = "FAIL"
$exitCode = 2  # 2 = script/runtime error unless overridden

# Step definition
$step = @{
    id      = "memory_inventory_check"
    index   = 1
    name    = "Memory inventory and requirement check"
    status  = "FAIL"
    expected = @{
        min_total_gb = $N_MinTotalGB
        min_module_count = $N_MinModuleCount
        expected_ddr_type = $E_ExpectedDdrType
        min_configured_clock_mhz = $N_MinConfiguredClockMHz
        allow_mixed_ddr_types = $B_AllowMixedDdrTypes
    }
    actual = @{
        module_count = $null
        total_gb = $null
        ddr_types_present = @()
        min_configured_clock_mhz = $null
        modules_sample = @()
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

    # Enumerate memory modules
    $raw = Get-CimInstance Win32_PhysicalMemory

    if ($null -eq $raw -or ($raw | Measure-Object).Count -eq 0) {
        throw "No physical memory modules were returned by Win32_PhysicalMemory."
    }

    $modules = $raw | Select-Object `
        BankLabel, DeviceLocator, `
        @{n="CapacityGB";e={[math]::Round($_.Capacity/1GB)}}, `
        Manufacturer, PartNumber, `
        Speed, ConfiguredClockSpeed, `
        @{n="DDR_Type";e={ Convert-SmbiosMemoryTypeToDdrType ([int]$_.SMBIOSMemoryType) }}, `
        SMBIOSMemoryType

    if ($B_ExportModulesJson) {
        Write-JsonFile -Path $ModulesPath -Obj $modules
    }

    $moduleCount = ($modules | Measure-Object).Count
    $totalGb = [int](($modules | Measure-Object -Property CapacityGB -Sum).Sum)

    $typesNorm = @(
        $modules | ForEach-Object {
            $t = $_.DDR_Type
            if ($null -eq $t) { "" } else { ([string]$t).ToLowerInvariant() }
        }
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    $uniqueTypes = @($typesNorm | Select-Object -Unique)

    $cfgClocks = @(
        $modules | ForEach-Object {
            if ($null -eq $_.ConfiguredClockSpeed) { $null } else { [int]$_.ConfiguredClockSpeed }
        }
    ) | Where-Object { $null -ne $_ -and $_ -gt 0 }

    $minCfgClock = $null
    if ($cfgClocks.Count -gt 0) {
        $minCfgClock = ($cfgClocks | Measure-Object -Minimum).Minimum
    }

    $step.actual.module_count = $moduleCount
    $step.actual.total_gb = $totalGb
    $step.actual.ddr_types_present = $uniqueTypes
    $step.actual.min_configured_clock_mhz = $minCfgClock

    # Provide a small sample for report readability
    $step.actual.modules_sample = @(
        $modules | Select-Object -First 8 BankLabel, DeviceLocator, CapacityGB, Manufacturer, PartNumber, Speed, ConfiguredClockSpeed, DDR_Type
    )

    # Checks
    $reasons = New-Object System.Collections.Generic.List[string]

    if ($N_MinModuleCount -gt 0 -and $moduleCount -lt $N_MinModuleCount) {
        $reasons.Add(("Module count {0} is less than required minimum {1}." -f $moduleCount, $N_MinModuleCount))
    }

    if ($N_MinTotalGB -gt 0 -and $totalGb -lt $N_MinTotalGB) {
        $reasons.Add(("Total memory {0}GB is less than required minimum {1}GB." -f $totalGb, $N_MinTotalGB))
    }

    if ($E_ExpectedDdrType -ne "any") {
        $expectedLabel = $E_ExpectedDdrType.ToUpperInvariant()
        foreach ($m in $modules) {
            $t = if ($null -eq $m.DDR_Type) { "" } else { ([string]$m.DDR_Type).ToUpperInvariant() }
            if ($t -ne $expectedLabel) {
                $reasons.Add(("Module '{0}' reports DDR_Type '{1}', expected '{2}'." -f $m.DeviceLocator, $t, $expectedLabel))
            }
        }
    }

    if (-not $B_AllowMixedDdrTypes -and $uniqueTypes.Count -gt 1) {
        $reasons.Add(("Mixed DDR types detected: {0}. Set B_AllowMixedDdrTypes=true to allow." -f ($uniqueTypes -join ", ")))
    }

    if ($N_MinConfiguredClockMHz -gt 0) {
        foreach ($m in $modules) {
            $clk = if ($null -eq $m.ConfiguredClockSpeed) { 0 } else { [int]$m.ConfiguredClockSpeed }
            if ($clk -lt $N_MinConfiguredClockMHz) {
                $reasons.Add(("Module '{0}' ConfiguredClockSpeed {1}MHz is less than required minimum {2}MHz." -f $m.DeviceLocator, $clk, $N_MinConfiguredClockMHz))
            }
        }
    }

    if ($reasons.Count -eq 0) {
        $step.status = "PASS"
        $overallStatus = "PASS"
        $exitCode = 0
    }
    else {
        $step.status = "FAIL"
        $overallStatus = "FAIL"
        $exitCode = 1
        $step.message = ($reasons -join " ")
    }
}
catch {
    $step.status = "FAIL"
    $overallStatus = "FAIL"
    $exitCode = 2
    $step.error = @{
        type = $_.Exception.GetType().FullName
        message = $_.Exception.Message
    }
    if ($null -eq $step.message) {
        $step.message = $_.Exception.Message
    }
}
finally {
    if ($swStep) {
        $swStep.Stop()
        $step.timing.duration_ms = [int]$swStep.Elapsed.TotalMilliseconds
    }
    $swTotal.Stop()
    $totalMs = [int]$swTotal.Elapsed.TotalMilliseconds

    # counts
    $passCount = 0
    $failCount = 0
    $skipCount = 0
    if ($step.status -eq "PASS") { $passCount = 1 } else { $failCount = 1 }

    # Report schema (mirrors template)
    $report = @{
        schema = @{ version = "1.0" }
        test = @{
            id = $TestId
            name = $TestId
            params = @{
                n_min_total_gb = $N_MinTotalGB
                n_min_module_count = $N_MinModuleCount
                e_expected_ddr_type = $E_ExpectedDdrType
                n_min_configured_clock_mhz = $N_MinConfiguredClockMHz
                b_allow_mixed_ddr_types = $B_AllowMixedDdrTypes
                b_export_modules_json = $B_ExportModulesJson
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

    Write-JsonFile -Path $ReportPath -Obj $report

    # stdout (compact)
    $dotCount = [Math]::Max(3, 30 - $step.name.Length)
    $stepLine = "[1/1] {0} {1} {2}" -f $step.name, ("." * $dotCount), $step.status

    $details = New-Object System.Collections.Generic.List[string]
    if ($step.actual.module_count -ne $null) {
        $details.Add(("module_count={0} total_gb={1}" -f $step.actual.module_count, $step.actual.total_gb))
        if ($step.actual.ddr_types_present) {
            $details.Add(("ddr_types={0}" -f ($step.actual.ddr_types_present -join ",")))
        }
        if ($step.actual.min_configured_clock_mhz) {
            $details.Add(("min_configured_clock_mhz={0}" -f $step.actual.min_configured_clock_mhz))
        }
    }
    if ($step.status -ne "PASS" -and $step.message) {
        $details.Add(("reason: {0}" -f $step.message))
    }
    if ($B_ExportModulesJson) {
        $details.Add("modules_json=artifacts/memory_modules.json")
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
