## Parameters

| Name | Type | Default | Description |
|---|---|---:|---|
| TargetAddress | string | www.microsoft.com | Target address to ping (hostname or IP). |
| MaxReplyTimeMs | int | 0 | Optional maximum allowed reply time (ms). Use `0` to disable this threshold. |

## Expected Behavior

### Pass
- Target is reachable via ICMP ping.
- If `MaxReplyTimeMs > 0` and a reply time is available, the reply time is within the threshold.
- Script exits `0`.
- Console shows `[RESULT] Pass`.
- `artifacts/report.json` has `outcome = "Pass"`.

### Fail
- Target is not reachable via ICMP ping (DNS failure, timeout, unreachable, or ICMP blocked).
- Or ping succeeds but `replyTimeMs > MaxReplyTimeMs` when the threshold is enabled.
- Script exits `1`.
- Console shows `[RESULT] Fail`.
- `artifacts/report.json` has `outcome = "Fail"` and includes details/metrics.

### Error
- Script encounters an unhandled runtime error.
- Script exits `2`.
- Console shows `[RESULT] Fail`.
- `artifacts/report.json` includes exception details.

## Artifacts written
- `artifacts/report.json`

## Local quick run

```powershell
# Default target
pwsh .\run.ps1
echo $LASTEXITCODE

# Custom target
pwsh .\run.ps1 -TargetAddress "8.8.8.8"
echo $LASTEXITCODE

# Enforce a maximum reply time threshold (ms)
pwsh .\run.ps1 -TargetAddress "www.microsoft.com" -MaxReplyTimeMs 200
echo $LASTEXITCODE
```

## Notes
- Some networks block ICMP (ping). In that case, this test may fail even if other network traffic works.
