# PC Test System (Reference CLI/Engine/Runner)

This repository contains a reference .NET implementation of the PC Test System described in `PC_Test_System_SPEC.md` (v1.2). It focuses on manifest-driven PowerShell execution with a clear run folder contract and a simple CLI entry point.

## Projects
- `PcTest.Contracts` – shared DTOs and JSON serialization defaults.
- `PcTest.Engine` – manifest discovery/validation, parameter binding, and privilege enforcement.
- `PcTest.Runner` – authoritative run orchestration: run folder creation, pwsh discovery/version check, process control, timeout, logging, and `result.json` generation.
- `PcTest.Cli` – CLI entry point for discovery and single-test execution.

## Prerequisites
- Windows 11 with .NET SDK (targeting `net10.0`/`net10.0-windows`).
- PowerShell 7+ (`pwsh.exe`) available on PATH or installed under `Program Files/PowerShell/7`.

## Build
From the repository root:

```powershell
dotnet restore
dotnet build pc-test-system.sln -c Release
```

## CLI Usage
Discover tests under a root:

```powershell
dotnet run --project src/PcTest.Cli/src/PcTest.Cli.csproj -- discover --root assets/TestCases
```

Run a test by id with optional parameters:

```powershell
dotnet run --project src/PcTest.Cli/src/PcTest.Cli.csproj -- run --root assets/TestCases --id Sample.Params --param Name=Contoso --param Repeat=2
```

The runner creates a run folder under `Runs/<RunId>` with `manifest.json`, `params.json`, `stdout.log`, `stderr.log`, `events.jsonl`, `env.json`, and `result.json`. Use `--runs` to override the run output root.

## Sample Test Cases
- `Sample.Params` – demonstrates parameter binding and artifact output (see `assets/TestCases/SampleWithParams`).
- `Sample.Timeout` – sleeps past the manifest timeout to exercise timeout enforcement (`assets/TestCases/TimeoutSample`).

## Notes
- `result.json` is generated exclusively by the runner. Script output is treated as input only.
- Parameters are passed to PowerShell using the frozen `-Name Value` protocol with proper quoting for arrays and booleans.
- Timeout enforcement kills the full process tree via `taskkill /T /F` on Windows.
