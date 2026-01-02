param(
    [Parameter(Mandatory=$true)]  [string]  $E_Mode,
    [Parameter(Mandatory=$false)] [string]  $S_Text = "hello",
    [Parameter(Mandatory=$false)] [int]     $N_Int = 42,
    [Parameter(Mandatory=$false)] [bool]    $B_Flag = $true,
    [Parameter(Mandatory=$false)] [double]  $N_Double = 3.14,
    [Parameter(Mandatory=$false)] [string]  $P_Path = "C:\\Windows",
    [Parameter(Mandatory=$false)] [string]  $ItemsJson = "[1, 2, 3]",
    [Parameter(Mandatory=$false)] [string]  $ConfigJson = "{`"timeout`": 30, `"retry`": true}"
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

# Validate basic types
function Validate-ParameterTypes {
    $errors = @()
    
    if ($E_Mode -isnot [string]) {
        $errors += "E_Mode: Expected string, got $($E_Mode.GetType().Name)"
    }
    if ($S_Text -isnot [string]) {
        $errors += "S_Text: Expected string, got $($S_Text.GetType().Name)"
    }
    if ($N_Int -isnot [int] -and $N_Int -isnot [int32]) {
        $errors += "N_Int: Expected int, got $($N_Int.GetType().Name)"
    }
    if ($B_Flag -isnot [bool]) {
        $errors += "B_Flag: Expected boolean, got $($B_Flag.GetType().Name)"
    }
    if ($N_Double -isnot [double]) {
        $errors += "N_Double: Expected double, got $($N_Double.GetType().Name)"
    }
    if ($P_Path -isnot [string]) {
        $errors += "P_Path: Expected string, got $($P_Path.GetType().Name)"
    }
    if ($ItemsJson -isnot [string]) {
        $errors += "ItemsJson: Expected string (JSON), got $($ItemsJson.GetType().Name)"
    }
    if ($ConfigJson -isnot [string]) {
        $errors += "ConfigJson: Expected string (JSON), got $($ConfigJson.GetType().Name)"
    }
    
    if ($errors.Count -gt 0) {
        throw "Parameter type validation failed:`n" + ($errors -join "`n")
    }
}

# Validate enum value
$allowedModes = @("Alpha","Beta","Gamma")
if ($allowedModes -notcontains $E_Mode) {
    throw "Invalid E_Mode '$E_Mode'. Allowed: $($allowedModes -join ', ')"
}

Write-Output "=== Type Validation ==="
try {
    Validate-ParameterTypes
    Write-Output "✓ All parameter types are correct"
} catch {
    Write-Error $_
    exit 1
}

Write-Output ""
Write-Output "=== JSON Parsing ==="
$parsedItems = $null
$parsedConfig = $null
try {
    $parsedItems = $ItemsJson | ConvertFrom-Json
    Write-Output "✓ ItemsJson parsed successfully: $($parsedItems.GetType().Name) with $($parsedItems.Count) elements"
    
    $parsedConfig = $ConfigJson | ConvertFrom-Json
    Write-Output "✓ ConfigJson parsed successfully: $($parsedConfig.GetType().Name)"
} catch {
    Write-Error $_
    exit 1
}

$ArtifactsRoot = Join-Path (Get-Location) "artifacts"
Ensure-Dir $ArtifactsRoot

Write-Output ""
Write-Output "=== Parameter Types Sample: Received Parameters ==="
$kv = [ordered]@{
    E_Mode=$E_Mode; S_Text=$S_Text; N_Int=$N_Int; 
    B_Flag=$B_Flag; N_Double=$N_Double; P_Path=$P_Path;
    ItemsJson=$ItemsJson; ConfigJson=$ConfigJson
}
foreach ($k in $kv.Keys) {
    $v = $kv[$k]
    $typeName = TypeName $v
    $actualValue = if ($v -is [bool]) { if ($v) { "True" } else { "False" } } else { $v }
    Write-Output ("- {0} ({1}) = {2}" -f $k, $typeName, $actualValue)
}

Write-Output ""
Write-Output "=== Parsed JSON Data ==="
Write-Output ("- Items: {0} with {1} elements: {2}" -f (TypeName $parsedItems), $parsedItems.Count, ($parsedItems -join ', '))
Write-Output ("- Config: {0}" -f (TypeName $parsedConfig))
foreach ($prop in $parsedConfig.PSObject.Properties) {
    Write-Output ("  - {0}: {1}" -f $prop.Name, $prop.Value)
}

# Build report.json
$details = [ordered]@{
    "basicTypes" = [ordered]@{
        E_Mode = [ordered]@{ value = $E_Mode; psType = (TypeName $E_Mode) }
        S_Text = [ordered]@{ value = $S_Text; psType = (TypeName $S_Text) }
        N_Int = [ordered]@{ value = $N_Int; psType = (TypeName $N_Int) }
        B_Flag = [ordered]@{ value = $B_Flag; psType = (TypeName $B_Flag) }
        N_Double = [ordered]@{ value = $N_Double; psType = (TypeName $N_Double) }
        P_Path = [ordered]@{ value = $P_Path; psType = (TypeName $P_Path) }
    }
    "jsonTypes" = [ordered]@{
        ItemsJson = [ordered]@{ rawString = $ItemsJson; parsedType = (TypeName $parsedItems); parsedValue = $parsedItems }
        ConfigJson = [ordered]@{ rawString = $ConfigJson; parsedType = (TypeName $parsedConfig); parsedValue = $parsedConfig }
    }
}

$report = [ordered]@{
    testId  = "AllParamTypesSample"
    outcome = "Pass"
    summary = "All parameter types validated and JSON parameters parsed successfully."
    details = $details
    metrics = [ordered]@{
        basicParamCount = 6
        jsonParamCount = 2
    }
}

Write-JsonFile (Join-Path $ArtifactsRoot "report.json") $report
Write-Output ""
Write-Output "[RESULT] Pass"
exit 0
