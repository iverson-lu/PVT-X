# AllParamTypesSample

This test case exists to validate **UI + Engine + Runner parameter handling**.

## What it covers

It declares one parameter for every type in `PcTest.Contracts.ParameterTypes`:

- `string`, `int`, `double`, `boolean`, `enum`
- `path`, `file`, `folder`
- `int[]`, `double[]`, `string[]`, `boolean[]`, `path[]`, `file[]`, `folder[]`, `enum[]`

It prints all received parameters to stdout and writes `artifacts/report.json` containing:
- each parameter's value
- the PowerShell runtime type (`psType`) that the runner actually bound

## How to use

1. Copy this folder into `assets/TestCases/AllParamTypesSample/`
2. Launch UI, discover/load test cases
3. Run the case and verify:
   - UI renders editors for each type
   - Engine validates required/enum/min/max/pattern as expected
   - Report shows expected PS types and values
