# PC Test System MVP

## Dependencies
- .NET 10 SDK
- PowerShell 7+

## Discover
```bash
pc-test discover --testCaseRoot assets/TestCases --testSuiteRoot assets/TestSuites --testPlanRoot assets/TestPlans --runsRoot Runs
```

## Run a standalone TestCase
```bash
pc-test run testCase --id CpuStress@2.0.0 --testCaseRoot assets/TestCases --runsRoot Runs
```

## Run a Suite
```bash
pc-test run suite --id ThermalSuite@1.0.0 --testCaseRoot assets/TestCases --testSuiteRoot assets/TestSuites --runsRoot Runs
```

## Run a Plan
```bash
pc-test run plan --id SystemValidation@1.0.0 --testCaseRoot assets/TestCases --testSuiteRoot assets/TestSuites --testPlanRoot assets/TestPlans --runsRoot Runs
```

## Runs layout
`Runs/` contains:
- `index.jsonl` (Engine-only writer)
- `{RunId}/` for TestCase runs with `manifest.json`, `params.json`, `stdout.log`, `stderr.log`, `events.jsonl`, `env.json`, `result.json`
- `{GroupRunId}/` for TestSuite/TestPlan runs with `manifest.json`, `controls.json`, `environment.json`, `runRequest.json`, `children.jsonl`, `events.jsonl`, `result.json`

Inspect `result.json` for final status, and `index.jsonl` for cross-run summary.

## Tests
```bash
dotnet test
```
