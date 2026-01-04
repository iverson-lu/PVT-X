param(
    [Parameter(Mandatory=$true)] [string] $P_FilePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ----------------------------
# Helpers
# ----------------------------
function Ensure-Dir([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Write-JsonFile([string] $Path, $Obj) {
    $Obj | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $Path -Encoding UTF8
}

# ----------------------------
# Metadata
# ----------------------------
$TestId = "ReadFileCase"
$TsUtc  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# PVT-X injected environment variables
$TestCasePath = $env:PVTX_TESTCASE_PATH
$TestCaseName = $env:PVTX_TESTCASE_NAME
$TestCaseId = $env:PVTX_TESTCASE_ID
$TestCaseVer = $env:PVTX_TESTCASE_VER

Write-Output "=================================================="
Write-Output ("TEST: {0}" -f $TestId)
Write-Output ("UTC:  {0}" -f $TsUtc)
Write-Output ("Test Case Path: {0}" -f $TestCasePath)
Write-Output ("Test Case Name: {0}" -f $TestCaseName)
Write-Output ("Test Case ID:   {0}" -f $TestCaseId)
Write-Output ("Test Case Ver:  {0}" -f $TestCaseVer)
Write-Output "--------------------------------------------------"

# Artifacts
$ArtifactsRoot = Join-Path (Get-Location) "artifacts"
Ensure-Dir $ArtifactsRoot
$ReportPath = Join-Path $ArtifactsRoot "report.json"

# Defaults
$overallStatus = "FAIL"
$exitCode = 1
$startMs = (Get-Date).Ticks / 10000

try {
    # ----------------------------
    # Step 1: Check if file exists
    # ----------------------------
    Write-Output "[1/2] Check if file exists ..."

    # Resolve file path: if relative, use test case path as root
    if ([System.IO.Path]::IsPathRooted($P_FilePath)) {
        $resolvedPath = $P_FilePath
        Write-Output ("      Using absolute path: {0}" -f $resolvedPath)
    } else {
        $resolvedPath = Join-Path $TestCasePath $P_FilePath
        Write-Output ("      Resolved relative path: {0} -> {1}" -f $P_FilePath, $resolvedPath)
    }

    $step1 = @{
        id      = "check_file_exists"
        index   = 1
        name    = "Check if file exists"
        status  = "FAIL"
        expected = @{
            file_exists = $true
        }
        actual = @{
            resolved_path = $resolvedPath
        }
    }

    $fileExists = Test-Path -LiteralPath $resolvedPath -PathType Leaf

    if ($fileExists) {
        Write-Output ("      File exists: {0}" -f $resolvedPath)
        $step1.status = "PASS"
        $step1.actual.file_exists = $true
    } else {
        Write-Output ("      ERROR: File not found: {0}" -f $resolvedPath)
        $step1.status = "FAIL"
        $step1.actual.file_exists = $false
        $step1.error = @{
            message = "File not found: $resolvedPath"
        }
        throw "File not found: $resolvedPath"
    }

    # ----------------------------
    # Step 2: Read and output file content
    # ----------------------------
    Write-Output "[2/2] Read and output file content ..."

    $step2 = @{
        id      = "read_file_content"
        index   = 2
        name    = "Read and output file content"
        status  = "FAIL"
        expected = @{
            content_read = $true
        }
        actual = @{}
    }

    $content = Get-Content -LiteralPath $resolvedPath -Raw
    $lines = @(Get-Content -LiteralPath $resolvedPath)
    $lineCount = $lines.Count

    Write-Output ""
    Write-Output "---------- FILE CONTENT START ----------"
    Write-Output $content
    Write-Output "---------- FILE CONTENT END ----------"
    Write-Output ""
    Write-Output ("      Lines read: {0}" -f $lineCount)

    $step2.status = "PASS"
    $step2.actual.content_read = $true
    $step2.actual.line_count = $lineCount
    $step2.actual.content_length = $content.Length

    # ----------------------------
    # Success
    # ----------------------------
    $overallStatus = "PASS"
    $exitCode = 0

    $steps = @($step1, $step2)

} catch {
    # ----------------------------
    # Error handling
    # ----------------------------
    Write-Output ""
    Write-Output "ERROR: $_"
    
    if ($null -eq $step2) {
        $steps = @($step1)
    } else {
        $step2.status = "FAIL"
        $step2.error = @{
            message = $_.Exception.Message
            type = $_.Exception.GetType().FullName
        }
        $steps = @($step1, $step2)
    }

    $overallStatus = "FAIL"
    $exitCode = 2

} finally {
    # ----------------------------
    # Summary
    # ----------------------------
    $endMs = (Get-Date).Ticks / 10000
    $durationMs = [int]($endMs - $startMs)

    $passCount = @($steps | Where-Object { $_.status -eq "PASS" }).Count
    $failCount = @($steps | Where-Object { $_.status -eq "FAIL" }).Count
    $totalCount = @($steps).Count

    Write-Output "--------------------------------------------------"
    Write-Output ("SUMMARY: total={0} passed={1} failed={2} skipped=0" -f $totalCount, $passCount, $failCount)
    Write-Output "=================================================="
    Write-Output ("MACHINE: overall={0} exit_code={1}" -f $overallStatus, $exitCode)

    # ----------------------------
    # Generate report.json
    # ----------------------------
    $report = @{
        schema = @{
            version = "1.0"
        }
        test = @{
            id = $TestId
            name = $TestId
            params = @{
                P_FilePath = $P_FilePath
                resolved_path = $resolvedPath
            }
        }
        summary = @{
            status = $overallStatus
            exit_code = $exitCode
            counts = @{
                total = $totalCount
                pass = $passCount
                fail = $failCount
                skip = 0
            }
            duration_ms = $durationMs
        }
        steps = $steps
    }

    Write-JsonFile -Path $ReportPath -Obj $report

    exit $exitCode
}
