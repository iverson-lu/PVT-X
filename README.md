# PC Test System v1.5.0 (Minimal Implementation)

This repository provides a minimal, end-to-end implementation of the PC Test System described in `PC_Test_System_SPEC_v1.5.0.md`.

## Projects

- `PcTest.Contracts`: DTOs, error/warning/event codes, `id@version` parsing helpers.
- `PcTest.Engine`: discovery, validation, orchestration, suite/plan execution, `index.jsonl` writer.
- `PcTest.Runner`: PowerShell runner and case run folder writer (authoritative for case results).
- `PcTest.Cli`: CLI entry point.

## Demo Assets

Located under `assets/`:

- `TestCases/CaseA` and `TestCases/CaseB`
- `TestSuites/suite.manifest.json`
- `TestPlans/plan.manifest.json`

These assets demonstrate:
- input override order
- `retryOnError`, `continueOnFailure`, `repeat`
- `EnvRef` with secret redaction
- `index.jsonl` and run folder artifacts

## Running (Windows 11)

```pwsh
# Discover
pctest discover --caseRoot .\assets\TestCases --suiteRoot .\assets\TestSuites --planRoot .\assets\TestPlans

# List
pctest list cases --caseRoot .\assets\TestCases --suiteRoot .\assets\TestSuites --planRoot .\assets\TestPlans

# Run case
pctest run case demo.case.a@1.0.0 --runsRoot .\runs

# Run suite
pctest run suite demo.suite@1.0.0 --runsRoot .\runs

# Run plan
pctest run plan demo.plan@1.0.0 --runsRoot .\runs
```

## Error/Warning/Event Codes

**Errors**
- `Discovery.DuplicateId`
- `Manifest.Invalid`
- `Suite.TestCaseRef.Invalid`
- `EnvRef.ResolveFailed`
- `RunnerError`
- `ScriptError`

**Warnings**
- `Controls.MaxParallel.Ignored`
- `EnvRef.SecretOnCommandLine`

**Events**
- `Runner.Started`
- `Runner.Completed`

## Spec Defaults & Assumptions

The spec does not define the following behaviors; this implementation uses the minimal, extensible defaults below:

- `workingDir` is set to the case run folder by default. Suite-level `workingDir` is not modeled yet.
- Pre-node validation for `file`/`folder` typed parameters is not implemented because the spec does not provide a concrete schema for those parameter types in v1.5.0.
- Environment redaction uses the `envOverride` keys as secret markers when no explicit `EnvRef.secret` is present.
