# CpuStress Test Case

A simple CPU stress test for thermal validation.

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| DurationSec | int | No | 5 | Duration of stress test in seconds |
| Mode | enum | No | Normal | Test intensity: Normal or Intensive |
| ShouldPass | boolean | No | true | If false, script exits with code 1 |

## Usage

This test case can be run standalone or as part of a suite.

### Standalone

```bash
dotnet run --project src/PcTest.Cli -- run --target testcase --id CpuStress@1.0.0
```

### With Parameters

```bash
dotnet run --project src/PcTest.Cli -- run --target testcase --id CpuStress@1.0.0 --inputs '{"DurationSec": 10, "Mode": "Intensive"}'
```
