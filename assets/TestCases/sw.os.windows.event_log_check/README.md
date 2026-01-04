# EventViewerCheck Test Case

Checks the most recent N events in Event Viewer to ensure none have a specified severity level.

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| EventCount | int | No | 100 | Number of recent events to check |
| ProhibitedLevel | enum | No | Error | Event level to check for (Critical, Error, Warning, Information, Verbose) |
| LogName | string | No | System | Event log name to check (System, Application, Security, etc.) |

## Usage

This test case can be run standalone or as part of a suite.

### Standalone

```bash
dotnet run --project src/PcTest.Cli -- run --target testcase --id EventViewerCheck@1.0.0
```

### With Parameters

```bash
dotnet run --project src/PcTest.Cli -- run --target testcase --id EventViewerCheck@1.0.0 --inputs '{"EventCount": 50, "ProhibitedLevel": "Critical", "LogName": "Application"}'
```

## Event Levels

The test supports the following event levels (mapped to Windows Event Log severity):
- **Critical** (Level 1): System failures requiring immediate attention
- **Error** (Level 2): Significant problems that may require user action
- **Warning** (Level 3): Events that aren't necessarily significant but may indicate future problems
- **Information** (Level 4): Events that describe successful operations
- **Verbose** (Level 5): Detailed tracking information

## Artifacts

The test generates a JSON report at `artifacts/event-viewer-check.json` containing:
- Log name and parameters used
- Total events checked
- Count of events with prohibited level
- Details of prohibited level events (time, level, ID, provider, message preview)
- Any validation failures
