# Event View Check – Requirement Specification

## 1. Purpose

This test case verifies Windows Event Logs within a defined relative time window to detect:

- Critical events that must not occur (immediate failure)
- Abnormal frequency or patterns of warning/error events (trend-based failure or warning)

This specification targets **PC stability, reliability, and health validation**, and is not tied to any specific test action.

---

## 2. Log Scope

The test case supports checking events from the following Windows Event Logs:

- **System** (default)
- **Application**

Logs can be selected individually or combined. Multiple log selection is supported via UI multiselect.

> Future versions may extend support to additional event logs or custom channels.

---

## 3. Event Level Filtering

The test case supports filtering events by minimum severity level (`MinLevel`):

- Critical
- Error
- Warning
- Information (default)

**Filtering Behavior:**

- **Collection Phase**: ALL events within the time window are collected regardless of MinLevel
- **Rule Evaluation**: Blocklist and Allowlist rules are evaluated against ALL collected events
- **Threshold Pool**: MinLevel filter is applied **only** to threshold pool counting
  - Events below MinLevel are excluded from threshold pool
  - This ensures low-severity events can still trigger blocklist rules while not counting toward threshold

**Example**: If MinLevel=Error and a blocklist rule matches Information events, the test will still FAIL even though Information events don't count toward the threshold.

---

## 4. Rule System Overview

Event evaluation rules are defined in external CSV files and are divided into two categories:

- **Allowlist Rules** – events that are acceptable and should be ignored
- **Blocklist Rules (Hard Fail)** – events that cause immediate test failure

Rules are data-driven and independent of test case logic.

---

## 5. CSV Rule Schema

Each rule entry contains the following fields:

| Field        | Required | Description                                                  |
| ------------ | -------- | ------------------------------------------------------------ |
| rule\_id    | No       | Unique rule identifier (e.g., `BL-001`, `AL-001`)           |
| log          | No       | Log name to match: `System` or `Application` (exact match, case-insensitive) |
| level        | No       | Event level to match: `Critical`, `Error`, `Warning`, `Information` (exact match, case-insensitive) |
| provider     | No       | Event provider name (exact match, case-insensitive). **Must use full PowerShell provider name** (e.g., `Microsoft-Windows-Kernel-General`, not `Kernel-General`) |
| event\_id   | No       | Event ID number (exact match)                               |
| message      | No       | Text to search in event message (matching mode determined by `match_mode`) |
| match\_mode | No       | How to match `message` field: `contains` (default), `exact`, or `regex` |
| owner        | No       | Rule owner/team for tracking purposes                        |
| comment      | No       | Rule description and justification                           |

**Important Notes:**

- All fields are optional (empty = wildcard)
- **Provider names**: Use `Get-WinEvent` PowerShell provider names, not Event Viewer display names
  - ✅ Correct: `Microsoft-Windows-Kernel-General`
  - ❌ Wrong: `Kernel-General`
  - To find correct name: `Get-WinEvent -LogName System -MaxEvents 1 | Select-Object ProviderName`

---

## 6. Message Matching Modes (match\_mode)

The `match_mode` field defines how `message_contains` is evaluated against an event message.

Supported values:

- **contains** (default)
  - Event message must contain the specified keyword(s)
- **exact**
  - Event message must exactly match the specified text
- **regex**
  - Event message must match the specified regular expression

Rules:

- If `match_mode` is empty, it defaults to `contains`
- Matching is case-insensitive
- Empty or unspecified rule fields do not participate in matching

---

## 7. Rule Matching Semantics

An event matches a rule if and only if the event satisfies **all non-empty fields** in the rule.

**Matching Logic:**

- **AND semantics**: All non-empty columns must match
- **Empty columns**: Act as wildcards (ignored)
- **Field matching modes**:
  - `log`, `level`, `provider`, `event_id`: **Exact match** (case-insensitive for text fields)
  - `message`: Supports flexible matching via `match_mode` (contains/exact/regex)
- **First match wins**: Rules are evaluated in CSV file order

