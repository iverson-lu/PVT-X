param(
  [Parameter(Mandatory)]
  [ValidateSet('inline','ps1','bat')]
  [string]$Mode,
  [string]$Command = '',
  [string]$ScriptPath = '',
  [string]$Args = ''
)

$ErrorActionPreference = 'Stop'

# ----------------------------
# Metadata
# ----------------------------
$TestId = $env:PVTX_TESTCASE_ID ?? 'case.os.command.core.execute'
$Ts  = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
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
# Step 1: Validate parameters
# ----------------------------
$step1 = New-Step 'validate_parameters' 1 'Validate execution parameters'
$sw1 = [Diagnostics.Stopwatch]::StartNew()
try {
  $failureReason = $null
  
  switch ($Mode) {
    'inline' {
      if ([string]::IsNullOrWhiteSpace($Command)) {
        $failureReason = 'invalid_param: Command is required when Mode=inline'
      }
    }
    'ps1' {
      if ([string]::IsNullOrWhiteSpace($ScriptPath)) {
        $failureReason = 'invalid_param: ScriptPath is required when Mode=ps1'
      }
    }
    'bat' {
      if ([string]::IsNullOrWhiteSpace($ScriptPath)) {
        $failureReason = 'invalid_param: ScriptPath is required when Mode=bat'
      }
    }
  }

  if ($failureReason) {
    throw $failureReason
  }

  $step1.metrics = @{
    mode = $Mode
  }
  
  if ($Mode -eq 'inline') {
    $step1.metrics.command_length = $Command.Length
  } else {
    $step1.metrics.script_path = $ScriptPath
    $step1.metrics.args_provided = -not [string]::IsNullOrWhiteSpace($Args)
  }

  $step1.status = 'PASS'
  $step1.message = 'Parameter validation successful.'
}
catch {
  Fail-Step $step1 "Parameter validation failed: $($_.Exception.Message)" $_
  $details.Add("failure_reason: $($_.Exception.Message)")
  throw
}
finally {
  $sw1.Stop()
  $step1.timing.duration_ms = [int]$sw1.ElapsedMilliseconds
  $steps += $step1
}

# ----------------------------
# Step 2: Execute command/script
# ----------------------------
$step2 = New-Step 'execute_command' 2 'Execute command or script'
$sw2 = [Diagnostics.Stopwatch]::StartNew()

$stdout = ''
$stderr = ''
$actualExitCode = 0

