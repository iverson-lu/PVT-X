# PC Test System Specification
## Version 1.4

> This revision resets the model around Test Suite as the primary execution unit.
> It removes legacy compatibility constraints and focuses on clarity and correctness.

---

## 1. Project Goal

Develop a local PC test system running on the target machine to:
- Execute automated hardware and system tests
- Standardize discovery, parameter configuration, execution, logging, and results
- Deliver test cases as Manifest + PowerShell scripts

The system MUST provide:
- Stable and explicit contracts (Manifest / Result / Folder Layout)
- Clear responsibility boundaries
- Engineering-grade observability and debuggability
- A Suite-centered execution model

---

## 2. Core Model and Architecture Principles

### 2.1 Entities
- Test Case: Defines capability and parameter schema only; no concrete values are stored.
- Test Suite: The primary execution unit; defines an execution graph of Test Case runs.
- Test Plan: A lightweight composition layer that only references Test Suites.
- RunRequest: The runtime override payload for a specific Suite run.

### 2.2 Principles (Normative)
- Test Suite is the primary execution unit. A Suite MUST define an execution graph.
- Each node in a Suite graph MUST correspond to exactly one Test Case execution.
- A Suite MUST store per-node default inputs for its nodes (optional on each node).
- Test Plan MUST only reference Test Suites. A Plan MUST NOT define or override Test Case inputs.
- Test Case MUST define capability only. TestCase.parameters MUST define schema and defaults.
- All concrete runtime values MUST come from Suite node inputs and/or RunRequest overrides.

---

## 3. Technology Stack

### 3.1 Platform
- Windows 11

### 3.2 Runtime and Languages
- UI: WPF, net10.0-windows
- Engine / Runner / Core: net10.0
- Script Runtime: PowerShell 7+ (pwsh.exe)

Notes:
- The system MUST target the latest .NET LTS.
- PowerShell 5.x is NOT supported.

### 3.3 Repository Structure (Reference Only)

```
PVT-X/
  README.md
  docs/
    SPEC.md
    architecture.md
    security.md
    schema/
      manifest.schema.json
      result.schema.json

  src/
    PcTest.UI/
    PcTest.Runner/
    PcTest.Engine/
    PcTest.Contracts/
    PcTest.Cli/

  assets/
    TestCases/
      CpuStress/
        test.manifest.json
        run.ps1
        README.md

  Runs/
    .gitkeep

  tools/
    signing/
    packaging/

  .gitignore
  Directory.Build.props
  pc-test-system.sln
```

Rules:
- The repository layout is normative only for reference implementation.
- Runtime behavior MUST NOT depend on physical source tree layout.
- Runtime roots MUST be resolved to logical roots: ResolvedTestCaseRoot, ResolvedTestSuiteRoot, ResolvedTestPlanRoot, ResolvedRunsRoot.
- Engine and Runner MUST NOT assume other fixed locations at runtime.

---

## 4. System Architecture

```
[ UI (WPF) ]
      |
      v
[ Engine ]
      |
      v
[ Runner ]
      |
      v
[ PowerShell Script ]
```

### 4.1 UI Responsibilities
- Test discovery and metadata display
- Parameter input and validation
- Execution control (start / stop)
- Log and result visualization
- Run history browsing

### 4.2 Engine Responsibilities
- Load and validate manifests
- Resolve Suite execution graphs
- Bind parameters and compute effective inputs
- Orchestrate execution lifecycle
- Generate Test Suite / Test Plan summary results

### 4.3 Runner Responsibilities (Authoritative)
- Discover and validate pwsh.exe
- Enforce execution rules and timeout
- Create Test Case Run Folder
- Apply environment injection
- Launch and terminate process tree
- Capture stdout / stderr
- Generate authoritative result.json for Test Case runs
- Record execution environment

### 4.4 Execution Authority
- Runner is the final authority for Test Case runs.
- Engine is the authority for Test Suite / Test Plan summary generation.

---

## 5. Layout and Discovery

### 5.1 Test Case Layout

```
TestCases/
  CpuStress/
    test.manifest.json
    run.ps1
    README.md (optional)
```

Rules:
- Test Case folders are immutable during execution.
- Discovery is read-only.
- All outputs MUST be written to the Run Folder.

