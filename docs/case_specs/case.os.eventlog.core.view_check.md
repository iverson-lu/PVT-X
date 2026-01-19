# Event View Check – Requirement Specification

## 1. Purpose

This test case verifies Windows Event Logs within a defined relative time window to detect:

- Critical events that must not occur (immediate failure)
- Abnormal frequency or patterns of warning/error events (trend-based failure or warning)

This specification targets **PC stability, reliability, and health validation**, and is not tied to any specific test action.

---

## 2. Log Scope

The test case shall support checking events from the following logical log scopes:

- **System**
- **Application**
- **Administrative Events** (strongly recommended as default)
- Optional extended providers or log sets, such as:
  - Microsoft-Windows-WHEA-Logger
  - Diagnostics-Performance

> Log scopes are logical definitions; implementation details of underlying channels are out of scope for this spec.

---

## 3. Event Level Threshold

The test case shall support filtering events by a minimum severity level:

- Critical
- Error
- Warning
- Information

Behavior:

- Only events with severity **greater than or equal to** the configured threshold are considered
- An empty threshold means no severity filtering

---

## 4. Rule System Overview

Event evaluation rules are defined in external CSV files and are divided into two categories:

- **Allowlist Rules** – events that are acceptable and should be ignored
- **Blocklist Rules (Hard Fail)** – events that cause immediate test failure

Rules are data-driven and independent of test case logic.

---

## 5. CSV Rule Schema

Each rule entry shall contain the following fields:

| Field             | Description                                                  |
| ----------------- | ------------------------------------------------------------ |
| rule\_id          | Unique rule identifier; recommended prefixes: `ALW-`, `BLK-` |
| enabled           | Whether the rule is active (`Y` / `N`)                       |
| log\_names        | Applicable log scopes (comma-separated)                      |
| level\_min        | Minimum event level for rule applicability (optional)        |
| provider          | Event provider / source (optional)                           |
| event\_ids        | Event IDs (comma-separated, optional)                        |
| message\_contains | Message matching condition                                   |
| match\_mode       | Message matching mode                                        |
| owner             | Rule owner (required)                                        |
| comment           | Rule description and justification (required for allowlist)  |

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

An event matches a rule if and only if:

- The rule is enabled
- The event satisfies **all non-empty conditions** defined by the rule

All conditions within a rule are evaluated using **logical AND**.

---

## 8. Allowlist Behavior

- Events matching an allowlist rule:
  - Are excluded from further evaluation
  - Do not participate in blocklist checks or threshold statistics
- Allowlist rules must clearly document the reason for exclusion in `comment`

---

## 9. Blocklist (Hard Fail) Behavior

- Events matching a blocklist rule:
  - Cause the test result to be **Fail**
  - Must still be recorded and reported for traceability
- Blocklist evaluation has the **highest priority**

---

## 10. Threshold-Based Evaluation

The test case shall support evaluating event frequency trends, including:

- Aggregation of identical events (e.g., same Provider + Event ID)
- Configurable thresholds such as:
  - Occurrence count ≥ N → Fail
  - Occurrence count ≥ M → Warn

Threshold evaluation applies only to events that:

- Pass severity filtering
- Are not allowlisted
- Do not trigger blocklist rules

---

## 11. Result Priority Order

Test result determination follows this priority order:

1. Blocklist match → **Fail**
2. Threshold-based failure → **Fail**
3. Threshold-based warning → **Warn**
4. No violations → **Pass**

---

## 12. Reporting and Traceability Requirements

The test output shall include at minimum:

- Effective time window used for evaluation
- Triggered rules (rule\_id, owner, comment)
- Summary of events leading to Fail or Warn
- Event details, including:
  - Timestamp
  - Severity level
  - Log scope
  - Provider
  - Event ID
  - Message (may be truncated)

---

## 13. Non-Goals

This specification does **not** define:

- Event log collection or access mechanisms
- Automatic generation of rule content
- Causal linkage between specific test actions and events

---

**End of Specification**

