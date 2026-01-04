# Timezone Verification Test

## Description

Validates that the system timezone is configured with the expected UTC offset. This test is useful for verifying that systems are configured to the correct timezone, particularly important for distributed systems or when coordinating operations across multiple regions.

## Default Behavior

- **Default Expected UTC Offset**: +08:00 (UTC+8)
- Retrieves current system timezone information
- Compares the current UTC offset with the expected value
- Displays detailed timezone configuration including DST status

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `ExpectedUtcOffset` | string | No | `+08:00` | Expected UTC offset in format '+HH:MM' or '-HH:MM' |

## Examples

### Default (UTC+8)

```json
{
  "ExpectedUtcOffset": "+08:00"
}
```

### UTC (Greenwich Mean Time)

```json
{
  "ExpectedUtcOffset": "+00:00"
}
```

### Eastern Time (US)

```json
{
  "ExpectedUtcOffset": "-05:00"
}
```

### Skip Validation

```json
{
  "ExpectedUtcOffset": ""
}
```

## Exit Codes

- `0` - Timezone matches expected UTC offset or validation skipped
- `1` - Timezone UTC offset mismatch or error retrieving timezone information

## Output

The test displays:
- Timezone ID
- Display name
- Standard and Daylight names
- Current UTC offset
- Base UTC offset
- Daylight Saving Time support and status
- Validation result

## Notes

- The test compares the **current** UTC offset, which may differ from the base offset if Daylight Saving Time is active
- Leave `ExpectedUtcOffset` empty to retrieve timezone information without validation
- UTC offset format must be in '+HH:MM' or '-HH:MM' format (e.g., '+08:00', '-05:00', '+00:00')
- Common UTC+8 timezones include: Singapore, Hong Kong, Beijing, Taipei, Perth, Manila

## Requirements

- PowerShell 7+
- Windows OS
- User privilege
