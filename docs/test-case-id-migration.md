# Test Case ID Migration Summary

This document records the migration from old 3-level IDs to new 4-level IDs following the `case.<domain>.<subsystem>.<feature>.<action>` schema.

## Migration Date
January 4, 2026

## ID Mapping

| Old ID | New ID | Directory Name |
|--------|--------|----------------|
| `hw.bios.version_check` | `case.hw.bios.core.version_check` | case.hw.bios.core.version_check |
| `hw.camera.detect_devices` | `case.hw.camera.core.detect` | case.hw.camera.core.detect |
| `hw.devices.health_check` | `case.hw.device.core.health_check` | case.hw.device.core.health_check |
| `hw.memory.size_check` | `case.hw.memory.core.size_check` | case.hw.memory.core.size_check |
| `sw.network.ping_connectivity` | `case.network.core.connectivity.ping` | case.network.core.connectivity.ping |
| `sw.os.windows.system_info_verify` | `case.os.windows.core.system_info_verify` | case.os.windows.core.system_info_verify |
| `sw.os.windows.timezone_verify` | `case.os.windows.core.timezone_verify` | case.os.windows.core.timezone_verify |
| `sw.os.windows.event_log_check` | `case.os.windows.core.event_log_check` | case.os.windows.core.event_log_check |
| `sw.os.windows.update_history_verify` | `case.os.update.core.history_verify` | case.os.update.core.history_verify |

## Changes Made

### 1. Directory Renaming
All test case directories have been renamed to match their new IDs.

### 2. Manifest Updates
The `id` field in all `test.manifest.json` files has been updated to the new 4-level schema.

### 3. README Restructuring
All `README.md` files have been restructured to follow the mandatory template:
- **Purpose**: One sentence describing what the test validates
- **Test Logic**: Core test approach and pass/fail decision logic
- **Parameters**: Semantic explanation of manifest parameters
- **How to Run Manually**: Example command with `pwsh ./run.ps1`
- **Expected Result**: Expected behavior on success and failure

### 4. Suite Reference Updates
The following suite manifests have been updated to reference new case IDs:
- `suite.sw.os.sanity_check/suite.manifest.json`
- `suite.sys.full_test/suite.manifest.json`
- `suite.sw.os.network.ping_test/suite.manifest.json`

## 4-Level ID Schema Structure

```
case.<domain>.<subsystem>.<feature>.<action>
```

### Breakdown by Domain

#### Hardware (hw)
- `case.hw.bios.core.version_check` - BIOS subsystem
- `case.hw.camera.core.detect` - Camera subsystem
- `case.hw.device.core.health_check` - Device subsystem
- `case.hw.memory.core.size_check` - Memory subsystem

#### Operating System (os)
- `case.os.windows.core.system_info_verify` - Windows subsystem
- `case.os.windows.core.timezone_verify` - Windows subsystem
- `case.os.windows.core.event_log_check` - Windows subsystem
- `case.os.update.core.history_verify` - Update subsystem

#### Network (network)
- `case.network.core.connectivity.ping` - Network domain (using "core" as subsystem when no specific subsystem applies)

## Notes

- All cases use `core` as the feature level when no further subdivision is needed
- Directory names now match IDs exactly for consistency
- Version information (`@1.0.0`) is appended during execution but not part of the base ID
- Template cases (not production) remain unchanged with their original naming
