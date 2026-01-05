# Power State Verification

## Purpose
Verify that the system's current power state matches expectations:
- Power source: AC (plugged in) vs Battery (unplugged/on battery)
- Active power scheme category: Performance vs Balanced vs Power Saver

## Test Logic
- Reads power source state using WMI:
  - First attempts `root\wmi:BatteryStatus` (PowerOnline if available)
  - Falls back to `Win32_Battery` (BatteryStatus code inference) when needed
- Reads the active power scheme using `powercfg /getactivescheme`
  - Maps well-known scheme GUIDs to categories (Balanced/Performance/Saver)
  - Falls back to name matching when GUID is not recognized
- Compares observed values to expected parameters (if not set to `any`)
- Writes `artifacts/report.json` and returns exit code:
  - 0 = Pass
  - 1 = Fail (validation mismatch)
  - 2+ = Script / environment error

## Parameters
- **E_ExpectedPowerSource** (enum, optional, default: `any`): `any | ac | battery`
- **E_ExpectedPowerScheme** (enum, optional, default: `any`): `any | performance | balanced | saver`
- **B_RequireBattery** (bool, optional, default: `false`): Fail if power-source telemetry is unavailable.
- **B_FailOnUnknownScheme** (bool, optional, default: `false`): Fail if active power scheme cannot be mapped.

## How to Run Manually
```powershell
# Expect plugged-in and Balanced
pwsh .\run.ps1 -E_ExpectedPowerSource ac -E_ExpectedPowerScheme balanced
echo $LASTEXITCODE

# Only check scheme category
pwsh .\run.ps1 -E_ExpectedPowerScheme performance
echo $LASTEXITCODE

# Only check power source
pwsh .\run.ps1 -E_ExpectedPowerSource battery
echo $LASTEXITCODE
```

## Expected Result
- **PASS** when the observed power source / scheme matches expected parameters (or expectations are `any`).
- **FAIL** when a specified expectation is not met, or when `B_RequireBattery` / `B_FailOnUnknownScheme` triggers failure.
- `artifacts/report.json` is created on every run and contains the observed and expected values.