Test Case Root Resolution:
- Engine MUST resolve ResolvedTestCaseRoot before discovery.
- Discovery MUST be recursive under the resolved root.
- ResolvedTestCaseRoot MUST be treated as read-only at runtime.

### 5.2 Test Suite and Test Plan Layout

Example:
```
TestPlans/
  SystemValidation/
    plan.manifest.json
    Suites/
      Thermal/
        suite.manifest.json
        Cases/
          CpuStress/
            test.manifest.json
            run.ps1
```

Rules:
- Suite and Plan folders are immutable during execution.
- Discovery is read-only.
- Suite and Plan manifests are:
  - suite.manifest.json
  - plan.manifest.json
- Child references inside manifests MUST be resolved relative to the manifest folder.
- Engine MUST normalize resolved target paths before containment checks.
- After normalization, targets MUST be within ResolvedTestCaseRoot or ResolvedTestSuiteRoot or ResolvedTestPlanRoot; otherwise the Engine MUST reject the manifest.

---

## 6. Manifest Specifications

### 6.1 Test Case Manifest (test.manifest.json)

| Field | Type | Required | Description |
|---|---|---|---|
| schemaVersion | string | Yes | Manifest schema version |
| id | string | Yes | Globally unique test ID |
| name | string | Yes | Test name |
| category | string | Yes | Test category |
| description | string | No | Description |
| version | string | Yes | Test version |
| privilege | enum | No | User / AdminPreferred / AdminRequired |
| timeoutSec | int | No | Execution timeout (seconds) |
| tags | string[] | No | Tags |
| parameters | ParameterDefinition[] | No | Test Case input definitions |

Rules:
- Test Case identity is id + version.
- Folder name MUST NOT affect identity.
- parameters define schema and defaults only; no concrete runtime values are stored here.

Example:
```json
{
  "schemaVersion": "1.4",
  "id": "CpuStress",
  "name": "CPU Stress",
  "category": "Thermal",
  "version": "2.0.0",
  "privilege": "User",
  "timeoutSec": 600,
  "parameters": [
    { "name": "DurationSec", "type": "int", "required": true, "default": 30 },
    { "name": "Mode", "type": "enum", "required": false, "enumValues": ["A", "B"] },
    { "name": "Modes", "type": "enum[]", "required": false, "enumValues": ["A", "B"] }
  ]
}
```

---

### 6.2 Parameter Definition

| Field | Type | Required | Description |
|---|---|---|---|
| name | string | Yes | Parameter name |
| type | string | Yes | string / int / double / bool / enum / path / file / folder / string[] / int[] / enum[] |
| required | bool | Yes | Required or not |
| default | any | No | Default value |
| min / max | number | No | Numeric range |
| enumValues | string[] | No | Enum candidates |
| unit | string | No | Display unit |
| uiHint | string | No | textbox / dropdown / checkboxes / filePicker / folderPicker / multiline |
| pattern | string | No | Regex validation |
| help | string | No | Help text |

Notes:
- The type list above is authoritative.

---

### 6.3 Test Suite Manifest (suite.manifest.json)

| Field | Type | Required | Description |
|---|---|---|---|
| schemaVersion | string | Yes | Manifest schema version |
| id | string | Yes | Globally unique suite ID |
| name | string | Yes | Suite name |
| description | string | No | Description |
| version | string | Yes | Suite version |
| tags | string[] | No | Tags |
| controls | object | No | Orchestration controls (repeat, maxParallel, continueOnFailure, retryOnError, timeoutPolicy, etc.) |
| environment | object | No | Environment injection (env vars, workingDir, optional runner hints) |
| testCases | TestCaseNode[] | Yes | Execution graph nodes |

TestCaseNode schema:
```
{
  "nodeId": "string (required, unique within suite)",
  "ref": "relative path to TestCase",
  "inputs": { "...": "..." }
}
```

Rules:
- testCases MUST be an ordered array that defines the execution graph.
- nodeId identifies the execution node, not the Test Case definition.
- The same Test Case (id@version) MAY appear multiple times with different nodeId and inputs.
- ref MUST be a relative path to a folder containing test.manifest.json.
- inputs, when present, MUST only include parameter names declared by the referenced Test Case.

