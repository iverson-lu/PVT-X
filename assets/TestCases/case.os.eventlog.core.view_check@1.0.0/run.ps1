param(
  [int]$WindowMinutes = 60,
  [string]$LogNames = '["System"]',
  [ValidateSet('Critical','Error','Warning','Information','None')]
  [string]$MinLevel = 'Warning',
  [string]$AllowlistCsv = 'rules/allowlist.csv',
  [string]$BlocklistCsv = 'rules/blocklist.csv',
  [int]$FailThreshold = 10,
  [int]$MaxEventsPerLog = 5000,
  [int]$TruncateMessageChars = 300,
  [bool]$CaptureEventsToFile = $false
)

$ErrorActionPreference = 'Stop'

# ----------------------------
# Helper functions (local)
# ----------------------------
function Resolve-UnderTestPath([string]$MaybeRelative) {
  $root = $env:PVTX_TESTCASE_PATH
  if ([string]::IsNullOrWhiteSpace($root)) { $root = (Get-Location).Path }
  if ([System.IO.Path]::IsPathRooted($MaybeRelative)) { return $MaybeRelative }
  return (Join-Path $root $MaybeRelative)
}

function Convert-LevelName([object]$EventRecord) {
  # Prefer LevelDisplayName when available; fall back to numeric mapping.
  $ldn = $EventRecord.LevelDisplayName
  if (-not [string]::IsNullOrWhiteSpace($ldn)) { return (Normalize-Text $ldn) }
  switch ([int]$EventRecord.Level) {
    1 { return 'Critical' }
    2 { return 'Error' }
    3 { return 'Warning' }
    4 { return 'Information' }
    5 { return 'Verbose' }
    default { return 'Unknown' }
  }
}

function Level-Rank([string]$LevelName) {
  switch ($LevelName) {
    'Critical' { return 1 }
    'Error' { return 2 }
    'Warning' { return 3 }
    'Information' { return 4 }
    'Verbose' { return 5 }
    default { return 99 }
  }
}

function Truncate-Message([string]$S, [int]$MaxChars) {
  if ($MaxChars -le 0) { return $S }
  if ([string]::IsNullOrWhiteSpace($S)) { return $S }
  if ($S.Length -le $MaxChars) { return $S }
  return ($S.Substring(0, $MaxChars) + '...')
}

function Read-RulesCsv([string]$Path) {
  if (-not (Test-Path -LiteralPath $Path)) { return @() }
  try {
    return (Import-Csv -LiteralPath $Path)
  }
  catch {
    throw "Failed to parse CSV: $Path. $($_.Exception.Message)"
  }
}

function Match-Text([string]$Haystack, [string]$Needle, [string]$Mode) {
  $hs = Normalize-Text $Haystack
  $nd = Normalize-Text $Needle
  if ([string]::IsNullOrWhiteSpace($nd)) { return $true }

  $m = (Normalize-Text $Mode).ToLowerInvariant()
  if ([string]::IsNullOrWhiteSpace($m)) { $m = 'contains' }

  switch ($m) {
    'exact' {
      return ($hs.Equals($nd, [System.StringComparison]::OrdinalIgnoreCase))
    }
    'regex' {
      return ($hs -match $nd)
    }
    default { # contains
      return ($hs.IndexOf($nd, [System.StringComparison]::OrdinalIgnoreCase) -ge 0)
    }
  }
}

function Match-Rule($Rule, $Evt) {
  # AND semantics: all non-empty fields must match.
  # Supported CSV columns (case-insensitive): rule_id, owner, comment, log, provider, event_id, level, message, match_mode
  $log = $Rule.log
  if (-not [string]::IsNullOrWhiteSpace($log)) {
    if (-not ($Evt.log_name.Equals((Normalize-Text $log), [System.StringComparison]::OrdinalIgnoreCase))) { return $false }
  }

  $prov = $Rule.provider
  if (-not [string]::IsNullOrWhiteSpace($prov)) {
    if (-not ($Evt.provider.Equals((Normalize-Text $prov), [System.StringComparison]::OrdinalIgnoreCase))) { return $false }
  }

  $eid = $Rule.event_id
  if (-not [string]::IsNullOrWhiteSpace($eid)) {
    try {
      $eidInt = [int]($eid)
      if ($Evt.event_id -ne $eidInt) { return $false }
    }
    catch { return $false }
  }

  $lvl = $Rule.level
  if (-not [string]::IsNullOrWhiteSpace($lvl)) {
    if (-not ($Evt.level.Equals((Normalize-Text $lvl), [System.StringComparison]::OrdinalIgnoreCase))) { return $false }
  }

  $msg = $Rule.message
  if (-not [string]::IsNullOrWhiteSpace($msg)) {
    if (-not (Match-Text -Haystack $Evt.message -Needle $msg -Mode $Rule.match_mode)) { return $false }
  }

  return $true
}

