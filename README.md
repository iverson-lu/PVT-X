# PC Test System MVP

## Requirements
- .NET 10 SDK
- PowerShell 7+

## Quick Start

### Discover
```bash
pctest discover --caseRoot assets/TestCases --suiteRoot assets/TestSuites --planRoot assets/TestPlans
```

### Run standalone TestCase
```bash
pctest run-testcase --id CpuStress@2.0.0 --caseRoot assets/TestCases --runsRoot Runs
```

### Run Suite
```bash
pctest run-suite --id ThermalSuite@1.0.0 --caseRoot assets/TestCases --suiteRoot assets/TestSuites --runsRoot Runs
```

### Run Plan
```bash
pctest run-plan --id SystemValidation@1.2.0 --caseRoot assets/TestCases --suiteRoot assets/TestSuites --planRoot assets/TestPlans --runsRoot Runs
```

## Runs Folder Layout

```
Runs/
  index.jsonl
  {RunId}/
    manifest.json
    params.json
    stdout.log
    stderr.log
    events.jsonl
    env.json
    result.json
  {GroupRunId}/
    manifest.json
    controls.json
    environment.json
    runRequest.json
    children.jsonl
    events.jsonl
    result.json
```

- Standalone TestCase runs only create `{RunId}` and omit suite/plan metadata.
- Suite/Plan runs create `{GroupRunId}` and child Case runs.
- `result.json` is authoritative for each run.

## Running Tests

```bash
dotnet test
```
