param(
  [Parameter(Mandatory)][ValidateSet('pass','fail','timeout','error')]
  [string]$E_Mode,
  [string]$S_Text     = 'hello',
  [int]   $N_Int      = 42,
  [bool]  $B_Flag     = $true,
  [double]$N_Double   = 3.14,
  [string]$P_Path     = 'data/test-data.txt',
  [string]$ItemsJson  = '[1, 2, 3]',
  [string]$ConfigJson = '{"timeout": 30, "retry": true}'
)

$ErrorActionPreference = 'Stop'

# ----------------------------
# Metadata
# ----------------------------
$TestId = $env:PVTX_TESTCASE_ID
$TsUtc  = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')


$ArtifactsRoot = Join-Path (Get-Location) 'artifacts'
Ensure-Dir $ArtifactsRoot
$ReportPath = Join-Path $ArtifactsRoot 'report.json'

$overallStatus = 'FAIL'
$exitCode = 2

$paramsMap = @{
  e_mode      = $E_Mode
  s_text      = $S_Text
  n_int       = $N_Int
  b_flag      = $B_Flag
  n_double    = $N_Double
  p_path      = $P_Path
  items_json  = $ItemsJson
  config_json = $ConfigJson
}

$step = @{
  id       = 'validate_params'
  index    = 1
  name     = 'Validate all parameters'
  status   = 'FAIL'
  expected = @{ all_params_valid = $true; mode_allowed = $true }
  actual   = @{
    mode         = $E_Mode
    text         = $S_Text
    int_value    = $N_Int
    flag         = $B_Flag
    double_value = $N_Double
    path         = $P_Path
  }
  metrics  = @{}
  message  = $null
  timing   = @{ duration_ms = $null }
  error    = $null
}

$swTotal = [Diagnostics.Stopwatch]::StartNew()
$swStep  = [Diagnostics.Stopwatch]::StartNew()

try {
  $items  = ParseJson $ItemsJson  'ItemsJson'
  $config = ParseJson $ConfigJson 'ConfigJson'

  if ($items -isnot [object[]])     { throw 'ItemsJson must be a JSON array (e.g., [1,2,3]).' }
  if ($config -isnot [hashtable])   { throw 'ConfigJson must be a JSON object (e.g., {"timeout":30}).' }

  $step.actual.items_parsed  = $items
  $step.actual.config_parsed = $config
  $step.metrics.items_count  = $items.Count

  $baseDir = $env:PVTX_TESTCASE_PATH ?? $PSScriptRoot
  $resolvedPath = [IO.Path]::IsPathRooted($P_Path) ? $P_Path : (Join-Path $baseDir $P_Path)

  $step.metrics.mode          = $E_Mode
  $step.metrics.text_length   = $S_Text.Length
  $step.metrics.int_value     = $N_Int
  $step.metrics.flag          = $B_Flag
  $step.metrics.double_value  = $N_Double
  $step.metrics.path_resolved = $resolvedPath
  $step.metrics.path_exists   = Test-Path -LiteralPath $resolvedPath -ErrorAction SilentlyContinue

  if ($step.metrics.path_exists) {
    try {
      $fc = Get-Content -LiteralPath $resolvedPath -Raw -ErrorAction Stop
      $step.metrics.file_size  = $fc.Length
      $step.metrics.file_lines = ($fc -split '\r?\n').Count
      $step.actual.file_content_preview = ($fc.Length -gt 100) ? ($fc.Substring(0,100) + '...') : $fc
    } catch {
      $step.metrics.file_read_error = $_.Exception.Message
    }
  }

  switch ($E_Mode) {
    'pass'    { $step.status='PASS'; $step.message='All parameters validated successfully. Forced PASS by E_Mode.'; $exitCode=0; $overallStatus='PASS' }
    'fail'    { $step.status='FAIL'; $step.message='Forced FAIL by E_Mode=fail'; $exitCode=1; $overallStatus='FAIL' }
    'timeout' { $step.status='FAIL'; $step.message='Forcing timeout by sleeping longer than allowed (Runner should mark as Timeout)'; $exitCode=1; $overallStatus='FAIL'; Start-Sleep 5 }
    'error'   { throw 'Forced error from E_Mode=error' }
  }
}
catch {
  $overallStatus = 'FAIL'
  $step.status   = 'FAIL'
  $step.message  = "Script error: $($_.Exception.Message)"
  $step.error = @{
    kind           = 'SCRIPT'
    code           = 'SCRIPT_ERROR'
    message        = $_.Exception.Message
    exception_type = $_.Exception.GetType().FullName
    stack          = $_.ScriptStackTrace
  }
  $exitCode = 2
}
finally {
  $swStep.Stop()
  $swTotal.Stop()

  $step.timing.duration_ms = [int]$swStep.ElapsedMilliseconds
  $totalMs = [int]$swTotal.ElapsedMilliseconds

  $passCount = [int]($step.status -eq 'PASS')
  $failCount = [int]($step.status -eq 'FAIL')
  $skipCount = [int]($step.status -eq 'SKIP')

  $report = @{
    schema  = @{ version = '1.0' }
    test    = @{ id = $TestId; name = $TestId; params = $paramsMap }
    summary = @{
      status      = $overallStatus
      exit_code   = $exitCode
      counts      = @{ total = 1; pass = $passCount; fail = $failCount; skip = $skipCount }
      duration_ms = $totalMs
    }
    steps = @($step)
  }

  Write-JsonFile $ReportPath $report

  $dotCount = [Math]::Max(3, 30 - $step.name.Length)
  $stepLine = "[1/1] {0} {1} {2}" -f $step.name, ("." * $dotCount), $step.status

  $details = [System.Collections.Generic.List[string]]::new()
  if ($step.status -eq 'PASS') {
    $details.Add("mode=$E_Mode text='$S_Text' int=$N_Int flag=$B_Flag double=$N_Double")
    $details.Add("items_count=$($step.metrics.items_count) path_exists=$($step.metrics.path_exists)")
  } else {
    $step.message | ForEach-Object { if ($_){ $details.Add("reason: $_") } }
    $details.Add('expected: all_params_valid=true mode_allowed=true')
    $details.Add("actual:   mode=$E_Mode text_length=$($S_Text.Length) int=$N_Int")
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
