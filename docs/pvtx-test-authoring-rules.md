# Test Case Authoring Rules v1

> Unified authoring rules for **all Test Cases** in the PVT-X PC Test Suite.
>
> Goals:

- Ensure consistency, maintainability, and scalability of Test Cases
- **Improve readability and understandability of Test Case execution results**
- Enable both humans and AI to reliably produce high-quality Test Cases

---

## 1. Case Directory Structure (Mandatory)

Each Test Case **must contain at least** the following files:

```
CaseName/
 ├─ test.manifest.json   (required)
 ├─ run.ps1              (required)
 ├─ README.md            (strongly recommended)
 └─ <other files>        (optional, e.g. test data)
```

### Rules

- `test.manifest.json` and `run.ps1` are **mandatory**
- `README.md` is **strongly recommended** to describe test purpose and logic
- A Case may include additional data files (configs, samples, reference data, etc.)
- All bundled files must be accessed via `PVTX_TESTCASE_PATH` + relative paths
- ❌ Do NOT pre-commit:
  - `artifacts/`

### Case Execution Outputs

- **Console output** during execution is the most basic feedback
- It is **strongly recommended** that a Case produces:
  - `report.json` (describing the Case’s own execution result)
- Producing other files is completely optional
- Pass / Fail is determined by the **exit code**, not by output artifacts
- The directory name is used only for physical organization and loading
- The **only stable and unique identifier** is the ID

---

## 2. test.manifest.json Specification

### 2.1 Meta Information

#### ID Rules (Mandatory)

- Every **Case / Suite / Plan** must define a **globally unique, stable ID**
- IDs follow **reverse-domain style naming** with controlled semantic layers
- Case / Suite / Plan share a **single global ID namespace**, distinguished by prefixes

Examples:

```
case.hw.power.core.sleep_resume
case.sw.os.windows.service_status
suite.fw.bios_ver_check
plan.sw.smoke_test
```

##### Core Design Principles (Summary)

> A complete and scalable ID structure is the foundation of long-term system governance.

- ID semantics are split into **fixed layers** to avoid ambiguity as the library grows
- Recommended structure (Case example):
  ```
  case.<domain>.<subsystem>.<feature>.<action>
  ```
- Layer responsibilities:
  - `domain`: top-level technical area (hw / sw / fw / os / network)
  - `subsystem`: major functional block (power / storage / wifi)
  - `feature`: specific capability or focus area (use `core` when not applicable)
  - `action`: test intent or behavior (check / verify / stress, etc.)

> Notes:
> - This is a **recommended and governed standard** for scalability, UI grouping, and analytics
> - Full definitions, whitelists, and constraints are defined in: `pvtx-id-schema.md`

##### Rules

- IDs **must be globally unique** and **must not change once published**
- Case / Suite / Plan use unified prefixes:
  - `case.*`
  - `suite.*`
  - `plan.*`
- It is **strongly recommended** that directory names match their IDs (not mandatory)
- IDs are **decoupled from UI display names**
- IDs must appear in:
  - `test.manifest.json` (meta section)
  - `report.json` produced by the Case

---

### 2.2 Meta Fields

#### Required fields

```json
{
  "name": "Display Name",
  "description": "...",
  "id": "case.domain.subsystem.feature.action",
  "version": "1.0.0",
  "category": "...",
  "tags": ["..."]
}
```

### Rules

- `name` **does not need to match** the directory name
- `description`: short description for UI display
- `id`: must follow the ID schema defined above
- `version`: follows semantic versioning (major.minor.patch)
- `category`: logical grouping for Suite / Plan / UI
  - Recommended values: `Hardware` / `Software` / `Firmware`
- `tags`:
  - Used **only for classification and filtering**

---

### 2.3 Parameters Definition

Each parameter supports the following fields:

```json
{
  "name": "ParamName",
  "type": "string | enum | boolean | int | double | path | json",
  "required": true,
  "default": "...",
  "description": "..."
}
```

For details parameter usage, see template test case test.manifest.json files.

---

## 3. README.md Specification (Mandatory Structure)

All Test Case READMEs **must follow this exact structure and order**:

```md
# Case Name

## Purpose
One sentence describing what this test validates.

## Test Logic
- Core test approach
- System capabilities / commands / APIs used
- Pass / fail decision logic

## Parameters
- Semantic explanation of manifest parameters

## How to Run Manually
Provide an example command to run `run.ps1` directly for debugging:

```powershell
pwsh ./run.ps1 -ParamA valueA -ParamB valueB
```

## Expected Result
- Expected behavior on success
- Typical behavior on failure
```
```

---

## 4. run.ps1 Specification

### 4.0 System-Level Environment Variables

Injected automatically by Runner:

| Variable | Description |
|--------|-------------|
| `PVTX_TESTCASE_PATH` | Absolute path of the Test Case |
| `PVTX_TESTCASE_NAME` | Test Case display name |
| `PVTX_TESTCASE_ID` | Unique execution ID |
| `PVTX_TESTCASE_VER` | Test Case version identifier |

> Plan- and Suite-level environment variable injection is also supported.

---

### 4.1 Exit Codes

| Code | Meaning |
|-----|--------|
| 0 | Pass |
| 1 | Fail |
| ≥2 | Script / Environment Error |

---

## 5. Template Positioning

- **TemplateCase**: full-featured reference
- **TemplateCaseMinimal**: recommended starting point

---

## 6. Design Principles

- ID defines **identity**
- Manifest defines **structure**
- README defines **intent**
- Script defines **execution**

---

**Version**: v1  
**Status**: Frozen
