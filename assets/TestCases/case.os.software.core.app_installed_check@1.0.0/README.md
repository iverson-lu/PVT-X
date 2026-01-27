# Application Installed Check

## Purpose
Verifies that a specific application is installed on the Windows system by checking both the Windows registry and installed packages.

## Test Logic
1. Validates the `appName` parameter
2. Searches Windows registry uninstall keys (both 32-bit and 64-bit) for matching applications
3. Searches installed packages using PowerShell's Get-Package cmdlet
4. Supports both exact and partial name matching (case-insensitive)
5. Reports all matching applications with version and publisher information
6. Test passes if the application is found in at least one source
7. Produces structured `report.json` with detailed results

## Parameters
- **appName** (string, required): The name of the application to check. Supports partial match by default (case-insensitive).
- **exactMatch** (boolean, optional, default: false): If true, requires exact name match (case-insensitive). If false, allows partial match.
- **checkRegistry** (boolean, optional, default: true): If true, checks Windows registry for installed applications.
- **checkPackages** (boolean, optional, default: true): If true, checks installed packages using Get-Package.

## How to Run Manually
```powershell
# Check if Google Chrome is installed (partial match)
pwsh ./run.ps1 -appName "Chrome"

# Check if Microsoft Edge is installed (exact match)
pwsh ./run.ps1 -appName "Microsoft Edge" -exactMatch $true

# Check only in registry
pwsh ./run.ps1 -appName "Visual Studio Code" -checkPackages $false

# Check only in packages
pwsh ./run.ps1 -appName "Python" -checkRegistry $false
```

## Expected Result
- **Success (Exit 0)**: Application is found in registry and/or installed packages
- **Failure (Exit 1)**: Application is not found in any checked source
- **Error (Exit ≥2)**: Script error or invalid parameters

## Artifacts
- `artifacts/report.json`: Detailed execution report with:
  - List of matching applications from registry (name, version, publisher, install date)
  - List of matching packages (name, version, source, provider)
  - Match counts and installation status

## Use Cases
- Verify required software is installed before running tests
- Check software installation in automated test pipelines
- Validate software deployment
- Pre-flight checks for test suites requiring specific applications
- Software inventory verification

## Notes
- Search is case-insensitive by default
- Checks both 32-bit and 64-bit registry locations (HKLM and HKCU)
- Partial match allows flexible searching (e.g., "Chrome" matches "Google Chrome")
- Get-Package may require PackageManagement module (included in PowerShell 5.1+)
- Registry search is more comprehensive for MSI-installed applications
- Package search covers applications installed via package managers (Chocolatey, NuGet, etc.)
