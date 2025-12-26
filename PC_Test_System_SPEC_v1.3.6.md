# PC Test System Specification
## Version 1.3.6

> This version adds Test Plan / Test Suite hierarchy and reporting.
> It also clarifies execution controls, environment injection, and run records.

---

## 1. Project Goal

Develop a **local PC test system running on the target machine**, used to:
- Execute automated hardware and system tests
- Standardize test discovery, parameter configuration, execution, logging, and results
- Deliver test cases as **Manifest + PowerShell scripts**

The system must provide:
- Stable and explicit contracts (Manifest / Result / Folder Layout)
- Clear responsibility boundaries
- Forward-compatible evolution strategy
- Engineering-grade observability and debuggability
- Test Plan / Test Suite hierarchy with parameter override and reporting

---

## 2. Technology Stack

### 2.1 Platform
- Windows 11

### 2.2 Runtime & Languages
- UI: WPF, `net10.0-windows`
- Engine / Runner / Core: `net10.0`
- Script Runtime: PowerShell 7+ (`pwsh.exe`)

> Notes:
> - Always target the latest .NET LTS.
> - PowerShell 5.x is explicitly **not supported**.

### 2.3 Repository Structure

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
    PcTest.UI/                   # WPF app (net10.0-windows)
      PcTest.UI.csproj
      App.xaml
      Views/
      ViewModels/
      Resources/
      ...

    PcTest.Runner/               # runner host (net10.0) - can be console or service-like
      PcTest.Runner.csproj
      RunnerHost.cs              # entry / API facade
      Process/
        PowerShellLocator.cs      # pwsh discovery + version check
        PowerShellRunner.cs       # spawn + capture logs + kill tree
      Storage/
        RunFolderWriter.cs        # create run folder + write index.jsonl
      ...

    PcTest.Engine/               # orchestration (net10.0)
      PcTest.Engine.csproj
      Execution/
        TestExecutor.cs           # load manifest + validate + call runner
      Validation/
        ManifestValidator.cs
        ParameterBinder.cs
      ...

    PcTest.Contracts/            # shared DTOs (net10.0)
      PcTest.Contracts.csproj
      Manifest/
        ManifestModels.cs
      Result/
        ResultModels.cs
      Serialization/
        Json.cs

    PcTest.Cli/                  # optional: CLI runner for CI or lab automation (net10.0)
      PcTest.Cli.csproj

  assets/
    TestCases/
      CpuStress/
        test.manifest.json
        run.ps1
        README.md

  Runs/                          # default output folder (gitignored)
    .gitkeep

  tools/
    signing/                     # optional: script signing helpers
    packaging/                   # optional: msix/installer scripts

  .gitignore
  Directory.Build.props
  pc-test-system.sln
```

Rules:
- The repository structure is normative for reference implementation only.
- Runtime behavior MUST NOT depend on physical source tree layout.
- Runtime stable roots are logical resolved roots: ResolvedTestCaseRoot, ResolvedTestPlanRoot, ResolvedRunsRoot.
- Actual directory names are not required to be fixed; these roots define the only locations Engine/Runner may access at runtime.
- Engine MAY read ResolvedTestPlanRoot at runtime (read-only) to build execution graphs.
- Engine and Runner MUST NOT assume any other fixed locations; all other directories (src/, tools/, docs/) are non-runtime and MUST NOT be accessed by Runner or scripts at execution time.

---

## 3. System Architecture

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

### 3.1 UI Responsibilities
- Test discovery and metadata display
- Parameter input and validation
- Execution control (start / stop)
- Log and result visualization
- Run history browsing

### 3.2 Engine Responsibilities
- Load and validate Manifest
- Resolve execution request (controls, environment, case inputs)
- Bind parameters
- Enforce privilege policy
- Orchestrate execution lifecycle
- Generate Test Suite / Test Plan summary results

### 3.3 Runner Responsibilities (Authoritative)
- Discover and validate `pwsh.exe`
- Enforce execution rules and timeout
- Create Run Folder
- Apply environment injection (process environment and working directory)
- Launch and terminate process tree
- Capture stdout / stderr
- Generate **authoritative result.json** for Test Case runs
- Record execution environment

### 3.4 Execution Authority

Runner is the final authority for Test Case runs:
- Process lifecycle
- Timeout enforcement
- Result generation

Engine must not override Runner execution decisions for Test Case runs.
Engine is the authority for Test Suite / Test Plan summary generation.

---

## 4. Test Case Layout

```
TestCases/
  CpuStress/
    test.manifest.json
    run.ps1
    README.md (optional)
