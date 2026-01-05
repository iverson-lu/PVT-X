# CPU Static Requirements - Frequency and Name

## Purpose
Validate CPU static requirements for machine gating:
- CPU frequency must be **>= a configured minimum (GHz)**
- CPU name must **contain a configured substring**
- Optional static capability checks (cores, threads, cache, x64, virtualization)

## Test Logic
1. Enumerate CPU information using `Win32_Processor` (CIM/WMI).
2. Validate CPU frequency against `MinFrequencyGHz` using `FrequencySource` (`MaxClockSpeed` or `CurrentClockSpeed`).
3. Validate CPU name contains `CpuNameContains` (case-sensitive optional).
4. Optionally validate static capabilities (per-socket):
   - Minimum cores / logical processors
   - Minimum L2 / L3 cache size
   - x64 (AddressWidth >= 64)
   - Virtualization extensions and SLAT support
   - Hyper-Threading heuristic (logical processors > cores)
5. Write `artifacts/report.json` and exit with PASS/FAIL codes.

## Parameters
- `MinFrequencyGHz` (double, required): Minimum CPU frequency in GHz.
- `CpuNameContains` (string, required): Substring required in `Win32_Processor.Name`.
- `FrequencySource` (enum, optional): `MaxClockSpeed` (default) or `CurrentClockSpeed`.
- `CpuMatchMode` (enum, optional): `all` (default) or `any` for multi-CPU systems.
- `NameCaseSensitive` (bool, optional): If true, match is case-sensitive.
- `MinCores` (int, optional): Minimum per-socket cores; 0 disables.
- `MinLogicalProcessors` (int, optional): Minimum per-socket logical processors; 0 disables.
- `RequireX64` (bool, optional): Require 64-bit CPU; default true.
- `MinL3CacheMB` (int, optional): Minimum per-socket L3 cache in MB; 0 disables.
- `MinL2CacheMB` (int, optional): Minimum per-socket L2 cache in MB; 0 disables.
- `RequireVMMonitorModeExtensions` (bool, optional): Require virtualization extensions.
- `RequireSLAT` (bool, optional): Require SLAT support.
- `RequireHyperThreading` (bool, optional): Require logical processors > cores (heuristic).

## How to Run Manually
```powershell
# Basic gating: 3.5 GHz and name must include "Intel"
pwsh .\run.ps1 -MinFrequencyGHz 3.5 -CpuNameContains "Intel"

# Use CurrentClockSpeed (can vary with power plan / idle state)
pwsh .\run.ps1 -MinFrequencyGHz 3.5 -CpuNameContains "Intel" -FrequencySource CurrentClockSpeed

# Multi-socket rule: at least one CPU satisfies the gates
pwsh .\run.ps1 -MinFrequencyGHz 3.5 -CpuNameContains "Intel" -CpuMatchMode any

# Add static capability gates
pwsh .\run.ps1 -MinFrequencyGHz 3.5 -CpuNameContains "Intel" -MinCores 8 -MinLogicalProcessors 16 -MinL3CacheMB 16 -RequireSLAT $true
```

## Expected Result
- **PASS (exit code 0)**: All enabled requirements are satisfied according to `CpuMatchMode`.
- **FAIL (exit code 1)**: One or more enabled requirements are not met.
- **ERROR (exit code >= 2)**: Script or environment error (e.g., unable to query CPU information).