try {
  switch ($Mode) {
    'inline' {
      # Execute inline PowerShell command
      $scriptBlock = [scriptblock]::Create($Command)
      $stdout = & $scriptBlock 2>&1 | Out-String
      $actualExitCode = $LASTEXITCODE
      if ($null -eq $actualExitCode) { $actualExitCode = 0 }
    }
    
    'ps1' {
      # Resolve script path
      $baseDir = $env:PVTX_TESTCASE_PATH ?? $PSScriptRoot
      $resolvedPath = if ([IO.Path]::IsPathRooted($ScriptPath)) { 
        $ScriptPath 
      } else { 
        Join-Path $baseDir $ScriptPath 
      }
      
      if (-not (Test-Path -LiteralPath $resolvedPath -ErrorAction SilentlyContinue)) {
        throw "script_not_found: PowerShell script not found at '$resolvedPath'"
      }
      
      # Execute PowerShell script
      if ([string]::IsNullOrWhiteSpace($Args)) {
        $stdout = & $resolvedPath 2>&1 | Out-String
      } else {
        # Split args by space (simple approach)
        $argArray = $Args -split '\s+'
        $stdout = & $resolvedPath @argArray 2>&1 | Out-String
      }
      $actualExitCode = $LASTEXITCODE
      if ($null -eq $actualExitCode) { $actualExitCode = 0 }
    }
    
    'bat' {
      # Resolve script path
      $baseDir = $env:PVTX_TESTCASE_PATH ?? $PSScriptRoot
      $resolvedPath = if ([IO.Path]::IsPathRooted($ScriptPath)) { 
        $ScriptPath 
      } else { 
        Join-Path $baseDir $ScriptPath 
      }
      
      if (-not (Test-Path -LiteralPath $resolvedPath -ErrorAction SilentlyContinue)) {
        throw "script_not_found: Batch script not found at '$resolvedPath'"
      }
      
      # Execute batch script via cmd.exe
      $cmdArgs = "/c `"$resolvedPath`" $Args"
      $stdout = & cmd.exe $cmdArgs 2>&1 | Out-String
      $actualExitCode = $LASTEXITCODE
      if ($null -eq $actualExitCode) { $actualExitCode = 0 }
    }
  }
  
  # Truncate output if too large (max 4KB)
  $maxLength = 4096
  if ($stdout.Length -gt $maxLength) {
    $stdout = $stdout.Substring(0, $maxLength) + "`n... (truncated)"
  }
  
  $step2.metrics = @{
    exit_code = $actualExitCode
    stdout_length = $stdout.Length
  }
  
  # Overall result based on exit code
  if ($actualExitCode -eq 0) {
    $overall = 'PASS'
    $exitCode = 0
    $step2.status = 'PASS'
    $step2.message = "Command executed successfully (exit code: $actualExitCode)."
  } else {
    $overall = 'FAIL'
    $exitCode = $actualExitCode
    $step2.status = 'FAIL'
    $step2.message = "Command failed with exit code: $actualExitCode"
    $details.Add("failure_reason: non_zero_exit_code")
  }
}
catch {
  Fail-Step $step2 "Execution failed: $($_.Exception.Message)" $_
  $overall = 'FAIL'
  $exitCode = 2
  $details.Add("failure_reason: runner_failed")
  $details.Add("exception: $($_.Exception.Message)")
}
finally {
  $sw2.Stop()
  $step2.timing.duration_ms = [int]$sw2.ElapsedMilliseconds
  $steps += $step2
}

# ----------------------------
# Generate report
# ----------------------------
$swTotal.Stop()

$passCount = @($steps | Where-Object { $_.status -eq 'PASS' }).Count
$failCount = @($steps | Where-Object { $_.status -eq 'FAIL' }).Count
$skipCount = @($steps | Where-Object { $_.status -eq 'SKIP' }).Count

$report = @{
  schema  = @{ version = '1.0' }
  test    = @{
    id = $TestId
    name = $TestId
    params = @{
      mode = $Mode
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
  steps = $steps
  execution = @{
    mode = $Mode
    actual_exit_code = $actualExitCode
    stdout = $stdout
  }
}

# Add mode-specific fields
if ($Mode -eq 'inline') {
  $report.execution.command = $Command
} else {
  $report.execution.script_path = $ScriptPath
  $report.execution.args = $Args
}

$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $ReportPath -Encoding utf8NoBOM

# ----------------------------
# Console output
# ----------------------------
$stepLines = @()
for ($i=0; $i -lt $steps.Count; $i++) {
  $nm = $steps[$i].name
  $dots = [Math]::Max(3, 30 - $nm.Length)
  $stepLines += ("[{0}/{1}] {2} {3} {4}" -f ($i+1), $steps.Count, $nm, ("." * $dots), $steps[$i].status)
}

if ($Mode -eq 'inline') {
  $details.Add("mode: inline, command_length: $($Command.Length)")
} else {
  $details.Add("mode: $Mode, script_path: $ScriptPath, args: $Args")
}
$details.Add("actual_exit_code: $actualExitCode")

# Truncate stdout for console display (max 500 chars)
$stdoutDisplay = if ($stdout.Length -gt 500) {
  $stdout.Substring(0, 500) + "... (truncated)"
} else {
  $stdout
}
if (-not [string]::IsNullOrWhiteSpace($stdoutDisplay)) {
  $details.Add("stdout: $stdoutDisplay")
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
