# NetworkPing Test Case

Validates network connectivity by pinging a specified target address.

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| TargetAddress | string | No | www.google.com | Target address or hostname to ping |

## Usage

This test case can be run standalone or as part of a suite.

### Standalone

```bash
dotnet run --project src/PcTest.Cli -- run --target testcase --id NetworkPing@1.0.0
```

### With Parameters

```bash
dotnet run --project src/PcTest.Cli -- run --target testcase --id NetworkPing@1.0.0 --inputs '{"TargetAddress": "8.8.8.8"}'
```

## Behavior

The test sends 4 ping packets to the target address and validates:
- All 4 packets are successfully received
- Network connectivity is established
- Response times are recorded

The test passes only if all 4 packets receive a response.

## Artifacts

The test generates a JSON report at `artifacts/network-ping.json` containing:
- Target address
- Overall success status
- Detailed ping results for each packet (address, response time, status)
- Any validation failures
