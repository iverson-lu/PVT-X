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
dotnet run --project src/PcTest.Cli -- discover --casesRoot assets/TestCases --suitesRoot assets/TestSuites --plansRoot assets/TestPlans
```

## Run a standalone TestCase
```bash
dotnet run --project src/PcTest.Cli -- run --target testcase --id CpuStress@1.0.0 --casesRoot assets/TestCases --suitesRoot assets/TestSuites --plansRoot assets/TestPlans --runsRoot Runs
```

## Run a Suite
```bash
dotnet run --project src/PcTest.Cli -- run --target suite --id ThermalSuite@1.0.0 --casesRoot assets/TestCases --suitesRoot assets/TestSuites --plansRoot assets/TestPlans --runsRoot Runs
```

## Run a Plan
```bash
dotnet run --project src/PcTest.Cli -- run --target plan --id SystemValidation@1.0.0 --casesRoot assets/TestCases --suitesRoot assets/TestSuites --plansRoot assets/TestPlans --runsRoot Runs
```

## Runs folder layout
- Standalone TestCase runs write a single `{RunId}/` folder under `Runs/` containing:
  - `manifest.json`, `params.json`, `env.json`, `stdout.txt`, `stderr.txt`, `events.jsonl`, `result.json`
- Suite/Plan runs create a `{GroupRunId}/` folder under `Runs/` containing:
  - `manifest.json`, `environment.json`, `runRequest.json`, `children.json`, `events.jsonl`, `result.json`, `index.jsonl`
- TestCase runs triggered by suites/plans are written to their own `{RunId}/` folders in `Runs/`.

Use `result.json` for authoritative status and `index.jsonl` for suite/plan rollups.

## Tests
```bash
dotnet test pc-test-system.sln
```
