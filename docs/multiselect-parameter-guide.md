# Multi-Select Parameter Guide

## Overview

PVT-X now supports multi-select parameters (checkboxes) for test cases. This allows users to select multiple values from a predefined list.

## Implementation Details

### Manifest Definition

To create a multi-select parameter, use `type: "json"` combined with `enumValues`:

```json
{
  "name": "Options",
  "type": "json",
  "required": false,
  "enumValues": ["OptionA", "OptionB", "OptionC", "OptionD"],
  "default": "[\"OptionA\", \"OptionC\"]",
  "help": "Select multiple options"
}
```

**Key Points:**
- `type` must be `"json"`
- `enumValues` must be present (list of selectable options)
- `default` is a JSON array string (e.g., `["OptionA", "OptionC"]`)

### UI Behavior

When the UI detects a parameter with `type: "json"` and non-empty `enumValues`, it automatically renders a collapsible multi-select checkbox list.

**Rendering Rules:**
- `json` + `enumValues` → **Collapsible multi-select checkboxes** (Expander)
- `json` without `enumValues` → **Textarea** (existing behavior)
- `enum` → **Single-select dropdown** (existing behavior)

**Expander Behavior:**
- **Header**: Shows parameter name and selection count (e.g., "Options (2 selected)")
- **Collapsed (default)**: Saves vertical space, shows only one line
- **Expanded**: Displays full checkbox list with 24px left indent

### PowerShell Script Usage

The parameter value is passed as a JSON array string:

```powershell
param(
  [string]$Options = '["OptionA", "OptionC"]'
)

# Parse the JSON array
$selectedOptions = $Options | ConvertFrom-Json
# Result: @("OptionA", "OptionC")

# Use the values
foreach ($opt in $selectedOptions) {
  Write-Host "Selected: $opt"
}
```

## Example Test Case

See: `assets/TestCases/case.template.demo.core.all_types@1.0.0/`

This template demonstrates all parameter types including multi-select.

## Technical Components

### Added Components

1. **ParameterViewModel** (`src/PcTest.Ui/ViewModels/CasesTabViewModel.cs`)
   - Added `IsMultiSelect` property to detect `json + enumValues`

2. **ParameterEditorTemplateSelector** (`src/PcTest.Ui/Resources/ParameterEditorTemplateSelector.cs`)
   - Added `MultiSelectEditorTemplate` property
   - Updated selection logic to prioritize multi-select over default

3. **JsonArrayContainsConverter** (`src/PcTest.Ui/Resources/Converters.cs`)
   - MultiBinding converter that checks if a value exists in a JSON array
   - Used for CheckBox `IsChecked` binding

4. **JsonArrayCountConverter** (`src/PcTest.Ui/Resources/Converters.cs`)
   - Converts JSON array to selection count string (e.g., "2 selected")
   - Used in Expander header to show current selection count

5. **MultiSelectJsonBehavior** (`src/PcTest.Ui/Behaviors/MultiSelectJsonBehavior.cs`)
   - Attached behavior that synchronizes CheExpander wrapper
   - Expander header displays parameter name + selection count
   - Collapsed by default to save vertical space
   - CheckBox list indented 24px when expanded
   - Updates `CurrentValue` when checkboxes are checked/unchecked

5. **App.xaml Template**
   - Added `MultiSelectEditorTemplate` with ItemsControl and CheckBoxes
   - Uses `JsonArrayContainsConverter` for one-way binding (JSON → CheckBox)
   - Uses `MultiSelectJsonBehavior` for updates (CheckBox → JSON)

### Tests

- **ConverterTests.cs**: Tests for `JsonArrayContainsConverter`
- **ParameterViewModelTests.cs**: Tests for `IsMultiSelect` property

All tests pass ✅

## Design Rationale

### Why `json` + `enumValues`?

1. **Core Spec Compliance**: `json` type is already supported in the spec
2. **Backward Compatible**: Doesn't require spec changes
3. **UI-Layer Intelligence**: Detection logic lives in UI, not core
4. **Script Simplicity**: Scripts receive JSON arrays (standard format)
5. **Extensible**: Can support other JSON UI hints in the future

### Alternative Considered

Adding native `enum[]` or `string[]` types would require:
- Core spec updates
- Runner parameter passing changes (multiple `-Param "Val1" "Val2"`)
- More complex validation logic

The current approach achieves the same goal with less change.

## Spec Compliance

This implementation is **fully compliant** with PVT-X Core Spec v1.5.1:

- Section 6.2: `json` type is explicitly supported
- Section 6.2: `enumValues` is an optional field (no type restriction)
- UI Spec Section 8: Editor selection is UI-layer responsibility

No spec changes required. ✅
