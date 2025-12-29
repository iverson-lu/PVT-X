param(
    [Parameter(Mandatory = $true)][int] $MinExpectedCount = 1,
    [Parameter(Mandatory = $false)][string] $NameContains
)

# Removes the surrounding quotes injected by the runner so comparisons use the raw string.
function Normalize-QuotedString {
    param(
        [string] $Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $Value
    }

    $trimmed = $Value.Trim()

    if ($trimmed.Length -ge 2 -and $trimmed.StartsWith("'") -and $trimmed.EndsWith("'")) {
        $inner = $trimmed.Substring(1, $trimmed.Length - 2)
        return $inner.Replace("''", "'")
    }

    if ($trimmed.Length -ge 2 -and $trimmed.StartsWith('"') -and $trimmed.EndsWith('"')) {
        $inner = $trimmed.Substring(1, $trimmed.Length - 2)
        return $inner.Replace('`"', '"')
    }

    return $trimmed
}

$NameContains = Normalize-QuotedString -Value $NameContains

$cameraNames = @()

try {
    $devices = Get-CimInstance -ClassName Win32_PnPEntity -Filter "PNPClass = 'Camera' OR PNPClass = 'Image'" -ErrorAction Stop
    $cameraNames = $devices | Select-Object -ExpandProperty Name -ErrorAction SilentlyContinue
} catch {
    $cameraNames = @()
}

if (-not $cameraNames -or $cameraNames.Count -eq 0) {
    try {
        $fallback = Get-CimInstance -ClassName Win32_PnPEntity -ErrorAction Stop | Where-Object { $_.Name -match "Camera" }
        $cameraNames = $fallback | Select-Object -ExpandProperty Name -ErrorAction SilentlyContinue
    } catch {
        $cameraNames = @()
    }
}

$cameraNames = $cameraNames | Where-Object { $_ } | Sort-Object -Unique
$detectedCount = $cameraNames.Count
$failures = @()

if ($MinExpectedCount -lt 0) {
    $failures += "MinExpectedCount must be >= 0."
} elseif ($detectedCount -lt $MinExpectedCount) {
    $failures += "Expected at least $MinExpectedCount camera(s) but found $detectedCount."
}

if (-not [string]::IsNullOrWhiteSpace($NameContains)) {
    $escaped = [regex]::Escape($NameContains)
    $matching = $cameraNames | Where-Object { $_ -match $escaped }
    if (@($matching).Count -eq 0) {
        $failures += "No detected camera names contain '$NameContains'."
    }
}

Write-Host "Detected cameras ($detectedCount):"
foreach ($name in $cameraNames) {
    Write-Host " - $name"
}

New-Item -ItemType Directory -Force -Path "artifacts" | Out-Null
$report = [ordered]@{
    MinexpectedCount = $MinExpectedCount
    nameContains = $NameContains
    detectedCount = $detectedCount
    names = $cameraNames
    failures = $failures
}
$report | ConvertTo-Json -Depth 4 | Set-Content -Path "artifacts/camera-check.json"

if ($failures.Count -gt 0) {
    Write-Host "Result: FAIL"
    exit 1
}

Write-Host "Result: PASS"
exit 0
