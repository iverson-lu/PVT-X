#region parameters
param(
    [Parameter(Mandatory=$false)] [string] $CPU_ProcessorName = "Intel Core Ultra",
    [Parameter(Mandatory=$false)] [int]    $WindowsUpdate_MaxDaysSinceLastUpdate = 30,
    [Parameter(Mandatory=$false)] [double] $CPU_MinFrequency = 1.0,
    [Parameter(Mandatory=$true)]  [ValidateSet("Windows 24H2", "Windows 25H1", "Windows 25H2")] [string] $OS_Version,
    [Parameter(Mandatory=$false)] [bool]   $Windows_MustBeActivated = $true,
    [Parameter(Mandatory=$false)] [string] $System_WindowsPath = "C:\\Windows",
    [Parameter(Mandatory=$false)] [string] $RequiredSoftware = "[`"Microsoft Edge`"]",
    [Parameter(Mandatory=$false)] [string] $MinimumRequirements = "{`"cores`": 4, `"memoryGB`": 8, `"diskTotalGB`": 100, `"webcamCount`": 1}"
)
#endregion

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

#region constants / globals
$script:TestId = "SystemInfoVerification"
$script:TotalChecks = 8
$script:StartTimeUtc = (Get-Date).ToUniversalTime()
$script:ValidationFailures = @()
$script:SkippedChecks = @()
$script:ArtifactsRoot = Join-Path (Get-Location) "artifacts"

$script:CollectedData = @{
    CpuInfo = $null
    OsInfo = $null
    ParsedSoftware = $null
    ParsedRequirements = $null
}
#endregion

#region helper functions
function Invoke-Timestamp {
    Write-Output "Timestamp: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')"
}

function Ensure-ArtifactsDirectory {
    if (-not (Test-Path -LiteralPath $script:ArtifactsRoot)) {
        New-Item -ItemType Directory -Path $script:ArtifactsRoot -Force | Out-Null
    }
}

function Write-ReportJson {
    param([hashtable] $Report)
    Ensure-ArtifactsDirectory
    $Report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $script:ArtifactsRoot "report.json") -Encoding UTF8
}

function Write-TestResult {
    param(
        [Parameter(Mandatory=$true)] [ValidateSet("Pass", "Fail")] [string] $Outcome,
        [Parameter(Mandatory=$true)] [string] $Summary,
        [Parameter(Mandatory=$false)] [hashtable] $Details = @{},
        [Parameter(Mandatory=$false)] [hashtable] $Metrics = @{}
    )
    
    $endTimeUtc = (Get-Date).ToUniversalTime()
    
    $report = [ordered]@{
        testId = $script:TestId
        outcome = $Outcome
        summary = $Summary
        details = $Details
        metrics = $Metrics
        startTimeUtc = $script:StartTimeUtc.ToString("o")
        endTimeUtc = $endTimeUtc.ToString("o")
    }
    
    if ($script:SkippedChecks.Count -gt 0) {
        $report.skippedChecks = $script:SkippedChecks
    }
    
    Write-ReportJson $report
    Write-Output "[RESULT] $Outcome"
    exit $(if ($Outcome -eq "Pass") { 0 } else { 1 })
}

function Add-ValidationFailure {
    param(
        [Parameter(Mandatory=$true)] [string] $CheckName,
        [Parameter(Mandatory=$true)] [string] $Reason,
        [Parameter(Mandatory=$false)] [hashtable] $Details = @{}
    )
    
    Write-Output "[FAIL] $CheckName - $Reason"
    
    $script:ValidationFailures += [ordered]@{
        check = $CheckName
        reason = $Reason
        details = $Details
    }
}

function Add-SkippedCheck {
    param(
        [Parameter(Mandatory=$true)] [string] $CheckName,
        [Parameter(Mandatory=$true)] [string] $Reason
    )
    
    Write-Output "⚠ $Reason - skipping check"
    
    $script:SkippedChecks += [ordered]@{
        check = $CheckName
        reason = $Reason
    }
}

