# TimeoutSample Test Case

Sleeps longer than timeout to validate enforcement.

## Parameters

This test case has no parameters.

## Usage

This test case can be run standalone or as part of a suite.

### Standalone

```bash
dotnet run --project src/PcTest.Cli -- run --target testcase --id TimeoutSample@1.0.0
```

## Behavior

This is a demonstration test case that intentionally exceeds its timeout limit:
- Timeout is set to 2 seconds in the manifest
- The script sleeps for 5 seconds
- The test runner should terminate the test after 2 seconds

This test is useful for validating that the test runner properly enforces timeouts.

## Expected Result

This test should **timeout** and be terminated by the test runner after 2 seconds, before it completes naturally at 5 seconds.