# ----------------------------
# Metadata
# ----------------------------
$TestId = $env:PVTX_TESTCASE_ID ?? 'unknown_test'
$Ts  = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')

$ArtifactsRoot = Join-Path (Get-Location) 'artifacts'
Ensure-Dir $ArtifactsRoot
$ReportPath = Join-Path $ArtifactsRoot 'report.json'
$SummaryCsvPath = Join-Path $ArtifactsRoot 'events_summary.csv'
$DetailCsvPath  = Join-Path $ArtifactsRoot 'events_detail.csv'

$overall  = 'FAIL'
$exitCode = 2
$details  = [System.Collections.Generic.List[string]]::new()

$steps = @()
$swTotal = [Diagnostics.Stopwatch]::StartNew()

try {
# ----------------------------
# Step 1: validate + resolve params
# ----------------------------
$step1 = New-Step 'validate_params' 1 'Validate parameters'
$sw1 = [Diagnostics.Stopwatch]::StartNew()
try {
  if ($WindowMinutes -le 0) { throw 'WindowMinutes must be > 0.' }
  if ($FailThreshold -lt 0) { throw 'FailThreshold must be >= 0.' }
  if ($MaxEventsPerLog -le 0) { throw 'MaxEventsPerLog must be > 0.' }
  if ($TruncateMessageChars -lt 0) { throw 'TruncateMessageChars must be >= 0.' }

  $logList = @()
  try { $logList = (ConvertFrom-Json -InputObject $LogNames) }
  catch { throw "LogNames must be valid JSON array. $($_.Exception.Message)" }

  if ($null -eq $logList -or $logList.Count -eq 0) { throw 'LogNames JSON array is empty.' }

  foreach ($ln in $logList) {
    $n = Normalize-Text ([string]$ln)
    if ($n -notin @('System','Application')) {
      throw "Unsupported log name: '$n'. Only System and Application are supported."
    }
  }

  $allowPath = Resolve-UnderTestPath $AllowlistCsv
  $blockPath = Resolve-UnderTestPath $BlocklistCsv

  $step1.metrics = @{
    window_minutes = $WindowMinutes
    log_names = ($logList -join ',')
    min_level = $MinLevel
    allowlist_csv = $allowPath
    blocklist_csv = $blockPath
    fail_threshold = $FailThreshold
    max_events_per_log = $MaxEventsPerLog
    truncate_message_chars = $TruncateMessageChars
    capture_events_to_file = $CaptureEventsToFile
  }

  $step1.status = 'PASS'
  $step1.message = 'Parameter validation passed.'
}
catch {
  Fail-Step $step1 "Parameter validation failed: $($_.Exception.Message)" $_
  throw
}
finally {
  $sw1.Stop()
  $step1.timing.duration_ms = [int]$sw1.ElapsedMilliseconds
  $steps += $step1
}

# ----------------------------
# Step 2: collect events
# ----------------------------
$step2 = New-Step 'collect_events' 2 'Collect events from logs'
$sw2 = [Diagnostics.Stopwatch]::StartNew()

$allEvents = @()
$rawCountsByLog = @{}
$startTime = $null
$endTime = $null

try {
  $endTime = Get-Date
  $startTime = $endTime.AddMinutes(-1 * $WindowMinutes)

  $logList = (ConvertFrom-Json -InputObject $LogNames)

  foreach ($logName in $logList) {
    $logName = Normalize-Text ([string]$logName)
    $fh = @{
      LogName = $logName
      StartTime = $startTime
      EndTime = $endTime
    }

    $raw = @()
    try {
      $raw = Get-WinEvent -FilterHashtable $fh -ErrorAction Stop
    }
    catch {
      # No events found is not an error - it means the log is clean
      if ($_.Exception.Message -like "*No events were found*") {
        $raw = @()
      }
      else {
        throw
      }
    }
    $rawCountsByLog[$logName] = $raw.Count

    # Convert all events (no level filter yet - rules need to check all events)
    # Apply MaxEventsPerLog cap only
    $count = 0
    foreach ($e in $raw) {
      if ($count -ge $MaxEventsPerLog) { break }
      $lvl = Convert-LevelName $e
      $msg = Truncate-Message -S ([string]$e.Message) -MaxChars $TruncateMessageChars
      $allEvents += [pscustomobject]@{
        time_created = $e.TimeCreated
        log_name     = $logName
        level        = $lvl
        provider     = (Normalize-Text ([string]$e.ProviderName))
        event_id     = [int]$e.Id
        message      = (Normalize-Text $msg)
        record_id    = $e.RecordId
      }
      $count++
    }
  }

  $step2.metrics = @{
    start_time = $startTime.ToString('o')
    end_time   = $endTime.ToString('o')
    total_events = $allEvents.Count
  }

  $step2.status = 'PASS'
  $step2.message = "Collected $($allEvents.Count) events."
}
catch {
  Fail-Step $step2 "Event collection failed: $($_.Exception.Message)" $_
  throw
}
finally {
  $sw2.Stop()
  $step2.timing.duration_ms = [int]$sw2.ElapsedMilliseconds
  $steps += $step2
}

# ----------------------------
# Step 3: apply rules + threshold
# ----------------------------
$step3 = New-Step 'evaluate_rules' 3 'Evaluate rules and threshold'
$sw3 = [Diagnostics.Stopwatch]::StartNew()

$blockRules = @()
$allowRules = @()
$blockHits = @()
$thresholdPool = @()
$allowHitCount = 0

try {
  $allowPath = Resolve-UnderTestPath $AllowlistCsv
  $blockPath = Resolve-UnderTestPath $BlocklistCsv

  $blockRules = Read-RulesCsv $blockPath
  $allowRules = Read-RulesCsv $allowPath

  $minRank = 99
  if ($MinLevel -ne 'None') { $minRank = Level-Rank $MinLevel }

  foreach ($evt in $allEvents) {
    $evt | Add-Member -NotePropertyName block_hit -NotePropertyValue $false -Force
    $evt | Add-Member -NotePropertyName block_rule_id -NotePropertyValue '' -Force
    $evt | Add-Member -NotePropertyName allow_hit -NotePropertyValue $false -Force
    $evt | Add-Member -NotePropertyName allow_rule_id -NotePropertyValue '' -Force

    $matchedBlock = $null
    foreach ($r in $blockRules) {
      if (Match-Rule $r $evt) { $matchedBlock = $r; break }
    }

    if ($null -ne $matchedBlock) {
      $evt.block_hit = $true
      $evt.block_rule_id = (Normalize-Text ([string]$matchedBlock.rule_id))
      $blockHits += [pscustomobject]@{
        rule_id = (Normalize-Text ([string]$matchedBlock.rule_id))
        owner   = (Normalize-Text ([string]$matchedBlock.owner))
        comment = (Normalize-Text ([string]$matchedBlock.comment))
        log_name = $evt.log_name
        provider = $evt.provider
        event_id = $evt.event_id
        level    = $evt.level
        time_created = $evt.time_created
        message  = $evt.message
      }
      continue
    }

    $matchedAllow = $null
    foreach ($r in $allowRules) {
      if (Match-Rule $r $evt) { $matchedAllow = $r; break }
    }

    if ($null -ne $matchedAllow) {
      $evt.allow_hit = $true
      $evt.allow_rule_id = (Normalize-Text ([string]$matchedAllow.rule_id))
      $allowHitCount++
      continue
    }

    # Apply MinLevel filter for threshold pool
    # Events below MinLevel are not counted in threshold pool
    if ($MinLevel -eq 'None' -or (Level-Rank $evt.level) -le $minRank) {
      $thresholdPool += $evt
    }
  }

  $poolCount = $thresholdPool.Count
  $blockCount = $blockHits.Count

  $step3.metrics = @{
    block_rules = $blockRules.Count
    allow_rules = $allowRules.Count
    block_hits  = $blockCount
    allow_hits  = $allowHitCount
    threshold_pool_count = $poolCount
    fail_threshold = $FailThreshold
    min_level = $MinLevel
  }

  if ($blockCount -gt 0) {
    $overall = 'FAIL'
    $exitCode = 1
    $details.Add("Blocklist hit: $blockCount event(s).") | Out-Null
    $step3.status = 'FAIL'
    $step3.message = 'Blocklist hit detected (overall FAIL).'
  }
  elseif ($poolCount -ge $FailThreshold) {
    $overall = 'FAIL'
    $exitCode = 1
    $details.Add("FailThreshold exceeded: pool=$poolCount threshold=$FailThreshold.") | Out-Null
    $step3.status = 'FAIL'
    $step3.message = 'Threshold exceeded (overall FAIL).'
  }
  else {
    $overall = 'PASS'
    $exitCode = 0
    $details.Add("All checks passed: events=$($allEvents.Count) pool=$poolCount threshold=$FailThreshold block=$blockCount allow=$allowHitCount") | Out-Null
    $step3.status = 'PASS'
    $step3.message = 'No blocklist hits and threshold not exceeded.'
  }
}
catch {
  Fail-Step $step3 "Rule evaluation failed: $($_.Exception.Message)" $_
  throw
}
finally {
  $sw3.Stop()
  $step3.timing.duration_ms = [int]$sw3.ElapsedMilliseconds
  $steps += $step3
}

# ----------------------------
# Step 4: write artifacts
# ----------------------------
$step4 = New-Step 'write_artifacts' 4 'Write artifacts'
$sw4 = [Diagnostics.Stopwatch]::StartNew()

try {
  # events_summary.csv (always)
  $rows = @()

  foreach ($k in @('System','Application')) {
    if ($rawCountsByLog.ContainsKey($k)) {
      $rows += [pscustomobject]@{
        scope = $k
        raw_events = ($rawCountsByLog[$k] ?? 0)
        collected_events = ($allEvents | Where-Object { $_.log_name -eq $k }).Count
        blocklist_hits = ($allEvents | Where-Object { $_.log_name -eq $k -and $_.block_hit }).Count
        allowlist_hits = ($allEvents | Where-Object { $_.log_name -eq $k -and $_.allow_hit }).Count
        threshold_pool = ($thresholdPool | Where-Object { $_.log_name -eq $k }).Count
      }
    }
  }

  $rows += [pscustomobject]@{
    scope = 'TOTAL'
    raw_events = ($rawCountsByLog.Values | Measure-Object -Sum).Sum
    collected_events = $allEvents.Count
    blocklist_hits = ($allEvents | Where-Object { $_.block_hit }).Count
    allowlist_hits = ($allEvents | Where-Object { $_.allow_hit }).Count
    threshold_pool = $thresholdPool.Count
  }

  $rows | Export-Csv -LiteralPath $SummaryCsvPath -NoTypeInformation -Encoding utf8NoBOM

  # events_detail.csv (optional)
  if ($CaptureEventsToFile) {
    $allEvents |
      Select-Object time_created, log_name, level, provider, event_id, record_id, block_hit, block_rule_id, allow_hit, allow_rule_id, message |
      Export-Csv -LiteralPath $DetailCsvPath -NoTypeInformation -Encoding utf8NoBOM
  }

  $step4.metrics = @{
    summary_csv = $SummaryCsvPath
    detail_csv  = ($(if ($CaptureEventsToFile) { $DetailCsvPath } else { '' }))
    block_hits = $blockHits.Count
  }

  $step4.status = 'PASS'
  $step4.message = 'Artifacts written.'
}
catch {
  Fail-Step $step4 "Artifact writing failed: $($_.Exception.Message)" $_
  throw
}
finally {
  $sw4.Stop()
  $step4.timing.duration_ms = [int]$sw4.ElapsedMilliseconds
  $steps += $step4
}

}  # End of outer try block
catch {
  $overall='FAIL'
  $exitCode=2
  $details.Clear()
  $details.Add("reason: $($_.Exception.Message)") | Out-Null
  $details.Add("exception: $($_.Exception.GetType().FullName)") | Out-Null
}
finally {
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
        window_minutes = $WindowMinutes
        log_names = $LogNames
        min_level = $MinLevel
        allowlist_csv = $AllowlistCsv
        blocklist_csv = $BlocklistCsv
        fail_threshold = $FailThreshold
        max_events_per_log = $MaxEventsPerLog
        truncate_message_chars = $TruncateMessageChars
        capture_events_to_file = $CaptureEventsToFile
      }
      blocklist_hits = $blockHits
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
      metrics = @{
        total_events = $allEvents.Count
        blocklist_hits = $blockHits.Count
        allowlist_hits = $allowHitCount
        threshold_pool_count = $thresholdPool.Count
        fail_threshold = $FailThreshold
        window_start = ($(if ($startTime) { $startTime.ToString('o') } else { '' }))
        window_end   = ($(if ($endTime) { $endTime.ToString('o') } else { '' }))
      }
      duration_ms = [int]$swTotal.ElapsedMilliseconds
      ts_utc      = $Ts
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
    -TsUtc $Ts `
    -StepLine ($stepLines -join "`n") `
    -StepDetails (@() + $details.ToArray()) `
    -Total $steps.Count `
    -Passed $passCount `
    -Failed $failCount `
    -Skipped $skipCount

  exit $exitCode
}