function Parse-JsonParameter {
    param(
        [Parameter(Mandatory=$true)] [string] $Json,
        [Parameter(Mandatory=$true)] [string] $ParameterName
    )
    
    try {
        $parsed = $Json | ConvertFrom-Json
        return $parsed
    } catch {
        Write-TestResult -Outcome Fail -Summary "Failed to parse JSON parameter '$ParameterName': $_"
    }
}

function Get-InstalledSoftwareNames {
    $software = @()
    $regPaths = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )
    
    foreach ($regPath in $regPaths) {
        if (Test-Path $regPath) {
            $items = Get-ItemProperty $regPath -ErrorAction SilentlyContinue
            foreach ($item in $items) {
                if ($item.PSObject.Properties['DisplayName'] -and $item.DisplayName) {
                    $software += $item.DisplayName
                }
            }
        }
    }
    
    return $software
}

function Test-SoftwareInstalled {
    param(
        [Parameter(Mandatory=$true)] [string] $SoftwareName,
        [Parameter(Mandatory=$true)] [string[]] $InstalledSoftware
    )
    
    $found = $InstalledSoftware | Where-Object { $_.ToLower().Contains($SoftwareName.ToLower()) }
    return $found
}

function Normalize-ProcessorName {
    param([Parameter(Mandatory=$true)] [string] $Name)
    
    return $Name -replace '\([^\)]*\)', '' -replace '\s+', ' ' -replace '^\s+|\s+$', ''
}
#endregion

#region check implementations
function Invoke-CpuProcessorNameCheck {
    Write-Output "=== [1/$script:TotalChecks] Checking CPU Processor Name ==="
    Invoke-Timestamp
    
    try {
        $cpu = Get-CimInstance -ClassName Win32_Processor | Select-Object -First 1
        $actualName = $cpu.Name.Trim()
        Write-Output "Actual: $actualName"
        Write-Output "Required pattern: $CPU_ProcessorName"
        
        $normalizedActual = Normalize-ProcessorName $actualName
        $normalizedPattern = Normalize-ProcessorName $CPU_ProcessorName
        Write-Output "Normalized actual: $normalizedActual"
        
        if ($normalizedActual.ToLower().IndexOf($normalizedPattern.ToLower()) -eq -1) {
            Add-ValidationFailure -CheckName "CPU Processor Name" `
                -Reason "CPU processor name does not match pattern '$CPU_ProcessorName'" `
                -Details @{
                    actual = $actualName
                    required = $CPU_ProcessorName
                    normalizedActual = $normalizedActual
                    normalizedPattern = $normalizedPattern
                }
        } else {
            Write-Output "✓ Processor name validation passed"
        }
        
        $script:CollectedData.CpuInfo = @{ Name = $actualName }
    } catch {
        Add-ValidationFailure -CheckName "CPU Processor Name" `
            -Reason "Failed to retrieve CPU information: $_"
    }
    
    Write-Output ""
}

function Invoke-WindowsUpdateCheck {
    Write-Output "=== [2/$script:TotalChecks] Checking Windows Update Status ==="
    Invoke-Timestamp
    
    try {
        $lastHotfix = Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 1
        
        if ($lastHotfix -and $lastHotfix.InstalledOn) {
            $daysSinceUpdate = [math]::Round(((Get-Date) - $lastHotfix.InstalledOn).TotalDays, 1)
            Write-Output "Last update: $($lastHotfix.HotFixID) on $($lastHotfix.InstalledOn)"
            Write-Output "Days since last update: $daysSinceUpdate"
            Write-Output "Maximum allowed: $WindowsUpdate_MaxDaysSinceLastUpdate days"
            
            if ($daysSinceUpdate -gt $WindowsUpdate_MaxDaysSinceLastUpdate) {
                Add-ValidationFailure -CheckName "Windows Update" `
                    -Reason "Last Windows Update was $daysSinceUpdate days ago, exceeds maximum of $WindowsUpdate_MaxDaysSinceLastUpdate days" `
                    -Details @{
                        daysSinceUpdate = $daysSinceUpdate
                        maxDays = $WindowsUpdate_MaxDaysSinceLastUpdate
                        lastUpdate = $lastHotfix.InstalledOn
                        lastHotFixID = $lastHotfix.HotFixID
                    }
            } else {
                Write-Output "✓ Windows Update status is acceptable"
            }
        } else {
            Add-SkippedCheck -CheckName "Windows Update" `
                -Reason "Unable to retrieve Windows Update history"
        }
    } catch {
        Add-SkippedCheck -CheckName "Windows Update" `
            -Reason "Failed to retrieve Windows Update information: $_"
    }
    
    Write-Output ""
}

function Invoke-CpuFrequencyCheck {
    Write-Output "=== [3/$script:TotalChecks] Checking CPU Current Frequency ==="
    Invoke-Timestamp
    
    try {
        $cpu = Get-CimInstance -ClassName Win32_Processor | Select-Object -First 1
        $actualFreqMHz = $cpu.CurrentClockSpeed
        $actualFreqGHz = [math]::Round($actualFreqMHz / 1000, 2)
        Write-Output "Current running frequency: ${actualFreqGHz} GHz"
        Write-Output "Minimum required: ${CPU_MinFrequency} GHz"
        
        if ($actualFreqGHz -lt $CPU_MinFrequency) {
            Add-ValidationFailure -CheckName "CPU Frequency" `
                -Reason "CPU frequency ${actualFreqGHz} GHz is below minimum ${CPU_MinFrequency} GHz" `
                -Details @{
                    actualFreq = $actualFreqGHz
                    minFreq = $CPU_MinFrequency
                }
        } else {
            Write-Output "✓ CPU frequency is acceptable"
        }
        
        if ($null -eq $script:CollectedData.CpuInfo) {
            $script:CollectedData.CpuInfo = @{}
        }
        $script:CollectedData.CpuInfo.FrequencyGHz = $actualFreqGHz
    } catch {
        Add-ValidationFailure -CheckName "CPU Frequency" `
            -Reason "Failed to retrieve CPU frequency: $_"
    }
    
    Write-Output ""
}

