# CPU Parameters Verify

## Purpose
Verify that key CPU parameters meet expected baselines (cores/threads, frequency, x64 capability, and virtualization properties) using `Win32_Processor`.

## Test Logic
- Collect CPU information via `Get-CimInstance Win32_Processor` (supports multi-socket systems)
- Compute summary values:
  - Socket count, total physical cores, total logical processors
  - Minimum reported `MaxClockSpeed` / `CurrentClockSpeed` across sockets
  - Address width (x64)
  - Virtualization-related flags (VM Monitor Mode, SLAT, firmware virtualization enabled)
- Validate collected values against provided parameters
- Write structured `artifacts/report.json`
- Optionally write `artifacts/cpu_raw.json` when `B_SaveRaw=true`

## Parameters
- **MinPhysicalCores** (int, optional, default: 2): Minimum total physical cores across all sockets.
- **MinLogicalProcessors** (int, optional, default: 2): Minimum total logical processors (threads) across all sockets.
- **MinMaxClockMHz** (int, optional, default: 1000): Minimum reported `MaxClockSpeed` in MHz. Set to `0` to skip.
- **RequireX64** (boolean, optional, default: true): Require CPU `AddressWidth` to be 64-bit on all sockets.
- **E_Virtualization** (enum, optional, default: "supported"): Virtualization requirement:
  - `any`: Skip virtualization validation
  - `supported`: Require `VMMonitorModeExtensions=true` on all sockets
  - `enabled`: Require `VirtualizationFirmwareEnabled=true` on all sockets
- **ExpectedVendor** (string, optional, default: ""): Expected vendor substring (e.g., "GenuineIntel" or "AuthenticAMD"). Empty = skip.
- **ExpectedNameRegex** (string, optional, default: ""): Regex to match `Win32_Processor.Name` for all sockets. Empty = skip.
- **MaxSockets** (int, optional, default: 0): Maximum allowed socket count. `0` = skip.
- **B_SaveRaw** (boolean, optional, default: false): If true, also write `artifacts/cpu_raw.json` with selected raw `Win32_Processor` fields.

## How to Run Manually
From the case directory:
```powershell
pwsh -File .\run.ps1 `
  -MinPhysicalCores 4 `
  -MinLogicalProcessors 8 `
  -MinMaxClockMHz 2500 `
  -RequireX64 $true `
  -E_Virtualization supported `
  -ExpectedVendor "GenuineIntel" `
  -ExpectedNameRegex "Intel\(R\).*" `
  -MaxSockets 2 `
  -B_SaveRaw $false
```

## Expected Result
- **PASS** (exit code `0`) when all enabled validations meet expectations.
- **FAIL** (exit code `1`) when any validation does not meet expectations (reason is included in stdout and `report.json`).
- **SCRIPT ERROR** (exit code `>= 2`) when the script cannot collect CPU info or encounters an unexpected error.
