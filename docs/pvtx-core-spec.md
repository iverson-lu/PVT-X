# PC Test System Specification
## Version 1.5.1

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
- Test Suite: The primary execution unit; defines an ordered pipeline of Test Case runs (v1 has no DAG dependencies).
- Test Plan: A lightweight composition layer that only references Test Suites.
- RunRequest: The runtime override payload for a specific run.
- Standalone TestCase run: Direct execution of a Test Case outside any Suite pipeline.

### 2.2 Principles (Normative)
- Test Suite is the primary execution unit. A Suite MUST define an ordered pipeline (no DAG) of Test Case runs.
- A Suite node identifies one execution node; nodeId identifies the node, not the Test Case definition.
- A Suite MUST store per-node default inputs for its nodes (optional on each node).
- Test Plan MUST only reference Test Suites. A Plan MUST NOT define or override Test Case inputs.
- Test Case MUST define capability only. TestCase.parameters MUST define schema and defaults.
- For suite-triggered TestCase run, all concrete runtime values MUST come from Suite node inputs and/or RunRequest overrides.
- For standalone TestCase run, all concrete runtime values MUST come from TestCase defaults and/or RunRequest.caseInputs.
- The system MUST support standalone TestCase run.
- A standalone TestCase run MUST NOT belong to any Suite pipeline and MUST NOT use node-level inputs.

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
- ResolvedTestSuiteRoot contains standalone Suite definitions and is independent of ResolvedTestPlanRoot.
- Engine and Runner MUST NOT assume other fixed locations at runtime.

### 3.4 Shared PowerShell Modules

Common PowerShell utilities for Test Cases are maintained under `assets/PowerShell/Modules/`.

- Engine computes AssetsRoot from Discovery (parent of ResolvedTestCaseRoot) and passes to Runner
- Runner derives `PVTX_MODULES_ROOT` as `<AssetsRoot>/PowerShell/Modules` and injects into environment
- Runner prepends `PVTX_MODULES_ROOT` to `PSModulePath` before Test Case execution
- Test Cases import modules via standard PowerShell syntax: `Import-Module <ModuleName>`
- Modules are auto-discovered; no explicit path specification required

Shared modules provide reusable utilities for hardware info, validation, logging, and common test operations.

See `pvtx-test-authoring_rules.md` for Test Case usage patterns.

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
- Resolve Suite pipelines (ordered, non-DAG)
- Bind parameters and compute effective inputs
- Compute AssetsRoot from Discovery (parent of ResolvedTestCaseRoot) and pass to Runner
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
  hw.bios.version_check/
    test.manifest.json
    run.ps1
    README.md (optional)
```

Rules:
- Test Case folder name SHOULD follow `id@version` format matching manifest id and version (e.g., `hw.bios.version_check@1.0.0`). Discovery relies on manifest content, not folder name; this convention aids version distinction and consistency.
- Test Case folders are immutable during execution.
- Discovery is read-only.
- All outputs MUST be written to the Run Folder.

Test Case Root Resolution:
- Engine MUST resolve ResolvedTestCaseRoot before discovery.
- Discovery MUST be recursive under the resolved root.
- ResolvedTestCaseRoot MUST be treated as read-only at runtime.

### 5.2 Test Suite and Test Plan Layout

Example (repository organization only; Suites are independent of Plans and MUST NOT embed Test Cases):
```
TestSuites/
  Thermal/
    suite.manifest.json

TestPlans/
  SystemValidation/
    plan.manifest.json
