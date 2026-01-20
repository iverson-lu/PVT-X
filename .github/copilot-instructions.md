# Copilot Agent Instructions – PVT-X

PVT-X is a **local, offline, Windows desktop** PC test suite tool.
It runs **entirely on the target machine**.
It is **NOT** a cloud service, web app, or client–server system.

These instructions are **mandatory** for all Copilot Agent code changes.

---

## 1. Scope & Stack (Non-Negotiable)

- UI: **WPF (.NET)**
- Test execution: **PowerShell scripts**
- Persistence: **local files only**
- No databases unless explicitly required by spec

Do NOT:
- Turn this into a service/daemon/web API
- Move test logic from PowerShell into C#
- Introduce unnecessary frameworks/services

---

## 2. Test Cases & PowerShell (Core Rules)

### Execution
- Each test case runs via **PowerShell**
- PowerShell script is the **single source of truth** for test logic
- UI/engine orchestrate and visualize; they do not re-implement test logic

### Artifacts
Each case MUST produce:
- `artifacts/report.json`

Create these ONLY when needed:
- `artifacts/raw/`
- `artifacts/attachments/`

Do NOT generate `raw/` or `attachments/` by default.

### Script language
- PowerShell scripts (including comments and output) are **always in English**
- Keep scripts readable and structured (setup → validation → output)
- When counting filtered results, always wrap in `@()` to force array conversion:
  ```powershell
  # ✅ Correct
  $passCount = @($steps | Where-Object { $_.status -eq 'PASS' }).Count
  # ❌ Wrong - fails when single match
  $passCount = ($steps | Where-Object { $_.status -eq 'PASS' }).Count
  ```

### Parameters
When creating/modifying a case, explicitly list parameters:
- name, required/optional, type, default
For complex cases, briefly describe the execution flow and what capability is validated.

### Modules
- Use existing shared modules from `assets/PowerShell/Modules/` (e.g., `Pvtx.Core`)
- Do NOT create custom helper functions within test cases
- Runner automatically injects `PVTX_MODULES_ROOT` into `PSModulePath`

---

## 3. UI Rules (UX & Structure)

- UI must reflect hierarchy: **Plan → Suite → Case**
- Avoid flattening hierarchical data into unrelated lists
- Execution UX:
  - show progress in pipeline/execution views
  - avoid fully blocking overlays
  - support abort when applicable
- Style: modern, minimal, Fluent-like, consistent

---

## 4. Quality, Tests, and Spec Sync

### Tests (Always Check)
For every change, ALWAYS evaluate whether tests must be added/updated.

Add/update tests when changing:
- behavior / execution flow
- parsing/serialization or `report.json` schema
- artifact filesystem behavior
- error handling, retries, timeouts, cancel/abort
- filtering/sorting/search logic
- any bug fix (needs regression coverage)

Pure visual-only XAML styling/layout may skip tests.

### Spec (Authoritative + When to update)
Specs are authoritative. If spec conflicts with these instructions, **ask**—do not guess.

Spec locations:
- Core (engine/runner/contracts): `docs/pvtx-core-spec.md`
- UI (design/UX): `docs/pvtx-ui-spec.md`
- Case authoring (manifest/script/structure): `docs/pvtx-test-authoring_rules.md`

Update specs when:
- Core behavior/contracts/artifacts change → update Core spec
- UI workflows/structure/terminology change → update UI spec
- Case structure/manifest/script rules change → update Case authoring rules
Pure visual polish/refactors with no behavior change → spec update not required

---

## 5. Agent Behavior (Mental Model)

Build a predictable, offline Windows test runner that orchestrates **PowerShell-driven test cases**
and visualizes results in a clean WPF UI. Avoid unrelated features or “architecture improvements”
unless explicitly requested.
