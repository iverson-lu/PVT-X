---
name: pvt-x-case-generator
description: Generate a new PVT-X test case (run.ps1, test.manifest.json, README.md) based on existing reference cases and a provided case spec.
version: 1.0
---

## Purpose

This skill generates a **new test case** for the PVT-X PC test suite by
**strictly following existing reference cases in the repository**.

The output of this skill is always a **3-file case bundle**:

- `run.ps1`
- `test.manifest.json`
- `README.md`

This skill is designed to ensure **structural consistency**, **parameter compatibility**,
and **stable runner integration** across all cases.

---

## When to Use This Skill

Use this skill when:

- You are adding a **new test case**
- You already have a **case specification** (markdown or text)
- You want the new case to **match existing cases exactly** in structure and behavior
- The implementation language is **PowerShell**

Do NOT use this skill for:
- Refactoring existing cases
- Changing the runner execution model
- Introducing new manifest schemas

---

## Inputs

This skill expects:

1. **A case specification**
   - Plain text or markdown
   - Describes purpose, parameters, execution logic, and pass/fail rules

2. **Existing reference cases in the workspace**
   - Located under folder `assets/TestCases/case.template.demo.core.all_types@1.0.0`
   - Each reference case contains:
     - `run.ps1`
     - `test.manifest.json`
     - `README.md`

---

## Outputs

This skill produces exactly **three files** for the new case:

```
assets/TestCases/<new-case-id@version>/
  run.ps1
  test.manifest.json
  README.md
```

Optional sample plans may be included **only if reference cases include them**.

---

## Reference Case Resolution Rules

When generating a new case, reference cases MUST be selected in the following order:

1. A reference case from the **same functional category**, if available
2. A generic command or execution-style reference case
3. The simplest available reference case as a fallback

Reference cases define:
- Script structure
- Parameter parsing
- Error handling
- Result JSON schema
- Artifact handling conventions

---

## Hard Constraints (Must Follow)

- **Do NOT invent new top-level fields** in `test.manifest.json`
- **Do NOT change** the result JSON structure
- **Do NOT introduce new execution phases**
- **Do NOT depend on external PowerShell modules**
- **Do NOT modify runner assumptions**
- All PowerShell scripts must be **self-contained**

If the spec is incomplete or ambiguous, the behavior MUST default to the
reference case behavior.

---

## Parameter Rules

- Parameter names use `camelCase`
- Optional parameters inherit defaults from reference cases
- Required parameters must be explicitly validated in `run.ps1`
- All parameters must be documented in `README.md`
- All documented parameters must appear in `test.manifest.json`

---

## Error Handling Rules

- Execution errors must be captured using `try/catch`
- Failures must produce:
  - `pass: false`
  - A structured `details` list
- Fatal errors must include a readable error message
- Exit codes must follow reference case conventions

---

## Artifact Rules

- Artifact directories and file naming MUST follow reference cases
- Artifact paths must be included in the result JSON
- Artifacts must be deterministic and reproducible

---

## Output Format Requirements

When responding, output files MUST be formatted as:

```
<relative file path>
```powershell / json / markdown
<file content>
```

Each file must be clearly separated and complete.

---

## Validation Checklist (Self-Check Before Output)

Before finalizing output, ensure:

- All manifest parameters are parsed in `run.ps1`
- All parameters are documented in `README.md`
- Result JSON structure matches reference cases exactly
- No undocumented defaults exist
- No unused parameters exist