function Invoke-OsVersionCheck {
    Write-Output "=== [4/$script:TotalChecks] Checking Windows Version ==="
    Invoke-Timestamp
    
    try {
        $os = Get-CimInstance -ClassName Win32_OperatingSystem
        $displayVersion = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion").DisplayVersion
        $actualVersion = $os.Caption
        Write-Output "Actual OS: $actualVersion"
        Write-Output "Display Version: $displayVersion"
        Write-Output "Build Number: $($os.BuildNumber)"
        Write-Output "Expected version: $OS_Version"
        
        $versionMatch = $false
        if ($displayVersion -eq $OS_Version) {
            $versionMatch = $true
        } elseif ($OS_Version -eq "Windows 24H2" -and $displayVersion -eq "24H2") {
            $versionMatch = $true
        } elseif ($OS_Version -eq "Windows 25H1" -and $displayVersion -eq "25H1") {
            $versionMatch = $true
        } elseif ($OS_Version -eq "Windows 25H2" -and $displayVersion -eq "25H2") {
            $versionMatch = $true
        }
        
        if (-not $versionMatch) {
            Add-ValidationFailure -CheckName "OS Version" `
                -Reason "Windows version mismatch" `
                -Details @{
                    actual = $actualVersion
                    displayVersion = $displayVersion
                    actualBuild = $os.BuildNumber
                    expected = $OS_Version
                }
        } else {
            Write-Output "✓ Windows version validation passed"
        }
        
        $script:CollectedData.OsInfo = @{
            Caption = $actualVersion
            BuildNumber = $os.BuildNumber
        }
    } catch {
        Add-ValidationFailure -CheckName "OS Version" `
            -Reason "Failed to retrieve Windows version: $_"
    }
    
    Write-Output ""
}

function Invoke-WindowsActivationCheck {
    Write-Output "=== [5/$script:TotalChecks] Checking Windows Activation Status ==="
    Invoke-Timestamp
    
    try {
        $licenseStatus = Get-CimInstance -ClassName SoftwareLicensingProduct `
                         -Filter "ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL" |
                         Select-Object -First 1 -ExpandProperty LicenseStatus
        
        $isActivated = $licenseStatus -eq 1
        Write-Output "Windows activated: $isActivated (LicenseStatus: $licenseStatus)"
        Write-Output "Activation required: $Windows_MustBeActivated"
        
        if ($Windows_MustBeActivated -and -not $isActivated) {
            Add-ValidationFailure -CheckName "Windows Activation" `
                -Reason "Windows is not activated, but activation is required" `
                -Details @{
                    isActivated = $isActivated
                    licenseStatus = $licenseStatus
                    required = $Windows_MustBeActivated
                }
        } elseif (-not $Windows_MustBeActivated -and $isActivated) {
            Add-ValidationFailure -CheckName "Windows Activation" `
                -Reason "Windows is activated, but must NOT be activated" `
                -Details @{
                    isActivated = $isActivated
                    licenseStatus = $licenseStatus
                    required = $Windows_MustBeActivated
                }
        } else {
            Write-Output "✓ Windows activation status check passed"
        }
        
        if ($null -eq $script:CollectedData.OsInfo) {
            $script:CollectedData.OsInfo = @{}
        }
        $script:CollectedData.OsInfo.IsActivated = $isActivated
    } catch {
        Add-SkippedCheck -CheckName "Windows Activation" `
            -Reason "Failed to retrieve Windows activation status: $_"
    }
    
    Write-Output ""
}

function Invoke-WindowsPathCheck {
    Write-Output "=== [6/$script:TotalChecks] Checking Windows Installation Path ==="
    Invoke-Timestamp
    
    try {
        $actualWindowsPath = $env:SystemRoot
        Write-Output "Actual: $actualWindowsPath"
        Write-Output "Expected: $System_WindowsPath"
        
        if ($actualWindowsPath.ToLower() -ne $System_WindowsPath.ToLower()) {
            Add-ValidationFailure -CheckName "Windows Path" `
                -Reason "Windows installation path mismatch" `
                -Details @{
                    actual = $actualWindowsPath
                    expected = $System_WindowsPath
                }
        } else {
            Write-Output "✓ Windows installation path validation passed"
        }
        
        if ($null -eq $script:CollectedData.OsInfo) {
            $script:CollectedData.OsInfo = @{}
        }
        $script:CollectedData.OsInfo.WindowsPath = $actualWindowsPath
    } catch {
        Add-ValidationFailure -CheckName "Windows Path" `
            -Reason "Failed to retrieve Windows installation path: $_"
    }
    
    Write-Output ""
}

