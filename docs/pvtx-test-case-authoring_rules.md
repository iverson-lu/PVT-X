# Test Case Authoring Rules v1

> Official authoring rules for **all Test Cases** in the PVT-X PC Test Suite.
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
- Runner / Engine may generate additional directories or files during execution
- These artifacts are **not part of the Test Case design**

### Case Execution Outputs

- **Console output** during execution is the most basic feedback
- It is **strongly recommended** that a Case produces:
  - `report.json` (describing the Case’s own execution result)
- Producing other files is completely optional
- Pass / Fail is determined by the **exit code**, not by output files
- The directory name is used only for physical organization and loading
- The **only stable, unique identifier** is the ID (Case / Suite / Plan)

---

## 2. test.manifest.json Specification

### 2.1 Meta Information

#### ID Rules (Mandatory)

- Every **Case / Suite / Plan** must define a **globally unique, stable ID**
- IDs must follow **reverse-domain style naming**
- IDs are globally unique across **Case / Suite / Plan namespaces**

Examples:

```
case.hw.power.sleep_test
case.sw.os.windows.service_status
suite.fw.bios_ver_check
plan.sw.smoke_test
```

##### Rules

- IDs **must be globally unique** and **must not change once published**
- Case / Suite / Plan share a **single ID namespace**, distinguished by prefixes:
  - `case.*`
  - `suite.*`
  - `plan.*`
- It is **strongly recommended** that directory names match their IDs
  - This is not mandatory, but greatly improves long-term maintenance and management
- IDs are **decoupled from UI display names**
- Recommended structure:
  ```
  <type>.<domain>.<subsystem>.<feature>
  ```
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
  "category": "...",
  "tags": ["..."]
}
```

### Rules

- `name` **does not need to match** the directory name
- `description`: short description for UI display
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
  "type": "string | enum | bool | int | double | json",
  "required": true,
  "default": "...",
  "description": "..."
}
```

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
Example command for local debugging:

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
| `PVTX_TESTCASE_NAME` | Display name |
| `PVTX_TESTCASE_ID` | Execution ID |
| `PVTX_TESTCASE_VER` | Version identifier |

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
