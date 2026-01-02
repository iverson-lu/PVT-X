# SystemInfoVerification Test Case

## Overview
Comprehensive system information verification test case that validates CPU, OS version, activation status, installed software, and minimum hardware requirements.

## Purpose
This test case demonstrates all supported parameter types in a real-world scenario:
- **String**: Validates processor name contains specific text
- **Int**: Validates CPU temperature is below threshold
- **Double**: Validates CPU frequency is below threshold
- **Enum**: Validates specific Windows version
- **Boolean**: Validates Windows activation status
- **Path**: Validates Windows installation directory
- **JSON Array**: Validates required software is installed
- **JSON Object**: Validates minimum system requirements

## Parameters

### CPU_ProcessorName (string)
Required processor name pattern that must appear in the CPU name (case-insensitive substring match).
- **Default**: `"Intel Core Ultra"`
- **Example**: If set to "Intel Core Ultra", the actual processor name must contain this text

### CPU_MaxTemperature (int)
Maximum allowed CPU temperature in Celsius. Test fails if current temperature exceeds this value.
- **Default**: `80`
- **Range**: 0-150
- **Unit**: °C
- **Note**: Requires thermal sensor support; skipped if not available

### CPU_MaxFrequency (double)
Maximum allowed CPU frequency in GHz. Test fails if current frequency exceeds this value.
- **Default**: `5.5`
- **Range**: 0.1-10.0
- **Unit**: GHz

### OS_Version (enum) **[Required]**
Expected Windows version. Test fails if the actual DisplayVersion doesn't match exactly.
- **Options**: 
  - `"Windows 24H2"`
  - `"Windows 25H1"`
  - `"Windows 25H2"`
- **Default**: `"Windows 24H2"`
- **Validation**: Strict match against DisplayVersion registry value (e.g., "24H2", "25H1", "25H2"). No fallback to Windows 11 detection.

### Windows_MustBeActivated (boolean)
Whether Windows must be activated.
- **Default**: `true`
- **Behavior**:
  - If `true`: Test fails if Windows is NOT activated
  - If `false`: Test fails if Windows IS activated (useful for testing non-activated systems)
- **CLI Usage**: `-Windows_MustBeActivated:$true` or `-Windows_MustBeActivated:$false`

### System_WindowsPath (path)
Expected Windows installation directory path.
- **Default**: `"C:\\Windows"`

### RequiredSoftware (json)
JSON array of software names that must be installed on the system. Performs substring matching against installed software list.
- **Type**: JSON Array of strings
- **Default**: `["Microsoft Edge"]`
- **Example**: 
  ```json
  ["Microsoft Edge", "Google Chrome", "Visual Studio Code"]
  ```

### MinimumRequirements (json)
JSON object defining minimum system requirements. All checks must pass.
- **Type**: JSON Object
- **Default**: 
  ```json
  {
    "cores": 4,
    "memoryGB": 8,
    "diskGB": 50,
    "webcamCount": 1
  }
  ```
- **Properties**:
  - `cores` (int): Minimum CPU core count
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
