param(
    [int]$delaySec = 10,
    [string]$reason = "Reboot requested by test case",
    [bool]$verifyReboot = $false
)

$ErrorActionPreference = 'Stop'

# ----------------------------
# Metadata
# ----------------------------
$TestId = $env:PVTX_TESTCASE_ID ?? 'case.system.reboot_resume.mini'
$Ts = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')

$ArtifactsRoot = Join-Path (Get-Location) 'artifacts'
Ensure-Dir $ArtifactsRoot
$ReportPath = Join-Path $ArtifactsRoot 'report.json'

$overallStatus = 'FAIL'
$exitCode = 2
$details = [System.Collections.Generic.List[string]]::new()
$steps = @()
$swTotal = [System.Diagnostics.Stopwatch]::StartNew()

# Get current phase
$phase = 0
if ($env:PVTX_PHASE) {
    $phase = [int]$env:PVTX_PHASE
}

$details.Add("phase=$phase delaySec=$delaySec verifyReboot=$verifyReboot")
$details.Add("reason: $reason")

try {
    if ($phase -eq 0) {
        # ----------------------------
        # Step 1: Request reboot
        # ----------------------------
        $step1 = New-Step 'request_reboot' 1 'Request system reboot'
        $sw1 = [System.Diagnostics.Stopwatch]::StartNew()
        
        try {
            $controlDir = $env:PVTX_CONTROL_DIR
            if ([string]::IsNullOrWhiteSpace($controlDir)) {
                throw 'PVTX_CONTROL_DIR is required for reboot control.'
            }

            # Prepare reboot request payload
            $payload = @{
                type = 'control.reboot_required'
                nextPhase = 1
                reason = $reason
                reboot = @{ delaySec = $delaySec }
            } | ConvertTo-Json -Depth 5

            # Write reboot request atomically
            $tmpPath = Join-Path $controlDir 'reboot.tmp'
            $finalPath = Join-Path $controlDir 'reboot.json'

            $payload | Set-Content -Path $tmpPath -Encoding UTF8
            Move-Item -Path $tmpPath -Destination $finalPath -Force

            $step1.metrics = @{
                delay_sec = $delaySec
                reason = $reason
                control_dir = $controlDir
            }
            $step1.status = 'PASS'
            $step1.message = "Reboot request written successfully (delay: ${delaySec}s)."
        }
        catch {
            $step1.status = 'FAIL'
            $step1.message = "Failed to request reboot: $($_.Exception.Message)"
            $step1.error = @{
                message = $_.Exception.Message
                type = $_.Exception.GetType().FullName
            }
            throw
        }
        finally {
            $sw1.Stop()
            $step1.timing.duration_ms = [int]$sw1.ElapsedMilliseconds
            $steps += $step1
        }

        $overallStatus = 'PASS'
        $exitCode = 0
    }
    else {
        # ----------------------------
        # Step 2: Verify resume
        # ----------------------------
        $step2 = New-Step 'verify_resume' 1 'Verify system resumed after reboot'
        $sw2 = [System.Diagnostics.Stopwatch]::StartNew()
        
        try {
            $step2.metrics = @{
                phase = $phase
                run_id = $env:PVTX_RUN_ID
                verify_reboot_enabled = $verifyReboot
            }

            # TODO: Implement verifyReboot logic when parameter is true
            if ($verifyReboot) {
                $step2.message = 'Resume successful. verifyReboot enabled but not yet implemented.'
            }
            else {
                $step2.message = 'Resume successful.'
            }
            
            $step2.status = 'PASS'
        }
        catch {
            $step2.status = 'FAIL'
            $step2.message = "Resume verification failed: $($_.Exception.Message)"
            $step2.error = @{
                message = $_.Exception.Message
                type = $_.Exception.GetType().FullName
            }
            throw
        }
        finally {
            $sw2.Stop()
            $step2.timing.duration_ms = [int]$sw2.ElapsedMilliseconds
            $steps += $step2
        }

        $overallStatus = 'PASS'
        $exitCode = 0
    }
}
catch {
    $overallStatus = 'FAIL'
    $exitCode = 2
    $details.Clear()
    $details.Add("error: $($_.Exception.Message)")
    $details.Add("exception: $($_.Exception.GetType().FullName)")
}
finally {
    $swTotal.Stop()

    $passCount = ($steps | Where-Object status -EQ 'PASS').Count
    $failCount = ($steps | Where-Object status -EQ 'FAIL').Count
    $skipCount = ($steps | Where-Object status -EQ 'SKIP').Count

    $report = @{
        schema = @{ version = '1.0' }
        test = @{
            id = $TestId
            name = $TestId
            params = @{
                delaySec = $delaySec
                reason = $reason
                verifyReboot = $verifyReboot
                phase = $phase
            }
        }
        summary = @{
            status = $overallStatus
            exit_code = $exitCode
            counts = @{
                total = $steps.Count
                pass = $passCount
                fail = $failCount
                skip = $skipCount
            }
            duration_ms = [int]$swTotal.ElapsedMilliseconds
            ts_utc = $Ts
        }
        steps = $steps
    }

    $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $ReportPath -Encoding utf8NoBOM

    $stepLines = @()
    for ($i = 0; $i -lt $steps.Count; $i++) {
        $nm = $steps[$i].name
        $dots = [Math]::Max(3, 30 - $nm.Length)
        $stepLines += ("[{0}/{1}] {2} {3} {4}" -f ($i + 1), $steps.Count, $nm, ("." * $dots), $steps[$i].status)
    }

    Write-Stdout-Compact `
        -TestName $TestId `
        -Overall $overallStatus `
        -ExitCode $exitCode `
        -TsUtc $Ts `
        -StepLine ($stepLines -join "`n") `
        -StepDetails $details.ToArray() `
        -Total $steps.Count `
        -Passed $passCount `
        -Failed $failCount `
        -Skipped $skipCount

    exit $exitCode
}
