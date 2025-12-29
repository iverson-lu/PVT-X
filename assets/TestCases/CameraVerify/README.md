# CameraVerify Test Case

Validates camera count and name substrings using PnP device discovery.

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| MinExpectedCount | int | Yes | 1 | Minimum number of cameras expected to be detected |
| NameContains | string | No | | String that at least one camera name should contain |

## Usage

This test case can be run standalone or as part of a suite.

### Standalone

```bash
dotnet run --project src/PcTest.Cli -- run --target testcase --id CameraVerify@1.0.0
```

### With Parameters

```bash
dotnet run --project src/PcTest.Cli -- run --target testcase --id CameraVerify@1.0.0 --inputs '{"MinExpectedCount": 2, "NameContains": "HD"}'
```

## Artifacts

The test generates a JSON report at `artifacts/camera-check.json` containing:
- Minimum expected camera count
- Name substring filter
- Detected camera count
- List of detected camera names
- Any validation failures
