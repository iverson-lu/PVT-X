# PC Test System Specification
## Version 1.4.4

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
- Folder name MUST NOT affect identity.
- parameters define schema and defaults only; no concrete runtime values are stored here.
- Test Case manifests MUST NOT define environment sections; any such fields MUST be ignored at discovery and execution time.

Example:
```json
{
  "schemaVersion": "1.4.4",
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
- For type path/file/folder: relative values MUST resolve against environment.workingDir (inside the Run Folder); file/folder MUST exist at validation time unless a schema field explicitly allows creation, while path MAY point to a non-existent target. UI hints such as filePicker/folderPicker MUST align with this resolution rule and MUST NOT imply alternative bases.

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
- The same Test Case (id@version) MAY appear multiple times with different nodeId and inputs.
- ref MUST resolve to a folder under ResolvedTestCaseRoot, and the manifest MUST be located at `<ResolvedTestCaseRoot>/<ref>/test.manifest.json` after normalization. The resolution algorithm is: normalize ref relative to ResolvedTestCaseRoot; reject if the normalized path escapes the root; load the manifest at the normalized location.
- If ref resolution fails (OutOfRoot, NotFound, or MissingManifest), Engine MUST fail validation with code "Suite.TestCaseRef.Invalid" and a message that includes suite path, ref, resolvedPath, expectedRoot, and reason (OutOfRoot / NotFound / MissingManifest). OutOfRoot applies when the normalized path escapes the root; NotFound applies when the target folder does not exist; MissingManifest applies when the folder exists but test.manifest.json is missing.
- inputs, when present, MUST only include parameter names declared by the referenced Test Case.
- Suite manifests MUST NOT embed or copy Test Case content; all Test Case manifests live under ResolvedTestCaseRoot.
- Suite.environment.env values MUST be strings; empty keys MUST be rejected during validation.

Example:
```json
{
  "schemaVersion": "1.4.4",
  "id": "ThermalSuite",
  "name": "Thermal Suite",
  "version": "1.0.0",
  "controls": { "repeat": 1, "continueOnFailure": false },
  "environment": { "env": { "LAB_MODE": "1" }, "workingDir": "work" },
  "testCases": [
    {
      "nodeId": "cpu-quick",
      "ref": "CpuStress",
      "inputs": { "DurationSec": 30, "Mode": "A" }
    },
    {
      "nodeId": "cpu-long",
      "ref": "CpuStress",
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
| suites | string[] | Yes | List of Test Suite identities (`suiteId@version`) |

Rules:
- A Plan MUST only reference Test Suites.
- suites entries MUST be suite identities in the form `suiteId@version`.
- Engine MUST resolve each suites entry against discovered Test Suites under ResolvedTestSuiteRoot; if not found, validation MUST fail; if multiple matches are found, validation MUST fail with an error containing entityType=suite, id, version, and conflictPaths.
- Order in suites defines execution order.
- A Plan MUST NOT define or override Test Case inputs, Suite.nodeInputs, or any per-node parameters.
- A Plan MUST NOT embed Test Suites or Test Cases.
- If environment is present in the Plan manifest, its env map participates in Effective Environment as defined in section 7.3; values MUST be strings and keys MUST be non-empty.
- Plan environment MUST be env-only; presence of any other key (e.g., workingDir, runnerHints) MUST fail validation.
- Plan environment MUST NOT introduce per-node mappings; it applies uniformly to all Suites executed under the Plan.

Example:
```json
{
  "schemaVersion": "1.4.4",
  "id": "SystemValidation",
  "name": "System Validation",
  "version": "1.2.0",
  "suites": [
    "ThermalSuite@1.0.0",
    "StorageSuite@1.0.0"
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

### 7.3 Environment Resolution (Normative)
- Effective Environment is the single authoritative rule set:
  - Plan run: Plan RunRequest.environmentOverrides.env > Plan manifest environment.env (if present) > Suite manifest environment.env > OS environment.
  - Suite run (without Plan): Suite RunRequest.environmentOverrides.env > Suite manifest environment.env > OS environment.
  - Standalone TestCase run: Case RunRequest.environmentOverrides.env > OS environment.
- Same-name keys MUST be overwritten by the higher-priority layer. All environment values MUST be strings. Empty keys or whitespace-only keys MUST fail validation. Duplicate keys are resolved deterministically by the precedence above.
- Test Case manifests MUST NOT define environment blocks; any such fields MUST be ignored.

### 7.4 Environment Variable References (EnvRef)
- Any input value (Suite.node.inputs, RunRequest.caseInputs, RunRequest.nodeOverrides.inputs) MAY be either a JSON literal consistent with ParameterDefinition.type (string/number/bool/enum/path/file/folder/string[]/int[]/enum[]) or an EnvRef object with the shape:
  - `$env` (string, required): environment variable name to read; MUST be non-empty.
  - `default` (any, optional): literal fallback when the env variable is missing or empty.
  - `required` (bool, optional, default false): if true and the env variable is missing/empty and no default is provided, validation MUST fail before execution.
  - `secret` (bool, optional, default false): if true, Engine/Runner MUST redact the value in logs, summaries, manifest snapshots, and index entries (e.g., replace with `***`); execution MUST still use the real value.
- Resolution process:
  - Engine MUST first compute the Effective Environment (section 7.3) for the run context.
  - Engine MUST then resolve all EnvRef values into concrete literals before invoking Runner.
  - If resolution fails (missing required env, parse failure, or type conversion failure), Engine MUST fail validation with a deterministic error (e.g., "EnvRef.ResolveFailed") including the parameter name and nodeId when applicable.
- Type conversion rules (env values originate as strings):
  - string: use the env string as-is (after optional redaction for secret).
  - number/int: MUST parse; on failure MUST fail validation.
  - boolean: MUST accept true/false/1/0 (case-insensitive); otherwise MUST fail validation.
  - string[]/enum[]/int[]: MUST parse as JSON array of the corresponding primitive type; on failure MUST fail validation.
  - For enum and enum[] targets, Engine MUST validate the resolved values are contained in enumValues; otherwise validation MUST fail.
  - If the target parameter type is not declared, treat as string.
- Structured payloads (objects) are NOT supported by ParameterDefinition.type; if structured data is needed, callers MUST use type "string" and encode JSON/text, and scripts are responsible for parsing.
- EnvRef is a value source only; it MUST NOT introduce new override layers or per-node mappings beyond existing inputs semantics.

Security Note (Normative):
- secret=true requires redaction only in artifacts/logs/snapshots/index; v1 still passes parameters via command-line arguments, so the OS/process list/diagnostic tools MAY observe the raw values.
- When any secret=true input would be passed via command-line, Engine/Runner MUST emit a warning with code "EnvRef.SecretOnCommandLine", include the affected parameter name (and nodeId when applicable), and record it in the run log (e.g., events.jsonl or equivalent).
- Sensitive information SHOULD be provided via environment variables pointing to credential files/paths rather than passing raw secret values directly.

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
- Numeric serialization uses invariant culture (decimal point)
- Strings and paths MUST be passed safely (no shell parsing)
- Missing optional parameters MUST be omitted

Example:
```powershell
pwsh.exe run.ps1 -DurationSec 30 -Modes "A" "B"
```

This protocol is immutable within schema version 1.4.x.

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
    manifest.json
    params.json
    stdout.log
    stderr.log
    events.jsonl
    env.json
    result.json
  {GroupRunId}/               # Test Suite / Test Plan run
    manifest.json
    controls.json
    environment.json
    runRequest.json
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
- manifest.json is a snapshot generated by Engine/Runner.
- manifest.json snapshot MUST include at minimum: sourceManifest (full original manifest object), resolvedRef (canonical resolved path or ref), resolvedIdentity (id and version), effectiveEnvironment (flattened map after applying section 7.3), and effectiveInputs (after EnvRef resolution). The snapshot MUST represent the exact inputs used for this run to enable reproduction. It MAY include inputTemplates (inputs before EnvRef resolution), resolvedAt (timestamp), and engineVersion.
- If any input was marked secret via EnvRef.secret=true, manifest.json and result.json (and any artifact that records inputs or environment values, such as params.json when present) MUST store a redacted value (e.g., `"***"`) while execution uses the real value in memory.
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
  - status (Passed / Failed / Error / Timeout / Aborted)

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
| nodeId | string | Yes (suite-triggered only) | Suite nodeId for this run |
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

Rules:
- nodeId MUST be present for suite-triggered TestCase run.
- nodeId MUST NOT be present for standalone TestCase run.
- For suite-triggered TestCase run, suiteId and suiteVersion MUST be present; if part of a plan run, planId and planVersion MUST be present.
- For standalone TestCase run, suiteId/suiteVersion/planId/planVersion MUST NOT be present.
- If any input value was derived from an EnvRef with secret=true, effectiveInputs and any echoed values in result.json MUST be redacted (e.g., `"***"`).

Example: suite-triggered TestCase run
```json
{
  "schemaVersion": "1.4.4",
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
  "schemaVersion": "1.4.4",
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
| status | enum | Yes | Passed / Failed / Error / Timeout / Aborted |
| startTime | string | Yes | ISO8601 UTC with trailing Z |
| endTime | string | Yes | ISO8601 UTC with trailing Z |
| counts | object | No | Count of child statuses |
| childRunIds | array | Yes | Child runIds (Test Case or Suite, depending on runType) |
| message | string | No | Summary |

Status aggregation rule:
- If the Suite/Plan run terminates due to user Stop or Engine abort, status MUST be Aborted.
- Otherwise: Error > Timeout > Failed > Passed.

Rules:
- When a Test Suite executes under a Test Plan, planId and planVersion MUST be populated in the Suite summary result.json (conditional required) and MUST match the values recorded in index.jsonl.

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
- If controls.maxParallel > 1 and parallel execution is not supported, Engine MUST ignore it and MUST emit the warning defined in 6.5.
- Runner MUST support hard timeout enforcement and full process tree termination.

---

## 16. Out of Scope

- Distributed execution
- Network scheduling
- Web UI

---

END OF SPEC v1.4.4
