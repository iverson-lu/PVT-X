# PC Test System MVP

## Prerequisites
- .NET 10 SDK
- PowerShell 7+

## Build
```bash
dotnet build pc-test-system.sln
```

## Discover
```bash
dotnet run --project src/PcTest.Cli/PcTest.Cli.csproj -- discover
```

## Run a standalone TestCase
```bash
dotnet run --project src/PcTest.Cli/PcTest.Cli.csproj -- run testCase CpuStress@1.0.0
```

## Run a Suite
```bash
dotnet run --project src/PcTest.Cli/PcTest.Cli.csproj -- run suite ThermalSuite@1.0.0
```

## Run a Plan
```bash
dotnet run --project src/PcTest.Cli/PcTest.Cli.csproj -- run plan SystemValidation@1.0.0
```

## Runs layout
Runs are written under `Runs/`:
- Standalone TestCase: `Runs/{RunId}/` with `manifest.json`, `params.json`, `stdout.log`, `stderr.log`, `events.jsonl`, `env.json`, `result.json`.
- Suite/Plan: `Runs/{GroupRunId}/` with `manifest.json`, `controls.json` (suite only), `environment.json`, `runRequest.json` (if provided), `children.jsonl`, `events.jsonl` (optional), `result.json`.
- `Runs/index.jsonl` aggregates all runs (Engine-only writer).

Inspect `result.json` for final status and `index.jsonl` for a full run timeline.

## Tests
```bash
dotnet test pc-test-system.sln
```