function Invoke-RequiredSoftwareCheck {
    Write-Output "=== [7/$script:TotalChecks] Checking Required Software ==="
    Invoke-Timestamp
    
    try {
        $installedSoftware = Get-InstalledSoftwareNames
        Write-Output "Found $($installedSoftware.Count) installed applications"
        
        $missingSoftware = @()
        foreach ($requiredApp in $script:CollectedData.ParsedSoftware) {
            $found = Test-SoftwareInstalled -SoftwareName $requiredApp -InstalledSoftware $installedSoftware
            if (-not $found) {
                $missingSoftware += $requiredApp
                Write-Output "✗ Missing: $requiredApp"
            } else {
                Write-Output "✓ Found: $requiredApp (matched: $($found | Select-Object -First 1))"
            }
        }
        
        if ($missingSoftware.Count -gt 0) {
            Add-ValidationFailure -CheckName "Required Software" `
                -Reason "Missing required software: $($missingSoftware -join ', ')" `
                -Details @{
                    missing = $missingSoftware
                    required = $script:CollectedData.ParsedSoftware
                }
        } else {
            Write-Output "✓ All required software is installed"
        }
    } catch {
        Add-ValidationFailure -CheckName "Required Software" `
            -Reason "Failed to check installed software: $_"
    }
    
    Write-Output ""
}

function Invoke-MinimumRequirementsCheck {
    Write-Output "=== [8/$script:TotalChecks] Checking Minimum System Requirements ==="
    Invoke-Timestamp
    
    try {
        $failedChecks = @()
        $requirements = $script:CollectedData.ParsedRequirements
        
        $actualCores = (Get-CimInstance -ClassName Win32_Processor).NumberOfCores
        $minCores = if ($requirements.PSObject.Properties['cores']) { $requirements.cores } else { 0 }
        Write-Output "CPU Cores: $actualCores (minimum: $minCores)"
        if ($minCores -gt 0 -and $actualCores -lt $minCores) {
            $failedChecks += "CPU cores: $actualCores < $minCores"
        }
        
        $totalMemoryBytes = (Get-CimInstance -ClassName Win32_ComputerSystem).TotalPhysicalMemory
        $actualMemoryGB = [math]::Round($totalMemoryBytes / 1GB, 2)
        $minMemoryGB = if ($requirements.PSObject.Properties['memoryGB']) { $requirements.memoryGB } else { 0 }
        Write-Output "Memory: ${actualMemoryGB} GB (minimum: $minMemoryGB GB)"
        if ($minMemoryGB -gt 0 -and $actualMemoryGB -lt $minMemoryGB) {
            $failedChecks += "Memory: ${actualMemoryGB} GB < $minMemoryGB GB"
        }
        
        $systemDrive = $env:SystemDrive
        $disk = Get-CimInstance -ClassName Win32_LogicalDisk | Where-Object { $_.DeviceID -eq $systemDrive }
        $actualDiskTotalGB = [math]::Round($disk.Size / 1GB, 2)
        $minDiskTotalGB = if ($requirements.PSObject.Properties['diskTotalGB']) { $requirements.diskTotalGB } else { 0 }
        Write-Output "Total Disk Size (${systemDrive}): ${actualDiskTotalGB} GB (minimum: $minDiskTotalGB GB)"
        if ($minDiskTotalGB -gt 0 -and $actualDiskTotalGB -lt $minDiskTotalGB) {
            $failedChecks += "Total disk size: ${actualDiskTotalGB} GB < $minDiskTotalGB GB"
        }
        
        $webcams = Get-CimInstance -ClassName Win32_PnPEntity | 
                   Where-Object { $_.PNPClass -eq 'Camera' -or $_.PNPClass -eq 'Image' }
        $actualWebcamCount = @($webcams).Count
        $minWebcamCount = if ($requirements.PSObject.Properties['webcamCount']) { $requirements.webcamCount } else { 0 }
        Write-Output "Webcams: $actualWebcamCount (minimum: $minWebcamCount)"
        if ($minWebcamCount -gt 0 -and $actualWebcamCount -lt $minWebcamCount) {
            $failedChecks += "Webcams: $actualWebcamCount < $minWebcamCount"
        }
        
        if ($failedChecks.Count -gt 0) {
            Add-ValidationFailure -CheckName "Minimum Requirements" `
                -Reason "System does not meet minimum requirements: $($failedChecks -join '; ')" `
                -Details @{
                    failedChecks = $failedChecks
                    actual = @{
                        cores = $actualCores
                        memoryGB = $actualMemoryGB
                        diskTotalGB = $actualDiskTotalGB
                        webcamCount = $actualWebcamCount
                    }
                    required = $requirements
                }
        } else {
            Write-Output "✓ All minimum requirements are met"
        }
        
        $script:CollectedData.Requirements = @{
            Cores = $actualCores
            MemoryGB = $actualMemoryGB
            DiskTotalGB = $actualDiskTotalGB
            WebcamCount = $actualWebcamCount
        }
    } catch {
        Add-ValidationFailure -CheckName "Minimum Requirements" `
            -Reason "Failed to check minimum requirements: $_"
    }
    
    Write-Output ""
}
#endregion

