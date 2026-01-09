# GPU Status Check

## Purpose
Validate that the system can enumerate at least one GPU with a healthy driver state, and optionally capture current GPU utilization and memory usage telemetry.

## Test Logic
- Enumerate GPUs using Windows CIM (`Win32_VideoController`) and capture basic device/driver fields.
- Validate device status (e.g., `Status == OK`) and that key fields (name/driver version) are present.
- Optionally sample Windows GPU performance counters for a short window to summarize utilization and memory usage.
- Optionally query vendor tools when available (e.g., `nvidia-smi`) and include parsed telemetry in the report.
- Apply optional thresholds (utilization and dedicated memory usage) when provided.

## Parameters
- **N_SampleDurationSec**: Total sampling window in seconds for performance counters.
- **N_SampleIntervalSec**: Sampling interval in seconds for performance counters.
- **B_CollectPerfCounters**: If `true`, attempt to collect Windows GPU performance counters.
- **B_RequirePerfCounters**: If `true`, missing/unreadable performance counters cause a FAIL; otherwise the perf step is skipped.
- **B_UseVendorTools**: If `true`, attempt vendor tool telemetry (only when the tool is present).
- **B_SaveRawOutputs**: If `true`, save raw outputs to `artifacts/gpu_status.txt`.
- **N_MaxUtilizationPercent**: Optional fail threshold on max sampled utilization (%). Use `100` to effectively disable.
- **N_MaxDedicatedMemUsagePercent**: Optional fail threshold on max sampled dedicated memory usage (%). Use `100` to effectively disable.

## How to Run Manually
```powershell
pwsh ./run.ps1 `
  -N_SampleDurationSec 5 `
  -N_SampleIntervalSec 1 `
  -B_CollectPerfCounters:$true `
  -B_RequirePerfCounters:$false `
  -B_UseVendorTools:$true `
  -B_SaveRawOutputs:$false `
  -N_MaxUtilizationPercent 100 `
  -N_MaxDedicatedMemUsagePercent 100
echo $LASTEXITCODE
```

## Expected Result
- **PASS (0)**: At least one GPU is enumerated and validated as healthy; optional telemetry collection succeeds or is skipped (when not required) with results recorded in `artifacts/report.json`.
- **FAIL (1)**: No GPU is found, a GPU is unhealthy, required telemetry is missing, or thresholds are exceeded.
- **ERROR (>=2)**: Script/environment error (unexpected exception, permissions, missing PowerShell 7+).