Example:
```json
{
  "schemaVersion": "1.4",
  "id": "ThermalSuite",
  "name": "Thermal Suite",
  "version": "1.0.0",
  "controls": { "repeat": 1, "continueOnFailure": false },
  "environment": { "env": { "LAB_MODE": "1" }, "workingDir": "work" },
  "testCases": [
    {
      "nodeId": "cpu-quick",
      "ref": "Cases/CpuStress",
      "inputs": { "DurationSec": 30, "Mode": "A" }
    },
    {
      "nodeId": "cpu-long",
      "ref": "Cases/CpuStress",
      "inputs": { "DurationSec": 120, "Mode": "B" }
    }
  ]
}
```

---

### 6.4 Test Plan Manifest (plan.manifest.json)

| Field | Type | Required | Description |
|---|---|---|---|
| schemaVersion | string | Yes | Manifest schema version |
| id | string | Yes | Globally unique plan ID |
| name | string | Yes | Plan name |
| description | string | No | Description |
| version | string | Yes | Plan version |
| tags | string[] | No | Tags |
| suites | string[] | Yes | List of Test Suite folder paths (relative) |

Rules:
- A Plan MUST only reference Test Suites.
- suites entries MUST be relative paths to folders containing suite.manifest.json.
- Order in suites defines execution order.
- A Plan MUST NOT define or override Test Case inputs.

Example:
```json
{
  "schemaVersion": "1.4",
  "id": "SystemValidation",
  "name": "System Validation",
  "version": "1.2.0",
  "suites": [
    "Suites/Thermal",
    "Suites/Storage"
  ]
}
```

---

### 6.5 Controls and Environment Objects (Suite Only)

Controls (recommended fields, optional):
- repeat (int, default 1)
- maxParallel (int, default 1)
- continueOnFailure (bool, default false)
- retryOnError (int, default 0)
- timeoutPolicy (string, default "AbortOnTimeout")

Environment (recommended fields, optional):
- env (object: name -> value)
- workingDir (string)
- runnerHints (object)

Rules:
- Controls and environment are free-form objects; unknown keys MUST be ignored.
- UI/Engine SHOULD warn on unknown keys in controls/environment (best-effort).
- environment.workingDir MUST resolve inside the current Run Folder.
- Runner MUST normalize workingDir before containment checks.
- Runner MUST reject any workingDir that resolves outside the Run Folder.
- If controls.maxParallel > 1 and the implementation only executes sequentially, Engine MUST ignore it and record a warning.

---

## 7. Inputs Resolution and Validation

### 7.1 Effective Inputs

effectiveInputs MUST be computed in the following order (last wins):
1. TestCase.parameters.default
2. Suite.testCases[node].inputs
3. RunRequest.nodeOverrides[nodeId].inputs (if present)

Plan does not participate in inputs resolution.

### 7.2 Validation Rules
- Each input name MUST exist in the referenced Test Case parameters.
- Unknown input names MUST be rejected.
- Required parameters without a resolved value MUST cause validation failure before execution.
- The UI SHOULD display the effective defaults based on this resolution order.

---

## 8. RunRequest

RunRequest SHOULD primarily target a Test Suite.

Schema:
```
{
  "suite": "suiteId@version",
  "nodeOverrides": {
    "nodeId": {
      "inputs": { "...": "..." }
    }
  }
}
```

Rules:
- nodeOverrides keys MUST match nodeId values in the target Suite.
- nodeOverrides MAY be omitted.
- No global case-level overrides are required.
- Plan execution MAY accept { "plan": "planId@version" } without inputs overrides.

Example:
```json
{
  "suite": "ThermalSuite@1.0.0",
  "nodeOverrides": {
    "cpu-quick": { "inputs": { "DurationSec": 45 } }
  }
}
```

---

## 9. Parameter Passing Protocol (Frozen)

All case inputs MUST be passed as named PowerShell parameters.

Rules:
- Format: -Name Value
- Array parameters MUST be passed as repeated values: -Modes "A" "B"
- Runner MUST use ProcessStartInfo.ArgumentList and append each element separately.
- Boolean values: $true / $false
- Numeric serialization uses invariant culture (decimal point)
- Strings and paths MUST be passed safely (no shell parsing)
- Missing optional parameters MUST be omitted

Example:
```powershell
pwsh.exe run.ps1 -DurationSec 30 -Modes "A" "B"
```

