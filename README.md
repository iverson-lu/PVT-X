# PC Test System (MVP)

## Requirements
- .NET 10 SDK
- PowerShell 7+

## Build
```bash
 dotnet build pc-test-system.sln
```

## Discover
```bash
 dotnet run --project src/PcTest.Cli -- discover --cases assets/TestCases --suites assets/TestSuites --plans assets/TestPlans --runs Runs
```

## Run a standalone TestCase
```bash
 dotnet run --project src/PcTest.Cli -- run testCase --id CpuStress@1.0.0 --cases assets/TestCases --suites assets/TestSuites --plans assets/TestPlans --runs Runs
```

## Run a Suite
```bash
 dotnet run --project src/PcTest.Cli -- run suite --id ThermalSuite@1.0.0 --cases assets/TestCases --suites assets/TestSuites --plans assets/TestPlans --runs Runs
```

## Run a Plan
```bash
 dotnet run --project src/PcTest.Cli -- run plan --id SystemValidation@1.0.0 --cases assets/TestCases --suites assets/TestSuites --plans assets/TestPlans --runs Runs
```

## Runs directory layout
- `Runs/index.jsonl` is the engine-owned run index.
- Each TestCase run creates `Runs/{RunId}/` with:
  - `manifest.json`, `params.json`, `env.json`, `stdout.txt`, `stderr.txt`, `result.json`, optional `events.jsonl`
- Each Suite/Plan run creates `Runs/{GroupRunId}/` with:
  - `manifest.json`, `children.jsonl`, `result.json`, optional `controls.json`, `environment.json`, `runRequest.json`

Use `Runs/{RunId}/result.json` for per-TestCase results and `Runs/index.jsonl` for cross-run history.

## Tests
```bash
 dotnet test pc-test-system.sln
```
