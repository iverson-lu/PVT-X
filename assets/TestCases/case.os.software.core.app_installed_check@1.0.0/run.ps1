param(
    [Parameter(Mandatory=$true)]  [string] $appName,
    [Parameter(Mandatory=$false)] [bool]   $exactMatch = $false,
    [Parameter(Mandatory=$false)] [bool]   $checkRegistry = $true,
    [Parameter(Mandatory=$false)] [bool]   $checkPackages = $true
)

$ErrorActionPreference = "Stop"

# ----------------------------
# Metadata
# ----------------------------
$TestId = $env:PVTX_TESTCASE_ID
if ([string]::IsNullOrWhiteSpace($TestId)) { $TestId = "case.os.software.core.app_installed_check" }
$TestName = $env:PVTX_TESTCASE_NAME
if ([string]::IsNullOrWhiteSpace($TestName)) { $TestName = "Application Installed Check" }
$TsUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# Normalize input
$appName = Normalize-Text $appName
if ([string]::IsNullOrWhiteSpace($appName)) {
    throw "appName parameter is required and cannot be empty."
}

# Artifacts
$ArtifactsRoot = Join-Path (Get-Location) "artifacts"
Ensure-Dir $ArtifactsRoot
$ReportPath = Join-Path $ArtifactsRoot "report.json"

# Create step
$step = New-Step -id "check_app" -index 1 -name "Check if application is installed"

# Timers
$swTotal = [System.Diagnostics.Stopwatch]::StartNew()
$swStep = [System.Diagnostics.Stopwatch]::StartNew()

# Overall result
$overallStatus = "FAIL"
$exitCode = 1
$foundInRegistry = $false
$foundInPackages = $false
$registryMatches = @()
$packageMatches = @()

