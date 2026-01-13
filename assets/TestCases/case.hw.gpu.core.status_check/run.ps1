param(
  [int]$N_SampleDurationSec = 5,
  [int]$N_SampleIntervalSec = 1,
  [bool]$B_CollectPerfCounters = $true,
  [bool]$B_RequirePerfCounters = $false,
  [bool]$B_UseVendorTools = $true,
  [bool]$B_SaveRawOutputs = $false,
  [int]$N_MaxUtilizationPercent = 100,
  [int]$N_MaxDedicatedMemUsagePercent = 100
)

$ErrorActionPreference = 'Stop'

# Require PowerShell 7+
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7+ is required.' }

# ----------------------------
# Helpers
# ----------------------------
function Normalize-Text {
  param([string]$Value)
  if ($null -eq $Value) { return '' }
  return ($Value.Trim())
}

function Ensure-Dir {
  param([Parameter(Mandatory)][string]$Path)
  if (-not (Test-Path -LiteralPath $Path)) {
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
  }
}

function New-Step {
  param(
    [Parameter(Mandatory)][string]$Name,
    [Parameter(Mandatory)][int]$Index,
    [Parameter(Mandatory)][string]$Title
  )
  return @{
    name    = $Name
    index   = $Index
    title   = $Title
    status  = 'FAIL'
    message = ''
    timing  = @{ duration_ms = 0 }
    metrics = @{}
    error   = $null
  }
}

function Fail-Step {
  param(
    [Parameter(Mandatory)][hashtable]$Step,
    [Parameter(Mandatory)][string]$Message,
    $ExceptionObject
  )
  $Step.status = 'FAIL'
  $Step.message = $Message
  if ($null -ne $ExceptionObject) {
    $ex = $ExceptionObject.Exception
    $Step.error = @{
      message = $ex.Message
      type    = $ex.GetType().FullName
      stack   = $ex.StackTrace
    }
  }
}

function Skip-Step {
  param(
    [Parameter(Mandatory)][hashtable]$Step,
    [Parameter(Mandatory)][string]$Message
  )
  $Step.status = 'SKIP'
  $Step.message = $Message
}

function Write-Stdout-Compact {
  param(
    [Parameter(Mandatory)][string]$TestName,
    [Parameter(Mandatory)][string]$Overall,
    [Parameter(Mandatory)][int]$ExitCode,
    [Parameter(Mandatory)][string]$Ts,
    [Parameter(Mandatory)][string]$StepLine,
    [string[]]$StepDetails = @(),
    [int]$Total = 0,
    [int]$Passed = 0,
    [int]$Failed = 0,
    [int]$Skipped = 0
  )

  Write-Output ("test_name: {0}" -f $TestName)
  Write-Output ("overall: {0}" -f $Overall)
  Write-Output ("exit_code: {0}" -f $ExitCode)
  Write-Output ("ts_utc: {0}" -f $Ts)
  Write-Output ("counts: total={0} pass={1} fail={2} skip={3}" -f $Total, $Passed, $Failed, $Skipped)
  Write-Output "steps:"
  if (-not [string]::IsNullOrWhiteSpace($StepLine)) {
    Write-Output $StepLine
  }
  if ($StepDetails.Count -gt 0) {
    Write-Output "details:"
    foreach ($d in $StepDetails) { Write-Output ("- {0}" -f $d) }
  }
}

function Append-Raw {
  param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][string]$Text
  )
  Add-Content -LiteralPath $Path -Value $Text -Encoding utf8NoBOM
}

# ----------------------------
# Metadata
# ----------------------------
$TestId  = $env:PVTX_TESTCASE_ID  ?? 'unknown_test'
$TestName= $env:PVTX_TESTCASE_NAME ?? $TestId
$TestVer = $env:PVTX_TESTCASE_VER ?? '0.0.0'
$Ts   = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ssZ')

$ArtifactsRoot = Join-Path (Get-Location) 'artifacts'
Ensure-Dir $ArtifactsRoot
$ReportPath = Join-Path $ArtifactsRoot 'report.json'
$RawPath = Join-Path $ArtifactsRoot 'gpu_status.txt'

$overall  = 'FAIL'
$exitCode = 2
$details  = [System.Collections.Generic.List[string]]::new()
$steps    = @()
$swTotal  = [Diagnostics.Stopwatch]::StartNew()

