# TemplateCaseSimple

Minimal template case for testing. Contains only one string parameter and simply prints it.

## Parameters

| Name    | Type   | Required | Default                         | Description       |
|---------|--------|----------|---------------------------------|-------------------|
| Message | string | No       | Hello from TemplateCaseSimple   | Message to print  |

## What It Does

1. Receives a `Message` parameter
2. Prints the message to console
3. Creates `artifacts/report.json` with basic structure
4. Exits with code 0 (success)

## Artifacts

**Always created:**
- `artifacts/report.json` - Minimal report with schema v1.0 format

## Console Output

```
==================================================
Message: Hello from TemplateCaseSimple
==================================================

RESULT: PASS
EXIT_CODE: 0
```

## Local Quick Test

```powershell
# Default message
pwsh .\run.ps1

# Custom message
pwsh .\run.ps1 -Message "Custom test message"

# Check exit code
pwsh .\run.ps1 -Message "Test"
echo $LASTEXITCODE
```

## Use Case

This is the simplest possible test case template. Use it as a starting point for:
- Learning the basic test case structure
- Quick testing of runner functionality
- Debugging parameter passing
- Minimal example for documentation
