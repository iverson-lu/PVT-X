param(
    [string]$FailOnStatus = '["Error"]',
    [string]$Allowlist = '[]',
    [bool]$AlwaysCollectDeviceList = $false
)

$ErrorActionPreference = 'Stop'

# ----------------------------
# Metadata
# ----------------------------
$TestId = $env:PVTX_TESTCASE_ID ?? 'case.hw.devmgr.core.status_check'
$Ts = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')

$ArtifactsRoot = Join-Path (Get-Location) 'artifacts'
Ensure-Dir $ArtifactsRoot
$ReportPath = Join-Path $ArtifactsRoot 'report.json'

$overall = 'FAIL'
$exitCode = 2
$details = [System.Collections.Generic.List[string]]::new()

$steps = @()
$swTotal = [Diagnostics.Stopwatch]::StartNew()

# ----------------------------
# Parse parameters
# ----------------------------
$failStatuses = @()
$allowlistRules = @()

try {
    $failStatuses = $FailOnStatus | ConvertFrom-Json -ErrorAction Stop
    if ($failStatuses -isnot [object[]]) {
        $failStatuses = @($failStatuses)
    }
    $failStatuses = $failStatuses | ForEach-Object { $_.ToString().ToLowerInvariant() }
}
catch {
    Write-Error "Failed to parse FailOnStatus: $($_.Exception.Message)"
    exit 2
}

try {
    $allowlistRules = $Allowlist | ConvertFrom-Json -AsHashtable -ErrorAction Stop
    if ($null -eq $allowlistRules) {
        $allowlistRules = @()
    }
    elseif ($allowlistRules -isnot [object[]]) {
        $allowlistRules = @($allowlistRules)
    }
}
catch {
    Write-Error "Failed to parse Allowlist: $($_.Exception.Message)"
    exit 2
}

# ----------------------------
# Status mapping function
# ----------------------------
function Get-NormalizedStatus {
    param([string]$Status, [int]$ProblemCode)

    # Problem codes take priority over Status string
    # CM_PROB_DISABLED (22) = device is disabled
    # CM_PROB_FAILED_INSTALL (28) = driver missing
    if ($ProblemCode -eq 22) { return 'disabled' }
    if ($ProblemCode -eq 28) { return 'drivermissing' }

    # Map PnpDevice status to our categories
    switch ($Status) {
        'OK' { return 'ok' }
        'Degraded' { return 'error' }
        'Unknown' { return 'unknown' }
        'Disabled' { return 'disabled' }
        'Error' {
            # Already handled specific problem codes above
            # Any remaining error is a generic error
            return 'error'
        }
        default {
            # Check remaining problem codes
            if ($ProblemCode -gt 0) { return 'error' }
            if ($Status -match 'Started|Running') { return 'ok' }
            if ($Status -match 'Stopped|NotStarted') { return 'notstarted' }
            return 'unknown'
        }
    }
}

# ----------------------------
# Allowlist matching function
# ----------------------------
function Test-AllowlistMatch {
    param(
        [hashtable]$Device,
        [array]$Rules
    )

    foreach ($rule in $Rules) {
        if ($null -eq $rule -or $rule.Count -eq 0) { continue }

        $matched = $true

        if ($rule.ContainsKey('device_name') -and $rule.device_name) {
            if ($Device.device_name -notlike $rule.device_name) {
                $matched = $false
            }
        }

        if ($matched -and $rule.ContainsKey('hardware_id') -and $rule.hardware_id) {
            $hwMatch = $false
            foreach ($hwid in $Device.hardware_ids) {
                if ($hwid -like $rule.hardware_id) {
                    $hwMatch = $true
                    break
                }
            }
            if (-not $hwMatch) { $matched = $false }
        }

        if ($matched -and $rule.ContainsKey('class') -and $rule.class) {
            if ($Device.class -notlike $rule.class) {
                $matched = $false
            }
        }

        if ($matched -and $rule.ContainsKey('status') -and $rule.status) {
            if ($Device.status -ne $rule.status.ToLowerInvariant()) {
                $matched = $false
            }
        }

        if ($matched) {
            return $true
        }
    }

    return $false
}

