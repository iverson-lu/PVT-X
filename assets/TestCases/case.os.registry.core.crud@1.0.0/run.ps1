param(
  [Parameter(Mandatory)]
  [ValidateSet('set_reg','verify_reg')]
  [string]$Mode,
  [string]$RegFilePath = '',
  [string]$ArtifactExportScope = '{"enabled": false}',
  [string]$VerifySpec = '[]'
)

$ErrorActionPreference = 'Stop'

# ----------------------------
# Metadata
# ----------------------------
$TestId = $env:PVTX_TESTCASE_ID ?? 'case.os.registry.core.crud'
$Ts = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$Mode = (Normalize-Text $Mode).ToLowerInvariant()

$ArtifactsRoot = Join-Path (Get-Location) 'artifacts'
Ensure-Dir $ArtifactsRoot
$ReportPath = Join-Path $ArtifactsRoot 'report.json'

$overall = 'FAIL'
$exitCode = 1
$steps = @()
$swTotal = [Diagnostics.Stopwatch]::StartNew()

# ----------------------------
# Helper Functions
# ----------------------------
function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]$identity
    $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-RegistryValue {
    param(
        [string]$Path,
        [string]$Name
    )
    
    try {
        $item = Get-ItemProperty -LiteralPath "Registry::$Path" -Name $Name -ErrorAction Stop
        $value = $item.$Name
        
        # Determine registry type
        $key = Get-Item -LiteralPath "Registry::$Path" -ErrorAction Stop
        $valueKind = $key.GetValueKind($Name)
        
        return @{
            success = $true
            type = $valueKind.ToString()
            data = $value
        }
    }
    catch [System.Management.Automation.ItemNotFoundException] {
        return @{ success = $false; reason = 'not_found'; message = 'Registry key or value not found' }
    }
    catch [System.Security.SecurityException] {
        return @{ success = $false; reason = 'access_denied'; message = 'Access denied' }
    }
    catch {
        return @{ success = $false; reason = 'read_error'; message = $_.Exception.Message }
    }
}

function Test-RegistryMatch {
    param(
        $ActualData,
        $ExpectedData,
        [string]$MatchMode = 'exact'
    )
    
    switch ($MatchMode) {
        'exact' {
            return ($ActualData -eq $ExpectedData)
        }
        'case_insensitive' {
            return ($ActualData.ToString().ToLowerInvariant() -eq $ExpectedData.ToString().ToLowerInvariant())
        }
        'contains' {
            return ($ActualData.ToString() -match [regex]::Escape($ExpectedData.ToString()))
        }
        'regex' {
            return ($ActualData.ToString() -match $ExpectedData.ToString())
        }
        default {
            return ($ActualData -eq $ExpectedData)
        }
    }
}

function Export-RegistrySnapshots {
    param(
        [string]$ExportConfigJson,
        [string]$ArtifactsRoot
    )
    
    $exportConfig = ParseJson $ExportConfigJson 'ArtifactExportScope'
    if ($exportConfig.enabled -ne $true) {
        return $null
    }
    
    # Ensure keys is an array
    $keys = $exportConfig.keys
    if ($keys -isnot [array]) {
        $keys = @($keys)
    }
    
    $exportDir = Join-Path $ArtifactsRoot 'registry_export'
    Ensure-Dir $exportDir
    
    $exportedKeys = @()
    foreach ($keyPath in $keys) {
        $fileName = ($keyPath -replace '\\', '_') + '.' + ($exportConfig.format ?? 'reg')
        $exportPath = Join-Path $exportDir $fileName
        
        try {
            if (($exportConfig.format ?? 'reg') -eq 'reg') {
                & reg.exe export $keyPath $exportPath /y 2>&1 | Out-Null
                $exportedKeys += @{ key = $keyPath; file = $exportPath; success = $true }
            }
            else {
                # JSON format - export via PowerShell
                $keyData = Get-ItemProperty -LiteralPath "Registry::$keyPath" -ErrorAction Stop
                Write-JsonFile -Path $exportPath -Obj $keyData
                $exportedKeys += @{ key = $keyPath; file = $exportPath; success = $true }
            }
        }
        catch {
            $exportedKeys += @{ key = $keyPath; error = $_.Exception.Message; success = $false }
        }
    }
    
    return $exportedKeys
}

