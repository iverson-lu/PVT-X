# Reboot Resume Test

## Purpose
Validates that the system can successfully request a reboot and resume test execution after the reboot completes.

## Test Logic
- **Phase 0**: Writes a reboot request to the control directory with configurable delay and reason, then exits cleanly
- **Phase 1**: Resumes after system reboot and validates successful continuation
- Uses the PVT-X reboot/resume protocol via `PVTX_CONTROL_DIR` and `PVTX_PHASE` environment variables
- Pass/fail is determined by successful completion of both phases

## Parameters
- **delaySec** (int, default: 10): Delay in seconds before initiating the system reboot. Allows time for cleanup and graceful shutdown.
- **reason** (string, default: "Reboot requested by test case"): Human-readable reason message logged with the reboot request. A unique RunId is automatically appended for tracking.
- **verifyReboot** (boolean, default: false): When true, validates reboot completion by checking Windows Event Log for:
  - **Event ID 1074**: Confirms reboot was initiated with the correct RunId
  - **Event ID 6005**: Confirms EventLog service started (boot completed successfully)

## How to Run Manually
To run this test case directly with PowerShell:

```powershell
# Run with defaults
pwsh ./run.ps1

# Run with custom parameters
pwsh ./run.ps1 -delaySec 15 -reason "Custom reboot test" -verifyReboot $true
```

**Note**: Manual execution requires the PVT-X Runner environment variables (`PVTX_CONTROL_DIR`, `PVTX_PHASE`, etc.) to be set properly. Direct execution outside the Runner will fail during Phase 0 when attempting to write the reboot request.

## Expected Result
- **Phase 0**: Test generates unique RunId, writes reboot request with RunId embedded in reason, and exits with code 0
- **System reboots** after the specified delay with reason recorded to Windows Event Log
- **Phase 1**: Test resumes automatically and:
  - If `verifyReboot=false`: Confirms successful resume and exits with code 0
  - If `verifyReboot=true`: Queries Windows Event Log to verify:
    - Event ID 1074 exists with matching RunId (reboot initiated correctly)
    - Event ID 6005 exists after boot time (system fully started)
  - Exits with code 0 if verification passes, code 2 if verification fails
- Overall test result is **Pass** when both phases complete successfully

## Verification Details
When `verifyReboot=true`, the test performs forensic validation:

1. **Pre-Reboot (Phase 0)**:
   - Generates unique RunId from `PVTX_RUN_ID` environment variable
   - Embeds RunId in reboot reason: `<reason> [RunId: <guid>]`
   - Reason is passed to `shutdown.exe /c` and recorded in Windows Event Log

2. **Post-Reboot (Phase 1)**:
   - Retrieves system boot time from `Win32_OperatingSystem.LastBootUpTime`
   - Searches System Event Log for Event ID 1074 (reboot initiation) containing RunId
   - Searches System Event Log for Event ID 6005 (EventLog service started) after boot time
   - Fails if either event is missing or RunId doesn't match

3. **Why Two Events?**
   - **Event 1074**: Proves reboot was triggered by *this specific test* (not manual/other process)
   - **Event 6005**: Proves system *completed boot successfully* (not stuck/crashed)

This dual-check ensures both reboot initiation and completion are validated end-to-end.