This protocol is immutable within schema version 1.4.

---

## 10. Execution Semantics (Non-Normative)

- Each TestCaseRef node produces one Test Case run per Suite iteration.
- Suite runs aggregate node results.
- Suite is the smallest reusable execution template.

---

## 11. PowerShell Execution Rules

- Runner invokes pwsh.exe
- Version MUST be >= 7.0
- Working directory is the Run Folder unless overridden by environment.workingDir
- Scripts MUST be treated as untrusted code

### 11.1 Exit Code Convention

| Exit Code | Meaning |
|---|---|
| 0 | Success |
| 1 | Test Failed |
| 2 | Script Error |
| 3 | Timeout / Aborted |

Exit codes are advisory; final status is determined by Runner.

---

## 12. Run Folder Layout

```
ResolvedRunsRoot/
  index.jsonl
  {RunId}/                    # Test Case run
    manifest.json
    params.json
    stdout.log
    stderr.log
    events.jsonl
    env.json
    result.json
  {GroupRunId}/               # Test Suite / Test Plan run
    manifest.json
    params.json
    children.jsonl
    events.jsonl
    result.json
```

### 12.1 Ownership Rules
- Test Case Run Folders are exclusively owned by Runner.
- Test Suite / Test Plan Run Folders are exclusively owned by Engine.
- Scripts MUST NOT create, delete, or rename top-level files in the Run Folder.
- Scripts MAY write additional files only under:
  {RunId}/artifacts/
- Runner MUST ignore unknown files and folders.

### 12.2 Immutability Rules
- manifest.json and params.json are snapshots generated by Engine/Runner.
- For Test Case runs, params.json contains effectiveInputs passed to the script.
- For Test Suite / Test Plan runs, manifest.json MUST include the original manifest snapshot and SHOULD include resolved refs.
- Scripts MUST NOT modify these files.

### 12.3 index.jsonl
- One JSON object per line
- Fields:
  - runId
  - runType (TestCase / TestSuite / TestPlan)
  - nodeId (required for TestCase runs)
  - testId (required for TestCase)
  - testVersion (required for TestCase)
  - suiteId (required for TestSuite; MUST be set for TestCase when parentRunId refers to a TestSuite run)
  - suiteVersion (required for TestSuite; MUST be set for TestCase when parentRunId refers to a TestSuite run)
  - planId (required for TestPlan; MUST be set for TestSuite/TestCase when parentRunId refers to a TestPlan run)
  - planVersion (required for TestPlan; MUST be set for TestSuite/TestCase when parentRunId refers to a TestPlan run)
  - parentRunId (optional; points to the Suite/Plan run)
  - startTime, endTime (ISO8601 UTC with trailing Z)
  - status (Passed / Failed / Error / Timeout / Aborted)

Example:
```json
{"runId":"R-001","runType":"TestCase","nodeId":"cpu-quick","testId":"CpuStress","testVersion":"2.0.0","suiteId":"ThermalSuite","suiteVersion":"1.0.0","planId":"SystemValidation","planVersion":"1.2.0","parentRunId":"R-100","startTime":"2025-12-24T10:00:00Z","endTime":"2025-12-24T10:01:00Z","status":"Passed"}
```

### 12.4 env.json
Records execution environment snapshot:
- OS version
- Runner version
- PowerShell version
- Elevation state

### 12.5 children.jsonl
- Only present for Test Suite / Test Plan runs
- Each line is a JSON object with child runId, id, version, status, and nodeId when applicable
- For Test Suite children (Test Case runs), nodeId MUST be included
- For Test Plan children (Test Suite runs), nodeId is not applicable
- Order MUST match manifest execution order

Example:
```json
{"runId":"R-201","nodeId":"cpu-quick","testId":"CpuStress","testVersion":"2.0.0","status":"Passed"}
{"runId":"R-301","suiteId":"ThermalSuite","suiteVersion":"1.0.0","status":"Passed"}
```

### 12.6 events.jsonl (Optional)
- Each line is a JSON object representing a timestamped event.
- Event producers may include Runner or Script.
- The schema is intentionally unspecified.
- Consumers MUST treat events as best-effort diagnostic data only.

---

## 13. Result Specification (result.json)

