param(
    [Parameter(Mandatory = $false)][string] $VersionContains
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

$VersionContains = Normalize-QuotedString -Value $VersionContains

$biosInfo = $null
$biosVersion = ""
$biosManufacturer = ""
$biosReleaseDate = ""
$failures = @()

try {
    $bios = Get-CimInstance -ClassName Win32_BIOS -ErrorAction Stop
    
    if ($bios) {
        $biosVersion = $bios.SMBIOSBIOSVersion
        $biosManufacturer = $bios.Manufacturer
        $biosReleaseDate = $bios.ReleaseDate
        
        $biosInfo = [ordered]@{
            Version = $biosVersion
            Manufacturer = $biosManufacturer
            ReleaseDate = if ($biosReleaseDate) { $biosReleaseDate.ToString("yyyy-MM-dd") } else { "" }
            SerialNumber = $bios.SerialNumber
        }
    } else {
        $failures += "Unable to retrieve BIOS information."
    }
} catch {
    $failures += "Error querying BIOS information: $($_.Exception.Message)"
    Write-Host "Error: $($_.Exception.Message)"
}

Write-Host "BIOS Information:"
Write-Host "  Manufacturer: $biosManufacturer"
Write-Host "  Version: $biosVersion"
Write-Host "  Release Date: $($biosInfo.ReleaseDate)"
Write-Host ""

if ([string]::IsNullOrWhiteSpace($biosVersion)) {
    $failures += "BIOS version could not be determined."
} elseif (-not [string]::IsNullOrWhiteSpace($VersionContains)) {
    $escaped = [regex]::Escape($VersionContains)
    if ($biosVersion -notmatch $escaped) {
        $failures += "BIOS version '$biosVersion' does not contain '$VersionContains'."
    } else {
        Write-Host "✓ BIOS version contains required string: '$VersionContains'"
    }
}

New-Item -ItemType Directory -Force -Path "artifacts" | Out-Null
$report = [ordered]@{
    versionContains = $VersionContains
    biosInfo = $biosInfo
    failures = $failures
}
$report | ConvertTo-Json -Depth 4 | Set-Content -Path "artifacts/bios-check.json"

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "Result: FAIL"
    foreach ($failure in $failures) {
        Write-Host "  ✗ $failure"
    }
    exit 1
}

Write-Host ""
Write-Host "Result: PASS"
Write-Host "  ✓ BIOS version validated successfully."
exit 0