#region main flow
Write-Output "=== System Information Verification ==="
Write-Output ""

# Parse JSON parameters
Write-Output "=== Parsing JSON Parameters ==="
$parsedSoftware = Parse-JsonParameter -Json $RequiredSoftware -ParameterName "RequiredSoftware"
if ($parsedSoftware -isnot [array]) {
    $parsedSoftware = @($parsedSoftware)
}
Write-Output "✓ RequiredSoftware parsed: $($parsedSoftware.Count) items"
$script:CollectedData.ParsedSoftware = $parsedSoftware

$parsedRequirements = Parse-JsonParameter -Json $MinimumRequirements -ParameterName "MinimumRequirements"
Write-Output "✓ MinimumRequirements parsed successfully"
$script:CollectedData.ParsedRequirements = $parsedRequirements
Write-Output ""

# Execute all checks
Invoke-CpuProcessorNameCheck
Invoke-WindowsUpdateCheck
Invoke-CpuFrequencyCheck
Invoke-OsVersionCheck
Invoke-WindowsActivationCheck
Invoke-WindowsPathCheck
Invoke-RequiredSoftwareCheck
Invoke-MinimumRequirementsCheck

# Summarize results
Write-Output "=== Validation Summary ==="
Invoke-Timestamp

if ($script:ValidationFailures.Count -gt 0) {
    Write-Output "Failed checks: $($script:ValidationFailures.Count)"
    foreach ($failure in $script:ValidationFailures) {
        Write-Output "  - $($failure.check): $($failure.reason)"
    }
    
    $details = [ordered]@{
        failures = $script:ValidationFailures
        summary = [ordered]@{
            totalChecks = $script:TotalChecks
            failedChecks = $script:ValidationFailures.Count
            passedChecks = $script:TotalChecks - $script:ValidationFailures.Count
            skippedChecks = $script:SkippedChecks.Count
        }
    }
    
    Write-TestResult -Outcome Fail `
        -Summary "$($script:ValidationFailures.Count) validation(s) failed" `
        -Details $details
}

