# PC Test System MVP

## Requirements
- .NET 10 SDK
- PowerShell 7+ (`pwsh` on PATH)

## Commands
From the repo root:

### Discover
```
dotnet run --project src/PcTest.Cli -- discover
```

### Run a standalone TestCase
```
dotnet run --project src/PcTest.Cli -- run testCase --id CpuStress@1.0.0
```

### Run a Suite
```
dotnet run --project src/PcTest.Cli -- run suite --id ThermalSuite@1.0.0
```

### Run a Plan
```
dotnet run --project src/PcTest.Cli -- run plan --id SystemValidation@1.0.0
```

## Runs folder layout
Runs are written under `Runs/`:

```
Runs/
  index.jsonl
  {RunId}/                    # Test Case run
    manifest.json
    params.json
    stdout.log
    stderr.log
    events.jsonl
    env.json
    result.json
  {GroupRunId}/               # Test Suite / Test Plan run
    manifest.json
    controls.json
    environment.json
    runRequest.json
    children.jsonl
    events.jsonl
    result.json
```

- `result.json` is the authoritative result for each run type.
- `index.jsonl` is the Engine-owned log of all runs.

## Tests
```
dotnet test
```