try {
    # Search in Windows Registry
    if ($checkRegistry) {
        $registryPaths = @(
            'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
            'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
        )

        foreach ($path in $registryPaths) {
            try {
                $apps = Get-ItemProperty -Path $path -ErrorAction SilentlyContinue |
                    Where-Object { $_.DisplayName } |
                    Select-Object DisplayName, DisplayVersion, Publisher, InstallDate, PSPath
                
                if ($exactMatch) {
                    $matches = @($apps | Where-Object { 
                        $_.DisplayName -and ($_.DisplayName -eq $appName) 
                    })
                } else {
                    $matches = @($apps | Where-Object { 
                        $_.DisplayName -and ($_.DisplayName -like "*$appName*") 
                    })
                }

                if ($matches.Count -gt 0) {
                    $foundInRegistry = $true
                    $registryMatches += $matches | ForEach-Object {
                        [PSCustomObject]@{
                            name = $_.DisplayName
                            version = $_.DisplayVersion
                            publisher = $_.Publisher
                            installDate = $_.InstallDate
                        }
                    }
                }
            }
            catch {
                # Silently continue if registry path doesn't exist
            }
        }
    }

    # Search in installed packages
    if ($checkPackages) {
        try {
            $packages = Get-Package -ErrorAction SilentlyContinue

            if ($exactMatch) {
                $matches = @($packages | Where-Object { 
                    $_.Name -eq $appName 
                })
            } else {
                $matches = @($packages | Where-Object { 
                    $_.Name -like "*$appName*" 
                })
            }

            if ($matches.Count -gt 0) {
                $foundInPackages = $true
                $packageMatches += $matches | ForEach-Object {
                    [PSCustomObject]@{
                        name = $_.Name
                        version = $_.Version
                        source = $_.Source
                        providerName = $_.ProviderName
                    }
                }
            }
        }
        catch {
            # Silently continue if Get-Package fails
        }
    }

    # Deduplicate registry matches
    $registryMatches = @($registryMatches | 
        Sort-Object name, version -Unique)

    # Deduplicate package matches
    $packageMatches = @($packageMatches | 
        Sort-Object name, version -Unique)

    # Determine result
    $isInstalled = $foundInRegistry -or $foundInPackages

    # Populate step data
    $step.metrics = @{
        registry_matches = $registryMatches.Count
        package_matches = $packageMatches.Count
        total_matches = $registryMatches.Count + $packageMatches.Count
    }

    $step.PSObject.Properties.Add([PSNoteProperty]::new('expected', @{
        app_name = $appName
        exact_match = $exactMatch
        check_registry = $checkRegistry
        check_packages = $checkPackages
    }))

    $step.PSObject.Properties.Add([PSNoteProperty]::new('actual', @{
        found_in_registry = $foundInRegistry
        found_in_packages = $foundInPackages
        is_installed = $isInstalled
        registry_matches = $registryMatches
        package_matches = $packageMatches
    }))

    if ($isInstalled) {
        $step.status = "PASS"
        $overallStatus = "PASS"
        $exitCode = 0
        $step.message = "Application '$appName' is installed. Found in: " + 
            $(if ($foundInRegistry) { "Registry" }) + 
            $(if ($foundInRegistry -and $foundInPackages) { ", " }) + 
            $(if ($foundInPackages) { "Packages" })
    }
    else {
        $step.status = "FAIL"
        $step.message = "Application '$appName' is not installed on the system."
    }
}
catch {
    $overallStatus = "FAIL"
    $step.status = "FAIL"
    Fail-Step -step $step -msg "Script error during application check" -ex $_
    $exitCode = 2
}
finally {
    $swStep.Stop()
    $step.timing.duration_ms = [int]$swStep.ElapsedMilliseconds
    
    $swTotal.Stop()
    $totalMs = [int]$swTotal.ElapsedMilliseconds

    # Count results
    $passCount = @($step | Where-Object { $_.status -eq 'PASS' }).Count
    $failCount = @($step | Where-Object { $_.status -eq 'FAIL' }).Count
    $skipCount = @($step | Where-Object { $_.status -eq 'SKIP' }).Count
    $totalCount = 1

    # Build report
    $report = @{
        id = $TestId
        name = $TestName
        version = "1.0.0"
        timestamp = $TsUtc
        duration_ms = $totalMs
        status = $overallStatus
        steps = @($step)
        summary = @{
            total = $totalCount
            passed = $passCount
            failed = $failCount
            skipped = $skipCount
        }
    }

    Write-JsonFile -Path $ReportPath -Obj $report

    # Stdout summary
    $stepLine = "STEP 1: $($step.name) -> $($step.status)"
    $stepDetails = @()
    
    if ($step.message) {
        $stepDetails += $step.message
    }
    
    if ($step.metrics.total_matches -gt 0) {
        $stepDetails += "Total matches: $($step.metrics.total_matches)"
        
        # Add matched items from registry
        if ($registryMatches.Count -gt 0) {
            $stepDetails += "Registry matches:"
            foreach ($match in $registryMatches) {
                $matchInfo = "  - $($match.name)"
                if ($match.version) { $matchInfo += " (v$($match.version))" }
                if ($match.publisher) { $matchInfo += " by $($match.publisher)" }
                $stepDetails += $matchInfo
            }
        }
        
        # Add matched items from packages
        if ($packageMatches.Count -gt 0) {
            $stepDetails += "Package matches:"
            foreach ($match in $packageMatches) {
                $matchInfo = "  - $($match.name)"
                if ($match.version) { $matchInfo += " (v$($match.version))" }
                if ($match.providerName) { $matchInfo += " [$($match.providerName)]" }
                $stepDetails += $matchInfo
            }
        }
    }

    Write-Stdout-Compact `
        -TestName $TestName `
        -Overall $overallStatus `
        -ExitCode $exitCode `
        -TsUtc $TsUtc `
        -StepLine $stepLine `
        -StepDetails $stepDetails `
        -Total $totalCount `
        -Passed $passCount `
        -Failed $failCount `
        -Skipped $skipCount
}

exit $exitCode