### 13.1 Authority Rule
- Runner is the sole authority of result.json for Test Case runs.
- Engine is the authority of result.json for Test Suite / Test Plan runs.

### 13.2 Test Case Result Fields

| Field | Type | Required | Description |
|---|---|---|---|
| schemaVersion | string | Yes | Result schema version |
| runType | enum | No | TestCase (default) |
| nodeId | string | Yes | Suite nodeId for this run |
| testId | string | Yes | Test ID |
| testVersion | string | Yes | Test version |
| suiteId | string | No | Suite ID (if known) |
| suiteVersion | string | No | Suite version (if known) |
| planId | string | No | Plan ID (if known) |
| planVersion | string | No | Plan version (if known) |
| status | enum | Yes | Passed / Failed / Error / Timeout / Aborted |
| startTime | string | Yes | ISO8601 UTC with trailing Z |
| endTime | string | Yes | ISO8601 UTC with trailing Z |
| metrics | object | No | Metrics |
| message | string | No | Summary |
| exitCode | int | No | Script exit code |
| effectiveInputs | object | Yes | Effective case inputs passed to the script |
| error | object | No | Error details |
| runner | object | No | Runner metadata |

Rule:
- nodeId MUST be included for all Test Case results.

Example:
```json
{
  "schemaVersion": "1.4",
  "runType": "TestCase",
  "nodeId": "cpu-quick",
  "testId": "CpuStress",
  "testVersion": "2.0.0",
  "suiteId": "ThermalSuite",
  "suiteVersion": "1.0.0",
  "planId": "SystemValidation",
  "planVersion": "1.2.0",
  "status": "Passed",
  "startTime": "2025-12-24T10:00:00Z",
  "endTime": "2025-12-24T10:01:00Z",
  "exitCode": 0,
  "effectiveInputs": { "DurationSec": 30, "Mode": "A" }
}
```

### 13.3 Error Object

| Field | Description |
|---|---|
| type | Timeout / ScriptError / RunnerError / Aborted |
| source | Script / Runner |
| message | Human-readable |
| stack | Optional stack trace |

Error-to-status mapping:
- If error.type = Timeout, status MUST be Timeout.
- If error.type = Aborted, status MUST be Aborted.
- If error.type = ScriptError or RunnerError, status MUST be Error.
- Failed is reserved for non-exception failures.

### 13.4 Test Suite / Test Plan Summary Result

| Field | Type | Required | Description |
|---|---|---|---|
| schemaVersion | string | Yes | Result schema version |
| runType | enum | Yes | TestSuite / TestPlan |
| suiteId | string | Yes | Suite ID (if runType = TestSuite) |
| suiteVersion | string | Yes | Suite version (if runType = TestSuite) |
| planId | string | Yes | Plan ID (if runType = TestPlan) |
| planVersion | string | Yes | Plan version (if runType = TestPlan) |
| status | enum | Yes | Passed / Failed / Error / Timeout / Aborted |
| startTime | string | Yes | ISO8601 UTC with trailing Z |
| endTime | string | Yes | ISO8601 UTC with trailing Z |
| counts | object | No | Count of child statuses |
| childRunIds | array | Yes | Child runIds (Test Case or Suite, depending on runType) |
| message | string | No | Summary |

Status aggregation rule:
- If the Suite/Plan run terminates due to user Stop or Engine abort, status MUST be Aborted.
- Otherwise: Error > Timeout > Failed > Passed.

---

## 14. Privilege Policy

| privilege | Behavior |
|---|---|
| User | Run as standard user |
| AdminPreferred | Warn if not elevated |
| AdminRequired | Block execution if not elevated |

Rules:
- Privilege enforcement is performed by Engine before execution.
- Suite privilege MUST be computed as the max(child privileges): AdminRequired > AdminPreferred > User.
- Engine MUST precheck privilege before starting a Suite.
- If any AdminRequired and not elevated, Engine MUST reject execution unless an explicit policy allows skipping those cases.

---

## 15. Concurrency and Isolation

- Implementations MAY execute sequentially by default.
- If controls.maxParallel > 1 and parallel execution is not supported, Engine MUST ignore it and record a warning.
- Runner MUST support hard timeout enforcement and full process tree termination.

---

## 16. Out of Scope

- Distributed execution
- Network scheduling
- Web UI

---

END OF SPEC v1.4
