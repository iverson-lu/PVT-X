# PC Test System MVP

This repository provides a runnable MVP for the PC Test System on Windows 11 using .NET 10 and PowerShell 7+.

## Prerequisites

- .NET 10 SDK
- PowerShell 7+ (pwsh.exe)

## Quick Start

The CLI requires explicit roots for test cases, suites, plans, and runs.

### Discover

```powershell
./src/PcTest.Cli/bin/Debug/net10.0/PcTest.Cli.exe discover --caseRoot ./assets/TestCases --suiteRoot ./assets/TestSuites --planRoot ./assets/TestPlans --runsRoot ./Runs
```

### Run a standalone TestCase

```powershell
./src/PcTest.Cli/bin/Debug/net10.0/PcTest.Cli.exe run testCase CpuStress@2.0.0 --caseRoot ./assets/TestCases --suiteRoot ./assets/TestSuites --planRoot ./assets/TestPlans --runsRoot ./Runs
```

### Run a Suite

```powershell
./src/PcTest.Cli/bin/Debug/net10.0/PcTest.Cli.exe run suite ThermalSuite@1.0.0 --caseRoot ./assets/TestCases --suiteRoot ./assets/TestSuites --planRoot ./assets/TestPlans --runsRoot ./Runs
```

### Run a Plan

```powershell
./src/PcTest.Cli/bin/Debug/net10.0/PcTest.Cli.exe run plan SystemValidation@1.2.0 --caseRoot ./assets/TestCases --suiteRoot ./assets/TestSuites --planRoot ./assets/TestPlans --runsRoot ./Runs
```

## Runs Folder Layout

All output artifacts are written under the Runs root.

- Standalone TestCase runs produce only `{RunId}/`.
- Suite and Plan runs produce `{GroupRunId}/` plus child TestCase run folders.
- `index.jsonl` is written only by the Engine and contains one line per run.

Example structure:

```
Runs/
  index.jsonl
  R-<caseRunId>/
    manifest.json
    params.json
    stdout.log
    stderr.log
    events.jsonl
    env.json
    result.json
  G-<suiteRunId>/
    manifest.json
    controls.json
    environment.json
    runRequest.json
    children.jsonl
    events.jsonl
    result.json
```

## Tests

Run all automated tests:

```powershell
dotnet test
```

## MVP Scope

Implemented:
- TestCase/Suite/Plan discovery
- Suite-ordered execution pipeline and Plan orchestration
- Inputs resolution (defaults, overrides, EnvRef)
- PowerShell parameter protocol and runner status mapping
- Run folder layout, manifest/result snapshots, redaction, and index.jsonl

Not implemented:
- UI (WPF) and distributed execution (explicitly out of scope for MVP)
