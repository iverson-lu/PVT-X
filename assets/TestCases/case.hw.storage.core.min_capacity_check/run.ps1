param(
    [Parameter(Mandatory=$false)] [int]  $MinimumDiskCount = 1,
    [Parameter(Mandatory=$false)] [int]  $MinimumDiskSizeGB = 64,
    [Parameter(Mandatory=$false)] [string] $E_Scope = "internal",
    [Parameter(Mandatory=$false)] [bool] $B_IgnoreVirtual = $true
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
$TestId   = if ($env:PVTX_TESTCASE_ID) { $env:PVTX_TESTCASE_ID } else { "case.hw.storage.core.min_capacity_check" }
$TestName = if ($env:PVTX_TESTCASE_NAME) { $env:PVTX_TESTCASE_NAME } else { "Storage Minimum Disk Count and Capacity Check" }
$TsUtc    = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# Normalize and validate E_Scope
$E_Scope = Normalize-Text $E_Scope
$E_Scope = $E_Scope.ToLowerInvariant()

$allowedScopes = @("internal", "all")
if ($allowedScopes -notcontains $E_Scope) {
    throw "Invalid E_Scope '$E_Scope'. Allowed: internal|all"
}

# Basic parameter validation
$validationErrors = @()
if ($MinimumDiskCount -isnot [int] -and $MinimumDiskCount -isnot [int32]) { $validationErrors += "MinimumDiskCount: Expected int" }
if ($MinimumDiskSizeGB -isnot [int] -and $MinimumDiskSizeGB -isnot [int32]) { $validationErrors += "MinimumDiskSizeGB: Expected int" }
if ($MinimumDiskCount -lt 1) { $validationErrors += "MinimumDiskCount: Must be >= 1" }
if ($MinimumDiskSizeGB -lt 1) { $validationErrors += "MinimumDiskSizeGB: Must be >= 1" }
if ($validationErrors.Count -gt 0) {
    throw "Parameter validation failed: " + ($validationErrors -join "; ")
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
    id      = "storage_requirements"
    index   = 1
    name    = "Validate storage requirements"
    status  = "FAIL"
    expected = @{
        minimum_disk_count = $MinimumDiskCount
        minimum_disk_size_gb = $MinimumDiskSizeGB
        scope = $E_Scope
        ignore_virtual = $B_IgnoreVirtual
    }
    actual = @{
        disk_count = 0
        undersized_disks = @()
    }
    metrics = @{
        enumerator = $null
        eligible_disks = @()
        excluded_disks = @()
    }
    message = $null
    timing = @{ duration_ms = $null }
    error = $null
}

# timers
$swTotal = [System.Diagnostics.Stopwatch]::StartNew()
$swStep = $null

try {
    $swStep = [System.Diagnostics.Stopwatch]::StartNew()

    # Enumerate disks (prefer Get-PhysicalDisk, fallback to Get-Disk)
    $rawDisks = @()
    $enumerator = $null

    if (Get-Command Get-PhysicalDisk -ErrorAction SilentlyContinue) {
        try {
            $rawDisks = @(Get-PhysicalDisk)
            $enumerator = "Get-PhysicalDisk"
        }
        catch {
            $rawDisks = @()
        }
    }

    if (-not $enumerator) {
        if (-not (Get-Command Get-Disk -ErrorAction SilentlyContinue)) {
            throw "Neither Get-PhysicalDisk nor Get-Disk is available in this environment."
        }
        $rawDisks = @(Get-Disk)
        $enumerator = "Get-Disk"
    }

    $step.metrics.enumerator = $enumerator

    # Normalize and filter
    $externalBusTypes = @("USB", "SD", "MMC")

    $eligible = New-Object System.Collections.Generic.List[object]
    $excluded = New-Object System.Collections.Generic.List[object]

    foreach ($d in $rawDisks) {
        $friendly = $null
        $busType = $null
        $mediaType = $null
        $sizeBytes = [int64]0

        try { $friendly = [string]$d.FriendlyName } catch { $friendly = $null }
        try {
            if ($d.PSObject.Properties.Name -contains "BusType") { $busType = [string]$d.BusType } else { $busType = $null }
        } catch { $busType = $null }
        try {
            if ($d.PSObject.Properties.Name -contains "MediaType") { $mediaType = [string]$d.MediaType } else { $mediaType = $null }
        } catch { $mediaType = $null }
        try {
            if ($d.PSObject.Properties.Name -contains "Size") { $sizeBytes = [int64]$d.Size } else { $sizeBytes = [int64]0 }
        } catch { $sizeBytes = [int64]0 }

        $isVirtual = $false
        $friendlyLower = if ($friendly) { $friendly.ToLowerInvariant() } else { "" }
        $busLower = if ($busType) { $busType.ToLowerInvariant() } else { "" }

        if ($busLower -eq "virtual") { $isVirtual = $true }
        if ($friendlyLower -match "virtual|vmware|vbox|hyper-v") { $isVirtual = $true }

        $disk = [ordered]@{
            friendly_name = $friendly
            bus_type = $busType
            media_type = $mediaType
            size_bytes = $sizeBytes
            size_gb = [int][Math]::Floor(($sizeBytes / 1GB))
            is_virtual = $isVirtual
        }

        $excludeReasons = New-Object System.Collections.Generic.List[string]

        if ($B_IgnoreVirtual -and $isVirtual) {
            $excludeReasons.Add("virtual")
        }

        if ($E_Scope -eq "internal") {
            if ($busType -and ($externalBusTypes -contains $busType)) {
                $excludeReasons.Add("scope_internal_excludes_bus_type")
            }
        }

        if ($excludeReasons.Count -gt 0) {
            $disk["exclude_reasons"] = $excludeReasons.ToArray()
            $excluded.Add($disk) | Out-Null
        }
        else {
            $eligible.Add($disk) | Out-Null
        }
    }

    $step.metrics.eligible_disks = $eligible.ToArray()
    $step.metrics.excluded_disks = $excluded.ToArray()

    $diskCount = $eligible.Count
    $step.actual.disk_count = $diskCount

    $undersized = @($eligible | Where-Object { $_.size_gb -lt $MinimumDiskSizeGB })
    $step.actual.undersized_disks = $undersized

    $reasons = New-Object System.Collections.Generic.List[string]
    if ($diskCount -lt $MinimumDiskCount) {
        $reasons.Add(("disk_count_too_low: actual={0} required={1}" -f $diskCount, $MinimumDiskCount))
    }
    if ($undersized.Count -gt 0) {
        $reasons.Add(("undersized_disks: count={0} required_min_gb={1}" -f $undersized.Count, $MinimumDiskSizeGB))
    }

    if ($reasons.Count -eq 0) {
        $step.status = "PASS"
        $step.message = "Storage requirements satisfied."
        $exitCode = 0
        $overallStatus = "PASS"
    }
    else {
        $step.status = "FAIL"
        $step.message = ($reasons -join "; ")
        $exitCode = 1
        $overallStatus = "FAIL"
    }
}
catch {
    $step.status = "FAIL"
    $step.message = $_.Exception.Message
    $step.error = @{
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
            name = $TestName
            params = @{
                minimum_disk_count = $MinimumDiskCount
                minimum_disk_size_gb = $MinimumDiskSizeGB
                e_scope = $E_Scope
                b_ignore_virtual = $B_IgnoreVirtual
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
        $details.Add(("scope={0} ignore_virtual={1} enumerator={2}" -f $E_Scope, $B_IgnoreVirtual, $step.metrics.enumerator))
        $details.Add(("disk_count={0} required_count={1} min_size_gb={2}" -f $step.actual.disk_count, $MinimumDiskCount, $MinimumDiskSizeGB))
    }
    else {
        if ($step.message) { $details.Add("reason: $($step.message)") }
        $details.Add(("expected: minimum_disk_count={0} minimum_disk_size_gb={1}" -f $MinimumDiskCount, $MinimumDiskSizeGB))
        $details.Add(("actual:   disk_count={0} undersized={1}" -f $step.actual.disk_count, @($step.actual.undersized_disks).Count))
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
