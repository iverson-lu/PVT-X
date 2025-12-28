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
dotnet run --project src/PcTest.Cli/PcTest.Cli.csproj -- discover --testCases assets/TestCases --testSuites assets/TestSuites --testPlans assets/TestPlans
```

## Run a standalone TestCase
```bash
dotnet run --project src/PcTest.Cli/PcTest.Cli.csproj -- run testcase --id CpuStress@1.0.0 --testCases assets/TestCases --testSuites assets/TestSuites --testPlans assets/TestPlans --runs Runs --input=DurationSec=1 --input=ExitCode=0
```

## Run a Suite
```bash
dotnet run --project src/PcTest.Cli/PcTest.Cli.csproj -- run suite --id ThermalSuite@1.0.0 --testCases assets/TestCases --testSuites assets/TestSuites --testPlans assets/TestPlans --runs Runs
```

## Run a Plan
```bash
dotnet run --project src/PcTest.Cli/PcTest.Cli.csproj -- run plan --id SystemValidation@1.0.0 --testCases assets/TestCases --testSuites assets/TestSuites --testPlans assets/TestPlans --runs Runs
```

## Runs layout
- `Runs/index.jsonl` is the Engine-owned index of all runs.
- Each TestCase run produces a `{RunId}/` folder containing `manifest.json`, `params.json`, `stdout.log`, `stderr.log`, `events.jsonl`, `env.json`, and `result.json`.
- Each Suite/Plan run produces a `{GroupRunId}/` folder containing `manifest.json`, `controls.json`, `environment.json`, `runRequest.json`, `children.jsonl`, `events.jsonl`, and `result.json`.

To inspect results, open `result.json` for the run and `index.jsonl` for the summary index.

## Tests
```bash
dotnet test pc-test-system.sln
```

## MVP scope
- Implemented: discovery, validation, inputs/env resolution with EnvRef, suite/plan orchestration, PowerShell runner, run folder layout, index writer, CLI.
- Not implemented: UI (WPF) and any non-MVP UX/visualization features.
