# Template Case - Minimal

## Purpose
Minimal template case demonstrating the simplest possible test case structure.

## Test Logic
- Receives a `Message` parameter
- Prints the message to console
- Creates structured `report.json` with basic format
- Always passes (exit code 0)

## Parameters
- **Message** (string, optional, default: "Hello from PVT-X"): Message to print to console.

## How to Run Manually
```powershell
# Default message
pwsh ./run.ps1

# Custom message
pwsh ./run.ps1 -Message "Custom test message"
```

## Expected Result
- **Success**: Message printed to console, basic report generated. Exit code 0.
- Learning the basic test case structure
- Quick testing of runner functionality
- Debugging parameter passing
- Minimal example for documentation