**Example:**
```csv
rule_id,log,level,provider,event_id,message,match_mode,owner,comment
BL-001,System,,,16,,,,Matches any event_id=16 in System log
BL-002,,Error,Microsoft-Windows-Disk,,,,,Matches any Error from specific provider
BL-003,,,,,failed,contains,,Matches any event with "failed" in message
```

---

## 8. Allowlist Behavior

- Events matching an allowlist rule:
  - Are **not counted** toward threshold pool
  - Are **not** evaluated against blocklist (blocklist has higher priority)
  - Are marked with `allow_hit=true` and `allow_rule_id` in detailed output
- Allowlist evaluation occurs **after** blocklist evaluation
- Best practice: Document reason for exclusion in `comment` field

---

## 9. Blocklist (Hard Fail) Behavior

- Events matching a blocklist rule:
  - Cause the test result to be **FAIL** immediately
  - Are marked with `block_hit=true` and `block_rule_id` in output
  - Are automatically saved to `artifacts/failed_events.csv` with rule metadata
- Blocklist evaluation has the **highest priority** (evaluated before allowlist)
- Any blocklist match causes FAIL regardless of MinLevel filter or threshold settings

---

## 10. Threshold-Based Evaluation

After blocklist and allowlist evaluation, remaining events form the **threshold pool**.

**Threshold Pool Rules:**

- Includes only events that:
  - Are **not** blocklist matches
  - Are **not** allowlist matches
  - Meet or exceed `MinLevel` severity filter
- Configurable threshold: `FailThreshold` (default: 1)
  - If `threshold_pool_count >= FailThreshold` → **FAIL**
  - Events in threshold pool are automatically saved to `artifacts/failed_events.csv`

**Note**: Current implementation supports FAIL only (no WARN threshold).

---

## 11. Result Priority Order

Test result determination follows this priority order:

1. Any blocklist match → **FAIL** (exit code 1)
2. Threshold pool count >= FailThreshold → **FAIL** (exit code 1)
3. No violations → **PASS** (exit code 0)
4. Script/environment error → **ERROR** (exit code >= 2)

**Note**: Current implementation does not support WARN result state.

---

## 12. Output Artifacts

The test produces the following artifacts in `artifacts/` directory:

### Always Generated:

1. **report.json** - Structured test result with:
   - Test metadata (name, version, timestamp, duration)
   - Step-by-step execution details
   - Metrics: total events, blocklist hits, allowlist hits, threshold pool count
   - Overall result (PASS/FAIL) and exit code

2. **events_summary.csv** - Event statistics per log:
   - Scope (System/Application/TOTAL)
   - Raw events collected
   - Events after filtering
   - Blocklist hits count
   - Allowlist hits count
   - Threshold pool count

### Conditionally Generated:

3. **failed_events.csv** - Automatically created on FAIL:
   - **If blocklist hit**: Contains matched events with rule metadata (rule_id, owner, comment)
   - **If threshold exceeded**: Contains all threshold pool events
   - Fields: log_name, level, provider, event_id, time_created, message

4. **events_detail.csv** - Created when `CaptureEventsToFile=true`:
   - Full list of ALL collected events (time window filtered only)
   - Fields: time_created, log_name, level, provider, event_id, record_id, block_hit, block_rule_id, allow_hit, allow_rule_id, message
   - Useful for debugging and detailed analysis

---

## 13. Implementation Details

**Technology Stack:**
- PowerShell 7+ with `Get-WinEvent` cmdlet
- CSV-based rule management
- JSON output format for structured reporting

**Limitations:**
- Maximum events per log: 5000 (configurable via `MaxEventsPerLog`)
- Message truncation: 300 characters (configurable via `TruncateMessageChars`)
- Time window: Relative minutes from current time (configurable via `WindowMinutes`)

## 14. Non-Goals

This specification does **not** define:

- Extended event log channels beyond System and Application
- WARN result state (only PASS/FAIL)
- Event aggregation or deduplication
- Real-time event monitoring
- Automatic generation of rule content
- Causal linkage between specific test actions and events

---

**End of Specification**