# ----------------------------
# Step 1: Enumerate Devices
# ----------------------------
$step1 = New-Step 'enumerate_devices' 1 'Enumerate Devices'
$sw1 = [Diagnostics.Stopwatch]::StartNew()
$allDevices = @()

try {
    $pnpDevices = Get-PnpDevice -ErrorAction Stop

    foreach ($dev in $pnpDevices) {
        $problemCode = 0
        try {
            $problemCode = $dev.ConfigManagerErrorCode
        }
        catch {
            # Some devices may not have this property
        }

        $hwIds = @()
        try {
            $hwIds = @($dev.HardwareID | Where-Object { $_ })
        }
        catch {
            # Some devices may not have hardware IDs
        }

        $normalizedStatus = Get-NormalizedStatus -Status $dev.Status -ProblemCode $problemCode

        $deviceInfo = @{
            device_name  = $dev.FriendlyName ?? $dev.Name ?? $dev.InstanceId
            class        = $dev.Class ?? 'Unknown'
            status       = $normalizedStatus
            problem_code = $problemCode
            instance_id  = $dev.InstanceId
            hardware_ids = $hwIds
        }

        $allDevices += $deviceInfo
    }

    $step1.metrics = @{
        total_devices = $allDevices.Count
    }
    $step1.status = 'PASS'
    $step1.message = "Enumerated $($allDevices.Count) devices."
}
catch {
    Fail-Step $step1 "Device enumeration failed: $($_.Exception.Message)" $_
    $steps += $step1
    throw
}
finally {
    $sw1.Stop()
    $step1.timing.duration_ms = [int]$sw1.ElapsedMilliseconds
    $steps += $step1
}

# ----------------------------
# Step 2: Evaluate Device Status
# ----------------------------
$step2 = New-Step 'evaluate_device_status' 2 'Evaluate Device Status'
$sw2 = [Diagnostics.Stopwatch]::StartNew()
$failedDevices = @()
$allowlistedDevices = @()

try {
    foreach ($device in $allDevices) {
        # Check if device status is in fail list
        if ($device.status -in $failStatuses) {
            # Check if allowlisted
            if (Test-AllowlistMatch -Device $device -Rules $allowlistRules) {
                $allowlistedDevices += $device
            }
            else {
                $failedDevices += $device
            }
        }
    }

    $step2.metrics = @{
        total_devices      = $allDevices.Count
        failed_devices     = $failedDevices.Count
        allowlisted        = $allowlistedDevices.Count
        fail_on_statuses   = ($failStatuses -join ', ')
    }

    if ($failedDevices.Count -eq 0) {
        $step2.status = 'PASS'
        $step2.message = "No devices in abnormal state (or all allowlisted)."
        $overall = 'PASS'
        $exitCode = 0
    }
    else {
        $step2.status = 'FAIL'
        $step2.message = "$($failedDevices.Count) device(s) in abnormal state."
        $overall = 'FAIL'
        $exitCode = 1
    }
}
catch {
    Fail-Step $step2 "Status evaluation failed: $($_.Exception.Message)" $_
    throw
}
finally {
    $sw2.Stop()
    $step2.timing.duration_ms = [int]$sw2.ElapsedMilliseconds
    $steps += $step2
}

# ----------------------------
# Prepare output details
# ----------------------------
$details.Add("total_devices=$($allDevices.Count) failed_devices=$($failedDevices.Count) allowlisted=$($allowlistedDevices.Count)")

if ($failedDevices.Count -gt 0) {
    $details.Add("--- Failed Devices ---")
    foreach ($fd in $failedDevices) {
        $details.Add("  [$($fd.status)] $($fd.device_name) (class=$($fd.class), problem=$($fd.problem_code))")
    }
}

