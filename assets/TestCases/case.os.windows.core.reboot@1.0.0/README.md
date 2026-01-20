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
- **reason** (string, default: "Reboot requested by test case"): Human-readable reason message logged with the reboot request for tracking and debugging.
- **verifyReboot** (boolean, default: false): When true, performs additional validation that reboot occurred correctly (future implementation - currently placeholder only).

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
- **Phase 0**: Test outputs reboot request details and exits with code 0
- **System reboots** after the specified delay
- **Phase 1**: Test resumes automatically, logs successful resume, and exits with code 0
- Overall test result is **Pass** when both phases complete successfully
- If `verifyReboot` is true, a placeholder message is logged (actual verification to be implemented)
