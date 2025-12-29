# SampleWithParams Test Case

Demonstrates parameter binding and artifact output.

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| Name | string | No | World | Name to greet in the test output |
| Repeat | int | No | 1 | Number of iterations to perform |
| Modes | string[] | No | | Array of mode strings to demonstrate string array parameter |

## Usage

This test case can be run standalone or as part of a suite.

### Standalone

```bash
dotnet run --project src/PcTest.Cli -- run --target testcase --id SampleWithParams@1.0.0
```

### With Parameters

```bash
dotnet run --project src/PcTest.Cli -- run --target testcase --id SampleWithParams@1.0.0 --inputs '{"Name": "Alice", "Repeat": 3, "Modes": ["Fast", "Verbose"]}'
```

## Behavior

This is a demonstration test case that:
- Greets the specified name
- Performs N iterations based on the Repeat parameter
- Displays the array of modes if provided
- Creates an artifact file with the output

## Artifacts

The test generates a text file at `artifacts/output.txt` containing:
- A greeting message with the provided parameters
