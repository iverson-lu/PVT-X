param(
    # Basic types that work well via command line
    [Parameter(Mandatory=$true)]  [string]  $E_Mode,
    [Parameter(Mandatory=$false)] [string]  $S_Text = "hello",
    [Parameter(Mandatory=$false)] [int]     $N_Int = 42,
    [Parameter(Mandatory=$false)] [bool]    $B_Flag = $true,
    [Parameter(Mandatory=$false)] [double]  $N_Double = 3.14,
    [Parameter(Mandatory=$false)] [string]  $P_Path = "C:\\Windows"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Ensure-Dir([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Write-JsonFile([string] $Path, $Obj) {
    $Obj | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function TypeName($x) {
    if ($null -eq $x) { return "<null>" }
    return $x.GetType().FullName
}

# Type validation function
function Validate-ParameterTypes {
    $errors = @()
    
    # E_Mode - should be string
    if ($E_Mode -isnot [string]) {
        $errors += "E_Mode: Expected string, got $($E_Mode.GetType().Name)"
    }
    
    # S_Text - should be string
    if ($S_Text -isnot [string]) {
        $errors += "S_Text: Expected string, got $($S_Text.GetType().Name)"
    }
    
    # N_Int - should be int32
    if ($N_Int -isnot [int] -and $N_Int -isnot [int32]) {
        $errors += "N_Int: Expected int, got $($N_Int.GetType().Name)"
    }
    
    # B_Flag - should be boolean
    if ($B_Flag -isnot [bool]) {
        $errors += "B_Flag: Expected boolean, got $($B_Flag.GetType().Name)"
    }
    
    # N_Double - should be double
    if ($N_Double -isnot [double]) {
        $errors += "N_Double: Expected double, got $($N_Double.GetType().Name)"
    }
    
    # P_Path - should be string
    if ($P_Path -isnot [string]) {
        $errors += "P_Path: Expected string, got $($P_Path.GetType().Name)"
    }
    
    if ($errors.Count -gt 0) {
        throw "Parameter type validation failed:`n" + ($errors -join "`n")
    }
}

# Minimal self-validation
$allowedModes = @("Alpha","Beta","Gamma")
if ($allowedModes -notcontains $E_Mode) {
    throw "Invalid E_Mode '$E_Mode'. Allowed: $($allowedModes -join ', ')"
}

# Validate parameter types
Write-Output "=== Type Validation ==="
try {
    Validate-ParameterTypes
    Write-Output "✓ All parameter types are correct"
} catch {
    Write-Error $_
    exit 1
}

$ArtifactsRoot = Join-Path (Get-Location) "artifacts"
Ensure-Dir $ArtifactsRoot

# Dump received values to console
Write-Output ""
Write-Output "=== AllParamTypesSample: Received Parameters ==="
$kv = [ordered]@{
    E_Mode=$E_Mode; S_Text=$S_Text; N_Int=$N_Int; 
    B_Flag=$B_Flag; N_Double=$N_Double; P_Path=$P_Path
}
foreach ($k in $kv.Keys) {
    $v = $kv[$k]
    $typeName = TypeName $v
    $actualValue = if ($v -is [bool]) { if ($v) { "True" } else { "False" } } else { $v }
    Write-Output ("- {0} ({1}) = {2}" -f $k, $typeName, $actualValue)
}

# Build report.json
$details = [ordered]@{}
foreach ($k in $kv.Keys) {
    $v = $kv[$k]
    $details[$k] = [ordered]@{
        value = $v
        psType = (TypeName $v)
    }
}

$report = [ordered]@{
    testId  = "AllParamTypesSample"
    outcome = "Pass"
    summary = "Basic parameter types were bound successfully."
    details = $details
    metrics = [ordered]@{
        paramCount = $kv.Keys.Count
    }
}

Write-JsonFile (Join-Path $ArtifactsRoot "report.json") $report
Write-Output "[RESULT] Pass"
exit 0