# ----------------------------
# Generate CSV device list files
# ----------------------------
function Export-DevicesToCsv {
    param(
        [array]$Devices,
        [string]$Path
    )
    
    $csvData = $Devices | ForEach-Object {
        [PSCustomObject]@{
            device_name  = $_.device_name
            class        = $_.class
            status       = $_.status
            problem_code = $_.problem_code
            instance_id  = $_.instance_id
            hardware_ids = ($_.hardware_ids -join '; ')
        }
    }
    
    # Use UTF-8 with BOM for Excel compatibility on Chinese Windows
    $csvData | Export-Csv -Path $Path -Encoding utf8BOM -NoTypeInformation
}

# Always output failed devices if any exist
if ($failedDevices.Count -gt 0) {
    $failedPath = Join-Path $ArtifactsRoot 'failed_devices.csv'
    Export-DevicesToCsv -Devices $failedDevices -Path $failedPath
}

# Output all devices if failed or AlwaysCollectDeviceList is enabled
if ($exitCode -ne 0 -or $AlwaysCollectDeviceList) {
    $allDevicesPath = Join-Path $ArtifactsRoot 'all_devices.csv'
    Export-DevicesToCsv -Devices $allDevices -Path $allDevicesPath
}

# Output allowlisted devices if any exist
if ($allowlistedDevices.Count -gt 0) {
    $allowlistedPath = Join-Path $ArtifactsRoot 'allowlisted_devices.csv'
    Export-DevicesToCsv -Devices $allowlistedDevices -Path $allowlistedPath
}

# ----------------------------
# Build report
# ----------------------------
$swTotal.Stop()

$passCount = ($steps | Where-Object status -EQ 'PASS').Count
$failCount = ($steps | Where-Object status -EQ 'FAIL').Count
$skipCount = ($steps | Where-Object status -EQ 'SKIP').Count

# Prepare evidence
$evidence = @{
    failed_devices = $failedDevices
}

# Include all devices if failed or if AlwaysCollectDeviceList is true
if ($exitCode -ne 0 -or $AlwaysCollectDeviceList) {
    $evidence.all_devices = $allDevices
}

if ($allowlistedDevices.Count -gt 0) {
    $evidence.allowlisted_devices = $allowlistedDevices
}

$report = @{
    schema  = @{ version = '1.0' }
    test    = @{
        id     = $TestId
        name   = 'Device Manager Status Check'
        params = @{
            fail_on_status             = $failStatuses
            allowlist                  = $allowlistRules
            always_collect_device_list = $AlwaysCollectDeviceList
        }
    }
    summary = @{
        status      = $overall
        exit_code   = $exitCode
        counts      = @{
            total = $steps.Count
            pass  = $passCount
            fail  = $failCount
            skip  = $skipCount
        }
        duration_ms = [int]$swTotal.ElapsedMilliseconds
        ts_utc      = $Ts
    }
    steps    = $steps
    evidence = $evidence
}

$report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $ReportPath -Encoding utf8NoBOM

# ----------------------------
# Console output
# ----------------------------
$stepLines = @()
for ($i = 0; $i -lt $steps.Count; $i++) {
    $nm = $steps[$i].name
    $dots = [Math]::Max(3, 30 - $nm.Length)
    $stepLines += ("[{0}/{1}] {2} {3} {4}" -f ($i + 1), $steps.Count, $nm, ("." * $dots), $steps[$i].status)
}

Write-Stdout-Compact `
    -TestName $TestId `
    -Overall $overall `
    -ExitCode $exitCode `
    -TsUtc $Ts `
    -StepLine ($stepLines -join "`n") `
    -StepDetails $details.ToArray() `
    -Total $steps.Count `
    -Passed $passCount `
    -Failed $failCount `
    -Skipped $skipCount

exit $exitCode
