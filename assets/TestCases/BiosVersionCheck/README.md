# BiosVersionCheck Test Case

Validates that the system BIOS version contains the specified string.

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| VersionContains | string | No | | String that BIOS version should contain |

## Usage

This test case can be run standalone or as part of a suite.

### Standalone

```bash
dotnet run --project src/PcTest.Cli -- run --target testcase --id BiosVersionCheck@1.0.0
```

### With Parameters

```bash
dotnet run --project src/PcTest.Cli -- run --target testcase --id BiosVersionCheck@1.0.0 --inputs '{"VersionContains": "A15"}'
```

## Artifacts

The test generates a JSON report at `artifacts/bios-check.json` containing:
- Version validation results
- BIOS information (version, manufacturer, release date, serial number)
- Any validation failures
