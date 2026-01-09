param(
  [Parameter(Mandatory)]
  [ValidateSet('pass','fail','timeout','error')]
  [string]$Mode,
  [string]$Message = 'hello',
  [int]$Count = 42,
  [bool]$Enabled = $true,
  [double]$Threshold = 3.14,
  [string]$DataPath = 'data/test-data.txt',
  [string]$Items  = '[1, 2, 3]',
  [string]$Config = '{"timeout":30,"retry":true}'
)

$ErrorActionPreference = 'Stop'

# ----------------------------
# Metadata
# ----------------------------
$TestId = $env:PVTX_TESTCASE_ID ?? 'unknown_test'
$TsUtc  = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$Mode = (Normalize-Text $Mode).ToLowerInvariant()

$ArtifactsRoot = Join-Path (Get-Location) 'artifacts'
Ensure-Dir $ArtifactsRoot
$ReportPath = Join-Path $ArtifactsRoot 'report.json'

$overall  = 'FAIL'
$exitCode = 2
$details  = [System.Collections.Generic.List[string]]::new()

$steps = @()
$swTotal = [Diagnostics.Stopwatch]::StartNew()

# ----------------------------
# Step 1: basic params
# ----------------------------
$step1 = New-Step 'verify_basic_params' 1 'Verify basic params'
$sw1 = [Diagnostics.Stopwatch]::StartNew()
try {
  # 这里不做“类型校验”（param 已经保证类型），只做一些“基本合理性”示例
  if ([string]::IsNullOrWhiteSpace($Message)) { throw 'Message cannot be empty.' }
  if ($Count -lt 0) { throw 'Count must be >= 0.' }
  if ([double]::IsNaN($Threshold) -or [double]::IsInfinity($Threshold)) { throw 'Threshold must be a finite number.' }

  $step1.metrics = @{
    mode           = $Mode
    message_length = $Message.Length
    count          = $Count
    enabled        = $Enabled
    threshold      = $Threshold
  }

  $step1.status = 'PASS'
  $step1.message = 'Basic params OK.'
}
catch {
  Fail-Step $step1 "Basic param verification failed: $($_.Exception.Message)" $_
  throw
}
finally {
  $sw1.Stop()
  $step1.timing.duration_ms = [int]$sw1.ElapsedMilliseconds
  $steps += $step1
}

# ----------------------------
# Step 2: path + json
# ----------------------------
$step2 = New-Step 'verify_path_and_json' 2 'Verify path + JSON params'
$sw2 = [Diagnostics.Stopwatch]::StartNew()
try {
  $itemsData  = $Items  | ConvertFrom-Json -AsHashtable -ErrorAction Stop
  $configData = $Config | ConvertFrom-Json -AsHashtable -ErrorAction Stop

  if ($itemsData -isnot [object[]])   { throw 'Items must be a JSON array (e.g., [1,2,3]).' }
  if ($configData -isnot [hashtable]) { throw 'Config must be a JSON object (e.g., {"timeout":30}).' }

  $baseDir = $env:PVTX_TESTCASE_PATH ?? $PSScriptRoot
  $resolvedPath = [IO.Path]::IsPathRooted($DataPath) ? $DataPath : (Join-Path $baseDir $DataPath)
  $pathExists = Test-Path -LiteralPath $resolvedPath -ErrorAction SilentlyContinue

  $step2.metrics = @{
    items_count   = $itemsData.Count
    timeout       = $configData.timeout
    retry         = $configData.retry
    path_exists   = $pathExists
    path_resolved = $resolvedPath
  }

  $step2.status = 'PASS'
  $step2.message = 'Path + JSON OK.'
}
catch {
  Fail-Step $step2 "Path/JSON verification failed: $($_.Exception.Message)" $_
  throw
}
finally {
  $sw2.Stop()
  $step2.timing.duration_ms = [int]$sw2.ElapsedMilliseconds
  $steps += $step2
}

# ----------------------------
# Mode forcing, for demo purpose only. Actaual case should rely on real step results.
# ----------------------------
try {
  switch ($Mode) {
    'pass'    { $overall='PASS'; $exitCode=0 }
    'fail'    { $overall='FAIL'; $exitCode=1 }
    'timeout' { $overall='FAIL'; $exitCode=1; Start-Sleep 5 }
    'error'   { throw 'Forced error from Mode=error' }
  }

  $details.Add("basic: message_len=$($step1.metrics.message_length) count=$($step1.metrics.count) enabled=$($step1.metrics.enabled) threshold=$($step1.metrics.threshold)")
  $details.Add("path+json: items=$($step2.metrics.items_count) path_exists=$($step2.metrics.path_exists) timeout=$($step2.metrics.timeout) retry=$($step2.metrics.retry)")
}
catch {
  $overall='FAIL'
  $exitCode=2
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
      id = $TestId
      name = $TestId
      params = @{
        mode      = $Mode
        message   = $Message
        count     = $Count
        enabled   = $Enabled
        threshold = $Threshold
        data_path = $DataPath
        items     = $Items
        config    = $Config
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
      ts_utc      = $TsUtc
    }
    steps = $steps
  }

  $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $ReportPath -Encoding utf8NoBOM

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
    -TsUtc $TsUtc `
    -StepLine ($stepLines -join "`n") `
    -StepDetails $details.ToArray() `
    -Total $steps.Count `
    -Passed ($steps | Where-Object status -EQ 'PASS').Count `
    -Failed ($steps | Where-Object status -EQ 'FAIL').Count `
    -Skipped ($steps | Where-Object status -EQ 'SKIP').Count

  exit $exitCode
}
