# PC Test System

A comprehensive test execution framework for PC hardware and software testing on Windows 11.

## Prerequisites

- **Windows 11** (x64)
- **.NET 10 SDK** (or later)
- **PowerShell 7+** (`pwsh.exe` must be in PATH)

## Quick Start

### Build

```powershell
dotnet build pc-test-system.sln
```

### Run Tests

```powershell
dotnet test
```

## Project Structure

```
pc-test-system/
├── src/
│   ├── PcTest.Contracts/    # Shared models, enums, validation utilities
│   ├── PcTest.Engine/       # Discovery, resolution, orchestration
│   ├── PcTest.Runner/       # PowerShell execution, Case Run Folders
│   └── PcTest.Cli/          # Command-line interface
├── tests/
│   ├── PcTest.Contracts.Tests/
│   ├── PcTest.Engine.Tests/
│   └── PcTest.Runner.Tests/
└── assets/
    ├── TestCases/           # Test case definitions
    ├── TestSuites/          # Test suite compositions
    └── TestPlans/           # Test plan orchestrations
```

## CLI Usage

### Discover Test Assets

```powershell
# Discover all test assets (from default locations)
dotnet run --project src/PcTest.Cli -- discover

# With custom asset roots
dotnet run --project src/PcTest.Cli -- discover --casesRoot ./my-cases --suitesRoot ./my-suites --plansRoot ./my-plans
```

### Run Test Case (Standalone)

```powershell
# Run with inputs
dotnet run --project src/PcTest.Cli -- run --target testcase --id "CpuStress@1.0.0" --inputs '{"DurationSec": 5, "ShouldPass": true}'

# Run with custom runs root
dotnet run --project src/PcTest.Cli -- run --target testcase --id "CpuStress@1.0.0" --runsRoot ./my-runs
```

### Run Test Suite

```powershell
dotnet run --project src/PcTest.Cli -- run --target suite --id "ThermalSuite@1.0.0"
```

### Run Test Plan

```powershell
dotnet run --project src/PcTest.Cli -- run --target plan --id "SystemValidation@1.0.0"
```

## Runs Folder Structure

After execution, the `Runs/` folder contains:

```
Runs/
└── {groupRunId}/                    # Group Run Folder (Suite/Plan execution)
    ├── index.jsonl                  # Streaming log of all test case results
    └── {nodeId}/                    # Case Run Folder per test case node
        ├── manifest.json            # Copy of TestCase manifest
        ├── params.json              # Resolved input parameters (secrets redacted)
        ├── env.json                 # Environment snapshot (secrets redacted)
        ├── result.json              # Final status, exitCode, duration
        ├── stdout.log               # PowerShell stdout
        ├── stderr.log               # PowerShell stderr
        └── events/                  # Structured events (optional)
```

### index.jsonl Format

Each line is a JSON object representing a test case result:

```json
{"timestamp":"2025-01-15T10:30:00Z","nodeId":"stress-01","caseIdentity":"CpuStress@1.0.0","status":"Passed","exitCode":0,"durationMs":5123}
```

## Test Assets Authoring

### Test Case (manifest.json)

```json
{
  "id": "CpuStress",
  "version": "1.0.0",
  "name": "CPU Stress Test",
  "category": "Performance",
  "entrypoint": "run.ps1",
  "timeout": 60000,
  "parameters": [
    { "name": "Duration", "type": "int", "required": true, "default": 10 },
    { "name": "Threads", "type": "int", "required": false, "default": 4 }
  ]
}
```

### Test Suite (manifest.json)

```json
{
  "id": "Thermal",
  "version": "1.0.0",
  "name": "Thermal Test Suite",
  "nodes": [
    {
      "nodeId": "stress-01",
      "ref": "CpuStress",
      "inputs": { "Duration": 30 }
    },
    {
      "nodeId": "memory-01",
      "ref": "MemoryCheck"
    }
  ]
}
```

### Test Plan (manifest.json)

```json
{
  "id": "SystemValidation",
  "version": "1.0.0",
  "name": "System Validation Plan",
  "suites": [
    { "ref": "Thermal@1.0.0" },
    { "ref": "System@1.0.0" }
  ],
  "env": [
    { "name": "LOG_LEVEL", "value": "Debug" }
  ]
}
```

## Identity Resolution

Test assets use `id@version` identity format:

- `CpuStress` - Latest version
- `CpuStress@1.0.0` - Specific version
- `./relative/path` - Relative path reference (suite nodes only)

## Environment Variables

Use `EnvRef` for environment variable resolution:

```json
{
  "$env": "SECRET_API_KEY",
  "required": true,
  "secret": true
}
```

Options:
- `$env`: Environment variable name (required)
- `default`: Default value if variable not set
- `required`: Fail if not set and no default (default: false)
- `secret`: Redact value in logs/files (default: false)

## Input Priority (Suite-Triggered)

1. **TestCase.defaults** - Parameter default values
2. **Suite.node.inputs** - Suite-level overrides
3. **RunRequest.nodeOverrides** - Runtime overrides

## Error Codes

| Code | Description |
|------|-------------|
| `DISCOVERY_FAILED` | Failed to discover test assets |
| `REF_NOT_FOUND` | Referenced asset not found |
| `REF_AMBIGUOUS` | Multiple assets match reference |
| `REF_OUT_OF_ROOT` | Reference escapes assets root |
| `PARAMETER_REQUIRED` | Required parameter missing |
| `PARAMETER_UNKNOWN` | Unknown parameter provided |
| `TYPE_CONVERSION_FAILED` | Parameter type conversion failed |
| `ENVREF_RESOLVE_FAILED` | Environment variable resolution failed |
| `CASE_TIMEOUT` | Test case exceeded timeout |
| `ABORT_REQUESTED` | Execution aborted by user |
| `SCRIPT_NOT_FOUND` | PowerShell script not found |

## Development

### Running Unit Tests

```powershell
# All tests
dotnet test

# Specific project
dotnet test tests/PcTest.Contracts.Tests
dotnet test tests/PcTest.Engine.Tests
dotnet test tests/PcTest.Runner.Tests

# With coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Building CLI Tool

```powershell
# Build release
dotnet publish src/PcTest.Cli -c Release -o ./publish

# Run published tool
./publish/PcTest.Cli discover
./publish/PcTest.Cli run --target testcase --id "CpuStress@1.0.0"
```

### Building UI Application

```powershell
dotnet publish .\src\PcTest.Ui\PcTest.Ui.csproj -c Release -r win-x64 --self-contained false /p:PublishSingleFile=false
```

Note: `assets` folder will be copied to the publish directory. .NET runtime is not bundled as self-contained and user will see a prompt to install .NET runtime if not already installed.

## License

MIT License