```

Rules:
- Suite and Plan folders are immutable during execution.
- Discovery is read-only.
- Suite and Plan manifests are:
  - suite.manifest.json
  - plan.manifest.json
- Child references inside manifests MUST be resolved relative to the manifest folder, except:
  - Suite.testCases[].ref MUST be resolved against ResolvedTestCaseRoot (not against the Suite folder).
  - Plan.suites entries MUST be resolved by identity (suiteId@version) against discovered Suites under ResolvedTestSuiteRoot; Plan MUST NOT rely on relative paths.
- Suite.manifest MUST NOT embed or carry any Test Case content; all Suite nodes MUST reference Test Cases located under ResolvedTestCaseRoot.
- For Suite manifests, ref resolution MUST normalize the target path, resolve the manifest at `<ResolvedTestCaseRoot>/<ref>/test.manifest.json`, and MUST ensure the resolved path is contained by ResolvedTestCaseRoot. Engine MUST fail validation with a single error code "Suite.TestCaseRef.Invalid" when ref resolution fails; the error payload MUST include entityType, suitePath, ref, resolvedPath, expectedRoot, and reason (OutOfRoot / NotFound / MissingManifest).
- For Plan manifests, Suite refs MUST resolve by identity within ResolvedTestSuiteRoot; not found MUST fail validation, and multiple matches MUST fail with an error containing entityType, id, version, and conflictPaths.
- Engine MUST normalize resolved target paths before containment checks.
- On Windows, containment checks MUST use canonical absolute paths (e.g., GetFullPath) with case-insensitive comparison. Junctions/symlinks/reparse points encountered during ref resolution MUST be resolved; if resolution fails or escapes the expected root, validation MUST fail with reason OutOfRoot.

### 5.3 Identity Uniqueness (Normative)

- Within a single discovery pass over the resolved roots (ResolvedTestCaseRoot, ResolvedTestSuiteRoot, ResolvedTestPlanRoot), each id@version pair for TestCase, TestSuite, and TestPlan MUST be unique.
- On duplicate identities, Engine MUST fail discovery/validation with an error whose payload MUST include: entityType (TestCase/TestSuite/TestPlan), id, version, and conflictPaths[] (all manifest paths contributing the duplicate).
- RunRequest resolution MUST rely on this uniqueness; if uniqueness is violated, downstream RunRequest resolution MUST fail rather than picking an arbitrary manifest.

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
- Folder name MUST NOT affect identity (discovery reads manifest, not folder name).
- parameters define schema and defaults only; no concrete runtime values are stored here.
- Test Case manifests MUST NOT define environment sections; any such fields MUST be ignored at discovery and execution time.

Example:
```json
{
  "schemaVersion": "1.5.0",
  "id": "CpuStress",
  "name": "CPU Stress",
  "category": "Thermal",
  "version": "2.0.0",
  "privilege": "User",
  "timeoutSec": 600,
  "parameters": [
    { "name": "DurationSec", "type": "int", "required": true, "default": 30 },
    { "name": "Mode", "type": "enum", "required": false, "enumValues": ["A", "B"] },
    { "name": "ModesJson", "type": "json", "required": false, "default": "[\"A\", \"B\"]", "help": "JSON array of modes" }
  ]
}
```

---

### 6.2 Parameter Definition

| Field | Type | Required | Description |
|---|---|---|---|
| name | string | Yes | Parameter name |
| type | string | Yes | Supported: int, double, string, boolean, path, file, folder, enum, json |
| required | boolean | Yes | Required or not |
| default | any | No | Default value |
| min / max | number | No | Numeric range |
| enumValues | string[] | No | Enum candidates |
| unit | string | No | Display unit |
| uiHint | string | No | textbox / dropdown / filePicker / folderPicker / textarea |
| pattern | string | No | Regex validation |
| help | string | No | Help text |

**Supported Parameter Types (Whitelist):**
- `int` - Integer number
- `double` - Floating-point number
- `string` - Text
- `boolean` - True/false value (CLI uses `-Flag:$true` / `-Flag:$false`)
- `enum` - String value from enumValues list
- `path` - File system path (string)
- `file` - File path (string)
- `folder` - Folder path (string)
- `json` - JSON-encoded string (object or array)

**Array Types are NOT Supported:**
- Do NOT use `int[]`, `double[]`, `string[]`, `boolean[]`, etc.
- For complex structures (arrays, objects), use `json` type instead.

**JSON Parameter Notes:**
- `json` type is transported as string containing JSON-encoded data.
- Engine and Runner do NOT parse or validate JSON content.
- Test Case PowerShell scripts MUST handle JSON parsing explicitly using `ConvertFrom-Json`.
- Use `json` type for any structured data (arrays, objects, nested structures).

**Type Conversion Rules:**
- `int` and `double` parse using invariant culture (dot decimal separator).
- `boolean` parses `true`/`false`/`1`/`0` (case-insensitive).
- `enum` values (whether literal or from EnvRef) MUST be in `enumValues` list.
- `path`/`file`/`folder`: relative values resolve against `environment.workingDir` (inside Run Folder); if `workingDir` absent, resolve against Case Run Folder root.
- `json`: passed as-is, no parsing or validation by Engine/Runner.

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
| testCases | TestCaseNode[] | Yes | Ordered pipeline nodes |

TestCaseNode schema:
```
{
  "nodeId": "string (required, unique within suite)",
  "ref": "folder ref under ResolvedTestCaseRoot containing test.manifest.json",
  "inputs": { "...": "..." }
}
```

Rules:
- testCases MUST be an ordered array that defines the pipeline (no branching in v1).
- nodeId identifies the execution node, not the Test Case definition.
- nodeId SHOULD use id@version format (e.g., `hw.cpu.stress@1.0.0`). Multiple instances MAY append `_1`, `_2` suffixes.
- The same Test Case (id@version) MAY appear multiple times with different nodeId and inputs.
- ref MUST resolve to a folder under ResolvedTestCaseRoot, and the manifest MUST be located at `<ResolvedTestCaseRoot>/<ref>/test.manifest.json` after normalization. The resolution algorithm is: normalize ref relative to ResolvedTestCaseRoot; reject if the normalized path escapes the root; load the manifest at the normalized location.
- If ref resolution fails (OutOfRoot, NotFound, or MissingManifest), Engine MUST fail validation with code "Suite.TestCaseRef.Invalid" and a message that includes suite path, ref, resolvedPath, expectedRoot, and reason (OutOfRoot / NotFound / MissingManifest). OutOfRoot applies when the normalized path escapes the root; NotFound applies when the target folder does not exist; MissingManifest applies when the folder exists but test.manifest.json is missing.
- inputs, when present, MUST only include parameter names declared by the referenced Test Case.
- Suite manifests MUST NOT embed or copy Test Case content; all Test Case manifests live under ResolvedTestCaseRoot.
- Suite.environment.env values MUST be strings; empty keys MUST be rejected during validation.

Example:
```json
{
  "schemaVersion": "1.5.0",
  "id": "suite.thermal.stress",
  "name": "Thermal Stress Suite",
  "version": "1.0.0",
  "controls": { "repeat": 1, "continueOnFailure": false },
  "environment": { "env": { "LAB_MODE": "1" }, "workingDir": "work" },
  "testCases": [
    {
      "nodeId": "hw.cpu.stress@1.0.0",
      "ref": "CPU Stress Test",
      "inputs": { "DurationSec": 30, "Mode": "A" }
    },
    {
      "nodeId": "hw.cpu.stress@1.0.0_1",
      "ref": "CPU Stress Test",
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
| environment | object | No | Environment injection (env vars only) |
| testSuites | TestSuiteNode[] | Yes | List of Test Suite nodes |

#### TestSuiteNode Schema

| Field | Type | Required | Description |
|---|---|---|---|
| nodeId | string | Yes | Suite identity in the form `suiteId@version` |
| ref | string | No | Human-readable reference name for this node instance; allows distinguishing multiple instances of the same suite |
| controls | object | No | Execution control overrides for this suite instance |

Rules:
- A Plan MUST only reference Test Suites via testSuites array.
- testSuites[].nodeId MUST be suite identities in the form `suiteId@version`.
- Engine MUST resolve each nodeId against discovered Test Suites under ResolvedTestSuiteRoot; if not found, validation MUST fail; if multiple matches are found, validation MUST fail with an error containing entityType=suite, id, version, and conflictPaths.
- Order in testSuites defines execution order.
- The same suite MAY appear multiple times in testSuites with different refs and/or controls.
- testSuites[].ref is used to distinguish multiple instances of the same suite; if not provided, the suite name is used.
- testSuites[].controls MAY override suite-level execution controls (repeat, maxParallel, continueOnFailure, retryOnError, timeoutPolicy); plan-level overrides take precedence over suite defaults.
- A Plan MUST NOT define or override Test Case inputs, Suite.nodeInputs, or any per-node parameters.
- A Plan MUST NOT embed Test Suites or Test Cases.
- If environment is present in the Plan manifest, its env map participates in Effective Environment as defined in section 7.3; values MUST be strings and keys MUST be non-empty.
- Plan environment MUST be env-only; presence of any other key (e.g., workingDir, runnerHints) MUST fail validation.
- Plan environment MUST NOT introduce per-node mappings; it applies uniformly to all Suites executed under the Plan.

Example:
```json
{
  "schemaVersion": "1.5.0",
  "id": "SystemValidation",
  "name": "System Validation",
  "version": "1.2.0",
  "testSuites": [
    {
      "nodeId": "ThermalSuite@1.0.0",
      "ref": "Thermal Test"
    },
    {
      "nodeId": "StorageSuite@1.0.0",
      "ref": "Storage Test",
      "controls": {
        "repeat": 3,
        "continueOnFailure": true
      }
    },
    {
      "nodeId": "ThermalSuite@1.0.0",
      "ref": "Thermal Retest",
      "controls": {
        "retryOnError": 2
      }
    }
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
- The "unknown keys MUST be ignored" rule applies to Suite.controls and Suite.environment only. Plan.environment remains strict env-only and MUST reject any other key per section 6.4.
- UI/Engine SHOULD warn on unknown keys in controls/environment (best-effort).
- environment.workingDir MUST resolve inside the current Run Folder. For Suite.environment.workingDir (and any future Plan-level workingDir), the effective cwd for each Test Case run MUST be rooted at that node's Case Run Folder (CaseRunId), not the Suite/Plan GroupRunId folder.
- Runner MUST normalize workingDir before containment checks and MUST ensure the directory exists (create if missing) before starting the script process.
- Runner MUST reject any workingDir that resolves outside the Case Run Folder.
- On Windows, containment checks MUST use canonical absolute paths (e.g., GetFullPath) with case-insensitive comparison. Junctions/symlinks/reparse points MUST be resolved before the containment check; if resolution fails or escapes the Case Run Folder, the runner MUST fail validation for that node.
- If controls.maxParallel > 1 and the implementation only executes sequentially, Engine MUST ignore it and record a warning entry with code "Controls.MaxParallel.Ignored", location "suite.manifest.json", and message describing the ignored value; the warning MUST be persisted in events.jsonl or equivalent run log.

---

## 7. Inputs Resolution and Validation

### 7.1 Effective Inputs

For suite-triggered TestCase run, effectiveInputs MUST be computed in the following order (last wins):
1. TestCase.parameters.default
2. Suite.testCases[node].inputs
3. RunRequest.nodeOverrides[nodeId].inputs (if present)

For standalone TestCase run, effectiveInputs MUST be computed in the following order (last wins):
1. TestCase.parameters.default
2. RunRequest.caseInputs (if present)

Plan does not participate in inputs resolution.

Rules:
- Plan RunRequest MUST NOT introduce any additional input override layer; attempts MUST be rejected during validation.

### 7.2 Validation Rules
- Each input name MUST exist in the referenced Test Case parameters.
- Unknown input names MUST be rejected.
- Required parameters without a resolved value MUST cause validation failure before execution.
- The UI SHOULD display the effective defaults based on this resolution order.

### 7.2.1 Validation Stages (Normative)
- Static validation (discovery/loading time): schema correctness, required fields, type membership, enumValues presence, EnvRef shape, and identity parsing MUST be validated without requiring any filesystem targets to exist.
- Pre-node validation (immediately before executing a node/run): path/file/folder resolution MUST occur using the node's effective workingDir and Case Run Folder (CaseRunId) as the root; file/folder types MUST exist at this stage, and containment checks MUST be enforced. Path types MAY point to non-existent targets.
- Runner MUST perform (or re-check) pre-node validation for filesystem-dependent rules (existence/containment/workingDir-based resolution) using the resolved workingDir for that node.
- If pre-node validation fails, Runner MUST NOT start the script process, MUST produce result.json with status=Error, and MUST classify the error as Runner-side (e.g., error.type=RunnerError per section 11.2) before exiting.
- Any validation rule that requires knowing the node's workingDir or Run Folder (e.g., existence checks) MUST be evaluated during pre-node validation, not during static validation.

### 7.3 Environment Resolution (Normative)
- Effective Environment is the single authoritative rule set:
  - Plan run: Plan RunRequest.environmentOverrides.env > Plan manifest environment.env (if present) > Suite manifest environment.env > OS environment.
  - Suite run (without Plan): Suite RunRequest.environmentOverrides.env > Suite manifest environment.env > OS environment.
  - Standalone TestCase run: Case RunRequest.environmentOverrides.env > OS environment.
- Same-name keys MUST be overwritten by the higher-priority layer. All environment values MUST be strings. Empty keys or whitespace-only keys MUST fail validation. Duplicate keys are resolved deterministically by the precedence above.
- Test Case manifests MUST NOT define environment blocks; any such fields MUST be ignored.

### 7.4 Environment Variable References (EnvRef)
- Any input value (Suite.node.inputs, RunRequest.caseInputs, RunRequest.nodeOverrides.inputs) MAY be either a JSON literal consistent with ParameterDefinition.type (string/int/double/boolean/enum/path/file/folder/string[]/int[]/double[]/boolean[]/enum[]/path[]/file[]/folder[]) or an EnvRef object with the shape:
  - `$env` (string, required): environment variable name to read; MUST be non-empty.
  - `default` (any, optional): literal fallback when the env variable is missing or empty (per empty definition below).
  - `required` (boolean, optional, default false): if true and the env variable is missing/empty (per empty definition below) and no default is provided, validation MUST fail before execution.
  - `secret` (boolean, optional, default false): if true, Engine/Runner MUST redact the value in logs, summaries, manifest snapshots, and index entries (e.g., replace with `***`); execution MUST still use the real value.
- Empty is defined as the env variable value being null or the empty string (`""`). Whitespace-only strings are NOT treated as empty unless callers trim them before setting the variable. This empty definition applies to all "missing or empty" checks in this specification.
- Resolution process:
  - Engine MUST first compute the Effective Environment (section 7.3) for the run context.
  - Engine MUST then resolve all EnvRef values into concrete literals before invoking Runner.
  - If resolution fails (missing required env, parse failure, or type conversion failure), Engine MUST fail validation with a deterministic error (e.g., "EnvRef.ResolveFailed") including the parameter name and nodeId when applicable.
- Type conversion rules (env values originate as strings):
  - string: use the env string as-is (after optional redaction for secret).
  - int: MUST parse using invariant culture; on failure MUST fail validation.
  - double: MUST parse using invariant culture (dot decimal); on failure MUST fail validation.
  - boolean: MUST accept true/false/1/0 (case-insensitive); otherwise MUST fail validation.
  - string[]/int[]/double[]/boolean[]/enum[]: MUST parse as JSON array of the corresponding primitive type; empty arrays MUST be accepted; on failure MUST fail validation.
  - For enum and enum[] targets, Engine MUST validate the resolved values are contained in enumValues; otherwise validation MUST fail.
  - If the target parameter type is not declared, treat as string.
- Structured payloads (objects) are NOT supported by ParameterDefinition.type; if structured data is needed, callers MUST use type "string" and encode JSON/text, and scripts are responsible for parsing.
- EnvRef is a value source only; it MUST NOT introduce new override layers or per-node mappings beyond existing inputs semantics.
- Engine MUST send Runner both (a) effectiveInputs and effectiveEnvironment with real, resolved values for execution, and (b) metadata identifying which inputs/environment entries are secret (e.g., inputMeta or redactionMap). Runner MUST use the real values for process execution, but MUST apply the redaction map when persisting manifest.json, params.json, result.json, events.jsonl, stdout/stderr, and any other externally visible artifacts.

Security Note (Normative):
- secret=true requires redaction only in artifacts/logs/snapshots/index; v1 still passes parameters via command-line arguments, so the OS/process list/diagnostic tools MAY observe the raw values.
- When any secret=true input would be passed via command-line, Engine/Runner MUST emit a warning with code "EnvRef.SecretOnCommandLine", include the affected parameter name (and nodeId when applicable), and record it in the run log (e.g., events.jsonl or equivalent).
- Sensitive information SHOULD be provided via environment variables pointing to credential files/paths rather than passing raw secret values directly.

### 7.5 Predefined Environment Variables (Normative)

Runner MUST inject the following predefined environment variables into every Test Case execution. These variables are automatically added to the effective environment after applying all environment resolution rules per section 7.3, and MUST be included in the manifest.json snapshot.

| Variable Name       | Type   | Description                                        | Example Value                                      |
|---------------------|--------|----------------------------------------------------|----------------------------------------------------|
| PVTX_TESTCASE_PATH  | string | Absolute path to the Test Case source folder       | `D:\Dev\PVT-X-1\assets\TestCases\ReadFileCase`    |
| PVTX_TESTCASE_NAME  | string | Display name from Test Case manifest (name field)  | `Read File Case`                                   |
| PVTX_TESTCASE_ID    | string | Unique identifier from Test Case manifest (id)     | `ReadFileCase`                                     |
| PVTX_TESTCASE_VER   | string | Version string from Test Case manifest (version)   | `1.0.0`                                            |
| PVTX_ASSETS_ROOT    | string | Absolute path to the assets root directory         | `D:\Dev\PVT-X-1\assets`                            |
| PVTX_MODULES_ROOT   | string | Absolute path to PowerShell modules directory      | `D:\Dev\PVT-X-1\assets\PowerShell\Modules`        |
| PVTX_RUN_ID         | string | RunId for this Test Case execution                 | `R-2025-01-15-0001`                                |
| PVTX_PHASE          | int    | Reboot resume phase (0 = initial execution)        | `0`                                                |
| PVTX_CONTROL_DIR    | string | Control directory for reboot requests              | `D:\Dev\PVT-X-1\Runs\R-2025-01-15-0001\control`     |

Rules:
- These variables MUST be injected by Runner immediately before script execution.
- If any of these variable names already exist in the effectiveEnvironment computed per section 7.3, Runner MUST overwrite them with the correct predefined values (predefined variables take precedence).
- Scripts MAY access these variables via standard PowerShell syntax: `$env:PVTX_TESTCASE_PATH`, `$env:PVTX_TESTCASE_NAME`, `$env:PVTX_TESTCASE_ID`, `$env:PVTX_TESTCASE_VER`, `$env:PVTX_ASSETS_ROOT`, `$env:PVTX_MODULES_ROOT`, `$env:PVTX_RUN_ID`, `$env:PVTX_PHASE`, `$env:PVTX_CONTROL_DIR`.
- These variables are NOT secret and MUST NOT be redacted in artifacts.
- These variables MUST appear in the manifest.json effectiveEnvironment snapshot for full reproducibility and traceability.
- The PVTX_TESTCASE_PATH value MUST be the absolute, normalized path to the Test Case source folder (containing test.manifest.json and run.ps1), NOT the Case Run Folder.
- The PVTX_ASSETS_ROOT value MUST be computed by Engine as the parent directory of ResolvedTestCaseRoot and passed to Runner via RunContext. Engine MUST compute this value from Discovery (specifically, Directory.GetParent(discovery.ResolvedTestCaseRoot)).
- The PVTX_MODULES_ROOT value MUST be computed by Runner as `Path.Combine(AssetsRoot, "PowerShell", "Modules")` where AssetsRoot is provided by Engine in RunContext.
- PVTX_RUN_ID MUST be the Case RunId; for reboot-resume flows, the same RunId and Case Run Folder MUST be reused across phases.
- PVTX_PHASE MUST default to 0 for the initial execution; when resuming after a reboot request, Runner MUST set PVTX_PHASE to the requested nextPhase value.
- PVTX_CONTROL_DIR MUST be created by Runner before execution and MUST be a subfolder of the Case Run Folder; Runner MAY clear it before each phase to avoid stale control files.

Rationale:
- Enables Test Cases to reference companion files (test data, reference outputs, configuration files) relative to their source folder.
- Provides Test Cases with self-awareness for logging, reporting, and dynamic behavior.
- Ensures full traceability by recording these values in manifest.json.
- PVTX_RUN_ID and PVTX_PHASE enable multi-phase reboot/resume flows without losing run identity.
- PVTX_CONTROL_DIR provides a dedicated control channel for test-requested actions such as reboot.
- PVTX_ASSETS_ROOT and PVTX_MODULES_ROOT enable Test Cases to access shared PowerShell helper modules.
- Runner MUST prepend PVTX_MODULES_ROOT to the PSModulePath environment variable to enable PowerShell module auto-discovery and import.
- This allows Test Cases to use `Import-Module <ModuleName>` to load common utilities, without specifying full paths.
- Shared modules under assets/PowerShell/Modules/ MAY provide reusable functionality such as hardware info collection, system state validation, logging helpers, or data formatting utilities.

---

## 8. RunRequest

RunRequest SHOULD primarily target a Test Suite but MUST support standalone TestCase run and Test Plan run.

### 8.1 Identity Parsing and Resolution

- Identifiers MUST be provided as `id@version` with exactly one `@` separator; leading/trailing whitespace MUST be trimmed; internal whitespace is NOT allowed.
- id and version are case-sensitive and MUST match manifest values exactly. Allowed characters for id: `[A-Za-z0-9._-]+`. version MUST follow the manifest version string (typically SemVer) without additional whitespace.
- Resolution order is deterministic: resolve the identifier against the discovered, unique manifest set. If no match is found, Engine MUST fail resolution with an error containing entityType, id, version, and reason "NotFound". If multiple matches are found (which violates uniqueness), Engine MUST fail with reason "NonUnique" and MUST include conflictPaths[].

### 8.2 Suite-targeted RunRequest

Schema:
```
{
  "suite": "suiteId@version",
  "nodeOverrides": {
    "nodeId": {
      "inputs": { "...": "..." }
    }
  },
  "environmentOverrides": {
    "env": { "KEY": "VALUE" }
  }
}
```

Rules:
- RunRequest MUST specify exactly one of suite, testCase, or plan.
- nodeOverrides keys MUST match nodeId values in the target Suite; unknown nodeIds MUST cause validation failure.
- nodeOverrides MAY be omitted.
- environmentOverrides.env MAY be present; it participates in Effective Environment as defined in section 7.3. Values MUST be strings; empty keys MUST fail validation. All overrides are runtime-only and MUST NOT modify source manifests.

Suite-targeted example:
```json
{
  "suite": "ThermalSuite@1.0.0",
  "nodeOverrides": {
    "cpu-quick": { "inputs": { "DurationSec": 45 } }
  },
  "environmentOverrides": {
    "env": { "LAB_MODE": "2" }
  }
}
```

### 8.3 Plan-targeted RunRequest

Schema:
```
{
  "plan": "planId@version",
  "environmentOverrides": {
    "env": { "KEY": "VALUE" }
  }
}
```

Rules:
- Plan RunRequest MUST NOT include nodeOverrides or caseInputs; Plan-level overrides are restricted to environmentOverrides.env.
- environmentOverrides.env from the Plan RunRequest participates in Effective Environment as defined in section 7.3 and applies to all Suites and Test Cases executed under the Plan. Values MUST be strings; empty keys MUST fail validation.
- All overrides are runtime-only and MUST NOT modify source manifests.

Plan-targeted example (env-only override; no input overrides):
```json
{
  "plan": "SystemValidation@1.2.0",
  "environmentOverrides": {
    "env": { "LAB_MODE": "PLAN", "PATH": "C:/tools" }
  }
}
```

### 8.4 Standalone TestCase RunRequest

Schema:
```
{
  "testCase": "testId@version",
  "caseInputs": { "...": "..." },
  "environmentOverrides": { "env": { "KEY": "VALUE" } }
}
```

Rules:
- For standalone TestCase run, caseInputs MAY be omitted and nodeOverrides MUST NOT be present.
- environmentOverrides.env MAY be present and participates in Effective Environment as defined in section 7.3. Values MUST be strings; empty keys MUST fail validation.

---

## 9. Parameter Passing Protocol (Frozen)

All case inputs MUST be passed as named PowerShell parameters.

Rules:
- Format: -Name Value
- Array parameters MUST be passed as repeated values: -Modes "A" "B"
- Runner MUST use ProcessStartInfo.ArgumentList and append each element separately.
- Boolean values: $true / $false
- Test Case scripts SHOULD declare boolean parameters as [boolean] (accepting $true/$false) rather than [switch]; behavior with [switch] is not guaranteed.
- Numeric serialization uses invariant culture (decimal point)
- Strings and paths MUST be passed safely (no shell parsing)
- Missing optional parameters MUST be omitted

Example:
```powershell
pwsh.exe run.ps1 -DurationSec 30 -Modes "A" "B"
```

This protocol is immutable within schema version 1.5.x.

---

## 10. Execution Semantics (Normative for v1)

- Suite execution is a linear ordered pipeline; v1 does NOT support DAG dependencies or parallel branching.
- Nodes execute strictly in declaration order per iteration. repeat controls MUST re-run the entire ordered list in order.
- retryOnError applies per node before advancing: Engine MUST attempt the node up to (1 + retryOnError) times when status is Error/Timeout; Passed/Failed/Aborted MUST NOT be retried. Retries MUST be counted sequentially and included in events.jsonl if present.
- continueOnFailure=false MUST stop the pipeline after the first non-Passed status (Failed/Timeout/Error/Aborted). continueOnFailure=true MUST continue to the next node after recording the failed status.
- In each suite iteration, each node produces exactly one TestCase run (plus any retries for Error/Timeout if configured).
- Over multiple iterations, the same node MAY produce multiple TestCase runs (one per iteration, per retry policy).
- Suite runs aggregate node results.
- Suite is the smallest reusable execution template.
- A standalone TestCase run executes a single Test Case outside any Suite pipeline.

---

## 10A. Execution UX and Runtime State Model

This section defines normative requirements for runtime execution behavior, progress reporting, and user interaction during test execution.

### 10A.1 Non-Blocking Execution

- The execution host (UI or CLI) MUST remain responsive during test execution.
- Execution MUST NOT block user interaction with unrelated controls or navigation.
- The user MUST be able to view execution progress, logs, and status at all times during a run.
- Long-running operations MUST NOT prevent access to abort/stop controls.

### 10A.2 Execution Pipeline Visibility

When execution begins, the system MUST immediately display all planned execution nodes:

- For a standalone TestCase run: one node representing the Test Case.
- For a TestSuite run: all Test Case nodes defined in the Suite (in declaration order).
- For a TestPlan run: all Suite nodes defined in the Plan (in declaration order).

Each node in the visible pipeline MUST display:
- A node identifier (nodeId for Suite nodes, identity for Plan-level Suite nodes).
- The referenced entity identity (TestCase or Suite).
- Current execution state.

### 10A.3 Runtime Node States

Each execution node MUST be in exactly one of the following states at any time:

| State | Description |
|-------|-------------|
| Pending | Node is queued but has not started execution. |
| Running | Node execution is in progress. |
| Passed | Node completed successfully (status = Passed). |
| Failed | Node completed with test failure (status = Failed). |
| Error | Node completed with execution error (status = Error). |
| Timeout | Node exceeded time limit (status = Timeout). |
| Aborted | Node was terminated by user action or cascading abort (status = Aborted). |
| RebootRequired | Node requested reboot; execution paused awaiting resume. |
| Skipped | Node was not executed due to pipeline termination (continueOnFailure=false). |

State transitions MUST follow these rules:
- Initial state MUST be Pending.
- Pending MAY transition to Running when execution begins.
- Running MUST transition to one of: Passed, Failed, Error, Timeout, Aborted, or RebootRequired.
- Pending MAY transition directly to Skipped or Aborted without entering Running state.
- Terminal states (Passed, Failed, Error, Timeout, Aborted, Skipped) MUST NOT transition to any other state.
- RebootRequired is terminal for the current execution segment; on resume, the node re-enters Running.

### 10A.4 Progress Reporting

The execution engine MUST report progress events to the execution host:

1. **Run Planned**: When execution begins, the engine MUST report the complete list of planned nodes before any node starts execution.

2. **Node Started**: Immediately before a node begins execution, the engine MUST report the node as started.

3. **Node Finished**: Immediately after a node completes execution (regardless of outcome), the engine MUST report the node's final state including:
   - Final status
   - Execution duration
   - Error message (if applicable)
   - Retry count (if retries occurred)

4. **Run Finished**: When all nodes have completed (or execution is terminated), the engine MUST report the final aggregate status.

Progress reports MUST be delivered in real-time (within implementation-reasonable latency, typically < 100ms).
Progress reporting MUST NOT depend on polling run artifacts (children.jsonl, result.json); the engine MUST push state updates.

### 10A.5 Abort Semantics

The system MUST support user-initiated abort at any time during execution:

- **Abort availability**: The abort control MUST remain accessible and functional throughout execution.
- **Abort signaling**: When abort is requested, the engine MUST signal cancellation to the currently executing node.
- **Current node**: The currently running node SHOULD be terminated as quickly as possible. Its final status MUST be Aborted.
- **Pending nodes**: All nodes still in Pending state MUST transition to Aborted (or Skipped, at implementation discretion) without execution.
- **Completed nodes**: Nodes that have already reached a terminal state MUST NOT change state due to abort.
- **Run status**: The overall run status MUST be Aborted when any node is aborted due to user action.

Abort MUST be a synchronous operation from the user's perspective: after abort is acknowledged, no new nodes SHALL begin execution.

### 10A.6 State Consistency

- The displayed execution state MUST accurately reflect the engine's internal state.
- State updates MUST be applied incrementally; the system SHOULD NOT discard and rebuild the entire node list on each update.
- Selection state and scroll position SHOULD be preserved across state updates where possible.
- The system MUST handle rapid state changes without visual corruption or missed updates.

---

## 10B. Reboot and Resume Semantics (Normative)

The system MUST support reboot-aware execution initiated by a Test Case. A reboot request pauses execution and resumes the same run after OS restart. This applies to standalone TestCase runs and to Suite/Plan runs.

### 10B.1 Reboot Request Protocol

Reboot requests are delivered by the Test Case via a control file written to PVTX_CONTROL_DIR.

File: `reboot.json`

Payload example:
```json
{
  "type": "control.reboot_required",
  "nextPhase": 1,
  "reason": "Phase 0 completed; requesting reboot for resume validation.",
  "reboot": { "delaySec": 10 }
}
```

Rules:
- The file MUST be named `reboot.json` and written to PVTX_CONTROL_DIR.
- Scripts SHOULD write atomically (write to reboot.tmp then move/rename to reboot.json).
- type MUST be `control.reboot_required`.
- nextPhase MUST be an integer >= 1. (Phase 0 is reserved for initial execution.)
- reason MUST be a non-empty string.
- reboot.delaySec is optional and MUST be a non-negative integer when present (default 0).
- Unknown fields MUST cause validation failure (root only allows: type, nextPhase, reason, reboot).
- reboot object, when present, MUST only include delaySec; unknown fields MUST cause validation failure.

### 10B.2 Execution Semantics

- When a reboot request is detected, the current node MUST be suspended and no further nodes may start.
- Engine/Runner MUST persist resume metadata (session.json) including runId, groupRunId if any, nodeId, iteration, nextPhase, and resume token before initiating reboot.
- The OS reboot MUST be initiated by the host/engine; scripts MUST NOT reboot the system directly.
- After reboot, the engine MUST resume the same run and re-run the same node with PVTX_PHASE set to nextPhase. The same RunId and Case Run Folder MUST be reused.
- A node MUST NOT request more than one reboot within a single run; additional reboot requests during resume MUST be treated as a validation error.
- For Suite/Plan runs, the pipeline MUST resume from the suspended node and continue to subsequent nodes only after the rebooting node completes.
- When reboot is requested under Suite/Plan runs, the TestCase result.json MUST be written with status=RebootRequired and include reboot metadata; this entry MAY be overwritten by the final result after resume.
- For standalone TestCase runs, result.json MAY be deferred until resume completes; session.json remains the authority for resume context.
- If a reboot request cannot be honored or resume fails, the engine MUST terminate the run with status Error or Aborted and record a diagnostic message.

---

## 11. PowerShell Execution Rules

- Runner invokes pwsh.exe
- Version MUST be >= 7.0
- Working directory is the Test Case Run Folder (CaseRunId) unless overridden by environment.workingDir (which also resolves relative to that Case Run Folder)
- Scripts MUST be treated as untrusted code

### 11.1 Exit Code Convention (Non-normative)

| Exit Code | Meaning (advisory only) |
|---|---|
| 0 | Script indicated success |
| 1 | Script indicated test-level failure |
| other | Script-specific value; Runner will map per 11.2 |

Runner mapping in 11.2 is authoritative. Timeout/abort are enforced by Runner (e.g., kill/stop) and MUST NOT rely on script exit codes such as `3`.

### 11.2 Runner Status Determination (Normative)
- Runner/Engine MUST determine final status using the following rules:
  - If execution times out (per timeoutPolicy), status MUST be Timeout.
  - If the user stops the run or Engine aborts, status MUST be Aborted.
  - If Runner fails to start the script process or encounters an internal exception, status MUST be Error and error.type MUST be RunnerError.
  - If the script exits normally, map exitCode: 0 => Passed; 1 => Failed; any other exitCode => Error with error.type MUST be ScriptError.
- If a legacy exit code table conflicts with this mapping, this normative mapping takes precedence; the table is advisory only.

---

## 12. Run Folder Layout

A standalone TestCase run produces a single Test Case Run Folder and does not generate any parent Suite or Plan run folder.

```
ResolvedRunsRoot/
  index.jsonl
  {RunId}/                    # Test Case run
    control/                 # Control channel (reboot requests)
    artifacts/               # Script-owned artifacts
    manifest.json
    params.json
    stdout.log                # Real-time line-by-line append during execution
    stderr.log                # Real-time line-by-line append during execution
    events.jsonl
    env.json
    result.json
    session.json              # Reboot resume metadata (only when reboot requested)
  {GroupRunId}/               # Test Suite / Test Plan run
    manifest.json
    controls.json
    environment.json
    runRequest.json
    children.jsonl
    events.jsonl
    result.json
    session.json              # Reboot resume metadata (only when reboot requested)
```

Rules:
- RunId and GroupRunId MUST be unique at least within ResolvedRunsRoot (SHOULD be globally unique) to avoid folder collisions.
- RunId and GroupRunId MUST be safe as Windows folder names (no invalid characters, avoid reserved names, and respect practical length limits).
- The owner of the target Run Folder MUST handle collisions defensively (e.g., regenerate the id when the folder already exists) before materializing the folder:
  - Runner for {RunId}/ (standalone or suite-triggered TestCase run folders)
  - Engine for {GroupRunId}/ (Test Suite / Test Plan run folders)

### 12.1 Ownership Rules
- Test Case Run Folders are exclusively owned by Runner.
- Test Suite / Test Plan Run Folders are exclusively owned by Engine.
- For Test Case runs, Runner MUST be the sole writer of manifest.json, params.json, result.json, events.jsonl (when present), stdout/stderr logs, env.json, and session.json (when present) inside the Case Run Folder; Engine MUST NOT write inside a Case Run Folder.
- For Test Suite / Test Plan runs, Engine MUST be the sole writer of manifest.json, controls.json, environment.json, runRequest.json, children.jsonl, events.jsonl (when present), result.json, and session.json (when present) inside the Group Run Folder; Runner MUST NOT write inside a Group Run Folder.
- Scripts MUST NOT create, delete, or rename top-level files in the Run Folder.
- Enforcement of the top-level file restriction is best-effort unless a sandbox is present.
- Runner MAY perform pre/post directory scans of the Case Run Folder to detect top-level file violations and MAY mark the run as Failed or Error with a Runner-side policy violation classification when violations are detected.
- Scripts MAY write additional files only under:
  {RunId}/artifacts/
  {RunId}/control/ (control channel; see section 10B)
- Runner MUST ignore unknown files and folders.
- stdout.log and stderr.log MUST be written line-by-line in real-time during test execution with FileShare.ReadWrite to allow concurrent reading by UI for live monitoring.
- Runner MUST apply secret redaction to each line before writing to stdout.log/stderr.log.

### 12.2 Immutability Rules
- manifest.json is a snapshot computed by Engine (effective manifest, inputs, environment) and MUST be persisted by Runner inside the Case Run Folder using the redaction metadata provided by Engine.
- manifest.json snapshot MUST include at minimum: sourceManifest (full original manifest object), resolvedRef (canonical resolved path or ref), resolvedIdentity (id and version), effectiveEnvironment (flattened map after applying section 7.3 and including predefined variables per section 7.5), and effectiveInputs (after EnvRef resolution). The snapshot MUST represent the exact inputs used for this run to enable reproduction. It MAY include inputTemplates (inputs before EnvRef resolution), resolvedAt (timestamp), and engineVersion.
- If any input was marked secret via EnvRef.secret=true, manifest.json and result.json (and any artifact that records inputs or environment values, such as params.json when present) MUST store a redacted value (e.g., "***") while execution uses the real value in memory. Runner MUST apply redaction based on metadata from Engine.
- For Test Case runs, params.json contains effectiveInputs passed to the script.
- For Test Suite / Test Plan runs, params.json MUST NOT be generated.
- For Test Suite / Test Plan runs, manifest.json MUST include the original manifest snapshot and SHOULD include resolved refs.
- For Test Suite / Test Plan runs, controls.json SHOULD contain effective controls.
- For Test Suite / Test Plan runs, environment.json SHOULD contain effective environment injection.
- For Test Suite / Test Plan runs, runRequest.json SHOULD contain the original RunRequest when present.
- Scripts MUST NOT modify these files.

### 12.3 index.jsonl
- One JSON object per line
- Fields:
  - runId
  - runType (TestCase / TestSuite / TestPlan)
  - nodeId (MUST be present for suite-triggered TestCase run; MUST NOT be present for standalone TestCase run)
  - testId (required for TestCase)
  - testVersion (required for TestCase)
  - suiteId (required for TestSuite; MUST be set for suite-triggered TestCase run when parentRunId refers to a TestSuite run)
  - suiteVersion (required for TestSuite; MUST be set for suite-triggered TestCase run when parentRunId refers to a TestSuite run)
  - planId (required for TestPlan; MUST be set for TestSuite/TestCase when parentRunId refers to a TestPlan run)
  - planVersion (required for TestPlan; MUST be set for TestSuite/TestCase when parentRunId refers to a TestPlan run)
  - parentRunId (optional; points to the Suite/Plan run)
  - startTime, endTime (ISO8601 UTC with trailing Z)
  - status (Passed / Failed / Error / Timeout / Aborted / RebootRequired)

Rule:
- For standalone TestCase run, parentRunId MUST be omitted and suiteId/suiteVersion/planId/planVersion MUST NOT be present.
- Engine is the single writer for index.jsonl; Runner MUST NOT write or append to index.jsonl under any circumstance.
- Engine MUST append index entries in an atomic/safe manner (single-threaded append or file lock) to avoid corruption when multiple runs complete concurrently.
- Runner MUST produce result.json and manifest.json for each Test Case run; Engine MUST consume these artifacts (and env.json when needed) to construct the corresponding index.jsonl entry.
- When a Test Case or Suite is executed under a Plan run, Engine MUST include planId/planVersion in the index entry and MUST ensure the same fields are present in the corresponding summary result.json.
- index.jsonl MUST NOT record secret values; current fields (ids, versions, status, times) MUST remain free of secret content. If future fields could contain user-provided text (e.g., message), they MUST be redacted when derived from secret inputs.

Example: suite-triggered TestCase run
```json
{"runId":"R-001","runType":"TestCase","nodeId":"cpu-quick","testId":"CpuStress","testVersion":"2.0.0","suiteId":"ThermalSuite","suiteVersion":"1.0.0","planId":"SystemValidation","planVersion":"1.2.0","parentRunId":"R-100","startTime":"2025-12-24T10:00:00Z","endTime":"2025-12-24T10:01:00Z","status":"Passed"}
```

Example: standalone TestCase run
```json
{"runId":"R-010","runType":"TestCase","testId":"CpuStress","testVersion":"2.0.0","startTime":"2025-12-24T11:00:00Z","endTime":"2025-12-24T11:01:00Z","status":"Passed"}
```

### 12.4 env.json
Records execution environment snapshot for Test Case runs:
- OS version
- Runner version
- PowerShell version
- Elevation state
env.json is distinct from environment.json, which captures effective environment injection for Suite/Plan runs.

### 12.5 children.jsonl
- Only present for Test Suite / Test Plan runs.
- Each run folder has exactly one children.jsonl file; its content depends on runType.
- Order MUST match manifest execution order.

Example: children.jsonl for Test Suite run (child TestCase runs; nodeId required)
```json
{"runId":"R-201","nodeId":"cpu-quick","testId":"CpuStress","testVersion":"2.0.0","status":"Passed"}
{"runId":"R-202","nodeId":"cpu-long","testId":"CpuStress","testVersion":"2.0.0","status":"Failed"}
```

Example: children.jsonl for Test Plan run (child TestSuite runs; nodeId omitted)
```json
{"runId":"R-301","suiteId":"ThermalSuite","suiteVersion":"1.0.0","status":"Passed"}
{"runId":"R-302","suiteId":"StorageSuite","suiteVersion":"1.0.0","status":"Passed"}
```

### 12.6 events.jsonl (Optional)
- Each line is a JSON object representing a timestamped event.
- There are two optional events.jsonl files:
  - {RunId}/events.jsonl (inside the Test Case Run Folder) MUST be written only by Runner.
  - {GroupRunId}/events.jsonl (inside the Test Suite / Test Plan Run Folder) MUST be written only by Engine.
- "Script events" means Runner MAY translate/forward structured events emitted by the script (e.g., via stdout/stderr protocol or IPC) into {RunId}/events.jsonl, but the script MUST NOT write events.jsonl directly.
- The schema is intentionally unspecified.
- Consumers MUST treat events as best-effort diagnostic data only.

#### Standard Event Codes (Reference Implementation)
The reference implementation uses these event codes for Test Case runs:

| Code | Level | When | Description |
|---|---|---|---|
| TestCase.Started | info | Test execution begins | Records test start with testId, testVersion, runId, and context (standalone/suite) |
| TestCase.Completed | info/warning | Test execution finishes normally | Records completion with status, exitCode, and duration. Level is "info" for Passed, "warning" for Failed/Timeout/Aborted |
| TestCase.Error | error | Unexpected exception | Records runtime exception during test execution |
| EnvRef.SecretOnCommandLine | warning | Secret value passed as CLI arg | Per spec section 7.4, warns when secret input is exposed via command line |

Event entry schema:
```json
{
  "timestamp": "ISO8601 UTC with Z",
  "code": "string",
  "level": "info|warning|error",
  "message": "string",
  "location": "optional string",
  "data": { "optional": "key-value pairs" }
}
```

---

### 12.7 session.json (Reboot Resume)
- Present only when a reboot request is accepted.
- Written by Runner for standalone TestCase runs; written by Engine for Suite/Plan runs.
- Contains resume metadata such as runId, entityType, state, nextPhase, resumeToken, currentNodeIndex, currentChildRunId, originTestId, and resume paths.
- Consumers SHOULD treat session.json as internal state; scripts MUST NOT modify it.

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
| nodeId | string | Yes (suite-triggered only) | Suite nodeId for this run |
| testId | string | Yes | Test ID |
| testVersion | string | Yes | Test version |
| suiteId | string | No | Suite ID (if known) |
| suiteVersion | string | No | Suite version (if known) |
| planId | string | No | Plan ID (if known) |
| planVersion | string | No | Plan version (if known) |
| status | enum | Yes | Passed / Failed / Error / Timeout / Aborted / RebootRequired |
| startTime | string | Yes | ISO8601 UTC with trailing Z |
| endTime | string | Yes | ISO8601 UTC with trailing Z |
| metrics | object | No | Metrics |
| message | string | No | Summary |
| exitCode | int | No | Script exit code |
| effectiveInputs | object | Yes | Effective case inputs passed to the script |
| error | object | No | Error details |
| runner | object | No | Runner metadata |
| reboot | object | No | Reboot resume metadata |

Rules:
- nodeId MUST be present for suite-triggered TestCase run.
- nodeId MUST NOT be present for standalone TestCase run.
- For suite-triggered TestCase run, suiteId and suiteVersion MUST be present; if part of a plan run, planId and planVersion MUST be present.
- For standalone TestCase run, suiteId/suiteVersion/planId/planVersion MUST NOT be present.
- If any input value was derived from an EnvRef with secret=true, effectiveInputs and any echoed values in result.json MUST be redacted (e.g., `"***"`).
- Runner MUST apply the redaction metadata provided by Engine when writing result.json and any other persisted artifacts for the Case Run Folder.
- If status = RebootRequired, reboot MUST be present.
- If status != RebootRequired, reboot MUST be omitted.

#### Reboot Info (Test Case)

| Field | Type | Required | Description |
|---|---|---|---|
| nextPhase | int | Yes | Phase to resume after reboot (>= 1) |
| reason | string | Yes | Non-empty reboot reason |
| originTestId | string | No | Origin test ID (if available) |
| delaySec | int | No | Reboot delay seconds (>= 0) |

Example: suite-triggered TestCase run
```json
{
  "schemaVersion": "1.5.0",
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

Example: standalone TestCase run
```json
{
  "schemaVersion": "1.5.0",
  "runType": "TestCase",
  "testId": "CpuStress",
  "testVersion": "2.0.0",
  "status": "Passed",
  "startTime": "2025-12-24T11:00:00Z",
  "endTime": "2025-12-24T11:01:00Z",
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
| planId | string | Conditional | Required if runType = TestPlan; also required when runType = TestSuite and the suite executed under a plan |
| planVersion | string | Conditional | Required if runType = TestPlan; also required when runType = TestSuite and the suite executed under a plan |
| status | enum | Yes | Passed / Failed / Error / Timeout / Aborted / RebootRequired |
| startTime | string | Yes | ISO8601 UTC with trailing Z |
| endTime | string | Yes | ISO8601 UTC with trailing Z |
| counts | object | No | Count of child statuses |
| childRunIds | array | Yes | Child runIds (Test Case or Suite, depending on runType) |
| message | string | No | Summary |
| reboot | object | No | Reboot resume metadata |

Status aggregation rule:
- If any child requests reboot, status MUST be RebootRequired and execution MUST pause.
- If the Suite/Plan run terminates due to user Stop or Engine abort, status MUST be Aborted.
- Otherwise: Error > Timeout > Failed > Passed.

Rules:
- When a Test Suite executes under a Test Plan, planId and planVersion MUST be populated in the Suite summary result.json (conditional required) and MUST match the values recorded in index.jsonl.
- counts SHOULD exclude RebootRequired entries (they represent in-progress, not terminal results).
- If status = RebootRequired, reboot MUST be present.
- If status != RebootRequired, reboot MUST be omitted.

#### Reboot Info (Suite/Plan)

Same schema as Test Case reboot info (section 13.2).

---

## 14. Privilege Policy

| privilege | Behavior |
|---|---|
| User | Run as standard user |
| AdminPreferred | Warn if not elevated, allow user to continue |
| AdminRequired | Block execution if not elevated |

### Privilege Enforcement

**Elevation Check:**
- Windows: Process checks `WindowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator)`
- Non-Windows: Assumes elevated (not enforced on other platforms)

**Hierarchical Privilege Computation:**
- Suite privilege = max(child test case privileges): `AdminRequired` > `AdminPreferred` > `User`
- Plan privilege = max(referenced suite privileges)
- The Engine performs privilege validation ONCE before execution begins

**Execution Behavior:**

1. **AdminRequired violations:**
   - UI: Displays error dialog, prevents execution start
   - CLI: Writes error to stderr, exits with code 1
   - Message: "'{name}' requires administrator privileges but the current process is not elevated. Please restart the application as administrator to run this {type}."

2. **AdminPreferred violations:**
   - UI: Displays confirmation dialog with warning, user can choose to continue
   - CLI: Writes warning to stdout, prompts for Y/N confirmation
   - Message: "'{name}' prefers administrator privileges but the current process is not elevated. Some tests may fail or produce incomplete results. Do you want to continue anyway?"
   - User can cancel (UI: No button / CLI: any input other than Y/YES)

3. **User level:**
   - No validation performed, execution proceeds normally

**Implementation Notes:**
- Privilege checking is performed by `PrivilegeChecker` utility class in `PcTest.Engine`
- UI uses `IFileDialogService.ShowError()` and `ShowConfirmation()` for user interaction
- CLI uses `Console.Error.WriteLine()` for errors and `Console.ReadLine()` for confirmation
- Validation occurs after discovery completes and before engine.ExecuteAsync() is called
- For Suites/Plans, the maximum privilege among all contained test cases is computed recursively

---

## 15. Concurrency and Isolation

- Implementations MAY execute sequentially by default.
- If controls.maxParallel > 1 and parallel execution is not supported, Engine MUST ignore it and MUST emit the warning defined in 6.5.
- Runner MUST support hard timeout enforcement and full process tree termination.

---

## 16. Out of Scope

- Distributed execution
- Network scheduling
- Web UI

---

END OF SPEC v1.5.1
