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
- The Case directory name is used only for physical organization and loading
- The **only stable, unique identifier** of a Case is the Case ID

---

## 2. test.manifest.json Specification

### 2.1 Meta Information

#### Case ID Rules (Mandatory)

- Each Test Case **must define a globally unique and stable Case ID**
- Case ID must follow **reverse-domain naming style**

Examples:

```
hw.power.powersupply.voltage_check
sw.os.windows.service_status
hw.bios.version_check
```

##### Rules

- Case ID **must be globally unique** and **must not change once published**
- Case ID is **decoupled from the directory name**
- Case ID is used for long-term tracking, statistics, and result aggregation
- Recommended structure:
  ```
  <org>.<category>.<subsystem>.<feature>.<case>
  ```
- Case ID must appear in:
  - `test.manifest.json` (meta section)
  - `report.json` produced by the Case

---

### 2.1 Meta Fields

#### Required fields

```json
{
  "name": "CaseName",
  "description": "...",
  "category": "...",
  "tags": ["..."]
}
```

### Rules

- `name` **does not need to match** the Case directory name
  - `name` is for UI display and human-readable identification
  - The directory name is for physical organization only
- `description`: short description for UI display
- `category`: logical grouping for Suite / Plan / UI
  - Recommended values: `Hardware` / `Software` / `Firmware`
- `tags`:
  - Used **only for classification and filtering**
  - Must not carry detailed semantics
  - Avoid uncontrolled free text (e.g. network / networking / net)

---

### 2.2 Parameters Definition

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

#### Parameter Rules (Mandatory)

1. **`required` and `default` are mutually exclusive**
   - `required: true` → ❌ `default` is not allowed

2. **Responsibility of `enum`**
   - `enum` is used for **UI guidance and constraints only**
   - Runner / Engine do **not** guarantee runtime validation
   - PowerShell scripts must validate values explicitly

3. **Semantics of `json` type**
   - `json` is still a `string`
   - Runner does not parse it
   - Scripts must use `ConvertFrom-Json`
   - This type exists to **improve readability and user understanding**

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
- Do not repeat schema fields, explain meaning only

## How to Run Manually
Provide an example command to run `run.ps1` directly for debugging, e.g.:

```powershell
pwsh ./run.ps1 -ParamA valueA -ParamB valueB
```

## Expected Result
- Expected behavior on success
- Typical behavior on failure
```
```

### Rules

- README is written for **humans**, not machines
- Brevity is allowed, completeness is required

---

## 4. run.ps1 Specification

### 4.0 System-Level Environment Variables

The Runner injects the following **system-level environment variables** for each Test Case:

| Variable | Description |
|--------|-------------|
| `PVTX_TESTCASE_PATH` | Absolute path of the Test Case |
| `PVTX_TESTCASE_NAME` | Test Case display name |
| `PVTX_TESTCASE_ID` | Unique ID of the current execution |
| `PVTX_TESTCASE_VER` | Version identifier of the Test Case |

#### Usage Rules

- Scripts **must prefer** `PVTX_TESTCASE_PATH` to locate local resources
- Do not assume the current working directory (CWD)
- All bundled data files must be referenced explicitly

> Notes:
> - Plan- and Suite-level environment variable injection is also supported
> - Test Cases can consume injected variables without knowing their origin

---

### 4.1 Basic Requirements

- Language: **PowerShell (English comments and output only)**
- Parameters must be explicitly declared
- Must not contain UI / Plan / Suite logic

---

### 4.2 Output Rules

- ✅ `Write-Output`: normal output
- ✅ `Write-Error`: error output
- ⚠️ `Write-Host`: debug only (discouraged)

---

### 4.3 Exit Code Rules (Mandatory)

| Exit Code | Meaning |
|----------|--------|
| 0 | Test Pass |
| 1 | Test Fail (condition not met) |
| ≥ 2 | Script / Environment Error |

---

### 4.4 External Dependencies

- All dependencies (commands, modules, system capabilities)
  - Must be documented in README or script header

---

## 5. Template Case Positioning

### 5.1 template.demo_all_types

**Purpose**:

- Demonstrates all supported capabilities and best practices
- May include multiple parameters and types
- Serves as a complete example, not a starting point

---

### 5.2 template.minimal_demo (Recommended Starting Point)

**Purpose**:

- Default starting point for new Test Cases
- Minimal but complete
- May use very few parameters, but structure must be complete

> Strongly recommended:
> New Case → template.minimal_demo → Extend as needed

---

## 6. Design Principles

- Manifest defines **structure and constraints**
- README defines **intent, semantics, and readability**
- run.ps1 defines **execution and judgment**

> A good Test Case:
>
> - Produces clear and readable results
> - Outputs information for humans, not just machines
> - Has predictable script behavior
> - Is hard to misuse by humans or AI

---

**Version**: v1  
**Status**: Frozen