# All checks passed
Write-Output "All checks passed: $script:TotalChecks/$script:TotalChecks"

$details = [ordered]@{
    cpu = [ordered]@{
        processorName = $script:CollectedData.CpuInfo.Name
        frequencyGHz = $script:CollectedData.CpuInfo.FrequencyGHz
    }
    os = [ordered]@{
        caption = $script:CollectedData.OsInfo.Caption
        buildNumber = $script:CollectedData.OsInfo.BuildNumber
        isActivated = $script:CollectedData.OsInfo.IsActivated
        windowsPath = $script:CollectedData.OsInfo.WindowsPath
    }
    requirements = [ordered]@{
        cores = $script:CollectedData.Requirements.Cores
        memoryGB = $script:CollectedData.Requirements.MemoryGB
        diskTotalGB = $script:CollectedData.Requirements.DiskTotalGB
        webcamCount = $script:CollectedData.Requirements.WebcamCount
    }
    software = [ordered]@{
        requiredCount = $script:CollectedData.ParsedSoftware.Count
        allFound = $true
    }
}

$metrics = [ordered]@{
    cpuCores = $script:CollectedData.Requirements.Cores
    memoryGB = $script:CollectedData.Requirements.MemoryGB
    totalDiskGB = $script:CollectedData.Requirements.DiskTotalGB
    webcamCount = $script:CollectedData.Requirements.WebcamCount
    cpuFrequencyGHz = $script:CollectedData.CpuInfo.FrequencyGHz
}

Write-TestResult -Outcome Pass `
    -Summary "All system information validations passed successfully" `
    -Details $details `
    -Metrics $metrics
#endregion