# ----------------------------
# Mode: set_reg
# ----------------------------
if ($Mode -eq 'set_reg') {
    $step = New-Step 'set_registry_file' 1 'Set registry via .reg file'
    $sw = [Diagnostics.Stopwatch]::StartNew()
    
    try {
        # Validate parameters
        if ([string]::IsNullOrWhiteSpace($RegFilePath)) {
            throw 'RegFilePath is required when Mode=execute_reg'
        }
        
        # Resolve path
        $baseDir = $env:PVTX_TESTCASE_PATH ?? $PSScriptRoot
        $resolvedPath = [IO.Path]::IsPathRooted($RegFilePath) ? $RegFilePath : (Join-Path $baseDir $RegFilePath)
        
        # Validate file exists
        if (-not (Test-Path -LiteralPath $resolvedPath)) {
            throw "Registry file not found: $resolvedPath"
        }
        
        # Validate .reg extension
        if ([IO.Path]::GetExtension($resolvedPath) -ne '.reg') {
            throw "File must have .reg extension: $resolvedPath"
        }
        
        # Copy .reg file to artifacts
        $regCopyPath = Join-Path $ArtifactsRoot ([IO.Path]::GetFileName($resolvedPath))
        Copy-Item -LiteralPath $resolvedPath -Destination $regCopyPath -Force
        
        # Execute registry import
        $result = & reg.exe import $resolvedPath 2>&1
        $regExitCode = $LASTEXITCODE
        
        if ($regExitCode -ne 0) {
            throw "Registry import failed with exit code $regExitCode : $($result -join ' ')"
        }
        
        $step.metrics = @{
            reg_file_path = $resolvedPath
            is_admin = (Test-IsAdmin)
            execution_result = 'success'
            execution_message = $result -join '; '
        }
        
        # Export registry snapshots if requested
        $exportedKeys = Export-RegistrySnapshots -ExportConfigJson $ArtifactExportScope -ArtifactsRoot $ArtifactsRoot
        if ($exportedKeys) {
            $step.metrics.exported_keys = $exportedKeys
        }
        
        $step.status = 'PASS'
        $step.message = 'Registry file executed successfully'
        $overall = 'PASS'
        $exitCode = 0
    }
    catch {
        Fail-Step $step "Registry execution failed: $($_.Exception.Message)" $_
        $overall = 'FAIL'
        $exitCode = 1
    }
    finally {
        $sw.Stop()
        $step.timing.duration_ms = [int]$sw.ElapsedMilliseconds
        $steps += $step
    }
}

# ----------------------------
# Mode: verify_reg
# ----------------------------
elseif ($Mode -eq 'verify_reg') {
    $step = New-Step 'verify_registry_values' 1 'Verify registry values'
    $sw = [Diagnostics.Stopwatch]::StartNew()
    
    try {
        # Parse verification spec
        $verifyList = ParseJson $VerifySpec 'VerifySpec'
        
        # Ensure $verifyList is an array (ConvertFrom-Json -AsHashtable returns hashtable for single-item arrays)
        if ($verifyList -isnot [array]) {
            $verifyList = @($verifyList)
        }
        
        if ($verifyList.Count -eq 0) {
            throw 'VerifySpec cannot be empty when Mode=verify_value'
        }
        
        $results = @()
        $passCount = 0
        $failCount = 0
        
        foreach ($spec in $verifyList) {
            $result = @{
                path = $spec.path
                name = $spec.name
                passed = $false
                reason = $null
            }
            
            # Read registry value
            $readResult = Get-RegistryValue -Path $spec.path -Name $spec.name
            
            if (-not $readResult.success) {
                $result.reason = $readResult.reason
                $result.message = $readResult.message
                $failCount++
                $results += $result
                continue
            }
            
            # Always include actual value in result
            $result.actual = @{
                type = $readResult.type
                data = $readResult.data
            }
            
            # If expected is specified, validate
            if ($spec.expected) {
                # Check type if specified
                if ($spec.expected.type -and $readResult.type -ne $spec.expected.type) {
                    $result.reason = 'type_mismatch'
                    $result.message = "Expected type $($spec.expected.type), got $($readResult.type)"
                    $failCount++
                    $results += $result
                    continue
                }
                
                # Check data if specified
                if ($null -ne $spec.expected.data) {
                    $matchMode = $spec.expected.match_mode ?? 'exact'
                    $matched = Test-RegistryMatch -ActualData $readResult.data -ExpectedData $spec.expected.data -MatchMode $matchMode
                    
                    if (-not $matched) {
                        $result.reason = 'value_mismatch'
                        $result.message = "Value mismatch (match_mode: $matchMode)"
                        $failCount++
                        $results += $result
                        continue
                    }
                }
            }
            
            # All checks passed
            $result.passed = $true
            $passCount++
            $results += $result
        }
        
        $step.metrics = @{
            total_count = $verifyList.Count
            pass_count = $passCount
            fail_count = $failCount
            results = $results
        }
        
        # Export registry snapshots if requested
        $exportedKeys = Export-RegistrySnapshots -ExportConfigJson $ArtifactExportScope -ArtifactsRoot $ArtifactsRoot
        if ($exportedKeys) {
            $step.metrics.exported_keys = $exportedKeys
        }
        
        if ($failCount -eq 0) {
            $step.status = 'PASS'
            $step.message = "$passCount/$($verifyList.Count) registry values verified successfully"
            $overall = 'PASS'
            $exitCode = 0
        }
        else {
            $step.status = 'FAIL'
            $step.message = "$failCount/$($verifyList.Count) registry values failed verification"
            $overall = 'FAIL'
            $exitCode = 1
        }
    }
    catch {
        Fail-Step $step "Registry verification failed: $($_.Exception.Message)" $_
        $overall = 'FAIL'
        $exitCode = 1
    }
    finally {
        $sw.Stop()
        $step.timing.duration_ms = [int]$sw.ElapsedMilliseconds
        $steps += $step
    }
}

