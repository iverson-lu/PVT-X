
## Parameter

| Name     | Type   | Default | Allowed values                  |
|----------|--------|---------|---------------------------------|
| TestMode | string | pass    | pass \| fail \| timeout \| error |

## Expected Behavior

### pass
- Script exits `0`
- Console shows `[RESULT] Pass`
- Runner result should be `Passed`

### fail
- Script exits `1`
- Console shows `[RESULT] Fail`
- Runner result should be `Failed`

### error
- Script throws and exits `2`
- stderr has exception
- Runner result should be `Error` (or ScriptError depending on your mapping)

### timeout
- Manifest `timeoutSeconds = 2`
- Script sleeps 10 seconds
- Runner should terminate and mark the run as `Timeout` (even if script doesn't complete)

## Artifacts always written (except if Runner kills the process before finally runs)
- `artifacts/report.json`
- `artifacts/raw/sample.json`
- `artifacts/raw/sample.txt`
- `artifacts/attachments/sample.log`
- `artifacts/attachments/sample.bin`

> Note: In **timeout** mode, Runner may kill the process before `finally` executes.
> In that case, you should still see:
> - partial stdout/stderr logs
> - and possibly the early-written files (raw/attachments) because they are created before the sleep.

## Local quick run

```powershell
pwsh .\run.ps1 -TestMode pass
echo $LASTEXITCODE

pwsh .\run.ps1 -TestMode fail
echo $LASTEXITCODE

pwsh .\run.ps1 -TestMode error
echo $LASTEXITCODE

pwsh .\run.ps1 -TestMode timeout
echo $LASTEXITCODE
