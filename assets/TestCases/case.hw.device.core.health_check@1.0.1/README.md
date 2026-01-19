# Device Manager Status Check

## Purpose
Validates that all devices in Windows Device Manager are in a healthy state, failing if any device exhibits specified abnormal statuses (unless allowlisted).

## Test Flow

```mermaid
flowchart TD
    Start([Start Test]) --> ParseParams[Parse Parameters<br/>FailOnStatus, Allowlist, AlwaysCollectDeviceList]
    ParseParams --> Step1[Step 1: Enumerate Devices<br/>Get-PnpDevice]
    
    Step1 --> ForEach1{For Each Device}
    ForEach1 --> GetProps[Get Status, ProblemCode,<br/>HardwareIDs, Class, InstanceId]
    GetProps --> Normalize[Normalize Status<br/>ProblemCode 22 → disabled<br/>ProblemCode 28 → drivermissing<br/>Status OK/Error/etc → normalized]
    Normalize --> Store1[Store Device Info]
    Store1 --> ForEach1
    
    ForEach1 -->|All Done| Step2[Step 2: Evaluate Status]
    
    Step2 --> ForEach2{For Each Device}
    ForEach2 --> CheckFail{Status in<br/>FailOnStatus?}
    
    CheckFail -->|No| ForEach2
    CheckFail -->|Yes| CheckAllow{Matches<br/>Allowlist?}
    
    CheckAllow -->|Yes| AddAllow[Add to allowlistedDevices]
    CheckAllow -->|No| AddFail[Add to failedDevices]
    
    AddAllow --> ForEach2
    AddFail --> ForEach2
    
    ForEach2 -->|All Done| EvalResult{failedDevices<br/>Count == 0?}
    
    EvalResult -->|Yes| Pass[Test PASS<br/>Exit 0]
    EvalResult -->|No| Fail[Test FAIL<br/>Exit 1]
    
    Pass --> Output[Generate report.json<br/>Console Output]
    Fail --> Output
    
    Output --> End([End])
    
    style Start fill:#e1f5e1
    style End fill:#e1f5e1
    style Pass fill:#90EE90
    style Fail fill:#FFB6C1
    style Step1 fill:#E6F3FF
    style Step2 fill:#E6F3FF
```

## Test Logic
- **Step 1 – Enumerate Devices:** Uses `Get-PnpDevice` to enumerate all PnP devices from Device Manager.
- **Step 2 – Map Status:** Maps each device's native status to normalized categories: OK, Error, Disabled, Unknown, NotStarted, DriverMissing.
- **Step 3 – Apply Allowlist:** Filters out devices matching any allowlist rule (AND logic within rule, OR logic across rules).
- **Step 4 – Evaluate:** If any non-allowlisted device matches `FailOnStatus`, test fails; otherwise passes.
- **Evidence:** Failed devices are always reported. Full device list is reported on failure or when `AlwaysCollectDeviceList=true`.

## Parameters
- `FailOnStatus` (json array, optional, default `["Error"]`): Which statuses trigger failure. Options: Error, Disabled, Unknown, NotStarted, DriverMissing.
- `Allowlist` (json array, optional, default `[]`): Rules to ignore known issues. Each rule is an object with optional fields: `device_name`, `hardware_id`, `class`, `status`, `note`.
- `AlwaysCollectDeviceList` (boolean, optional, default `false`): Force full device snapshot even on pass.

## How to Run Manually

```powershell
pwsh ./run.ps1 -FailOnStatus '["Error", "Disabled"]' -Allowlist '[]' -AlwaysCollectDeviceList:$false
```

## Expected Result
- **Pass:** No devices match `FailOnStatus`, or all matching devices are allowlisted.
- **Fail:** At least one device matches `FailOnStatus` and is not allowlisted. Console and report.json will list failed devices with details.

```
==================================================
TEST: case.hw.devmgr.core.status_check  RESULT: PASS  EXIT: 0
UTC:  2026-01-19T12:00:00Z
--------------------------------------------------
[1/2] enumerate_devices ................... PASS
[2/2] evaluate_device_status .............. PASS
--------------------------------------------------
      total_devices=142 failed_devices=0 allowlisted=0
--------------------------------------------------
STEPS: total=2  pass=2  fail=0  skip=0
MACHINE: overall=PASS exit_code=0
==================================================
```
