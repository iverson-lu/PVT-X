# System Information Verification

## Purpose
Comprehensive system information validation including CPU, OS version, activation status, installed software, and minimum hardware requirements.

## Test Logic
- Queries CPU information (name, frequency, temperature if available) using WMI
- Validates OS version against expected Windows DisplayVersion from registry
- Checks Windows activation status using Software Licensing service
- Validates Windows installation path
- Queries installed software from registry and checks against required software list
- Validates minimum system requirements (cores, memory, disk, webcam count)
- Pass if all validations succeed
- Fail if any validation fails

## Parameters
- **CPU_ProcessorName** (string, optional, default: "Intel"): Required processor name pattern (case-insensitive substring match)
- **WindowsUpdate_MaxDaysSinceLastUpdate** (int, optional, default: 30, range: 1-365): Maximum days since last Windows Update
- **CPU_MinFrequency** (double, optional, default: 1.0, range: 0.1-10.0 GHz): Minimum required current CPU running frequency in GHz
- **OS_Version** (enum, required, default: "Windows 25H2"): Expected Windows version ("Windows 24H2", "Windows 25H1", "Windows 25H2")
- **Windows_MustBeActivated** (boolean, optional): Whether Windows must be activated
- **System_WindowsPath** (string, optional, default: "C:\\Windows"): Expected Windows installation directory
- **RequiredSoftware** (json array, optional): JSON array of required software names for substring matching
- **MinimumRequirements** (json object, optional): JSON object with minimum system requirements (cores, memoryGB, diskGB, webcamCount)

## How to Run Manually
```powershell
pwsh ./run.ps1 -CPU_ProcessorName "Intel" -OS_Version "Windows 25H2" -Windows_MustBeActivated $true
```

## Expected Result
- **Success**: All system information validations pass. Exit code 0.
- **Failure**: One or more validations fail (CPU mismatch, OS version mismatch, not activated, missing software, insufficient hardware). Exit code 1.
  - `memoryGB` (int): Minimum total physical memory in GB
  - `diskGB` (int): Minimum free disk space on system drive in GB
  - `webcamCount` (int): Minimum number of camera devices

## Execution Flow

### 1. Parameter Validation
- Validates enum value for OS_Version
- Parses JSON parameters (RequiredSoftware, MinimumRequirements)

### 2. CPU Processor Name Check
- Retrieves CPU information using `Win32_Processor`
- Validates processor name contains the required substring

### 3. CPU Temperature Check
- Attempts to read thermal zone temperature from WMI
- Compares against maximum threshold
- Skipped if thermal sensors not available

### 4. CPU Frequency Check
- Reads current CPU clock speed
- Converts from MHz to GHz
- Validates against maximum threshold

### 5. OS Version Check
- Retrieves Windows version and build number from registry
- Validates DisplayVersion exactly matches expected version
- **Strict Matching**: No fallback to "Windows 11" detection; both DisplayVersion and expected value must match
- Maps parameter values (e.g., "Windows 24H2") to registry DisplayVersion values (e.g., "24H2")

### 6. Windows Activation Check
- Queries Windows licensing status via SoftwareLicensingProduct
- Validates activation state matches requirement:
  - **If Windows_MustBeActivated = true**: Fails if not activated
  - **If Windows_MustBeActivated = false**: Fails if activated (enforces non-activation requirement)

### 7. Windows Installation Path Check
- Reads system root directory from environment
- Validates against expected path

### 8. Required Software Check
- Scans registry for installed applications
- Checks both 32-bit and 64-bit registry paths
- Validates all required software is present

### 9. Minimum Requirements Check
- **CPU Cores**: Validates core count meets minimum
- **Memory**: Validates total physical memory meets minimum
- **Disk Space**: Validates free space on system drive meets minimum
- **Webcam**: Validates camera device count meets minimum

## Artifacts

### artifacts/report.json
Contains:
- **outcome**: Pass/Fail
- **summary**: Test result summary
- **details**: Detailed system information including:
  - CPU information (name, frequency)
  - OS information (version, build, activation, path)
  - Hardware requirements (cores, memory, disk, webcams)
  - Software validation results
- **metrics**: Numeric measurements:
  - `cpuCores`: Actual CPU core count
  - `memoryGB`: Actual memory in GB
  - `freeDiskGB`: Actual free disk space in GB
  - `webcamCount`: Actual webcam count
  - `cpuFrequencyGHz`: Actual CPU frequency in GHz

## Example Usage

### From UI
Select the test case and configure parameters in the UI, then run.

### From CLI
```powershell
# Basic run with defaults (note: id must include @version)
dotnet run --project src/PcTest.Cli/PcTest.Cli.csproj -- run --target testcase --id SystemInfoVerification@1.0.0 --inputs '{"OS_Version":"Windows 25H2"}'

# Custom requirements (using PowerShell variable for complex JSON)
$inputs = @'
{
  "OS_Version": "Windows 25H2",
  "CPU_ProcessorName": "Intel Core",
  "CPU_MinFrequency": 1.0,
  "Windows_MustBeActivated": true,
  "System_WindowsPath": "C:\\Windows",
  "RequiredSoftware": ["Microsoft Edge", "Windows Security"],
  "MinimumRequirements": {
    "cores": 8,
    "memoryGB": 16,
    "diskTotalGB": 100,
    "webcamCount": 1
  }
}
'@
dotnet run --project src/PcTest.Cli/PcTest.Cli.csproj -- run --target testcase --id SystemInfoVerification@1.0.0 --inputs $inputs

# Test non-activated Windows requirement
dotnet run --project src/PcTest.Cli/PcTest.Cli.csproj -- run --target testcase --id SystemInfoVerification@1.0.0 --inputs '{"OS_Version":"Windows 25H2","Windows_MustBeActivated":false}'
```

## Notes
- CPU temperature check is skipped if thermal sensors are not available
- Windows activation check is skipped on error (some systems may not expose this information)
- **OS version matching is strict**: DisplayVersion must exactly match the expected value (no Windows 11 fallback)
- **Activation validation**: When Windows_MustBeActivated=false, the test enforces that Windows must NOT be activated
- Software name matching uses substring matching (case-insensitive)
- All PowerShell output and comments are in English

## Capabilities Validated
- System information retrieval via WMI/CIM
- Hardware specifications verification
- OS version and activation status
- Software inventory checking
- Parameter type handling (all 8 types)
- JSON parsing and validation
- Complex requirement validation logic
