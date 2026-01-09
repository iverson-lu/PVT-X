param(
  [Parameter(Mandatory)]
  [ValidateSet('pass','fail','timeout','error')]
  [string]$E_Mode,
  [string]$S_Text = 'hello',
  [int]$N_Int = 42,
  [bool]$B_Flag = $true,
  [double]$N_Double = 3.14,
  [string]$P_Path = 'data/test-data.txt',
  [string]$ItemsJson  = '[1, 2, 3]',
  [string]$ConfigJson = '{"timeout":30,"retry":true}'
)

$ErrorActionPreference = 'Stop'

# ----------------------------
# Metadata
# ----------------------------
$TestId = $env:PVTX_TESTCASE_ID ?? 'unknown_test'
$TsUtc  = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$E_Mode = (Normalize-Text $E_Mode).ToLowerInvariant()

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
  if ([string]::IsNullOrWhiteSpace($S_Text)) { throw 'S_Text cannot be empty.' }
  if ($N_Int -lt 0) { throw 'N_Int must be >= 0.' }
  if ([double]::IsNaN($N_Double) -or [double]::IsInfinity($N_Double)) { throw 'N_Double must be a finite number.' }

  $step1.metrics = @{
    mode        = $E_Mode
    text_length = $S_Text.Length
    int_value   = $N_Int
    flag        = $B_Flag
    double_value= $N_Double
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
  $items  = $ItemsJson  | ConvertFrom-Json -AsHashtable -ErrorAction Stop
  $config = $ConfigJson | ConvertFrom-Json -AsHashtable -ErrorAction Stop

  if ($items -isnot [object[]])   { throw 'ItemsJson must be a JSON array (e.g., [1,2,3]).' }
  if ($config -isnot [hashtable]) { throw 'ConfigJson must be a JSON object (e.g., {"timeout":30}).' }

  $baseDir = $env:PVTX_TESTCASE_PATH ?? $PSScriptRoot
  $resolvedPath = [IO.Path]::IsPathRooted($P_Path) ? $P_Path : (Join-Path $baseDir $P_Path)
  $pathExists = Test-Path -LiteralPath $resolvedPath -ErrorAction SilentlyContinue

  $step2.metrics = @{
    items_count   = $items.Count
    timeout       = $config.timeout
    retry         = $config.retry
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
  switch ($E_Mode) {
    'pass'    { $overall='PASS'; $exitCode=0 }
    'fail'    { $overall='FAIL'; $exitCode=1 }
    'timeout' { $overall='FAIL'; $exitCode=1; Start-Sleep 5 }
    'error'   { throw 'Forced error from E_Mode=error' }
  }

  $details.Add("basic: text_len=$($step1.metrics.text_length) int=$($step1.metrics.int_value) flag=$($step1.metrics.flag) double=$($step1.metrics.double_value)")
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
        e_mode = $E_Mode
        s_text = $S_Text
        n_int  = $N_Int
        b_flag = $B_Flag
        n_double = $N_Double
        p_path = $P_Path
        items_json  = $ItemsJson
        config_json = $ConfigJson
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
