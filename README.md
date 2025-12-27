# PC Test System (MVP)

## Prerequisites
- .NET 10 SDK
- PowerShell 7+

## Build
```bash
# From repo root
dotnet build pc-test-system.sln
```

## Discover
```bash
# Writes discovery.json in the current directory
dotnet run --project src/PcTest.Cli -- discover --caseRoot assets/TestCases --suiteRoot assets/TestSuites --planRoot assets/TestPlans --output discovery.json
```

## Run a standalone Test Case
```bash
cat > runrequest.case.json <<'JSON'
{
  "testCase": "CpuStress@1.0.0",
  "caseInputs": { "DurationSec": 1, "Mode": "Quick" },
  "environmentOverrides": { "env": { "CPU_SECRET": "secret" } }
}
JSON

dotnet run --project src/PcTest.Cli -- run testCase --request runrequest.case.json --caseRoot assets/TestCases --suiteRoot assets/TestSuites --planRoot assets/TestPlans --runsRoot Runs
```

## Run a Suite
```bash
cat > runrequest.suite.json <<'JSON'
{
  "suite": "ThermalSuite@1.0.0",
  "nodeOverrides": {
    "cpu-quick": { "inputs": { "DurationSec": 2 } }
  },
  "environmentOverrides": { "env": { "CPU_SECRET": "secret" } }
}
JSON

dotnet run --project src/PcTest.Cli -- run suite --request runrequest.suite.json --caseRoot assets/TestCases --suiteRoot assets/TestSuites --planRoot assets/TestPlans --runsRoot Runs
```

## Run a Plan
```bash
cat > runrequest.plan.json <<'JSON'
{
  "plan": "SystemValidation@1.0.0",
  "environmentOverrides": { "env": { "LAB_MODE": "PLAN" } }
}
JSON

dotnet run --project src/PcTest.Cli -- run plan --request runrequest.plan.json --caseRoot assets/TestCases --suiteRoot assets/TestSuites --planRoot assets/TestPlans --runsRoot Runs
```

## Run Folder Layout
Runs are written under `Runs/`:
- `Runs/index.jsonl` is written by the Engine only.
- Each Test Case run writes `{RunId}/manifest.json`, `params.json`, `stdout.log`, `stderr.log`, `env.json`, and `result.json`.
- Suite/Plan runs write `{GroupRunId}/manifest.json`, `controls.json` (suite), `environment.json`, `runRequest.json`, `children.jsonl`, and `result.json`.

You can inspect `result.json` for status and `index.jsonl` for a global summary.

## Tests
```bash
dotnet test pc-test-system.sln
```
