# Windows Registry Test Case Specification

## Case Name
`windows.registry`

---

## Goal

Provide a unified Windows registry test case that supports:

- Applying registry changes by executing existing `.reg` files
- Verifying registry **values** for existence and optional content matching

This case is designed for PC test automation scenarios where registry setup and validation
are required as part of a test flow.

---

## Scope

- Windows operating systems only
- Registry write operations are performed **only** via `.reg` files
- Registry verification is **read-only**
- Registry key existence is implicitly validated through value access

---

## Operation Modes

The test case supports two mutually exclusive modes:

| Mode | Description |
|-----|-------------|
| `execute_reg` | Apply registry changes using an existing `.reg` file |
| `verify_value` | Verify that registry values exist and optionally match expected data |

---

## Common Parameters

### `mode` (required)

- Type: string
- Allowed values: `execute_reg`, `verify_value`

---

## Mode: `execute_reg`

### Parameters

#### `reg_file_path` (required)

- Type: string
- Path to an existing `.reg` file
- Can be absolute or relative to the test working directory

Example:
```
C:\tests\artifacts\enable_feature.reg
```

---

#### `require_admin` (optional, default: false)

- Type: boolean
- If enabled, the test case verifies administrator privileges before execution
- The case fails immediately if elevation is required but not present

---

#### `artifact_copy_reg` (optional, default: true)

- Type: boolean
- If enabled, the input `.reg` file is copied into test artifacts

---

#### `artifact_export_scope` (optional)

Defines registry snapshots to export **after** execution.

| Field | Type | Description |
|------|------|-------------|
| `enabled` | boolean | Enable registry export |
| `keys` | string[] | Registry keys to export (full paths including hive) |
| `format` | string | `reg` or `json` |

---

### Execution Behavior

1. Validate that `reg_file_path` exists and has a `.reg` extension
2. If `require_admin` is enabled:
   - Verify administrator privilege
   - Fail if not elevated
3. Execute the `.reg` file using native Windows registry import
4. Capture execution result and errors (if any)
5. Optionally export registry snapshots as artifacts

---

### Pass / Fail Rules

- **Pass**
  - Registry file is executed successfully
- **Fail**
  - File not found or invalid
  - Administrator privilege required but not available
  - Registry import fails

Notes:
- Deleting non-existing keys or values is considered successful
- The operation is naturally idempotent

---

## Mode: `verify_value`

### Parameters

#### `verify_spec` (required)

A JSON array.  
Each item defines one registry **value** that must exist and optionally match expected content.

---

#### `dump_snapshot` (optional, default: true)

- If enabled, the actual registry value read from the system is always included in the result

---

### `verify_spec` Item Schema

#### Required Fields

| Field | Type | Description |
|------|------|-------------|
| `path` | string | Full registry path including hive |
| `name` | string | Registry value name |

---

#### Optional Fields

| Field | Type | Description |
|------|------|-------------|
| `expected` | object | Expected type and/or data to validate |

---

### `expected` Object

All fields are optional.

| Field | Type | Description |
|------|------|-------------|
| `type` | string | `String`, `ExpandString`, `DWord`, `QWord`, `MultiString`, `Binary` |
| `data` | any | Expected value data |
| `match_mode` | string | `exact` (default), `case_insensitive`, `contains`, `regex` |

---

### Verification Logic

For each item in `verify_spec`:

1. Read the registry value
   - Key not found → Fail
   - Value not found → Fail
   - Access denied or read error → Fail

2. If `expected` is provided:
   - Type mismatch → Fail
   - Data mismatch (per `match_mode`) → Fail

3. Pass if the value exists and all specified expectations are met

---

## Result Output (Suggested)

### Overall
- `status`: `pass` or `fail`
- `summary`: e.g. `3 passed, 0 failed`

### Per-item Details (verify mode)
- `path`
- `name`
- `actual.type`
- `actual.data`
- `passed`
- `reason` (on failure: `not_found`, `access_denied`, `type_mismatch`, `value_mismatch`)

### Execution Details (execute mode)
- `reg_file_path`
- `require_admin`
- `execution_message` (if available)

---

## Recommended Usage Pattern

1. Apply registry changes using `mode = execute_reg`
2. Validate registry state using `mode = verify_value`

This separation keeps registry modification and verification responsibilities clear.

---

## Notes

- `.reg` files can create, update, and delete registry keys and values
- Writing to system hives such as `HKLM` usually requires administrator privileges
- This unified case is intended to be simple, explicit, and easy to reason about

---

End of document.