# ----------------------------
# Input validation (intent-level)
# ----------------------------
if ($N_SampleDurationSec -lt 1 -or $N_SampleDurationSec -gt 60) { throw "N_SampleDurationSec must be between 1 and 60." }
if ($N_SampleIntervalSec -lt 1 -or $N_SampleIntervalSec -gt 10) { throw "N_SampleIntervalSec must be between 1 and 10." }
if ($N_SampleIntervalSec -gt $N_SampleDurationSec) { throw "N_SampleIntervalSec must be <= N_SampleDurationSec." }
if ($N_MaxUtilizationPercent -lt 1 -or $N_MaxUtilizationPercent -gt 100) { throw "N_MaxUtilizationPercent must be between 1 and 100." }
if ($N_MaxDedicatedMemUsagePercent -lt 1 -or $N_MaxDedicatedMemUsagePercent -gt 100) { throw "N_MaxDedicatedMemUsagePercent must be between 1 and 100." }

try {
  # ----------------------------
  # Step 1: Enumerate GPUs
  # ----------------------------
  $step1 = New-Step 'enumerate_gpus' 1 'Enumerate GPUs (CIM)'
  $sw1 = [Diagnostics.Stopwatch]::StartNew()
  $gpuList = @()
  try {
    $gpuList = @(Get-CimInstance -ClassName Win32_VideoController | Sort-Object -Property Name)
    $step1.metrics.gpu_count = $gpuList.Count

    if ($B_SaveRawOutputs) {
      Append-Raw $RawPath ("=== Win32_VideoController ({0}) ===`n" -f $Ts)
      Append-Raw $RawPath (($gpuList | Select-Object Name, Status, DriverVersion, PNPDeviceID, AdapterCompatibility, VideoProcessor, AdapterRAM | Format-List | Out-String).TrimEnd() + "`n`n")
    }

    if ($gpuList.Count -lt 1) { throw 'No GPU was enumerated via Win32_VideoController.' }

    $step1.metrics.gpus = @(
      $gpuList | ForEach-Object {
        @{
          name              = $_.Name
          status            = $_.Status
          driver_version    = $_.DriverVersion
          pnp_device_id     = $_.PNPDeviceID
          adapter_ram_bytes = $_.AdapterRAM
          vendor            = $_.AdapterCompatibility
          video_processor   = $_.VideoProcessor
        }
      }
    )

    $step1.status = 'PASS'
    $step1.message = ("Enumerated {0} GPU(s)." -f $gpuList.Count)
  }
  catch {
    Fail-Step $step1 "GPU enumeration failed: $($_.Exception.Message)" $_
    throw
  }
  finally {
    $sw1.Stop()
    $step1.timing.duration_ms = [int]$sw1.ElapsedMilliseconds
    $steps += $step1
  }

  # ----------------------------
  # Step 2: Validate GPU status
  # ----------------------------
  $step2 = New-Step 'validate_gpu_status' 2 'Validate GPU status / driver'
  $sw2 = [Diagnostics.Stopwatch]::StartNew()
  $unhealthy = @()
  try {
    foreach ($g in $gpuList) {
      $nm = Normalize-Text $g.Name
      $st = Normalize-Text $g.Status
      $dv = Normalize-Text $g.DriverVersion

      $issues = @()
      if ([string]::IsNullOrWhiteSpace($nm)) { $issues += 'Name is empty' }
      if ([string]::IsNullOrWhiteSpace($st)) { $issues += 'Status is empty' }
      elseif ($st -ne 'OK') { $issues += ("Status is '{0}'" -f $st) }
      if ([string]::IsNullOrWhiteSpace($dv)) { $issues += 'DriverVersion is empty' }

      if ($issues.Count -gt 0) {
        $unhealthy += @{
          name = $nm
          status = $st
          driver_version = $dv
          issues = $issues
        }
      }
    }

    $step2.metrics.unhealthy_count = $unhealthy.Count
    if ($unhealthy.Count -gt 0) {
      $step2.metrics.unhealthy = $unhealthy
      throw ("One or more GPUs are unhealthy: {0}" -f (($unhealthy | ForEach-Object { $_.name } | Sort-Object -Unique) -join ', '))
    }

    $step2.status = 'PASS'
    $step2.message = 'All enumerated GPUs report Status=OK and have a driver version.'
  }
  catch {
    Fail-Step $step2 "GPU status validation failed: $($_.Exception.Message)" $_
    throw
  }
  finally {
    $sw2.Stop()
    $step2.timing.duration_ms = [int]$sw2.ElapsedMilliseconds
    $steps += $step2
  }

  # ----------------------------
  # Step 3: Performance counters (optional)
  # ----------------------------
  $step3 = New-Step 'collect_perf_counters' 3 'Collect GPU performance counters'
  $sw3 = [Diagnostics.Stopwatch]::StartNew()
  $utilMax = $null
  $dedPctMax = $null
  try {
    if (-not $B_CollectPerfCounters) {
      Skip-Step $step3 'Perf counter collection disabled.'
    }
    else {
      $samples = [Math]::Max(1, [int][Math]::Ceiling($N_SampleDurationSec / $N_SampleIntervalSec))

      $paths = @(
        '\GPU Engine(*)\Utilization Percentage',
        '\GPU Adapter Memory(*)\Dedicated Usage',
        '\GPU Adapter Memory(*)\Dedicated Limit',
        '\GPU Adapter Memory(*)\Shared Usage',
        '\GPU Adapter Memory(*)\Shared Limit'
      )

      $result = $null
      try {
        $result = Get-Counter -Counter $paths -SampleInterval $N_SampleIntervalSec -MaxSamples $samples
      }
      catch {
        if ($B_RequirePerfCounters) { throw }
        Skip-Step $step3 ("Perf counters not available or unreadable: {0}" -f $_.Exception.Message)
        $result = $null
      }

      if ($null -ne $result -and $step3.status -ne 'SKIP') {
        $cs = @($result.CounterSamples)
        if ($cs.Count -lt 1) {
          if ($B_RequirePerfCounters) { throw 'Perf counters returned no samples.' }
          Skip-Step $step3 'Perf counters returned no samples.'
        }
        else {
          if ($B_SaveRawOutputs) {
            Append-Raw $RawPath ("=== Perf Counters ({0}) ===`n" -f $Ts)
            Append-Raw $RawPath ("Requested samples={0} interval={1}s duration={2}s`n" -f $samples, $N_SampleIntervalSec, $N_SampleDurationSec)
            Append-Raw $RawPath (($cs | Select-Object Path, CookedValue | Format-Table -AutoSize | Out-String).TrimEnd() + "`n`n")
          }

          $util = @($cs | Where-Object { $_.Path -like '*\GPU Engine(*)\Utilization Percentage*' } | Select-Object -ExpandProperty CookedValue)
          if ($util.Count -gt 0) {
            $utilMax = [Math]::Round(($util | Measure-Object -Maximum).Maximum, 2)
            $utilAvg = [Math]::Round(($util | Measure-Object -Average).Average, 2)
            $step3.metrics.utilization_percent = @{ max = $utilMax; avg = $utilAvg }
          }

          $dedUsage = @($cs | Where-Object { $_.Path -like '*\GPU Adapter Memory(*)\Dedicated Usage*' } | Select-Object -ExpandProperty CookedValue)
          $dedLimit = @($cs | Where-Object { $_.Path -like '*\GPU Adapter Memory(*)\Dedicated Limit*' } | Select-Object -ExpandProperty CookedValue)
          if ($dedUsage.Count -gt 0) {
            $dedUsageMax = [Math]::Round(($dedUsage | Measure-Object -Maximum).Maximum, 0)
            $step3.metrics.dedicated_usage_bytes = @{ max = [int64]$dedUsageMax }
          }
          if ($dedUsage.Count -gt 0 -and $dedLimit.Count -gt 0) {
            $limitMax = ($dedLimit | Measure-Object -Maximum).Maximum
            if ($limitMax -gt 0) {
              $usageMax = ($dedUsage | Measure-Object -Maximum).Maximum
              $dedPctMax = [Math]::Round((100.0 * $usageMax / $limitMax), 2)
              $step3.metrics.dedicated_usage_percent = @{ max = $dedPctMax; limit_bytes = [int64]$limitMax }
            }
          }

          $step3.status = 'PASS'
          $step3.message = 'Perf counters collected and summarized.'
        }
      }
    }

    # Threshold checks (only if metrics are available)
    $violations = @()
    if ($null -ne $utilMax -and $utilMax -gt $N_MaxUtilizationPercent) {
      $violations += ("Utilization max {0}% > threshold {1}%" -f $utilMax, $N_MaxUtilizationPercent)
    }
    if ($null -ne $dedPctMax -and $dedPctMax -gt $N_MaxDedicatedMemUsagePercent) {
      $violations += ("Dedicated memory max {0}% > threshold {1}%" -f $dedPctMax, $N_MaxDedicatedMemUsagePercent)
    }
    if ($violations.Count -gt 0) {
      $step3.metrics.threshold_violations = $violations
      if ($step3.status -ne 'SKIP') {
        $step3.status = 'FAIL'
        $step3.message = ($violations -join '; ')
        throw ($violations -join '; ')
      }
    }
  }
  catch {
    if ($step3.status -eq 'SKIP' -and -not $B_RequirePerfCounters) {
      # keep SKIP
    }
    else {
      Fail-Step $step3 "Perf counter collection failed: $($_.Exception.Message)" $_
      throw
    }
  }
  finally {
    $sw3.Stop()
    $step3.timing.duration_ms = [int]$sw3.ElapsedMilliseconds
    $steps += $step3
  }

  # ----------------------------
  # Step 4: Vendor tool telemetry (optional)
  # ----------------------------
  $step4 = New-Step 'collect_vendor_telemetry' 4 'Collect vendor telemetry (optional)'
  $sw4 = [Diagnostics.Stopwatch]::StartNew()
  try {
    if (-not $B_UseVendorTools) {
      Skip-Step $step4 'Vendor tool collection disabled.'
    }
    else {
      $cmd = $null
      try { $cmd = Get-Command -Name 'nvidia-smi' -ErrorAction Stop } catch { $cmd = $null }

      if ($null -eq $cmd) {
        Skip-Step $step4 'nvidia-smi not found; vendor telemetry skipped.'
      }
      else {
        $query = 'name,driver_version,temperature.gpu,power.draw,memory.total,memory.used,utilization.gpu'
        $raw = $null
        try {
          $raw = & $cmd.Source "--query-gpu=$query" "--format=csv,noheader,nounits" 2>&1
          if ($LASTEXITCODE -ne 0) {
            throw ("nvidia-smi failed: {0}" -f ($raw | Out-String).Trim())
          }
        }
        catch {
          # Vendor telemetry is best-effort; do not fail the case unless a threshold is exceeded.
          $step4.error = @{
            message = $_.Exception.Message
            type    = $_.Exception.GetType().FullName
            stack   = $_.Exception.StackTrace
          }
          if ($B_SaveRawOutputs -and $null -ne $raw) {
            Append-Raw $RawPath ("=== nvidia-smi (failed) ({0}) ===`n" -f $Ts)
            Append-Raw $RawPath (($raw | Out-String).TrimEnd() + "`n`n")
          }
          Skip-Step $step4 ("nvidia-smi failed; vendor telemetry skipped: {0}" -f $_.Exception.Message)
          $raw = $null
        }

        if ($step4.status -ne 'SKIP') {
          if ($B_SaveRawOutputs) {
            Append-Raw $RawPath ("=== nvidia-smi ({0}) ===`n" -f $Ts)
            Append-Raw $RawPath (($raw | Out-String).TrimEnd() + "`n`n")
          }

          $headers = @('name','driver_version','temperature_c','power_w','memory_total_mb','memory_used_mb','utilization_percent')
          $objs = @()
          foreach ($line in @($raw)) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $objs += ($line | ConvertFrom-Csv -Header $headers)
          }

          foreach ($o in $objs) {
            foreach ($f in @('temperature_c','power_w','memory_total_mb','memory_used_mb','utilization_percent')) {
              if ($o.$f -ne $null -and $o.$f -ne '') {
                $o.$f = [double]($o.$f.ToString().Trim())
              }
            }
          }

          $step4.metrics.gpu_count = $objs.Count
          $step4.metrics.gpus = @(
            $objs | ForEach-Object {
              @{
                name = $_.name
                driver_version = $_.driver_version
                temperature_c = $_.temperature_c
                power_w = $_.power_w
                memory_total_mb = $_.memory_total_mb
                memory_used_mb = $_.memory_used_mb
                utilization_percent = $_.utilization_percent
              }
            }
          )

          # Threshold checks using vendor telemetry when available
          if ($objs.Count -gt 0) {
            $vendorUtilMax = [Math]::Round((($objs | Measure-Object -Property utilization_percent -Maximum).Maximum), 2)
            $step4.metrics.utilization_percent = @{ max = $vendorUtilMax }
            if ($vendorUtilMax -gt $N_MaxUtilizationPercent) {
              $step4.status = 'FAIL'
              $step4.message = ("Utilization max {0}% > threshold {1}%" -f $vendorUtilMax, $N_MaxUtilizationPercent)
              throw $step4.message
            }

            $memPct = @()
            foreach ($o in $objs) {
              if ($o.memory_total_mb -gt 0) { $memPct += (100.0 * $o.memory_used_mb / $o.memory_total_mb) }
            }
            if ($memPct.Count -gt 0) {
              $vendorMemMax = [Math]::Round((($memPct | Measure-Object -Maximum).Maximum), 2)
              $step4.metrics.dedicated_memory_percent = @{ max = $vendorMemMax }
              if ($vendorMemMax -gt $N_MaxDedicatedMemUsagePercent) {
                $step4.status = 'FAIL'
                $step4.message = ("Dedicated memory max {0}% > threshold {1}%" -f $vendorMemMax, $N_MaxDedicatedMemUsagePercent)
                throw $step4.message
              }
            }
          }

          $step4.status = 'PASS'
          $step4.message = 'Vendor telemetry collected via nvidia-smi.'
        }
      }
    }
  }
  catch {
    if ($step4.status -eq 'FAIL') {
      Fail-Step $step4 "Vendor telemetry threshold violation: $($_.Exception.Message)" $_
      throw
    }
    # If it was not a threshold violation, keep SKIP (best-effort).
    if ($step4.status -ne 'SKIP') {
      Skip-Step $step4 ("Vendor telemetry skipped: {0}" -f $_.Exception.Message)
    }
  }
  finally {
    $sw4.Stop()
    $step4.timing.duration_ms = [int]$sw4.ElapsedMilliseconds
    $steps += $step4
  }

  # ----------------------------
  # Overall result
  # ----------------------------
  $overall  = 'PASS'
  $exitCode = 0
  $details.Add("gpu_count: $($step1.metrics.gpu_count)")

  if ($step3.status -eq 'PASS' -and $step3.metrics.utilization_percent) {
    $details.Add("utilization_max_percent: $($step3.metrics.utilization_percent.max)")
  }
  elseif ($step4.status -eq 'PASS' -and $step4.metrics.utilization_percent) {
    $details.Add("utilization_max_percent: $($step4.metrics.utilization_percent.max)")
  }

  if ($step3.status -eq 'PASS' -and $step3.metrics.dedicated_usage_percent) {
    $details.Add("dedicated_mem_max_percent: $($step3.metrics.dedicated_usage_percent.max)")
  }
  elseif ($step4.status -eq 'PASS' -and $step4.metrics.dedicated_memory_percent) {
    $details.Add("dedicated_mem_max_percent: $($step4.metrics.dedicated_memory_percent.max)")
  }
}
catch {
  # If any step throws, overall FAIL unless it is an unhandled script error already set.
  if ($steps | Where-Object { $_.status -eq 'FAIL' }) {
    $overall = 'FAIL'
    $exitCode = 1
  }
  else {
    $overall = 'FAIL'
    $exitCode = 2
  }
  $details.Clear()
  $details.Add("reason: $($_.Exception.Message)")
  $details.Add("exception: $($_.Exception.GetType().FullName)")
}
finally {
  $swTotal.Stop()

  $passCount = ($steps | Where-Object status -EQ 'PASS').Count
  $failCount = ($steps | Where-Object status -EQ 'FAIL').Count
  $skipCount = ($steps | Where-Object status -EQ 'SKIP').Count

  $report = @{
    schema  = @{ version = '1.0' }
    test    = @{
      id      = $TestId
      name    = $TestName
      version = $TestVer
      ts_utc  = $Ts
      parameters = @{
        N_SampleDurationSec = $N_SampleDurationSec
        N_SampleIntervalSec = $N_SampleIntervalSec
        B_CollectPerfCounters = $B_CollectPerfCounters
        B_RequirePerfCounters = $B_RequirePerfCounters
        B_UseVendorTools = $B_UseVendorTools
        B_SaveRawOutputs = $B_SaveRawOutputs
        N_MaxUtilizationPercent = $N_MaxUtilizationPercent
        N_MaxDedicatedMemUsagePercent = $N_MaxDedicatedMemUsagePercent
      }
    }
    summary = @{
      overall = $overall
      exit_code = $exitCode
      counts = @{
        total = $steps.Count
        pass  = $passCount
        fail  = $failCount
        skip  = $skipCount
      }
      duration_ms = [int]$swTotal.ElapsedMilliseconds
      ts_utc      = $Ts
    }
    steps = $steps
  }

  $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $ReportPath -Encoding utf8NoBOM

  $stepLines = @()
  for ($i=0; $i -lt $steps.Count; $i++) {
    $nm = $steps[$i].name
    $dots = [Math]::Max(3, 30 - $nm.Length)
    $stepLines += ("[{0}/{1}] {2} {3} {4}" -f ($i+1), $steps.Count, $nm, ("." * $dots), $steps[$i].status)
  }

  Write-Stdout-Compact `
    -TestName $TestId `
    -Overall $overall `
    -ExitCode $exitCode `
    -Ts $Ts `
    -StepLine ($stepLines -join "`n") `
    -StepDetails $details.ToArray() `
    -Total $steps.Count `
    -Passed $passCount `
    -Failed $failCount `
    -Skipped $skipCount

  exit $exitCode
}