# ----------------------------
# Generate Report & Output
# ----------------------------
$swTotal.Stop()

$passSteps = @($steps | Where-Object { $_.status -eq 'PASS' }).Count
$failSteps = @($steps | Where-Object { $_.status -eq 'FAIL' }).Count
$skipSteps = @($steps | Where-Object { $_.status -eq 'SKIP' }).Count

$report = @{
    schema = @{ version = '1.0' }
    test = @{
        id = $TestId
        name = $TestId
        params = @{
            mode = $Mode
            reg_file_path = $RegFilePath
            verify_spec_count = if ($Mode -eq 'verify_reg' -and $steps[0].metrics.total_count) { $steps[0].metrics.total_count } else { 0 }
        }
    }
    execution = @{
        overall = $overall
        exit_code = $exitCode
        timestamp_utc = $Ts
        duration_ms = [int]$swTotal.ElapsedMilliseconds
    }
    steps = $steps
    summary = @{
        total = $steps.Count
        passed = $passSteps
        failed = $failSteps
        skipped = $skipSteps
    }
}

Write-JsonFile -Path $ReportPath -Obj $report

# Console output
$stepLine = ($steps | ForEach-Object {
    "[{0}/{1}] {2} {3} {4}" -f $_.index, $steps.Count, $_.name, ('.' * (35 - $_.name.Length)), $_.status
}) -join "`n"

$detailLines = @()
if ($Mode -eq 'set_reg') {
    $detailLines += "mode: $Mode"
    $detailLines += "executed: $RegFilePath"
    if ($steps[0].metrics.execution_result) {
        $detailLines += "result: $($steps[0].metrics.execution_result)"
    }
    if ($steps[0].metrics.exported_keys) {
        $exportSuccess = @($steps[0].metrics.exported_keys | Where-Object { $_.success -eq $true }).Count
        $exportTotal = $steps[0].metrics.exported_keys.Count
        $detailLines += "exported: $exportSuccess/$exportTotal keys to artifacts/registry_export/"
        foreach ($exp in $steps[0].metrics.exported_keys) {
            if ($exp.success) {
                $detailLines += "  ✓ $($exp.key)"
            } else {
                $detailLines += "  ✗ $($exp.key) - $($exp.error)"
            }
        }
    }
}
elseif ($Mode -eq 'verify_reg') {
    $detailLines += "mode: $Mode"
    $detailLines += "verified: $($steps[0].metrics.pass_count)/$($steps[0].metrics.total_count) registry values"
    
    # Show each verification result
    foreach ($result in $steps[0].metrics.results) {
        $status = if ($result.passed) { "✓" } else { "✗" }
        $detailLines += "  $status $($result.path)\$($result.name)"
        if ($result.actual) {
            $dataStr = if ($result.actual.data -is [array]) { "[$($result.actual.data -join ', ')]" } else { $result.actual.data }
            $detailLines += "    type: $($result.actual.type), data: $dataStr"
        }
        if (-not $result.passed -and $result.reason) {
            $detailLines += "    reason: $($result.reason)"
        }
    }
    
    if ($steps[0].metrics.exported_keys) {
        $exportSuccess = @($steps[0].metrics.exported_keys | Where-Object { $_.success -eq $true }).Count
        $exportTotal = $steps[0].metrics.exported_keys.Count
        $detailLines += "exported: $exportSuccess/$exportTotal keys to artifacts/registry_export/"
        foreach ($exp in $steps[0].metrics.exported_keys) {
            if ($exp.success) {
                $detailLines += "  ✓ $($exp.key)"
            } else {
                $detailLines += "  ✗ $($exp.key) - $($exp.error)"
            }
        }
    }
}

Write-Stdout-Compact `
    -TestName $TestId `
    -Overall $overall `
    -ExitCode $exitCode `
    -TsUtc $Ts `
    -StepLine $stepLine `
    -StepDetails $detailLines `
    -Total $steps.Count `
    -Passed $passSteps `
    -Failed $failSteps `
    -Skipped $skipSteps

exit $exitCode
