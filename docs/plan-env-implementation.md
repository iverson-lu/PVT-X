# Plan-Level Environment Variables Injection - Implementation Summary

## Overview
This implementation adds Plan-level environment variable injection into Suite/Case execution, completing the environment variable precedence chain defined in the specification.

## Priority Order (Low to High)
The complete environment variable resolution priority is now:

1. **OS Environment** (baseline)
2. **Plan Environment** (`plan.manifest.json` → `environment.env`)
3. **Suite Environment** (`suite.manifest.json` → `environment.env`)
4. **RunRequest / CLI EnvironmentOverrides** (highest priority)

Later sources override earlier sources for the same key.

## Changes Made

### 1. EnvironmentResolver.cs
- **Added new overload**: `ComputeSuiteEnvironment(TestPlanManifest?, TestSuiteManifest, EnvironmentOverrides?)`
  - Accepts optional Plan manifest parameter
  - Applies Plan environment before Suite environment
  - Maintains backward compatibility (null plan uses existing behavior)
- **Added comprehensive documentation** explaining the full precedence order

### 2. SuiteOrchestrator.cs
- **Updated `ExecuteAsync` signature**: Added optional `TestPlanManifest? planManifest` parameter
- **Updated environment computation logic**: Uses new resolver overload when plan manifest is provided
- **Maintains backward compatibility**: Standalone suite execution (no plan) uses original behavior

### 3. PlanOrchestrator.cs
- **Updated suite execution call**: Now passes `plan.Manifest` to `SuiteOrchestrator.ExecuteAsync()`
- **Removed dead code**: The computed `effectiveEnv` variable is now properly used (via the planManifest parameter)

### 4. EnvironmentResolverTests.cs
- **Added 6 comprehensive tests**:
  - `ComputeSuiteEnvironmentWithPlan_PlanProvidesDefaultWhenSuiteDoesNotSetIt()` - Verifies Plan provides defaults
  - `ComputeSuiteEnvironmentWithPlan_SuiteOverridesPlanForSameKey()` - Verifies Suite overrides Plan
  - `ComputeSuiteEnvironmentWithPlan_OverridesOverrideBothPlanAndSuite()` - Verifies Override priority
  - `ComputeSuiteEnvironmentWithPlan_VerifyFullPriorityOrder()` - Comprehensive priority verification
  - `ComputeSuiteEnvironmentWithPlan_NullPlan_UsesExistingBehavior()` - Backward compatibility test
  - All tests pass ✅

## How It Works

### When executing a Plan:
1. `PlanOrchestrator` iterates through suites in the plan
2. For each suite, it passes the plan manifest to `SuiteOrchestrator`
3. `SuiteOrchestrator` calls the new `ComputeSuiteEnvironment(plan, suite, overrides)` overload
4. The resolver merges environments in priority order: OS → Plan → Suite → Overrides
5. Result is stored in `RunContext.EffectiveEnvironment`
6. `PowerShellExecutor` applies this environment to the test case process

### When executing a standalone Suite:
- No plan manifest is passed
- Uses original `ComputeSuiteEnvironment(suite, overrides)` overload
- Maintains existing behavior: OS → Suite → Overrides

## Verification
- All existing tests pass
- New tests verify the complete priority chain
- No compilation errors
- Backward compatibility maintained for standalone suite execution

## Example

```json
// plan.manifest.json
{
  "environment": {
    "env": {
      "API_URL": "https://api.example.com",
      "TIMEOUT": "30"
    }
  }
}

// suite.manifest.json
{
  "environment": {
    "env": {
      "TIMEOUT": "60",        // Overrides plan's TIMEOUT
      "TEST_MODE": "full"     // Suite-specific variable
    }
  }
}

// RunRequest EnvironmentOverrides
{
  "env": {
    "API_URL": "https://api.test.com"  // Overrides plan's API_URL
  }
}

// Resulting effective environment for test case:
// API_URL=https://api.test.com    (from RunRequest)
// TIMEOUT=60                       (from Suite, overriding Plan)
// TEST_MODE=full                   (from Suite)
// ... plus OS environment variables
```

## Implementation Notes
- **Minimal changes**: Only touched 3 source files and 1 test file
- **No changes to Runner**: `PowerShellExecutor` continues to work as-is
- **No manifest schema changes**: Uses existing structures
- **No breaking changes**: All existing code continues to work
- **Well-tested**: 6 new tests cover all priority combinations
