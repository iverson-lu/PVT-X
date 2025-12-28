# PC Test System (MVP)

## Prerequisites
- .NET 10 SDK
- PowerShell 7+

## Quick Start

### Discover
```bash
pwsh -Command "dotnet run --project src/PcTest.Cli -- discover"
```

### Run a standalone TestCase
```bash
pwsh -Command "dotnet run --project src/PcTest.Cli -- run testcase CpuStress@1.0.0"
```

### Run a Suite
```bash
pwsh -Command "dotnet run --project src/PcTest.Cli -- run suite ThermalSuite@1.0.0"
```

### Run a Plan
```bash
pwsh -Command "dotnet run --project src/PcTest.Cli -- run plan SystemValidation@1.0.0"
```

## Runs Folder Structure
Runs are written under `Runs/`.

- `Runs/index.jsonl` (Engine-only writer)
- `{RunId}/` (TestCase run, Runner-owned)
  - `manifest.json`
  - `params.json`
  - `stdout.log`
  - `stderr.log`
  - `events.jsonl` (optional)
  - `env.json`
  - `result.json`
- `{GroupRunId}/` (Suite/Plan run, Engine-owned)
  - `manifest.json`
  - `controls.json`
  - `environment.json`
  - `runRequest.json`
  - `children.jsonl`
  - `events.jsonl` (optional)
  - `result.json`

Use `result.json` for authoritative outcomes and `index.jsonl` to correlate runs.

## Tests
```bash
dotnet test
```