```

Rules:
- Test case folders are immutable during execution
- Discovery is read-only
- All outputs must be written to the Run Folder

Test Case Root Resolution:
- Engine MUST resolve ResolvedTestCaseRoot before discovery.
- Discovery MUST be recursive under the resolved root.
- Test Case Root path is implementation-defined (config / CLI / UI), but MUST be treated as read-only at runtime.

### 4.1 Test Suite and Test Plan Layout

Test Suite and Test Plan are **logical grouping artifacts**:
- A Test Suite contains multiple Test Cases
- A Test Plan contains multiple Test Suites

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
- Suite/Plan folders are immutable during execution
- Discovery is read-only
- Each Suite/Plan is defined by a manifest file in its folder:
  - `suite.manifest.json`
  - `plan.manifest.json`
- Child references inside manifests are resolved relative to the manifest folder.
- Engine MUST normalize/absolutize resolved target paths (e.g., full path) before containment checks.
- After normalization, resolved targets MUST be within ResolvedTestCaseRoot or ResolvedTestPlanRoot; otherwise Engine MUST reject the manifest (e.g., `..\\..\\Windows\\System32`).
- Suite/Plan Root Resolution follows the same rules as Test Case Root Resolution.
- Executing a Test Suite runs all listed Test Cases in order.
- Executing a Test Plan runs all listed Test Suites in order, and each Suite runs its Test Cases in order.

Discovery and ref loading:
- Test Cases enter the execution graph either by discovery under ResolvedTestCaseRoot or by Suite/Plan ref loading.
- Ref-loaded Test Cases are not required to be within the discovery tree, but MUST be within ResolvedTestCaseRoot or ResolvedTestPlanRoot.
- If the UI/Engine exposes an "all test cases list", it MUST define whether it merges discovery and ref-loaded cases; if merged, duplicates MUST be de-duplicated by id+version.

---

## 5. Manifest Specifications

### 5.1 Test Case Manifest Top-Level Fields

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

ID and Folder Rules:
- `id` must be globally unique across all test cases
- Multiple versions of the same `id` are allowed but must not coexist in the same folder
- Test Case folder name is NOT part of the test identity.
- Only `id` + `version` define the test identity.
- Folder renaming MUST NOT affect test identity or history.

Notes:
- `parameters` define Test Case inputs only. Suite/Plan use `controls`, `environment`, and optional `sharedParameters`.

Example:
```json
{
  "schemaVersion": "1.3.6",
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

### 5.2 Parameter Definition

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

---

Note: The `type` list above is the authoritative set of parameter types.
Note: This schema is used by TestCase.parameters and Suite/Plan.sharedParameters.

### 5.3 Test Suite Manifest (`suite.manifest.json`)

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
| sharedParameters | ParameterDefinition[] | No | Shared test case input definitions (same schema as Test Case parameters) |
| testCases | string[] | Yes | List of Test Case folder paths (relative) |

Rules:
- `testCases[]` entries MUST be relative path strings and MUST point to folders containing `test.manifest.json`.
- Order in `testCases[]` is the execution order.
- `controls` and `environment` never require Test Case declarations and are not part of Test Case parameter validation.
- `sharedParameters` (if present) MUST be declared by all referenced Test Cases, and definitions MUST be strictly identical.
- Definition identity MUST include: type, required, min, max, enumValues, pattern, unit, uiHint (when present).
- Definition identity MUST exclude default; default MAY differ across Plan/Suite/TestCase as part of value resolution.
- If any mismatch is detected, Engine MUST fail-fast during Suite load and reject execution.

Example:
```json
{
  "schemaVersion": "1.3.6",
  "id": "ThermalSuite",
  "name": "Thermal Suite",
  "version": "1.0.0",
  "controls": { "repeat": 1, "continueOnFailure": false },
  "environment": { "env": { "LAB_MODE": "1" }, "workingDir": "work" },
  "sharedParameters": [
    { "name": "DurationSec", "type": "int", "required": true, "default": 60 }
  ],
  "testCases": [
    "Cases/CpuStress",
    "Cases/GpuStress"
  ]
}
```

---

### 5.4 Test Plan Manifest (`plan.manifest.json`)

| Field | Type | Required | Description |
|---|---|---|---|
| schemaVersion | string | Yes | Manifest schema version |
| id | string | Yes | Globally unique plan ID |
| name | string | Yes | Plan name |
| description | string | No | Description |
| version | string | Yes | Plan version |
| tags | string[] | No | Tags |
| controls | object | No | Orchestration controls (repeat, maxParallel, continueOnFailure, retryOnError, timeoutPolicy, etc.) |
| environment | object | No | Environment injection (env vars, workingDir, optional runner hints) |
| sharedParameters | ParameterDefinition[] | No | Shared test case input definitions (same schema as Test Case parameters) |
| suites | string[] | Yes | List of Test Suite folder paths (relative) |

Rules:
- `suites[]` entries MUST be relative path strings and MUST point to folders containing `suite.manifest.json`.
- Order in `suites[]` is the execution order.
- `controls` and `environment` never require Test Case declarations and are not part of Test Case parameter validation.
- `sharedParameters` (if present) MUST be declared by all descendant Test Cases, and definitions MUST be strictly identical.
- Definition identity MUST include: type, required, min, max, enumValues, pattern, unit, uiHint (when present).
- Definition identity MUST exclude default; default MAY differ across Plan/Suite/TestCase as part of value resolution.
- If any mismatch is detected, Engine MUST fail-fast during Plan load and reject execution.

Example:
```json
{
  "schemaVersion": "1.3.6",
  "id": "SystemValidation",
  "name": "System Validation",
  "version": "1.2.0",
  "controls": { "continueOnFailure": false },
  "environment": { "env": { "SITE": "LAB-A" } },
  "sharedParameters": [
    { "name": "DurationSec", "type": "int", "required": true, "default": 120 }
  ],
  "suites": [
    "Suites/Thermal",
    "Suites/Storage"
  ]
}
```

---

### 5.5 Controls and Environment Objects (Suite/Plan)

Controls (recommended fields, optional):
- `repeat` (int, default 1)
- `maxParallel` (int, default 1; v1 executes sequentially)
- `continueOnFailure` (bool, default false)
- `retryOnError` (int, default 0)
- `timeoutPolicy` (string, default "AbortOnTimeout")

Environment (recommended fields, optional):
- `env` (object: name -> value)
- `workingDir` (string)
- `runnerHints` (object; optional hints for Runner)

Rules:
- Controls and environment are free-form objects in v1.x; unknown keys MUST be ignored.
- UI/Engine SHOULD warn on unknown keys in controls/environment (best-effort).
- Controls and environment do not participate in Test Case parameter validation.
- If `controls.maxParallel` > 1, Engine MUST ignore it and record a warning (v1 executes sequentially).
- `environment.workingDir` MUST resolve inside the current Run Folder (e.g., `<runFolder>/work`).
- Runner MUST normalize workingDir (e.g., GetFullPath) before containment checks.
- Runner MUST reject any workingDir that resolves outside the Run Folder, including paths with `..` or absolute paths outside. If auto-rewrite is supported, Runner MUST rewrite to `<runFolder>/work` and record a warning.

---

## 6. Parameter Binding and Passing Protocol (Frozen)

### 6.1 Parameter/Context Resolution

Execution inputs are split into three categories:
- `controls`: orchestration only (Engine); never passed to scripts.
- `environment`: process environment injection (Runner); flows downward to all child runs.
- Case inputs: `parameters` (Test Case) and optional `sharedParameters` (Suite/Plan).

Definition resolution (case input definitions):
- If `sharedParameters` is present, every referenced Test Case MUST declare the same parameter name.
- Definitions MUST be strictly identical across Suite/Plan and Test Case, at least for: type, required, min, max, enumValues, pattern, unit, uiHint (when present).
- Definition identity MUST exclude default; defaults MAY differ across Plan/Suite/TestCase.
- If any mismatch is detected, Engine MUST fail-fast during Suite/Plan load and reject execution.
- The effective definition used for UI and validation is the identical sharedParameters/TestCase definition; defaults come from the value resolution chain.

Value resolution (effective values; last wins):
- Controls: Plan.controls -> Suite.controls -> Run Request overrides (plan/suite/case scope).
- Environment: Runner base env (lowest) -> Plan.environment -> Suite.environment -> Case-level overrides -> Runner enforced entries (highest, if any).
- Case inputs: Plan.sharedParameters default -> Suite.sharedParameters default -> TestCase.parameters default -> Run Request caseInputs overrides.
- `sharedParameters` defaults are default value sources, not runtime override dictionaries.

Rules:
- Case input values are validated against the effective Test Case definition; unknown names are rejected.
- `controls` and `environment` are not part of Test Case parameter validation.
- UI SHOULD display defaults based on the effective default after value resolution.

### 6.2 Execution Request / Run Request

Execution Request is the contract by which UI/CLI supplies values for controls, environment, and case input overrides.
Minimum v1 support: plan-level values plus optional suite-level and case-level overrides.

Fields (logical model):
- `target`: { type: TestPlan | TestSuite | TestCase, id, version or path }
- `controls`: object (applies to target scope)
- `environment`: object (applies to target scope)
- `caseInputs`: object (values for sharedParameters or Test Case parameters)
- `suiteOverrides`: map of "<suiteId>@<suiteVersion>" -> { controls?, environment?, caseInputs? }
- `caseOverrides`: map of "<testId>@<testVersion>" -> { environment?, caseInputs? }

Notes:
- `caseInputs` at Plan/Suite scope apply only to `sharedParameters` defined at that scope.
- When multiple versions of the same id exist, Engine MUST match overrides by exact id + version.
- If id is used (not path), version MUST be provided; otherwise Engine MUST reject the request.
- Overrides apply to all occurrences of the same id@version in the execution graph; per-node overrides are not supported in v1 and are reserved for v2.

Example:
```json
{
  "target": { "type": "TestPlan", "id": "SystemValidation", "version": "1.2.0" },
  "controls": { "continueOnFailure": false },
  "environment": { "env": { "LAB_MODE": "1" } },
  "caseInputs": { "DurationSec": 60 },
  "suiteOverrides": {
    "ThermalSuite@1.0.0": {
      "controls": { "repeat": 2 },
      "environment": { "workingDir": "work" }
    }
  },
  "caseOverrides": {
    "CpuStress@2.0.0": { "caseInputs": { "Mode": "A" } }
  }
}
```

### 6.3 Passing Protocol

All case inputs **MUST** be passed as named PowerShell parameters.

Rules:
- Format: `-<Name> <Value>`
- Array parameters (string[]/int[]/enum[]) MUST be passed as repeated values: `-Modes "A" "B"`.
- Runner MUST use `ProcessStartInfo.ArgumentList` and append each element separately (e.g., `"-Modes", "A", "B"`).
- Boolean values: `$true` / `$false`
- Numeric serialization uses invariant culture (decimal point)
- Strings and paths MUST be passed safely (no shell parsing). If serialized to PowerShell literals, use single quotes and double any embedded `'`.
- Runner MUST avoid command-line string concatenation to prevent quote/newline/backtick parsing issues.
- Environment variables are injected via process environment, not as script parameters.
- Missing optional parameters are omitted (not passed as null)

Example:
```powershell
pwsh.exe run.ps1 -DurationSec 30 -Modes "A" "B"
```

This protocol is **non-breaking and immutable** within schema v1.x.

### 6.4 Suite/Plan Execution Policy

Defaults:
- `continueOnFailure` = false

Rules:
- Failed Test Cases may continue only when `continueOnFailure` is true.
- Error / Timeout / Aborted MUST abort the current Suite/Plan.
- User Stop is treated as Abort; Engine and Runner MUST report status = Aborted.

---

## 7. PowerShell Execution Rules

- Runner invokes `pwsh.exe`
- Version must be >= 7.0
- Working directory is the Run Folder unless overridden by environment.workingDir (which MUST resolve inside the Run Folder)
- Script must be treated as untrusted code

### 7.1 Exit Code Convention

| Exit Code | Meaning |
|---|---|
| 0 | Success |
| 1 | Test Failed |
| 2 | Script Error |
| 3 | Timeout / Aborted |

Exit codes are **advisory**; final status is determined by Runner.
Exit code 3 MAY represent Timeout or Aborted; Runner MUST set status accordingly.

### 7.2 Trust Boundary:

- Scripts run with the same OS privileges as Runner.
- Scripts MUST be treated as untrusted input.
- Runner MUST NOT assume cooperative script behavior.
  
---

## 8. Run Folder Layout

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

### 8.1 Run Folder Ownership Rules:
- Test Case Run Folders are exclusively owned by Runner.
- Test Suite / Test Plan Run Folders are exclusively owned by Engine.
- Scripts MUST NOT create, delete, or rename top-level files in the Run Folder.
- Scripts MAY write additional files only under:
    {RunId}/artifacts/
- Runner MUST ignore unknown files and folders.
- stdout.log, stderr.log, env.json are only present for Test Case runs.

### 8.2 Immutability Rules:
- manifest.json and params.json are snapshots generated by Engine/Runner.
- For Test Case runs, params.json contains effective case inputs passed to the script.
- For Test Suite / Test Plan runs, manifest.json MUST include the original manifest snapshot plus (optional) resolved refs and (recommended) effectiveControls/effectiveEnvironment.
- For Test Suite / Test Plan runs, params.json (if present) contains effective sharedParameters used for child runs.
- Scripts MUST NOT modify these files.
- Runner MAY validate immutability for debugging purposes.

### 8.3 index.jsonl
- One JSON object per line
- Fields:
  - runId
  - runType (TestCase / TestSuite / TestPlan)
  - testId (required for TestCase)
  - testVersion (required for TestCase)
  - suiteId (required for TestSuite; MUST be set for TestCase when parentRunId refers to a TestSuite run)
  - suiteVersion (required for TestSuite; MUST be set for TestCase when parentRunId refers to a TestSuite run)
  - planId (required for TestPlan; MUST be set for TestSuite/TestCase when parentRunId refers to a TestPlan run)
  - planVersion (required for TestPlan; MUST be set for TestSuite/TestCase when parentRunId refers to a TestPlan run)
  - parentRunId (optional; points to the Suite/Plan run)
  - startTime, endTime (ISO8601 UTC with trailing Z), status (Passed / Failed / Error / Timeout / Aborted)

Rule:
- If parentRunId is present, the corresponding suite/plan identity fields MUST be populated based on the parent runType.

Example:
```json
{"runId":"R-001","runType":"TestCase","testId":"CpuStress","testVersion":"2.0.0","suiteId":"ThermalSuite","suiteVersion":"1.0.0","planId":"SystemValidation","planVersion":"1.2.0","parentRunId":"R-100","startTime":"2025-12-24T10:00:00Z","endTime":"2025-12-24T10:01:00Z","status":"Passed"}
```

### 8.4 env.json
Records execution environment snapshot:
- OS version
- Runner version
- PowerShell version
- Elevation state

### 8.5 children.jsonl
- Only present for Test Suite / Test Plan runs
- Each line is a JSON object with child runId, id, version, and status
- Test Suite children use testId/testVersion; Test Plan children use suiteId/suiteVersion
- startTime, endTime, durationSec are optional
- Order must match manifest execution order

Examples:
```json
{"runId":"R-201","testId":"CpuStress","testVersion":"2.0.0","status":"Passed","startTime":"2025-12-24T10:00:00Z","endTime":"2025-12-24T10:01:00Z"}
{"runId":"R-301","suiteId":"ThermalSuite","suiteVersion":"1.0.0","status":"Passed"}
```

### 8.6 events.jsonl (Optional):
- Each line is a JSON object representing a timestamped event.
- Event producers may include Runner or Script.
- The schema is intentionally unspecified in v1.x.
- Consumers MUST treat events as best-effort diagnostic data only.

---

## 9. Result Specification (`result.json`)

### 9.1 Authority Rule (Critical)

> **Runner is the sole authority of `result.json` for Test Case runs.**  
> **Engine is the authority of `result.json` for Test Suite / Test Plan runs.**
> Script-generated results are treated as input and must be validated.

### 9.2 Top-Level Fields

| Field | Type | Required | Description |
|---|---|---|---|
| schemaVersion | string | Yes | Result schema version |
| runType | enum | No | TestCase / TestSuite / TestPlan (default TestCase) |
| testId | string | Yes (TestCase only) | Test ID |
| testVersion | string | Yes (TestCase only) | Test version |
| suiteId | string | Yes (TestSuite only; MUST for TestCase when Engine provides suite identity) | Suite ID |
| suiteVersion | string | Yes (TestSuite only; MUST for TestCase when Engine provides suite identity) | Suite version |
| planId | string | Yes (TestPlan only; MUST for TestCase when Engine provides plan identity) | Plan ID |
| planVersion | string | Yes (TestPlan only; MUST for TestCase when Engine provides plan identity) | Plan version |
| status | enum | Yes | Passed / Failed / Error / Timeout / Aborted |
| startTime | string | Yes | ISO8601 UTC with trailing Z |
| endTime | string | Yes | ISO8601 UTC with trailing Z |
| metrics | object | No | Metrics |
| message | string | No | Summary |
| exitCode | int | No | Script exit code |
| effectiveInputs | object | Yes (TestCase only) | Effective case inputs passed to the script |
| error | object | No | Error details |
| runner | object | No | Runner metadata |

Rule:
- For Test Case runs in plan context, when Engine provides suite/plan identity, Runner MUST write suiteId/suiteVersion and planId/planVersion into result.json.

Example:
```json
{
  "schemaVersion": "1.3.6",
  "runType": "TestCase",
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

Note: Aborted indicates user stop or Engine-driven abort.

### 9.3 Error Object

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
- Failed is reserved for non-exception failures (e.g., script assertions with normal exit).

### 9.4 Result Priority Rules

1. Valid `result.json` generated by Runner is authoritative for Test Case runs
2. If script fails or crashes, Runner generates fallback result
3. Exit code alone must never override Runner decision

### 9.5 Test Suite / Test Plan Summary Result

Suite/Plan `result.json` is an aggregated summary over child Test Case runs.

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
- If the Suite/Plan run terminates due to user Stop or Engine abort, status MUST be Aborted and no severity comparison is performed.
- Otherwise: Error > Timeout > Failed > Passed.

Example:
```json
{
  "schemaVersion": "1.3.6",
  "runType": "TestSuite",
  "suiteId": "ThermalSuite",
  "suiteVersion": "1.0.0",
  "status": "Aborted",
  "startTime": "2025-12-24T10:00:00Z",
  "endTime": "2025-12-24T10:01:30Z",
  "childRunIds": ["R-201", "R-202"]
}
```

---

## 10. Privilege Policy

| privilege | Behavior |
|---|---|
| User | Run as standard user |
| AdminPreferred | Warn if not elevated |
| AdminRequired | Block execution if not elevated |

Privilege enforcement is performed by Engine before execution.

Plan/Suite privilege rules:
- Effective privilege = max(child privileges): AdminRequired > AdminPreferred > User.
- Engine MUST precheck privilege before starting a Suite/Plan.
- If any AdminRequired and not elevated, default behavior is to reject execution (unless an explicit policy allows skipping those cases).
- UI MAY surface the computed privilege requirement before execution.

---

## 11. Concurrency & Isolation

- v1 executes tests sequentially
- If `controls.maxParallel` > 1, Engine MUST ignore it and record a warning.
- Runner must support:
  - Hard timeout enforcement
  - Full process tree termination
- Reserved for future:
  - `exclusive`
  - `requiredResources`

---

## 12. Compatibility Strategy

- Runner must support schemaVersion v1.x
- Unknown fields must be ignored
- Breaking changes require schemaVersion v2.0

---

## 13. Out of Scope (v1.x)

- Distributed execution
- Network scheduling
- Web UI

---

END OF SPEC v1.3.6
