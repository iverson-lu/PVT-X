# Memory Inventory and Requirement Check

## Purpose
Enumerate installed physical memory (RAM) modules and optionally validate platform requirements such as total capacity, module count, DDR type, and configured clock speed.

## Test Logic
- Uses `Get-CimInstance Win32_PhysicalMemory` to enumerate all DIMMs.
- Builds a module inventory with key fields:
  - BankLabel, DeviceLocator
  - Capacity (rounded to GB)
  - Manufacturer, PartNumber
  - Speed, ConfiguredClockSpeed
  - DDR type mapped from `SMBIOSMemoryType` (26=DDR4, 34=DDR5, otherwise keeps the numeric value)
- Computes summary values (module count, total GB, DDR types present, minimum configured clock).
- Applies optional requirement checks based on parameters.
- Produces `artifacts/report.json` and, if enabled, `artifacts/memory_modules.json`.

## Parameters
- **N_MinTotalGB** (int, optional, default: 0): Minimum required total physical memory in GB. `0` disables the check.
- **N_MinModuleCount** (int, optional, default: 1): Minimum required number of memory modules. `0` disables the check.
- **E_ExpectedDdrType** (enum: any|ddr4|ddr5, optional, default: any): Expected DDR type for all modules.
- **N_MinConfiguredClockMHz** (int, optional, default: 0): Minimum required `ConfiguredClockSpeed` (MHz) for each module. `0` disables the check.
- **B_AllowMixedDdrTypes** (boolean, optional, default: false): If `false`, fails when multiple DDR types are present.
- **B_ExportModulesJson** (boolean, optional, default: true): If `true`, exports `artifacts/memory_modules.json`.

## How to Run Manually
From the case directory (PowerShell 7+):
```powershell
pwsh -File .\run.ps1
```

Example with requirements:
```powershell
pwsh -File .\run.ps1 -N_MinTotalGB 16 -N_MinModuleCount 2 -E_ExpectedDdrType ddr5 -N_MinConfiguredClockMHz 3200
```

## Expected Result
- **PASS (exit code 0)** when enumeration succeeds and all enabled requirement checks are met.
- **FAIL (exit code 1)** when enumeration succeeds but one or more enabled checks fail.
- **ERROR (exit code >= 2)** when the script cannot enumerate memory modules or encounters a runtime error.

Artifacts:
- `artifacts/report.json` is always created.
- `artifacts/memory_modules.json` is created when `B_ExportModulesJson=true`.
