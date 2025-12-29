# DeviceHealthCheck Test Case

Validates that devices in Device Manager have no errors or warnings.

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| RestrictedMode | boolean | No | false | If true, all devices must be enabled and working; if false, only fail on problem devices |

## Usage

This test case can be run standalone or as part of a suite.

### Standalone

```bash
dotnet run --project src/PcTest.Cli -- run --target testcase --id DeviceHealthCheck@1.0.0
```

### With Parameters

```bash
dotnet run --project src/PcTest.Cli -- run --target testcase --id DeviceHealthCheck@1.0.0 --inputs '{"RestrictedMode": true}'
```

## Behavior

### Normal Mode (RestrictedMode = false)
- Only fails if there are devices with errors (yellow bang in Device Manager)
- Disabled devices are allowed

### Restricted Mode (RestrictedMode = true)
- Fails if any devices are disabled
- Fails if any devices have errors
- All devices must be enabled and working properly

## Artifacts

The test generates a JSON report at `artifacts/device-health-check.json` containing:
- Device counts (total, OK, disabled, problem)
- List of problem devices with error codes
- List of disabled devices (in restricted mode)
- Any validation failures
