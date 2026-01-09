# Pvtx.Testing - Common helper functions for PVT-X Test Cases
# Target: PowerShell 7+
# Notes:
# - Keep functions small & stable.
# - Do NOT set StrictMode here.
# - Default stdout format matches existing majority of cases.

Set-StrictMode -Off  # safe no-op if StrictMode isn't enabled; avoids breaking module load

function Ensure-Dir {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [string] $Path
    )
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Write-JsonFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [string] $Path,

        [Parameter(Mandatory=$true)]
        $Obj,

        [int] $Depth = 50
    )

    # Ensure parent dir exists
    $parent = Split-Path -Parent $Path
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    $Obj | ConvertTo-Json -Depth $Depth | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Normalize-Text {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [string] $s
    )

    if ($null -eq $s) { return "" }
    $t = $s.Trim()
    while (($t.StartsWith('"') -and $t.EndsWith('"')) -or ($t.StartsWith("'") -and $t.EndsWith("'"))) {
        if ($t.Length -lt 2) { break }
        $t = $t.Substring(1, $t.Length - 2).Trim()
    }
    return $t
}

function ParseJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [string] $json,

        [Parameter(Mandatory=$true)]
        [string] $name
    )

    try {
        $json | ConvertFrom-Json -AsHashtable -ErrorAction Stop
    }
    catch {
        throw ('Failed to parse {0}: {1}' -f $name, $_.Exception.Message)
    }
}

function New-Step {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [string] $id,

        [Parameter(Mandatory=$true)]
        [int] $index,

        [Parameter(Mandatory=$true)]
        [string] $name
    )

    @{
        id      = $id
        index   = $index
        name    = $name
        status  = 'FAIL'
        message = $null
        metrics = @{}
        timing  = @{ duration_ms = $null }
        error   = $null
    }
}

function Fail-Step {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [hashtable] $step,

        [Parameter(Mandatory=$true)]
        [string] $msg,

        [Parameter(Mandatory=$true)]
        $ex
    )

    $step.status  = 'FAIL'
    $step.message = $msg
    $step.error   = @{
        kind           = 'SCRIPT'
        code           = 'STEP_ERROR'
        message        = $ex.Exception.Message
        exception_type = $ex.Exception.GetType().FullName
        stack          = $ex.ScriptStackTrace
    }
}

function Write-Stdout-Compact {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)] [string]   $TestName,
        [Parameter(Mandatory=$true)] [string]   $Overall,
        [Parameter(Mandatory=$true)] [int]      $ExitCode,
        [Parameter(Mandatory=$true)] [string]   $TsUtc,
        [Parameter(Mandatory=$true)] [string]   $StepLine,
        [Parameter(Mandatory=$true)] [string[]] $StepDetails,
        [Parameter(Mandatory=$true)] [int]      $Total,
        [Parameter(Mandatory=$true)] [int]      $Passed,
        [Parameter(Mandatory=$true)] [int]      $Failed,
        [Parameter(Mandatory=$true)] [int]      $Skipped,

        # When set, uses the older minimal format (used by case.hw.memory.core.inventory_check)
        [switch] $Minimal
    )

    if ($Minimal) {
        Write-Output ("TEST: name={0} ts_utc={1}" -f $TestName, $TsUtc)
        Write-Output ("STEPS: total={0} pass={1} fail={2} skip={3}" -f $Total, $Passed, $Failed, $Skipped)
        Write-Output ("STEP: {0}" -f $StepLine)
        foreach ($d in $StepDetails) {
            Write-Output ("  - {0}" -f $d)
        }
        Write-Output ("MACHINE: overall={0} exit_code={1}" -f $Overall, $ExitCode)
        return
    }

    # Default: the more readable format used by most cases
    Write-Output "=================================================="
    Write-Output ("TEST: {0}  RESULT: {1}  EXIT: {2}" -f $TestName, $Overall, $ExitCode)
    Write-Output ("UTC:  {0}" -f $TsUtc)
    Write-Output "--------------------------------------------------"
    Write-Output $StepLine
    foreach ($d in $StepDetails) { Write-Output ("      " + $d) }
    Write-Output "--------------------------------------------------"
    Write-Output ("STEPS: total={0}  pass={1}  fail={2}  skip={3}" -f $Total, $Passed, $Failed, $Skipped)
    Write-Output ("MACHINE: overall={0} exit_code={1}" -f $Overall, $ExitCode)
    Write-Output "=================================================="
}

Export-ModuleMember -Function Ensure-Dir, Write-JsonFile, Normalize-Text, ParseJson, New-Step, Fail-Step, Write-Stdout-Compact
