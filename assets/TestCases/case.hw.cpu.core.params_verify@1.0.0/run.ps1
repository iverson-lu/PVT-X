param(
    [Parameter(Mandatory=$false)] [int]    $MinPhysicalCores      = 2,
    [Parameter(Mandatory=$false)] [int]    $MinLogicalProcessors  = 2,
    [Parameter(Mandatory=$false)] [int]    $MinMaxClockMHz        = 1000,
    [Parameter(Mandatory=$false)] [bool]   $RequireX64            = $true,
    [Parameter(Mandatory=$false)] [string] $E_Virtualization      = "supported",
    [Parameter(Mandatory=$false)] [string] $ExpectedVendor        = "",
    [Parameter(Mandatory=$false)] [string] $ExpectedNameRegex     = "",
    [Parameter(Mandatory=$false)] [int]    $MaxSockets            = 0,
    [Parameter(Mandatory=$false)] [bool]   $B_SaveRaw             = $false
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
$TestId = $env:PVTX_TESTCASE_ID
if ([string]::IsNullOrWhiteSpace($TestId)) { $TestId = "case.hw.cpu.core.params_verify" }
$Ts  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# Normalize string inputs
$E_Virtualization  = (Normalize-Text $E_Virtualization).ToLowerInvariant()
$ExpectedVendor    = Normalize-Text $ExpectedVendor
$ExpectedNameRegex = Normalize-Text $ExpectedNameRegex

# Validate enum input explicitly
$allowedVirt = @("any", "supported", "enabled")
if ($allowedVirt -notcontains $E_Virtualization) {
    throw "Invalid E_Virtualization '$E_Virtualization'. Allowed: any|supported|enabled"
}

# Basic numeric sanity checks
if ($MinPhysicalCores -lt 0) { throw "MinPhysicalCores must be >= 0." }
if ($MinLogicalProcessors -lt 0) { throw "MinLogicalProcessors must be >= 0." }
if ($MinMaxClockMHz -lt 0) { throw "MinMaxClockMHz must be >= 0." }
if ($MaxSockets -lt 0) { throw "MaxSockets must be >= 0." }

# Artifacts
$ArtifactsRoot = Join-Path (Get-Location) "artifacts"
Ensure-Dir $ArtifactsRoot
$ReportPath = Join-Path $ArtifactsRoot "report.json"

# Step template
$step = @{
    name     = "Verify CPU parameters"
    status   = "FAIL"
    expected = @{
        min_physical_cores     = $MinPhysicalCores
        min_logical_processors = $MinLogicalProcessors
        min_max_clock_mhz      = $MinMaxClockMHz
        require_x64            = $RequireX64
        virtualization         = $E_Virtualization
        expected_vendor        = $ExpectedVendor
        expected_name_regex    = $ExpectedNameRegex
        max_sockets            = $MaxSockets
    }
    actual  = @{}
    metrics = @{}
    message = $null
    timing  = @{ duration_ms = $null }
    error   = $null
}

# timers
$swTotal = [System.Diagnostics.Stopwatch]::StartNew()
$swStep  = $null

# overall
$overallStatus = "FAIL"
$exitCode = 1

try {
    $swStep = [System.Diagnostics.Stopwatch]::StartNew()

    # Collect CPU info
    $cpus = @(Get-CimInstance -ClassName Win32_Processor)
    if ($null -eq $cpus -or $cpus.Count -lt 1) {
        throw "No CPU instances returned by Win32_Processor."
    }

    $socketCount = [int]$cpus.Count
    $totalCores = [int](($cpus | Measure-Object -Property NumberOfCores -Sum).Sum)
    $totalLogical = [int](($cpus | Measure-Object -Property NumberOfLogicalProcessors -Sum).Sum)

    $minMaxClock = ($cpus | Measure-Object -Property MaxClockSpeed -Minimum).Minimum
    $minCurrentClock = ($cpus | Measure-Object -Property CurrentClockSpeed -Minimum).Minimum

    $manufacturers = @($cpus | ForEach-Object { $_.Manufacturer } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
    $names = @($cpus | ForEach-Object { $_.Name } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    $addrWidths = @($cpus | ForEach-Object { $_.AddressWidth } | Where-Object { $null -ne $_ } | Select-Object -Unique)
    $x64Count = [int](@($cpus | Where-Object { $_.AddressWidth -eq 64 }).Count)

    $vmMonCount = [int](@($cpus | Where-Object { $_.VMMonitorModeExtensions -eq $true }).Count)
    $slatCount  = [int](@($cpus | Where-Object { $_.SecondLevelAddressTranslationExtensions -eq $true }).Count)
    $fwVirtEnabledCount = [int](@($cpus | Where-Object { $_.VirtualizationFirmwareEnabled -eq $true }).Count)

    # Populate actual & metrics
    $step.actual = @{
        socket_count              = $socketCount
        total_physical_cores      = $totalCores
        total_logical_processors  = $totalLogical
        manufacturers             = $manufacturers
        cpu_names                 = $names
        address_widths            = $addrWidths
        min_max_clock_mhz         = $minMaxClock
        min_current_clock_mhz     = $minCurrentClock
        vm_monitor_mode_supported = ($vmMonCount -eq $socketCount)
        slat_supported            = ($slatCount -eq $socketCount)
        virtualization_fw_enabled = ($fwVirtEnabledCount -eq $socketCount)
    }

    $step.metrics = @{
        socket_count               = $socketCount
        total_cores                = $totalCores
        total_logical              = $totalLogical
        min_max_clock_mhz          = $minMaxClock
        min_current_clock_mhz      = $minCurrentClock
        x64_socket_count           = $x64Count
        vm_monitor_supported_count = $vmMonCount
        slat_supported_count       = $slatCount
        fw_virt_enabled_count      = $fwVirtEnabledCount
        unique_vendor_count        = $manufacturers.Count
        unique_name_count          = (@($names | Select-Object -Unique)).Count
    }

    # Optional raw output
    if ($B_SaveRaw) {
        $rawPath = Join-Path $ArtifactsRoot "cpu_raw.json"
        $raw = $cpus | ForEach-Object {
            [pscustomobject]@{
                DeviceID = $_.DeviceID
                SocketDesignation = $_.SocketDesignation
                Manufacturer = $_.Manufacturer
                Name = $_.Name
                NumberOfCores = $_.NumberOfCores
                NumberOfLogicalProcessors = $_.NumberOfLogicalProcessors
                MaxClockSpeed = $_.MaxClockSpeed
                CurrentClockSpeed = $_.CurrentClockSpeed
                AddressWidth = $_.AddressWidth
                VMMonitorModeExtensions = $_.VMMonitorModeExtensions
                SecondLevelAddressTranslationExtensions = $_.SecondLevelAddressTranslationExtensions
                VirtualizationFirmwareEnabled = $_.VirtualizationFirmwareEnabled
                Revision = $_.Revision
                ProcessorId = $_.ProcessorId
            }
        }
        Write-JsonFile $rawPath $raw
    }

    # Validations
    $reasons = New-Object System.Collections.Generic.List[string]

    if ($MaxSockets -gt 0 -and $socketCount -gt $MaxSockets) {
        $reasons.Add("socket_count=$socketCount exceeds MaxSockets=$MaxSockets")
    }
    if ($totalCores -lt $MinPhysicalCores) {
        $reasons.Add("total_physical_cores=$totalCores is less than MinPhysicalCores=$MinPhysicalCores")
    }
    if ($totalLogical -lt $MinLogicalProcessors) {
        $reasons.Add("total_logical_processors=$totalLogical is less than MinLogicalProcessors=$MinLogicalProcessors")
    }

    if ($MinMaxClockMHz -gt 0) {
        if ($null -eq $minMaxClock -or [int]$minMaxClock -le 0) {
            $reasons.Add("MaxClockSpeed is not available (cannot validate MinMaxClockMHz=$MinMaxClockMHz)")
        }
        elseif ([int]$minMaxClock -lt $MinMaxClockMHz) {
            $reasons.Add("min_max_clock_mhz=$minMaxClock is less than MinMaxClockMHz=$MinMaxClockMHz")
        }
    }

    if ($RequireX64) {
        if ($x64Count -ne $socketCount) {
            $reasons.Add("RequireX64=true but address widths are $($addrWidths -join ', ')")
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedVendor)) {
        $ok = $true
        foreach ($v in $manufacturers) {
            if ($v -notlike ("*" + $ExpectedVendor + "*")) { $ok = $false; break }
        }
        if (-not $ok) {
            $reasons.Add("vendor mismatch. expected substring='$ExpectedVendor' actual='$($manufacturers -join '; ')'")
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedNameRegex)) {
        $ok = $true
        foreach ($n in $names) {
            if ($n -notmatch $ExpectedNameRegex) { $ok = $false; break }
        }
        if (-not $ok) {
            $reasons.Add("CPU name does not match regex '$ExpectedNameRegex'")
        }
    }

    switch ($E_Virtualization) {
        "any" { }
        "supported" {
            if ($vmMonCount -ne $socketCount) {
                $reasons.Add("virtualization supported check failed: VMMonitorModeExtensions is false on at least one socket")
            }
        }
        "enabled" {
            if ($fwVirtEnabledCount -ne $socketCount) {
                $reasons.Add("virtualization enabled check failed: VirtualizationFirmwareEnabled is not true on all sockets")
            }
        }
        default { }
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
        $step.message = ($reasons -join "; ")
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
                min_physical_cores      = $MinPhysicalCores
                min_logical_processors  = $MinLogicalProcessors
                min_max_clock_mhz        = $MinMaxClockMHz
                require_x64             = $RequireX64
                e_virtualization        = $E_Virtualization
                expected_vendor         = $ExpectedVendor
                expected_name_regex     = $ExpectedNameRegex
                max_sockets             = $MaxSockets
                b_save_raw              = $B_SaveRaw
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
        $details.Add("sockets=$($step.metrics.socket_count) cores=$($step.metrics.total_cores) logical=$($step.metrics.total_logical)")
        if ($step.metrics.min_max_clock_mhz) { $details.Add("min_max_clock_mhz=$($step.metrics.min_max_clock_mhz)") }
        $details.Add("x64_sockets=$($step.metrics.x64_socket_count)")
        $details.Add("vmmon_supported=$($step.actual.vm_monitor_mode_supported) slat_supported=$($step.actual.slat_supported) fwvirt_enabled=$($step.actual.virtualization_fw_enabled)")
    }
    else {
        if ($step.message) { $details.Add("reason: $($step.message)") }
        $details.Add(("actual: sockets={0} cores={1} logical={2} min_max_clock_mhz={3}" -f $step.metrics.socket_count, $step.metrics.total_cores, $step.metrics.total_logical, $step.metrics.min_max_clock_mhz))
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
