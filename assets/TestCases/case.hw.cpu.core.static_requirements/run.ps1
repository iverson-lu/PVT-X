param(
    [Parameter(Mandatory=$true)]  [double] $MinFrequencyGHz,
    [Parameter(Mandatory=$true)]  [string] $CpuNameContains,

    [Parameter(Mandatory=$false)] [string] $FrequencySource = "MaxClockSpeed",
    [Parameter(Mandatory=$false)] [string] $CpuMatchMode = "all",
    [Parameter(Mandatory=$false)] [bool]   $NameCaseSensitive = $false,

    [Parameter(Mandatory=$false)] [int]    $MinCores = 0,
    [Parameter(Mandatory=$false)] [int]    $MinLogicalProcessors = 0,

    [Parameter(Mandatory=$false)] [bool]   $RequireX64 = $true,
    [Parameter(Mandatory=$false)] [int]    $MinL3CacheMB = 0,
    [Parameter(Mandatory=$false)] [int]    $MinL2CacheMB = 0,

    [Parameter(Mandatory=$false)] [bool]   $RequireVMMonitorModeExtensions = $false,
    [Parameter(Mandatory=$false)] [bool]   $RequireSLAT = $false,
    [Parameter(Mandatory=$false)] [bool]   $RequireHyperThreading = $false
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
# Metadata
# ----------------------------
$TestId = if ($env:PVTX_TESTCASE_ID) { $env:PVTX_TESTCASE_ID } else { "case.hw.cpu.core.static_requirements" }
$TestName = if ($env:PVTX_TESTCASE_NAME) { $env:PVTX_TESTCASE_NAME } else { "CPU Static Requirements - Frequency and Name" }
$TestVer = if ($env:PVTX_TESTCASE_VER) { $env:PVTX_TESTCASE_VER } else { "1.0.0" }
$TsUtc  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# Normalize string parameters
$FrequencySource = (Normalize-Text $FrequencySource).ToLowerInvariant()
$CpuMatchMode    = (Normalize-Text $CpuMatchMode).ToLowerInvariant()

# Validate required inputs
if ($MinFrequencyGHz -le 0) {
    throw "MinFrequencyGHz must be > 0."
}
$CpuNameContains = Normalize-Text $CpuNameContains
if ([string]::IsNullOrWhiteSpace($CpuNameContains)) {
    throw "CpuNameContains must be a non-empty string."
}

# Validate enums (Runner enumValues are UI guidance only)
$allowedFreqSource = @("maxclockspeed", "currentclockspeed")
if ($allowedFreqSource -notcontains $FrequencySource) {
    throw "Invalid FrequencySource '$FrequencySource'. Allowed: MaxClockSpeed|CurrentClockSpeed"
}
$allowedMatch = @("all", "any")
if ($allowedMatch -notcontains $CpuMatchMode) {
    throw "Invalid CpuMatchMode '$CpuMatchMode'. Allowed: all|any"
}

# Validate numeric thresholds
if ($MinCores -lt 0) { throw "MinCores must be >= 0." }
if ($MinLogicalProcessors -lt 0) { throw "MinLogicalProcessors must be >= 0." }
if ($MinL3CacheMB -lt 0) { throw "MinL3CacheMB must be >= 0." }
if ($MinL2CacheMB -lt 0) { throw "MinL2CacheMB must be >= 0." }

# Artifacts
$ArtifactsRoot = Join-Path (Get-Location) "artifacts"
Ensure-Dir $ArtifactsRoot
$ReportPath = Join-Path $ArtifactsRoot "report.json"

# Defaults
$overallStatus = "FAIL"
$exitCode = 2  # 2 = script/environment error unless overridden
$steps = New-Object System.Collections.Generic.List[object]

$sw = [System.Diagnostics.Stopwatch]::StartNew()

function Eval-MatchMode([bool[]] $Results, [string] $Mode) {
    if ($null -eq $Results -or $Results.Count -eq 0) { return $false }
    if ($Mode -eq "all") {
        foreach ($r in $Results) { if (-not $r) { return $false } }
        return $true
    }
    else {
        foreach ($r in $Results) { if ($r) { return $true } }
        return $false
    }
}

function Get-FreqMhz($cpu, [string] $src) {
    if ($src -eq "maxclockspeed") { return [int]($cpu.MaxClockSpeed) }
    return [int]($cpu.CurrentClockSpeed)
}

try {
    # ----------------------------
    # Step 1: Enumerate CPU
    # ----------------------------
    $step1 = @{
        id = "enumerate_cpu"
        index = 1
        name = "Enumerate CPU"
        status = "FAIL"
        expected = @{ cpu_present = $true }
        actual = @{ }
        metrics = @{ }
        message = ""
        error = $null
    }

    $cpus = @()
    $cpuQueryFailed = $false
    try {
        $cpus = Get-CimInstance -ClassName Win32_Processor
    }
    catch {
        $cpuQueryFailed = $true
        $step1.status = "FAIL"
        $step1.message = "Failed to query Win32_Processor via CIM/WMI."
        $step1.error = @{ kind = "ENVIRONMENT"; message = $_.Exception.Message }
        $step1.actual.cpu_count = 0
        $steps.Add($step1)

        # Add placeholder SKIP steps for readability
        $steps.Add(@{ id="validate_frequency"; index=2; name="Validate Frequency"; status="SKIP"; expected=@{}; actual=@{}; metrics=@{}; message="Skipped due to CPU enumeration failure."; error=$null })
        $steps.Add(@{ id="validate_name"; index=3; name="Validate Name"; status="SKIP"; expected=@{}; actual=@{}; metrics=@{}; message="Skipped due to CPU enumeration failure."; error=$null })
        $steps.Add(@{ id="validate_static_capabilities"; index=4; name="Validate Static Capabilities"; status="SKIP"; expected=@{}; actual=@{}; metrics=@{}; message="Skipped due to CPU enumeration failure."; error=$null })

        $overallStatus = "FAIL"
        $exitCode = 2
    }

    if (-not $cpuQueryFailed) {

        if ($null -eq $cpus -or $cpus.Count -eq 0) {
        $step1.status = "FAIL"
        $step1.message = "Win32_Processor returned no instances."
        $step1.error = @{ kind = "ENVIRONMENT"; message = "No CPU instances returned." }
        $step1.actual.cpu_count = 0
        $steps.Add($step1)

        # Add placeholder SKIP steps for readability
        $steps.Add(@{ id="validate_frequency"; index=2; name="Validate Frequency"; status="SKIP"; expected=@{}; actual=@{}; metrics=@{}; message="Skipped due to CPU enumeration failure."; error=$null })
        $steps.Add(@{ id="validate_name"; index=3; name="Validate Name"; status="SKIP"; expected=@{}; actual=@{}; metrics=@{}; message="Skipped due to CPU enumeration failure."; error=$null })
        $steps.Add(@{ id="validate_static_capabilities"; index=4; name="Validate Static Capabilities"; status="SKIP"; expected=@{}; actual=@{}; metrics=@{}; message="Skipped due to CPU enumeration failure."; error=$null })

        $overallStatus = "FAIL"
        $exitCode = 2
    }
    else {
        $cpuList = New-Object System.Collections.Generic.List[object]
        $maxFreqGhzList = New-Object System.Collections.Generic.List[double]
        $curFreqGhzList = New-Object System.Collections.Generic.List[double]
        $totalCores = 0
        $totalLogical = 0

        $i = 0
        foreach ($c in $cpus) {
            $i++
            $maxMhz = [int]($c.MaxClockSpeed)
            $curMhz = [int]($c.CurrentClockSpeed)
            $maxGhz = if ($maxMhz -gt 0) { [Math]::Round($maxMhz / 1000.0, 3) } else { 0.0 }
            $curGhz = if ($curMhz -gt 0) { [Math]::Round($curMhz / 1000.0, 3) } else { 0.0 }

            if ($maxGhz -gt 0) { $maxFreqGhzList.Add($maxGhz) }
            if ($curGhz -gt 0) { $curFreqGhzList.Add($curGhz) }

            $cores = [int]($c.NumberOfCores)
            $logical = [int]($c.NumberOfLogicalProcessors)
            $totalCores += $cores
            $totalLogical += $logical

            $cpuList.Add(@{
                socket_index = $i
                name = $c.Name
                max_clock_mhz = $maxMhz
                current_clock_mhz = $curMhz
                number_of_cores = $cores
                number_of_logical_processors = $logical
                l2_cache_kb = [int]($c.L2CacheSize)
                l3_cache_kb = [int]($c.L3CacheSize)
                address_width = [int]($c.AddressWidth)
                vm_monitor_mode_extensions = [bool]($c.VMMonitorModeExtensions)
                slat = [bool]($c.SecondLevelAddressTranslationExtensions)
            })
        }

        $step1.status = "PASS"
        $step1.message = "CPU enumeration succeeded."
        $step1.actual.cpu_count = $cpus.Count
        $step1.actual.cpus = $cpuList
        $step1.metrics.total_cores = $totalCores
        $step1.metrics.total_logical_processors = $totalLogical

        if ($maxFreqGhzList.Count -gt 0) {
            $step1.metrics.maxclock_ghz_min = ($maxFreqGhzList | Measure-Object -Minimum).Minimum
            $step1.metrics.maxclock_ghz_max = ($maxFreqGhzList | Measure-Object -Maximum).Maximum
        }
        if ($curFreqGhzList.Count -gt 0) {
            $step1.metrics.currentclock_ghz_min = ($curFreqGhzList | Measure-Object -Minimum).Minimum
            $step1.metrics.currentclock_ghz_max = ($curFreqGhzList | Measure-Object -Maximum).Maximum
        }

        $steps.Add($step1)

        # ----------------------------
        # Step 2: Validate Frequency
        # ----------------------------
        $step2 = @{
            id = "validate_frequency"
            index = 2
            name = "Validate Frequency"
            status = "FAIL"
            expected = @{ min_frequency_ghz = $MinFrequencyGHz; source = $FrequencySource }
            actual = @{ }
            metrics = @{ }
            message = ""
            error = $null
        }

        $perCpu = New-Object System.Collections.Generic.List[object]
        $results = New-Object System.Collections.Generic.List[bool]
        foreach ($c in $cpuList) {
            $mhz = 0
            if ($FrequencySource -eq "maxclockspeed") { $mhz = [int]$c.max_clock_mhz } else { $mhz = [int]$c.current_clock_mhz }
            $ghz = if ($mhz -gt 0) { [Math]::Round($mhz / 1000.0, 3) } else { 0.0 }
            $ok = ($ghz -ge $MinFrequencyGHz) -and ($ghz -gt 0)
            $results.Add($ok)

            $perCpu.Add(@{
                socket_index = $c.socket_index
                name = $c.name
                freq_source = $FrequencySource
                freq_mhz = $mhz
                freq_ghz = $ghz
                pass = $ok
            })
        }

        $overallFreq = Eval-MatchMode -Results $results.ToArray() -Mode $CpuMatchMode
        $step2.actual.per_cpu = $perCpu
        $step2.actual.match_mode = $CpuMatchMode
        $step2.metrics.min_required_ghz = $MinFrequencyGHz
        $step2.metrics.pass = $overallFreq

        $observed = @()
        foreach ($p in $perCpu) { if ($p.freq_ghz -gt 0) { $observed += $p.freq_ghz } }
        if ($observed.Count -gt 0) {
            $step2.metrics.observed_ghz_min = ($observed | Measure-Object -Minimum).Minimum
            $step2.metrics.observed_ghz_max = ($observed | Measure-Object -Maximum).Maximum
        }

        if ($overallFreq) {
            $step2.status = "PASS"
            $step2.message = "CPU frequency meets minimum requirement."
        }
        else {
            $step2.status = "FAIL"
            $step2.message = "CPU frequency is below the required minimum or missing."
        }
        $steps.Add($step2)

        # ----------------------------
        # Step 3: Validate Name
        # ----------------------------
        $step3 = @{
            id = "validate_name"
            index = 3
            name = "Validate Name"
            status = "FAIL"
            expected = @{ name_contains = $CpuNameContains; case_sensitive = $NameCaseSensitive }
            actual = @{ }
            metrics = @{ }
            message = ""
            error = $null
        }

        $needle = $CpuNameContains
        if (-not $NameCaseSensitive) {
            $needle = $needle.ToLowerInvariant()
        }

        $perCpuName = New-Object System.Collections.Generic.List[object]
        $nameResults = New-Object System.Collections.Generic.List[bool]
        foreach ($c in $cpuList) {
            $hay = [string]$c.name
            $cmp = $hay
            if (-not $NameCaseSensitive) {
                $cmp = $cmp.ToLowerInvariant()
            }
            $ok = $cmp.Contains($needle)
            $nameResults.Add($ok)

            $perCpuName.Add(@{
                socket_index = $c.socket_index
                name = $hay
                pass = $ok
            })
        }

        $overallName = Eval-MatchMode -Results $nameResults.ToArray() -Mode $CpuMatchMode
        $step3.actual.per_cpu = $perCpuName
        $step3.actual.match_mode = $CpuMatchMode
        $step3.metrics.pass = $overallName

        if ($overallName) {
            $step3.status = "PASS"
            $step3.message = "CPU name contains the required substring."
        }
        else {
            $step3.status = "FAIL"
            $step3.message = "CPU name does not contain the required substring."
        }
        $steps.Add($step3)

        # ----------------------------
        # Step 4: Validate Static Capabilities (optional gates)
        # ----------------------------
        $step4 = @{
            id = "validate_static_capabilities"
            index = 4
            name = "Validate Static Capabilities"
            status = "FAIL"
            expected = @{ }
            actual = @{ }
            metrics = @{ }
            message = ""
            error = $null
        }

        $enabledChecks = New-Object System.Collections.Generic.List[string]
        if ($MinCores -gt 0) { $enabledChecks.Add("MinCores") ; $step4.expected.min_cores = $MinCores }
        if ($MinLogicalProcessors -gt 0) { $enabledChecks.Add("MinLogicalProcessors") ; $step4.expected.min_logical_processors = $MinLogicalProcessors }
        if ($MinL2CacheMB -gt 0) { $enabledChecks.Add("MinL2CacheMB") ; $step4.expected.min_l2_cache_mb = $MinL2CacheMB }
        if ($MinL3CacheMB -gt 0) { $enabledChecks.Add("MinL3CacheMB") ; $step4.expected.min_l3_cache_mb = $MinL3CacheMB }
        if ($RequireX64) { $enabledChecks.Add("RequireX64") ; $step4.expected.require_x64 = $true }
        if ($RequireVMMonitorModeExtensions) { $enabledChecks.Add("RequireVMMonitorModeExtensions") ; $step4.expected.require_vm_monitor_mode_extensions = $true }
        if ($RequireSLAT) { $enabledChecks.Add("RequireSLAT") ; $step4.expected.require_slat = $true }
        if ($RequireHyperThreading) { $enabledChecks.Add("RequireHyperThreading") ; $step4.expected.require_hyper_threading = $true }

        if ($enabledChecks.Count -eq 0) {
            $step4.status = "SKIP"
            $step4.message = "All optional static capability checks are disabled."
            $steps.Add($step4)
        }
        else {
            $checkResults = New-Object System.Collections.Generic.List[object]
            $allCheckPass = $true

            function Add-CheckResult([string] $CheckName, [object[]] $PerCpuItems, [bool[]] $PerCpuPass) {
                $overall = Eval-MatchMode -Results $PerCpuPass -Mode $CpuMatchMode
                $script:checkResults.Add(@{ name=$CheckName; match_mode=$CpuMatchMode; pass=$overall; per_cpu=$PerCpuItems })
                if (-not $overall) { $script:allCheckPass = $false }
            }

            if ($MinCores -gt 0) {
                $items = New-Object System.Collections.Generic.List[object]
                $passes = New-Object System.Collections.Generic.List[bool]
                foreach ($c in $cpuList) {
                    $ok = ([int]$c.number_of_cores -ge $MinCores)
                    $passes.Add($ok)
                    $items.Add(@{ socket_index=$c.socket_index; cores=[int]$c.number_of_cores; pass=$ok })
                }
                Add-CheckResult -CheckName "MinCores" -PerCpuItems $items.ToArray() -PerCpuPass $passes.ToArray()
            }

            if ($MinLogicalProcessors -gt 0) {
                $items = New-Object System.Collections.Generic.List[object]
                $passes = New-Object System.Collections.Generic.List[bool]
                foreach ($c in $cpuList) {
                    $ok = ([int]$c.number_of_logical_processors -ge $MinLogicalProcessors)
                    $passes.Add($ok)
                    $items.Add(@{ socket_index=$c.socket_index; logical_processors=[int]$c.number_of_logical_processors; pass=$ok })
                }
                Add-CheckResult -CheckName "MinLogicalProcessors" -PerCpuItems $items.ToArray() -PerCpuPass $passes.ToArray()
            }

            if ($MinL2CacheMB -gt 0) {
                $items = New-Object System.Collections.Generic.List[object]
                $passes = New-Object System.Collections.Generic.List[bool]
                foreach ($c in $cpuList) {
                    $mb = [Math]::Round(([int]$c.l2_cache_kb) / 1024.0, 3)
                    $ok = ($mb -ge $MinL2CacheMB)
                    $passes.Add($ok)
                    $items.Add(@{ socket_index=$c.socket_index; l2_cache_mb=$mb; pass=$ok })
                }
                Add-CheckResult -CheckName "MinL2CacheMB" -PerCpuItems $items.ToArray() -PerCpuPass $passes.ToArray()
            }

            if ($MinL3CacheMB -gt 0) {
                $items = New-Object System.Collections.Generic.List[object]
                $passes = New-Object System.Collections.Generic.List[bool]
                foreach ($c in $cpuList) {
                    $mb = [Math]::Round(([int]$c.l3_cache_kb) / 1024.0, 3)
                    $ok = ($mb -ge $MinL3CacheMB)
                    $passes.Add($ok)
                    $items.Add(@{ socket_index=$c.socket_index; l3_cache_mb=$mb; pass=$ok })
                }
                Add-CheckResult -CheckName "MinL3CacheMB" -PerCpuItems $items.ToArray() -PerCpuPass $passes.ToArray()
            }

            if ($RequireX64) {
                $items = New-Object System.Collections.Generic.List[object]
                $passes = New-Object System.Collections.Generic.List[bool]
                foreach ($c in $cpuList) {
                    $aw = [int]$c.address_width
                    $ok = ($aw -ge 64)
                    $passes.Add($ok)
                    $items.Add(@{ socket_index=$c.socket_index; address_width=$aw; pass=$ok })
                }
                Add-CheckResult -CheckName "RequireX64" -PerCpuItems $items.ToArray() -PerCpuPass $passes.ToArray()
            }

            if ($RequireVMMonitorModeExtensions) {
                $items = New-Object System.Collections.Generic.List[object]
                $passes = New-Object System.Collections.Generic.List[bool]
                foreach ($c in $cpuList) {
                    $cap = [bool]$c.vm_monitor_mode_extensions
                    $ok = ($cap -eq $true)
                    $passes.Add($ok)
                    $items.Add(@{ socket_index=$c.socket_index; vm_monitor_mode_extensions=$cap; pass=$ok })
                }
                Add-CheckResult -CheckName "RequireVMMonitorModeExtensions" -PerCpuItems $items.ToArray() -PerCpuPass $passes.ToArray()
            }

            if ($RequireSLAT) {
                $items = New-Object System.Collections.Generic.List[object]
                $passes = New-Object System.Collections.Generic.List[bool]
                foreach ($c in $cpuList) {
                    $cap = [bool]$c.slat
                    $ok = ($cap -eq $true)
                    $passes.Add($ok)
                    $items.Add(@{ socket_index=$c.socket_index; slat=$cap; pass=$ok })
                }
                Add-CheckResult -CheckName "RequireSLAT" -PerCpuItems $items.ToArray() -PerCpuPass $passes.ToArray()
            }

            if ($RequireHyperThreading) {
                $items = New-Object System.Collections.Generic.List[object]
                $passes = New-Object System.Collections.Generic.List[bool]
                foreach ($c in $cpuList) {
                    $cores = [int]$c.number_of_cores
                    $logical = [int]$c.number_of_logical_processors
                    $ok = ($logical -gt $cores)
                    $passes.Add($ok)
                    $items.Add(@{ socket_index=$c.socket_index; cores=$cores; logical_processors=$logical; pass=$ok })
                }
                Add-CheckResult -CheckName "RequireHyperThreading" -PerCpuItems $items.ToArray() -PerCpuPass $passes.ToArray()
            }

            $step4.actual.enabled_checks = $enabledChecks
            $step4.actual.checks = $checkResults

            if ($allCheckPass) {
                $step4.status = "PASS"
                $step4.message = "Static capability requirements met."
            }
            else {
                $step4.status = "FAIL"
                $step4.message = "One or more static capability requirements not met."
            }
            $steps.Add($step4)
        }

        # ----------------------------
        # Overall Result
        # ----------------------------
        $hasFail = $false
        $hasErr = $false
        foreach ($s in $steps) {
            if ($s.status -eq "FAIL") { $hasFail = $true }
            if ($null -ne $s.error) { $hasErr = $true }
        }

        if (-not $hasFail -and -not $hasErr) {
            $overallStatus = "PASS"
            $exitCode = 0
        }
        else {
            $overallStatus = "FAIL"
            if ($hasErr) { $exitCode = 2 } else { $exitCode = 1 }
        }
    }
    }
}
catch {
    # Unexpected script failure
    if ($exitCode -lt 2) { $exitCode = 2 }
    $overallStatus = "FAIL"

    $errStep = @{
        id = "script_error"
        index = ($steps.Count + 1)
        name = "Script Error"
        status = "FAIL"
        expected = @{ }
        actual = @{ }
        metrics = @{ }
        message = "Script error: $($_.Exception.Message)"
        error = @{ kind = "SCRIPT"; message = $_.Exception.Message }
    }
    $steps.Add($errStep)
}
finally {
    $sw.Stop()
    $totalMs = $sw.ElapsedMilliseconds

    $passCount = 0
    $failCount = 0
    $skipCount = 0
    foreach ($s in $steps) {
        if ($s.status -eq "PASS") { $passCount++ }
        elseif ($s.status -eq "FAIL") { $failCount++ }
        elseif ($s.status -eq "SKIP") { $skipCount++ }
    }

    # ----------------------------
    # report.json (structured format)
    # ----------------------------
    $report = @{
        schema = @{ version = "1.0" }
        test = @{
            id = $TestId
            name = $TestName
            version = $TestVer
            params = @{
                min_frequency_ghz = $MinFrequencyGHz
                cpu_name_contains = $CpuNameContains
                frequency_source = $FrequencySource
                cpu_match_mode = $CpuMatchMode
                name_case_sensitive = $NameCaseSensitive
                min_cores = $MinCores
                min_logical_processors = $MinLogicalProcessors
                require_x64 = $RequireX64
                min_l3_cache_mb = $MinL3CacheMB
                min_l2_cache_mb = $MinL2CacheMB
                require_vm_monitor_mode_extensions = $RequireVMMonitorModeExtensions
                require_slat = $RequireSLAT
                require_hyper_threading = $RequireHyperThreading
            }
        }
        summary = @{
            status = $overallStatus
            exit_code = $exitCode
            counts = @{
                total = $steps.Count
                pass = $passCount
                fail = $failCount
                skip = $skipCount
            }
            duration_ms = $totalMs
        }
        steps = $steps
    }

    Write-JsonFile $ReportPath $report

    # ----------------------------
    # stdout (compact)
    # ----------------------------
    $total = $steps.Count
    $primary = $null
    foreach ($s in $steps) { if ($s.status -eq "FAIL") { $primary = $s; break } }
    if ($null -eq $primary) { $primary = $steps[0] }

    $dotCount = [Math]::Max(3, 30 - $primary.name.Length)
    $stepLine = "[0/1] 2 3 4" -f $primary.index, $total, $primary.name, ("." * $dotCount), $primary.status

    $details = New-Object System.Collections.Generic.List[string]
    foreach ($s in $steps) {
        $details.Add(("0. 1: 2" -f $s.index, $s.name, $s.status))
    }
    if ($primary.message) {
        $details.Add(("reason: 0" -f $primary.message))
    }

    Write-Stdout-Compact `
        -TestName $TestId `
        -Overall $overallStatus `
        -ExitCode $exitCode `
        -TsUtc $TsUtc `
        -StepLine $stepLine `
        -StepDetails $details.ToArray() `
        -Total $total `
        -Passed $passCount `
        -Failed $failCount `
        -Skipped $skipCount

    exit $exitCode
}
