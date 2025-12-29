# WindowsUpdateHistoryVerify Test Case

Validates that Windows Update history contains at least a minimum number of update records.

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| MinExpectedCount | int | Yes | 1 | Minimum number of update records expected in Windows Update history |

## Usage

This test case can be run standalone or as part of a suite.

### Standalone

```bash
dotnet run --project src/PcTest.Cli -- run --target testcase --id WindowsUpdateHistoryVerify@1.0.0
```

### With Parameters

```bash
dotnet run --project src/PcTest.Cli -- run --target testcase --id WindowsUpdateHistoryVerify@1.0.0 --inputs '{"MinExpectedCount": 5}'
```

## Behavior

The test queries Windows Update history using COM automation and:
- Retrieves all update history entries
- Counts the total number of updates
- Validates that the count meets or exceeds the minimum expected count
- Records details about each update (title, date, operation, result)

## Update Information

For each update, the test captures:
- **Title**: The update name/description
- **Date**: When the update was installed
- **Operation**: Installation, Uninstallation, or Other
- **ResultCode**: Succeeded, Failed, In Progress, etc.

## Artifacts

The test generates a JSON report at `artifacts/windows-update-history.json` containing:
- Minimum expected count
- Actual detected count
- First 50 update entries with details
- Any validation failures
